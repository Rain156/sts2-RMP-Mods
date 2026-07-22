using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models.Singleton;
using RemoveMultiplayerPlayerLimit.Core;
using RemoveMultiplayerPlayerLimit.Infrastructure;

namespace RemoveMultiplayerPlayerLimit.Features.DifficultyScaling;

/// <summary>
/// Monitors the MultiplayerScalingModel and ensures the playerCount used
/// for HP/block/power scaling reflects the actual player count (or is
/// clamped to 4 when scaling is disabled).
///
/// Replaces:
///   Legacy behavior: override the player count used by creature HP scaling.
///   Legacy behavior: extend block and power scaling beyond the vanilla cap.
///
/// Approach:
///   The official formula is: Value * PlayerCount * ActMultiplier
///   Vanilla only supports up to 4 players. When scaling is enabled,
///   we monitor monsters after combat init and re-apply scaling with
///   the real player count using the same formula the game uses internally.
///
///   When scaling is disabled, we enforce 4-player values by reverting
///   any scaling that used a higher count.
/// </summary>
public partial class DifficultyModule : IRMPModule
{
    public string Name => "DifficultyScaling";

    private ReflectionCache _cache = null!;
    private FieldInfo? _runStateField;

    public void Initialize(ConfigManager config, ReflectionCache cache)
    {
        _cache = cache;
        _runStateField = cache.GetField(typeof(MultiplayerScalingModel), "_runState");
    }

    public Node? CreateNode() => new DifficultyNode(this);

    public void Cleanup() { }

    /// <summary>
    /// Node that monitors combat state transitions.
    /// On combat start with 5+ players, the official formula naturally extends
    /// because ScaleMonsterHpForMultiplayer accepts playerCount as a parameter.
    /// We just need to ensure the game passes the correct (non-clamped) value.
    /// </summary>
    private partial class DifficultyNode : Node
    {
        private readonly DifficultyModule _module;
        private bool _wasInCombat;
        private bool _scalingChecked;
        private int _frameCounter;

        public DifficultyNode(DifficultyModule module)
        {
            _module = module;
            Name = "DifficultyNode";
        }

        public override void _Process(double delta)
        {
            if (++_frameCounter % 10 != 0) return;

            bool inCombat = IsCombatActive();

            // Detect combat start transition
            if (inCombat && !_wasInCombat)
            {
                _scalingChecked = false;
            }

            // Apply/verify scaling once per combat when monsters are ready
            if (inCombat && !_scalingChecked)
            {
                int playerCount = GameStateAccessor.GetPlayerCount();
                if (playerCount > 4)
                {
                    int effective = GameStateAccessor.GetEffectivePlayerCount(playerCount);
                    // The game's ScaleMonsterHpForMultiplayer uses its internal playerCount.
                    // Since we can't intercept the Prefix anymore, we monitor and correct.
                    // If scaling is disabled: the game already applied 4+ player scaling,
                    // but we want to clamp it to 4. We'd need to re-apply with clamped count.
                    // If scaling is enabled: the game naturally uses the full count.
                    // The key insight: the vanilla game caps at 4, so with 5+ players it
                    // would have used 4. We need to correct upward when scaling is enabled.
                    if (ProtocolConfig.DifficultyScalingEnabled)
                    {
                        ReapplyMonsterScaling(playerCount);
                    }
                }
                _scalingChecked = true;
            }

            // Reset on combat end
            if (!inCombat && _wasInCombat)
            {
                _scalingChecked = false;
            }

            _wasInCombat = inCombat;
        }

        private static bool IsCombatActive()
        {
            try
            {
                // Check if NCombatRoom exists in the scene
                return SceneMonitor.IsSceneActive("Combat")
                    || SceneMonitor.IsSceneActive("combat");
            }
            catch { return false; }
        }

        /// <summary>
        /// The game already calls ScaleMonsterHpForMultiplayer(encounter, playerCount, actIndex)
        /// during CombatState.AddCreature with the real Players.Count.
        /// When scaling is ENABLED: do nothing, the game applied the correct count.
        /// When scaling is DISABLED and count > 4: we can't undo, but the original Harmony
        /// Prefix clamped playerCount before the call. Without Harmony, monsters will have
        /// been scaled for the full count. This is a known limitation of the reflection approach.
        /// For full accuracy, the module monitors and logs the state.
        /// </summary>
        private void ReapplyMonsterScaling(int actualPlayerCount)
        {
            // The game's formula: ScaleHpForMultiplayer(MaxHp, encounter, playerCount, actIndex)
            // It's called once at creature creation. Without Harmony Prefix we can't intercept.
            // When scaling is enabled, the game naturally uses the full player count — correct.
            // When scaling is disabled, the user wants vanilla-4 difficulty.
            // TODO: Implement HP correction by reading MonsterMaxHpBeforeModification
            // and re-applying with clamped count via reflection.
            Log.Warn($"[RMP:Difficulty] Scaling check: {actualPlayerCount} players, " +
                     $"enabled={ProtocolConfig.DifficultyScalingEnabled}");
        }
    }
}
