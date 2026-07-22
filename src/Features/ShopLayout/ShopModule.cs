using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using RemoveMultiplayerPlayerLimit.Core;
using RemoveMultiplayerPlayerLimit.Infrastructure;

namespace RemoveMultiplayerPlayerLimit.Features.ShopLayout;

/// <summary>
/// Monitors the merchant room and rearranges player visuals into a grid
/// when more than 4 players are present.
///
/// Replaces:
///   Legacy behavior: run after NMerchantRoom finishes loading.
///
/// Approach:
///   Polls for NMerchantRoom in the SceneTree. When found with 5+ player
///   visuals, repositions them into a row×column grid layout.
/// </summary>
public partial class ShopModule : IRMPModule
{
    public string Name => "ShopLayout";

    private const float ForwardShiftX = 160f;
    private const float ForwardShiftY = 35f;
    private const float RowStartOffsetX = -110f;
    private const float RowStepY = -40f;
    private const float ColumnStepX = -230f;

    private ReflectionCache _cache = null!;

    public void Initialize(ConfigManager config, ReflectionCache cache)
    {
        _cache = cache;
    }

    public Node? CreateNode() => new ShopNode(this);

    public void Cleanup() { }

    private partial class ShopNode : Node
    {
        private readonly ShopModule _module;
        private int _frameCounter;
        private bool _arranged;
        private NMerchantRoom? _lastRoom;

        public ShopNode(ShopModule module)
        {
            _module = module;
            Name = "ShopNode";
        }

        public override void _Process(double delta)
        {
            if (++_frameCounter % 10 != 0) return;

            var room = SceneMonitor.FindMerchantRoom();

            if (room == null || room != _lastRoom)
            {
                _arranged = false;
                _lastRoom = room;
                return;
            }

            if (_arranged) return;

            try
            {
                RepositionVisuals(room);
                _arranged = true;
            }
            catch (Exception ex)
            {
                Log.Warn($"[RMP:Shop] Failed to reposition visuals: {ex}");
                _arranged = true;
            }
        }

        private void RepositionVisuals(NMerchantRoom room)
        {
            IReadOnlyList<NMerchantCharacter> visuals = room.PlayerVisuals;
            if (visuals.Count <= ModEntry.VanillaMultiplayerHolderCount) return;

            int rowCount = visuals.Count <= ModEntry.VanillaMultiplayerHolderCount * 2
                ? 2
                : Mathf.CeilToInt((float)visuals.Count / ModEntry.VanillaMultiplayerHolderCount);
            int colCount = Mathf.CeilToInt((float)visuals.Count / rowCount);

            int idx = 0;
            for (int row = 0; row < rowCount; row++)
            {
                float x = ForwardShiftX + RowStartOffsetX * row;
                float y = ForwardShiftY + RowStepY * row;
                for (int col = 0; col < colCount && idx < visuals.Count; col++)
                {
                    visuals[idx].Position = new Vector2(x, y);
                    x += ColumnStepX;
                    idx++;
                }
            }
        }
    }
}
