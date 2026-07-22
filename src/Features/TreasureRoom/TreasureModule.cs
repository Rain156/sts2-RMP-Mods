using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.TreasureRelicPicking;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using RemoveMultiplayerPlayerLimit.Core;
using RemoveMultiplayerPlayerLimit.Infrastructure;

namespace RemoveMultiplayerPlayerLimit.Features.TreasureRoom;

/// <summary>
/// Monitors treasure room relic collection and expands holder layout
/// for 5+ players. Also manages the skip button and vote resolution.
///
/// Replaces:
///   Legacy behavior: patch NTreasureRoomRelicCollection and TreasureRoomRelicSynchronizer.
///
/// Approach:
///   Polls for NTreasureRoomRelicCollection. When found, expands holders,
///   applies grid layout, and manages skip button lifecycle.
/// </summary>
public partial class TreasureModule : IRMPModule
{
    public string Name => "TreasureRoom";

    private const float FallbackXStep = 220f;
    private const float MinXStep = 190f;
    private const float MinYStep = 120f;

    private ReflectionCache _cache = null!;

    // Reflection targets
    internal FieldInfo? HoldersInUseField;
    internal FieldInfo? MultiplayerHoldersField;
    internal FieldInfo? RunStateField;
    internal FieldInfo? SyncPlayerCollectionField;
    internal FieldInfo? SyncLocalPlayerIdField;
    internal FieldInfo? SyncActionQueueField;
    internal FieldInfo? SyncCurrentRelicsField;
    internal FieldInfo? SyncRngField;
    internal FieldInfo? SyncVotesField;
    internal FieldInfo? SyncPredictedVoteField;
    internal FieldInfo? VotesChangedEventField;
    internal FieldInfo? RelicsAwardedEventField;
    internal MethodInfo? EndRelicVotingMethod;

    // PeerInputSynchronizer.GetOrCreateStateForPlayer is private — we reach it via
    // reflection so we can pre-populate PeerInputState for all players before
    // NTreasureRoomRelicCollection.Initialize runs NHandImageCollection.UpdateHandVisibility,
    // which otherwise throws "Tried to get PeerInputState for non-existent player X!"
    // on any client whose PeerInputSynchronizer never received a PeerInputMessage from
    // that player (e.g. when another mod is interfering with the PeerInputMessage
    // handler registration — real-player logs show this happens in the wild).
    internal MethodInfo? PeerInputGetOrCreateMethod;

    internal readonly HashSet<TreasureRoomRelicSynchronizer> LocalVotePending = new();
    internal readonly HashSet<TreasureRoomRelicSynchronizer> LocalSkipLocked = new();

    public void Initialize(ConfigManager config, ReflectionCache cache)
    {
        _cache = cache;
        var collType = typeof(NTreasureRoomRelicCollection);
        var syncType = typeof(TreasureRoomRelicSynchronizer);

        HoldersInUseField = cache.GetField(collType, "_holdersInUse");
        MultiplayerHoldersField = cache.GetField(collType, "_multiplayerHolders");
        RunStateField = cache.GetField(collType, "_runState");
        SyncPlayerCollectionField = cache.GetField(syncType, "_playerCollection");
        SyncLocalPlayerIdField = cache.GetField(syncType, "_localPlayerId");
        SyncActionQueueField = cache.GetField(syncType, "_actionQueueSynchronizer");
        SyncCurrentRelicsField = cache.GetField(syncType, "_currentRelics");
        SyncRngField = cache.GetField(syncType, "_rng");
        SyncVotesField = cache.GetField(syncType, "_votes");
        SyncPredictedVoteField = cache.GetField(syncType, "_predictedVote");
        VotesChangedEventField = cache.GetField(syncType, "VotesChanged");
        RelicsAwardedEventField = cache.GetField(syncType, "RelicsAwarded");
        EndRelicVotingMethod = cache.GetMethod(syncType, "EndRelicVoting");

        PeerInputGetOrCreateMethod = typeof(PeerInputSynchronizer).GetMethod(
            "GetOrCreateStateForPlayer",
            BindingFlags.Instance | BindingFlags.NonPublic);
    }

