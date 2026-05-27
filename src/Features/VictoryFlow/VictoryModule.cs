using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using RemoveMultiplayerPlayerLimit.Core;
using RemoveMultiplayerPlayerLimit.Infrastructure;

namespace RemoveMultiplayerPlayerLimit.Features.VictoryFlow;

/// <summary>
/// Safety net for multiplayer victory flow.
/// If the victory room finishes and the game-over summary never appears,
/// force-open it after a short grace period instead of leaving the player stuck.
/// </summary>
public partial class VictoryModule : IRMPModule
{
    private const int CheckIntervalFrames = 15;
    private const int GracePeriodTicks = 12; // ~3 seconds at 60 FPS.

    public string Name => "VictoryFlow";

    public void Initialize(ConfigManager config, ReflectionCache cache)
    {
    }

    public Node? CreateNode() => new VictoryNode();

    public void Cleanup()
    {
    }

    private sealed partial class VictoryNode : Node
    {
        private ulong _lastRunNodeId;
        private int _frameCounter;
        private int _missingGameOverTicks;
        private bool _summaryForcedForCurrentRun;

        public VictoryNode()
        {
            Name = "VictoryNode";
        }

        public override void _Process(double delta)
        {
            if (++_frameCounter % CheckIntervalFrames != 0)
                return;

            NRun? runNode = NRun.Instance;
            if (runNode == null)
            {
                ResetState();
                return;
            }

            ulong runNodeId = runNode.GetInstanceId();
            if (runNodeId != _lastRunNodeId)
            {
                _lastRunNodeId = runNodeId;
                _missingGameOverTicks = 0;
                _summaryForcedForCurrentRun = false;
            }

            if (_summaryForcedForCurrentRun)
                return;

            RunState? runState = GameStateAccessor.GetRunState();
            if (runState?.CurrentRoom?.IsVictoryRoom != true)
            {
                _missingGameOverTicks = 0;
                return;
            }

            if (NOverlayStack.Instance?.Peek() is NGameOverScreen)
            {
                _missingGameOverTicks = 0;
                return;
            }

            bool areAllPlayersDead = runState.Players.Count > 0 && runState.Players.All(player => player.Creature.IsDead);
            if (!areAllPlayersDead)
            {
                _missingGameOverTicks = 0;
                return;
            }

            _missingGameOverTicks++;
            if (_missingGameOverTicks < GracePeriodTicks)
                return;

            Log.Warn("[RMP:Victory] Victory summary did not appear in time. Forcing GameOverScreen.");
            runNode.ShowGameOverScreen(RunManager.Instance.ToSave(preFinishedRoom: null));
            _summaryForcedForCurrentRun = true;
            _missingGameOverTicks = 0;
        }

        private void ResetState()
        {
            _lastRunNodeId = 0;
            _missingGameOverTicks = 0;
            _summaryForcedForCurrentRun = false;
        }
    }
}
