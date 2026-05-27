using System;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using RemoveMultiplayerPlayerLimit.Core;
using RemoveMultiplayerPlayerLimit.Infrastructure;

namespace RemoveMultiplayerPlayerLimit.Network;

/// <summary>
/// Central coordinator for multiplayer lobbies.
/// Keeps StartRunLobby synchronized, binds the RMP protocol, and emits
/// one-time diagnostics when a loaded-run lobby becomes active.
/// </summary>
public partial class LobbyManagerModule : IRMPModule
{
    public string Name => "LobbyManager";

    private FieldInfo? _maxPlayersField;

    public void Initialize(ConfigManager config, ReflectionCache cache)
    {
        _maxPlayersField = cache.GetField(typeof(StartRunLobby), "<MaxPlayers>k__BackingField");
    }

    public Node? CreateNode() => new LobbyManagerNode(this);

    public void Cleanup()
    {
        RmpProtocol.Unbind();
    }

    private partial class LobbyManagerNode : Node
    {
        private readonly LobbyManagerModule _module;
        private int _frameCounter;
        private StartRunLobby? _lastLobby;
        private LoadRunLobby? _lastLoggedLoadLobby;
        private int _lastPlayerCount = -1;
        private int _lastTargetPlayerLimit = -1;

        public LobbyManagerNode(LobbyManagerModule module)
        {
            _module = module;
            Name = "LobbyManagerNode";
        }

        public override void _Process(double delta)
        {
            if (++_frameCounter % 15 != 0)
                return;

            HandleStartRunLobby(SceneMonitor.FindActiveStartRunLobby());
            HandleLoadedRunLobby(SceneMonitor.FindActiveLoadRunLobby());
        }

        private void HandleStartRunLobby(StartRunLobby? lobby)
        {
            if (!ReferenceEquals(lobby, _lastLobby))
            {
                if (_lastLobby != null && lobby == null)
                {
                    // IMPORTANT: Do NOT call StartRunLobby.CleanUp(false) here.
                    //
                    // Vanilla's NCharacterSelectScreen.StartNewMultiplayerRun runs:
                    //   SetUpNewMultiPlayer(runState, startRunLobby)     // creates RunLobby
                    //   await StartRun(runState)
                    //     ├── SetCurrentScene(NRun.Create(runState))     // NCharacterSelectScreen
                    //     │                                              // leaves scene tree
                    //     │                                              // ← we detect lobby == null HERE
                    //     └── await EnterAct(0)                           // still running!
                    //         └── await Hook.BeforeRoomEntered(...)      // hangs if we've already
                    //                                                    //  unsubscribed StartRunLobby's
                    //                                                    //  NetService message handlers
                    //   CleanUpLobby(disconnectSession: false)            // vanilla CleanUp
                    //
                    // A previous iteration called StartRunLobby.CleanUp(false) in this transition
                    // to swallow a post-disconnect ObjectDisposedException, but that tore down
                    // message handlers the vanilla async chain in EnterAct still relied on,
                    // black-screening the host with any player count.  We accept the cosmetic
                    // ObjectDisposedException instead — it's logged but non-fatal on disconnect
                    // — and let vanilla do its own CleanUp in the proper order.
                    RmpProtocol.Unbind();
                }

                _lastLobby = lobby;
                _lastPlayerCount = -1;
                _lastTargetPlayerLimit = -1;

                if (lobby != null)
                    OnLobbyActivated(lobby);
            }

            if (lobby == null)
                return;

            int currentCount = GetLobbyPlayerCount(lobby);
            int targetLimit = ProtocolConfig.TargetPlayerLimit;
            if (currentCount != _lastPlayerCount || targetLimit != _lastTargetPlayerLimit)
            {
                SyncLobbyState(lobby, targetLimit);
                _lastPlayerCount = currentCount;
                _lastTargetPlayerLimit = targetLimit;
            }
        }

        private void OnLobbyActivated(StartRunLobby lobby)
        {
            if (lobby.NetService?.Type is NetGameType.Host or NetGameType.Client)
                RmpProtocol.Bind(lobby.NetService);

            SyncLobbyState(lobby, ProtocolConfig.TargetPlayerLimit);
        }

        private void SyncLobbyState(StartRunLobby lobby, int targetLimit)
        {
            if (_module._maxPlayersField != null && lobby.MaxPlayers != targetLimit)
            {
                _module._maxPlayersField.SetValue(lobby, targetLimit);
                Log.Info($"[RMP] StartRunLobby.MaxPlayers synchronized to {targetLimit}");
            }

            if (lobby.NetService?.Type != NetGameType.Host)
                return;

            int steamLimit = SteamLobbyHelper.GetCurrentMemberLimit(lobby.NetService);
            if (steamLimit != -1 && steamLimit != targetLimit)
                SteamLobbyHelper.TryUpdateMemberLimit(lobby.NetService, targetLimit);
            else if (steamLimit == -1)
                SteamLobbyHelper.TryUpdateMemberLimit(lobby.NetService, targetLimit);

            RmpProtocol.BroadcastConfig(targetLimit);
            if (ExtendedLobbyModule.ShouldUseExtendedLobbyProtocol(lobby))
                RmpProtocol.BroadcastLobbySnapshot(lobby.Players);
        }

        private void HandleLoadedRunLobby(LoadRunLobby? loadRunLobby)
        {
            if (loadRunLobby == null)
            {
                _lastLoggedLoadLobby = null;
                return;
            }

            if (ReferenceEquals(loadRunLobby, _lastLoggedLoadLobby))
                return;

            _lastLoggedLoadLobby = loadRunLobby;

            int savePlayerCount = loadRunLobby.Run.Players.Count;
            int connectedPlayerCount = loadRunLobby.ConnectedPlayerIds.Count;
            int hostCapacity = HostBootstrapModule.TryGetTrackedHostCapacity(loadRunLobby.NetService, out int trackedCapacity)
                ? trackedCapacity
                : savePlayerCount;
            string screenName = SceneMonitor.GetActiveLoadLobbyScreenName() ?? "UnknownLoadLobbyScreen";

            Log.Info(
                $"[RMP] Loaded-run lobby active: screen={screenName}, transport={HostBootstrapModule.GetTransportName(loadRunLobby.NetService)}, " +
                $"hostCapacity={hostCapacity}, savePlayers={savePlayerCount}, connectedPlayers={connectedPlayerCount}");
        }

        private static int GetLobbyPlayerCount(StartRunLobby lobby)
        {
            try
            {
                return lobby.Players?.Count ?? 0;
            }
            catch
            {
                return 0;
            }
        }

    }
}