    /// <summary>
    /// Ensures PeerInputSynchronizer has a PeerInputState for every player in the
    /// current run. Idempotent — GetOrCreateStateForPlayer returns existing state
    /// if already present. Without this call, NTreasureRoomRelicCollection._Ready
    /// can throw on a client whose PeerInputMessage handler was never registered
    /// (seen in real-player logs with other mods in the mix).
    /// </summary>
    internal void PrewarmAllPlayerStates()
    {
        if (PeerInputGetOrCreateMethod == null) return;

        PeerInputSynchronizer? sync = RunManager.Instance?.InputSynchronizer;
        if (sync == null) return;

        // RunManager.State is private — reach through the shared accessor.
        var runState = GameStateAccessor.GetRunState();
        if (runState?.Players == null) return;

        foreach (var player in runState.Players)
        {
            try
            {
                PeerInputGetOrCreateMethod.Invoke(sync, new object[] { player.NetId });
            }
            catch
            {
                // ignored — best-effort prewarm
            }
        }
    }

    public Node? CreateNode() => new TreasureNode(this);

    public void Cleanup()
    {
        LocalVotePending.Clear();
        LocalSkipLocked.Clear();
    }

    // ── Reflection helpers (shared with TreasureNode) ─────────────────

    internal List<NTreasureRoomRelicHolder>? GetHoldersInUse(NTreasureRoomRelicCollection c)
        => HoldersInUseField?.GetValue(c) as List<NTreasureRoomRelicHolder>;

    internal List<NTreasureRoomRelicHolder>? GetMultiplayerHolders(NTreasureRoomRelicCollection c)
        => MultiplayerHoldersField?.GetValue(c) as List<NTreasureRoomRelicHolder>;

    internal IRunState? GetRunState(NTreasureRoomRelicCollection c)
        => RunStateField?.GetValue(c) as IRunState;

    internal IPlayerCollection? GetSyncPlayerCollection(TreasureRoomRelicSynchronizer s)
        => SyncPlayerCollectionField?.GetValue(s) as IPlayerCollection;

    internal ulong? GetSyncLocalPlayerId(TreasureRoomRelicSynchronizer s)
        => SyncLocalPlayerIdField?.GetValue(s) is ulong id ? id : null;

    internal ActionQueueSynchronizer? GetSyncActionQueue(TreasureRoomRelicSynchronizer s)
        => SyncActionQueueField?.GetValue(s) as ActionQueueSynchronizer;

    internal List<RelicModel>? GetSyncCurrentRelics(TreasureRoomRelicSynchronizer s)
        => SyncCurrentRelicsField?.GetValue(s) as List<RelicModel>;

    internal Rng? GetSyncRng(TreasureRoomRelicSynchronizer s)
        => SyncRngField?.GetValue(s) as Rng;

    internal List<int?>? GetSyncVotes(TreasureRoomRelicSynchronizer s)
        => SyncVotesField?.GetValue(s) as List<int?>;

    internal void SetSyncPredictedVote(TreasureRoomRelicSynchronizer s, int? vote)
    {
        if (SyncPredictedVoteField == null) return;
        var ft = SyncPredictedVoteField.FieldType;
        if (ft == typeof(int?)) SyncPredictedVoteField.SetValue(s, vote);
        else if (ft == typeof(int)) SyncPredictedVoteField.SetValue(s, vote ?? -1);
    }

    internal void InvokeVotesChanged(TreasureRoomRelicSynchronizer s)
    {
        if (VotesChangedEventField?.GetValue(s) is Action action) action();
    }

