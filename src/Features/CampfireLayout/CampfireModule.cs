using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Runs;
using RemoveMultiplayerPlayerLimit.Core;
using RemoveMultiplayerPlayerLimit.Infrastructure;

namespace RemoveMultiplayerPlayerLimit.Features.CampfireLayout;

/// <summary>
/// Monitors the rest site (campfire) scene and arranges extra character
/// containers when more than 4 players are present.
///
/// Core fix:
///   NRestSiteRoom._Ready() hardcodes 4 character containers but loops
///   over ALL players → IndexOutOfRangeException with 5+ players.
///
///   We use SceneTree.NodeAdded to intercept NRestSiteRoom BEFORE _Ready()
///   fires, pre-populating the _characterContainers list with extra
///   Control nodes so the loop completes without crashing.
///
///   After _Ready() succeeds, _Process() adds extra log sprites and
///   positions the extra seats visually.
/// </summary>
public partial class CampfireModule : IRMPModule
{
    public string Name => "CampfireLayout";

    private static readonly Vector2 LeftExtraFrontOffset = new(-250f, 35f);
    private static readonly Vector2 LeftExtraBackOffset = new(-240f, -20f);
    private static readonly Vector2 RightExtraFrontOffset = new(250f, 35f);
    private static readonly Vector2 RightExtraBackOffset = new(240f, -20f);
    private static readonly Vector2 LogXOffsetLeft = new(-250f, 0f);
    private static readonly Vector2 LogXOffsetRight = new(250f, 0f);
    private static readonly Vector2 ExtraSeatStep = new(70f, -45f);

    private FieldInfo? _containersField;
    private ReflectionCache _cache = null!;

    public void Initialize(ConfigManager config, ReflectionCache cache)
    {
        _cache = cache;
        _containersField = cache.GetField(typeof(NRestSiteRoom), "_characterContainers");
    }

    public Node? CreateNode() => new CampfireNode(this);

    public void Cleanup() { }

    private partial class CampfireNode : Node
    {
        private readonly CampfireModule _module;
        private int _frameCounter;
        private bool _arranged;
        private NRestSiteRoom? _lastRoom;

        public CampfireNode(CampfireModule module)
        {
            _module = module;
            Name = "CampfireNode";
        }

        public override void _EnterTree()
        {
            GetTree().NodeAdded += OnNodeAdded;
        }

        public override void _ExitTree()
        {
            var tree = GetTree();
            if (tree != null)
                tree.NodeAdded -= OnNodeAdded;
        }