    internal void InvokeRelicsAwarded(TreasureRoomRelicSynchronizer s, List<RelicPickingResult> results)
    {
        if (RelicsAwardedEventField?.GetValue(s) is Action<List<RelicPickingResult>> action) action(results);
    }

    internal void InvokeEndRelicVoting(TreasureRoomRelicSynchronizer s)
    {
        EndRelicVotingMethod?.Invoke(s, null);
    }

    internal void ClearLocalVoteState(TreasureRoomRelicSynchronizer s)
    {
        LocalVotePending.Remove(s);
        LocalSkipLocked.Remove(s);
        SetSyncPredictedVote(s, null);
    }

    // ── Treasure Node ─────────────────────────────────────────────────

    private partial class TreasureNode : Node
    {
        private readonly TreasureModule _mod;
        private int _frameCounter;
        private NTreasureRoomRelicCollection? _lastCollection;
        private bool _layoutApplied;

        public TreasureNode(TreasureModule mod)
        {
            _mod = mod;
            Name = "TreasureNode";
        }

        public override void _EnterTree()
        {
            GetTree().NodeAdded += OnNodeAdded;
        }

        public override void _ExitTree()
        {
            SceneTree? tree = GetTree();
            if (tree != null)
                tree.NodeAdded -= OnNodeAdded;
        }

        private void OnNodeAdded(Node node)
        {
            if (node is not NTreasureRoomRelicCollection collection)
                return;

            // CRITICAL: must run BEFORE NTreasureRoomRelicCollection._Ready(),
            // which will call NHandImageCollection.Initialize → UpdateHandVisibility →
            // PeerInputSynchronizer.ForceGetStateForPlayer. If any player lacks a
            // PeerInputState at that moment, vanilla throws and the treasure room
            // UI never renders (black screen). NodeAdded fires in Godot's scene
            // lifecycle AFTER _EnterTree but BEFORE _Ready, so this is the last
            // safe hook point before the vanilla call that throws.
            try
            {
                _mod.PrewarmAllPlayerStates();
            }
            catch (Exception ex)
            {
                Log.Warn($"[RMP:Treasure] Pre-warm peer input states failed: {ex.Message}");
            }

            try
            {
                ExpandHolders(collection);
            }
            catch (Exception ex)
            {
                Log.Warn($"[RMP:Treasure] Pre-expand failed: {ex.Message}");
            }
        }

        public override void _Process(double delta)
        {
            if (++_frameCounter % 10 != 0) return;

            var collection = SceneMonitor.FindTreasureRoomRelicCollection();

            if (collection == null || collection != _lastCollection)
            {
                _lastCollection = collection;
                _layoutApplied = false;
                return;
            }

            if (collection == null) return;

            // Phase 1: Expand _multiplayerHolders list (idempotent — bails out as a
            // no-op once mpHolders.Count >= currentRelics.Count). We call it every
            // tick instead of once, because on the first tick CurrentRelics may not
            // be populated yet (it's set by vanilla InitializeRelics which runs
            // after NTreasureRoomRelicCollection enters the tree). If we only ran
            // expansion once and it no-op'd due to null CurrentRelics, we'd be
            // stuck with only vanilla's 4 holders forever.
            ExpandHolders(collection);

            // Belt-and-braces: if OnNodeAdded's prewarm missed (e.g. RunManager.State
            // wasn't populated yet), run it again here. Still idempotent.
            _mod.PrewarmAllPlayerStates();

            // Phase 2: Wait for vanilla InitializeRelics() to populate _holdersInUse,
            // then bootstrap any extra holders vanilla missed (5+ players).
            var holdersInUse = _mod.GetHoldersInUse(collection);
            if (holdersInUse == null || holdersInUse.Count == 0) return;

            var sync = RunManager.Instance?.TreasureRoomRelicSynchronizer;
            var currentRelics = sync?.CurrentRelics;
            if (currentRelics == null) return;

            try
            {
                if (!_layoutApplied && holdersInUse.Count < currentRelics.Count)
                {
                    BootstrapExtraHolders(collection);
                }

                // Re-read after bootstrap — if it partially failed we must not advance.
                holdersInUse = _mod.GetHoldersInUse(collection);
                if (holdersInUse == null || holdersInUse.Count < currentRelics.Count)
                    return;

                // Phase 3: Apply grid layout
                if (!_layoutApplied)
                {
                    ApplyLayout(collection);
                    _layoutApplied = true;
                }
            }
            catch (Exception ex)
            {
                // Do NOT mark _layoutApplied — let the next 10-frame tick retry.
                Log.Warn($"[RMP:Treasure] Bootstrap/layout failed (will retry): {ex.Message}");
                return;
            }
        }