        /// <summary>
        /// Intercepts NRestSiteRoom BEFORE _Ready() fires.
        /// Pre-populates _characterContainers with extra Control nodes
        /// so that _Ready()'s player loop doesn't crash at index 4+.
        /// </summary>
        private void OnNodeAdded(Node node)
        {
            if (node is not NRestSiteRoom restSite) return;

            int playerCount = GameStateAccessor.GetPlayerCount();
            if (playerCount <= ModEntry.VanillaMultiplayerHolderCount) return;

            if (_module._containersField == null)
            {
                Log.Warn("[RMP:Campfire] _characterContainers field not found — cannot prevent crash.");
                return;
            }

            try
            {
                PreInjectContainers(restSite, playerCount);
            }
            catch (Exception ex)
            {
                Log.Warn($"[RMP:Campfire] Pre-injection failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates extra Character container Controls under BgContainer and
        /// pre-fills _characterContainers so _Ready() can iterate all players.
        ///
        /// After _Ready() appends its hardcoded 4 containers, the list will
        /// have N + 4 entries, but the for loop only accesses indices 0..N-1
        /// (all valid from our pre-fill). The trailing duplicates are harmless.
        /// </summary>
        private void PreInjectContainers(NRestSiteRoom restSite, int playerCount)
        {
            // Scene is instantiated; children exist but _Ready() hasn't fired
            Control? bgContainer = restSite.GetNodeOrNull<Control>("BgContainer");
            if (bgContainer == null) return;

            // Collect existing character containers from the scene
            var allContainers = new List<Control>();
            for (int i = 1; i <= ModEntry.VanillaMultiplayerHolderCount; i++)
            {
                var c = bgContainer.GetNodeOrNull<Control>($"Character_{i}");
                if (c != null) allContainers.Add(c);
            }

            if (allContainers.Count < ModEntry.VanillaMultiplayerHolderCount) return;

            // Create extra containers for players 5+
            for (int i = allContainers.Count; i < playerCount; i++)
            {
                Control extra = new Control();
                extra.Name = $"Character_{i + 1}";
                extra.Position = GetExtraContainerPosition(allContainers, i);
                bgContainer.AddChild(extra);
                allContainers.Add(extra);
            }

            // Pre-populate _characterContainers BEFORE _Ready() runs
            var containersList = _module._containersField!.GetValue(restSite) as List<Control>;
            if (containersList != null)
            {
                containersList.AddRange(allContainers);
            }

            Log.Info($"[RMP:Campfire] Pre-injected {playerCount - ModEntry.VanillaMultiplayerHolderCount} extra containers for {playerCount} players");
        }

        public override void _Process(double delta)
        {
            if (++_frameCounter % 10 != 0) return;

            var room = SceneMonitor.FindRestSiteRoom();

            // Reset when leaving campfire
            if (room == null || room != _lastRoom)
            {
                _arranged = false;
                _lastRoom = room;
                return;
            }

            if (_arranged) return;

            int playerCount = GameStateAccessor.GetPlayerCount();
            if (playerCount <= ModEntry.VanillaMultiplayerHolderCount) return;

            try
            {
                ArrangeVisuals(room, playerCount);
                _arranged = true;
            }
            catch (Exception ex)
            {
                Log.Warn($"[RMP:Campfire] Failed to arrange visuals: {ex}");
                _arranged = true; // Don't retry on failure
            }
        }

        /// <summary>
        /// Post-_Ready() visual arrangement: adds extra campfire logs.
        /// Container injection was already handled by PreInjectContainers.
        /// </summary>
        private void ArrangeVisuals(NRestSiteRoom room, int playerCount)
        {
            if (_module._containersField == null) return;

            var containers = _module._containersField.GetValue(room) as List<Control>;
            if (containers == null || containers.Count == 0) return;

            // Ensure extra containers exist (fallback if NodeAdded didn't fire)
            if (containers.Count < playerCount)
            {
                EnsureContainers(containers, playerCount);
            }

            // Add extra log sprites for visual flair
            Control parent = containers[0].GetParent<Control>();
            if (parent != null)
            {
                EnsureExtraLogs(parent);
            }
        }

        private static void EnsureContainers(List<Control> containers, int requiredCount)
        {
            if (requiredCount <= containers.Count) return;

            Control parent = containers[0].GetParent<Control>();
            if (parent == null) return;

            int templateCount = Math.Min(containers.Count, ModEntry.VanillaMultiplayerHolderCount);
            if (templateCount == 0) return;

            while (containers.Count < requiredCount)
            {
                int count = containers.Count;
                Control source = containers[count % templateCount];
                Control clone = source.Duplicate() as Control ?? new Control();
                RemoveAllChildren(clone);
                clone.Name = $"Character_Auto_{count + 1}";
                clone.Position = GetExtraContainerPosition(containers, count);
                parent.AddChild(clone);
                containers.Add(clone);
            }
        }

        private static Vector2 GetExtraContainerPosition(List<Control> containers, int index)
        {
            if (containers.Count < 4) return containers[^1].Position;
            if (index < 4) return containers[index].Position;

            int extraSeatIndex = index - 4;
            bool isLeft = extraSeatIndex % 2 == 0;
            int depth = extraSeatIndex / 2;

            Vector2 frontPos = isLeft
                ? containers[0].Position + LeftExtraFrontOffset
                : containers[1].Position + RightExtraFrontOffset;
            Vector2 backPos = isLeft
                ? containers[2].Position + LeftExtraBackOffset
                : containers[3].Position + RightExtraBackOffset;

            if (depth == 0) return frontPos;
            if (depth == 1) return backPos;

            int extraDepth = depth - 1;
            Vector2 offset = new(
                (isLeft ? -1f : 1f) * ExtraSeatStep.X * extraDepth,
                ExtraSeatStep.Y * extraDepth);
            return backPos + offset;
        }

        private static void EnsureExtraLogs(Control parent)
        {
            Node? background = parent.GetChildCount() > 0 ? parent.GetChild(0) : null;
            if (background == null) return;
            if (background.GetNodeOrNull<Node>("AutoExtraLogsMarker") != null) return;

            Node marker = new() { Name = "AutoExtraLogsMarker" };
            background.AddChild(marker);

            DuplicateShiftedNode(background, "RestSiteLLog", LogXOffsetLeft, "AutoL");
            DuplicateShiftedNode(background, "RestSiteRLog", LogXOffsetRight, "AutoR");
            DuplicateShiftedNode(background, "RestSiteLighting/RestSiteLLog2", LogXOffsetLeft, "AutoL");
            DuplicateShiftedNode(background, "RestSiteLighting/RestSiteRLog2", LogXOffsetRight, "AutoR");
        }

        private static void DuplicateShiftedNode(Node root, string nodePath, Vector2 offset, string suffix)
        {
            Node? node = root.GetNodeOrNull<Node>(nodePath);
            if (node == null) return;

            Node? nodeParent = node.GetParent();
            if (nodeParent == null) return;

            Node clone = node.Duplicate();
            clone.Name = $"{node.Name}_{suffix}";
            nodeParent.AddChild(clone);

            if (node is Control c && clone is Control cc)
                cc.Position = c.Position + offset;
            else if (node is Node2D n && clone is Node2D nc)
                nc.Position = n.Position + offset;
        }

        private static void RemoveAllChildren(Node node)
        {
            for (int i = node.GetChildCount() - 1; i >= 0; i--)
            {
                Node child = node.GetChild(i);
                node.RemoveChild(child);
                child.QueueFree();
            }
        }
    }
}