        private void ExpandHolders(NTreasureRoomRelicCollection collection)
        {
            var mpHolders = _mod.GetMultiplayerHolders(collection);
            if (mpHolders == null || mpHolders.Count == 0) return;

            var currentRelics = RunManager.Instance?.TreasureRoomRelicSynchronizer?.CurrentRelics;
            if (currentRelics == null || currentRelics.Count <= mpHolders.Count) return;

            var template = mpHolders[^1];
            string scenePath = template.SceneFilePath;
            PackedScene? scene = !string.IsNullOrEmpty(scenePath)
                ? PreloadManager.Cache.GetScene(scenePath) : null;
            Node parent = template.GetParent();

            for (int i = mpHolders.Count; i < currentRelics.Count; i++)
            {
                NTreasureRoomRelicHolder? newHolder = scene != null
                    ? scene.Instantiate<NTreasureRoomRelicHolder>()
                    : template.Duplicate() as NTreasureRoomRelicHolder;
                if (newHolder == null) continue;

                newHolder.Name = $"AutoHolder_{i + 1}";
                newHolder.Visible = false;
                parent.AddChild(newHolder);
                mpHolders.Add(newHolder);
            }
        }

        /// <summary>
        /// After vanilla InitializeRelics() has set up _holdersInUse (up to 4),
        /// ensure any extra holders (5+) are fully initialized, added to _holdersInUse,
        /// and connected with click handlers.
        /// </summary>
        private void BootstrapExtraHolders(NTreasureRoomRelicCollection collection)
        {
            var holdersInUse = _mod.GetHoldersInUse(collection);
            var mpHolders = _mod.GetMultiplayerHolders(collection);
            var runState = _mod.GetRunState(collection);
            var currentRelics = RunManager.Instance?.TreasureRoomRelicSynchronizer?.CurrentRelics;

            if (holdersInUse == null || mpHolders == null || runState == null || currentRelics == null) return;
            if (currentRelics.Count <= ModEntry.VanillaMultiplayerHolderCount) return;

            // Vanilla InitializeRelics only initializes up to _multiplayerHolders.Count (4) entries.
            // We may have added more to _multiplayerHolders in ExpandHolders, but vanilla
            // only iterated the original 4. Bootstrap any that are in _multiplayerHolders
            // but not yet in _holdersInUse.
            for (int i = holdersInUse.Count; i < mpHolders.Count && i < currentRelics.Count; i++)
            {
                var holder = mpHolders[i];
                try
                {
                    if (holder.Relic == null) continue;

                    holder.Visible = true;
                    holder.Relic.Model = currentRelics[i];
                    holder.Initialize(currentRelics[i], runState);
                    holder.Index = i;

                    // Capture i for lambda
                    int idx = i;
                    holder.Connect(NClickableControl.SignalName.Released,
                        Callable.From<NButton>(_ =>
                        {
                            var sync = RunManager.Instance?.TreasureRoomRelicSynchronizer;
                            if (sync?.CurrentRelics == null) return;
                            sync.PickRelicLocally(idx);
                        }));

                    holdersInUse.Add(holder);
                    holder.VoteContainer?.RefreshPlayerVotes();
                }
                catch (Exception ex)
                {
                    Log.Warn($"[RMP:Treasure] Failed to bootstrap holder {i}: {ex.Message}");
                }
            }

            RebuildHolderFocusNavigation(holdersInUse);
        }

        private void ApplyLayout(NTreasureRoomRelicCollection collection)
        {
            var holdersInUse = _mod.GetHoldersInUse(collection);
            if (holdersInUse == null || holdersInUse.Count <= ModEntry.VanillaMultiplayerHolderCount) return;

            // Compute bounds from the first 4 vanilla holders
            float minX = float.MaxValue, maxX = float.MinValue;
            float topY = float.MaxValue, bottomY = float.MinValue;
            for (int i = 0; i < ModEntry.VanillaMultiplayerHolderCount && i < holdersInUse.Count; i++)
            {
                var pos = holdersInUse[i].Position;
                minX = Math.Min(minX, pos.X); maxX = Math.Max(maxX, pos.X);
                topY = Math.Min(topY, pos.Y); bottomY = Math.Max(bottomY, pos.Y);
            }

            int count = holdersInUse.Count;
            // Always a 4-column grid (1-2-3-4 per row) so layouts from 5 to 16 share the same shape.
            int maxCols = Math.Max(1, Math.Min(ModEntry.VanillaMultiplayerHolderCount, count));
            int rowCount = (int)Math.Ceiling(count / (float)maxCols);
            float centerX = (minX + maxX) * 0.5f;
            float centerY = (topY + bottomY) * 0.5f;
            float xStep = (maxX - minX) / Math.Max(1, maxCols - 1);
            xStep = xStep > 0f ? Math.Max(MinXStep, xStep) : FallbackXStep;

            // Row spacing: vanilla's 4 holders share a Y, so bottomY-topY is 0 and MinYStep was
            // only tuned for 2 rows. Derive from actual holder height so 3-4 rows (9-16 players)
            // don't overlap, and clamp total height so the grid stays on-screen.
            float yStep = 0f;
            if (rowCount > 1)
            {
                float holderH = 0f;
                Vector2 sz = holdersInUse[0].GetCombinedMinimumSize();
                if (sz.Y > 0f) holderH = sz.Y;
                float preferred = holderH > 0f ? holderH * 1.05f : 0f;
                yStep = Math.Max(MinYStep, Math.Max(Math.Abs(bottomY - topY), preferred));
                const float MaxGridHeight = 640f;
                float totalH = yStep * (rowCount - 1);
                if (totalH > MaxGridHeight)
                    yStep = MaxGridHeight / (rowCount - 1);
            }

            int start = 0;
            for (int r = 0; r < rowCount; r++)
            {
                int cols = Math.Min(maxCols, count - start);
                float y = centerY + (r - (rowCount - 1) * 0.5f) * yStep;
                float startX = centerX - (cols - 1) * xStep * 0.5f;
                for (int c = 0; c < cols; c++)
                    holdersInUse[start + c].Position = new Vector2(startX + c * xStep, y);
                start += cols;
            }

            RebuildHolderFocusNavigation(holdersInUse);
        }

        private static void RebuildHolderFocusNavigation(List<NTreasureRoomRelicHolder> holdersInUse)
        {
            if (holdersInUse.Count == 0) return;

            for (int i = 0; i < holdersInUse.Count; i++)
            {
                NTreasureRoomRelicHolder holder = holdersInUse[i];
                holder.SetFocusMode(Control.FocusModeEnum.All);
                holder.FocusNeighborTop = holder.GetPath();
                holder.FocusNeighborBottom = holder.GetPath();
                holder.FocusNeighborLeft = i > 0
                    ? holdersInUse[i - 1].GetPath()
                    : holdersInUse[^1].GetPath();
                holder.FocusNeighborRight = i < holdersInUse.Count - 1
                    ? holdersInUse[i + 1].GetPath()
                    : holdersInUse[0].GetPath();
            }
        }
    }
}
