using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using RemoveMultiplayerPlayerLimit.Core;
using RemoveMultiplayerPlayerLimit.Infrastructure;

namespace RemoveMultiplayerPlayerLimit.Network;

/// <summary>
/// RMP independent protocol layer — runs alongside the official protocol.
///
/// Design:
///   1. Official protocol channel: preserves vanilla packet logic
///   2. Mod protocol channel: custom INetMessage / INetAction for extensions
///   3. Both channels run in parallel without interference
///
/// Custom message types are auto-registered by the game's
/// ReflectionHelper.GetSubtypesInMods, no manual wire-up needed.
/// </summary>
public static class RmpProtocol
{
    public const int ProtocolVersion = 2;

    private static INetGameService? _netService;

    public static bool IsActive => _netService != null;

    public static void Bind(INetGameService netService)
    {
        Unbind();
        _netService = netService;
        netService.RegisterMessageHandler<RmpConfigSyncMessage>(HandleConfigSync);
        netService.RegisterMessageHandler<RmpLobbySnapshotMessage>(HandleLobbySnapshot);
        netService.RegisterMessageHandler<RmpExtendedReadyStateMessage>(HandleExtendedReadyState);
        netService.RegisterMessageHandler<RmpExtendedBeginRunMessage>(HandleExtendedBeginRun);
        Log.Info($"[RMP] Protocol v{ProtocolVersion} bound to {netService.Type} (NetId={netService.NetId})");
    }

    public static void Unbind()
    {
        if (_netService == null) return;
        try
        {
            _netService.UnregisterMessageHandler<RmpConfigSyncMessage>(HandleConfigSync);
            _netService.UnregisterMessageHandler<RmpLobbySnapshotMessage>(HandleLobbySnapshot);
            _netService.UnregisterMessageHandler<RmpExtendedReadyStateMessage>(HandleExtendedReadyState);
            _netService.UnregisterMessageHandler<RmpExtendedBeginRunMessage>(HandleExtendedBeginRun);
        }
        catch { /* Service may already be disposed */ }
        _netService = null;
    }

    /// <summary>
    /// Host broadcasts current mod config to all clients.
    /// Called on player join and config changes.
    /// </summary>
    public static void BroadcastConfig(int maxPlayerLimit)
    {
        if (_netService == null || _netService.Type != NetGameType.Host) return;
        _netService.SendMessage(new RmpConfigSyncMessage
        {
            ProtocolVersion = ProtocolVersion,
            MaxPlayerLimit = maxPlayerLimit
        });
    }

    public static void BroadcastLobbySnapshot(IReadOnlyList<MegaCrit.Sts2.Core.Entities.Multiplayer.LobbyPlayer> players)
    {
        if (_netService == null || _netService.Type != NetGameType.Host) return;

        _netService.SendMessage(new RmpLobbySnapshotMessage
        {
            players = players.Select(RmpLobbyPlayerState.FromLobbyPlayer).ToList()
        });
    }

    public static void BroadcastExtendedReady(bool ready)
    {
        if (_netService == null || _netService.Type != NetGameType.Host) return;

        _netService.SendMessage(new RmpExtendedReadyStateMessage
        {
            Ready = ready
        });
    }

    public static void BroadcastExtendedBeginRun(
        IReadOnlyList<MegaCrit.Sts2.Core.Entities.Multiplayer.LobbyPlayer> players,
        string seed,
        string act1,
        IReadOnlyList<ModifierModel> modifiers)
    {
        if (_netService == null || _netService.Type != NetGameType.Host) return;

        _netService.SendMessage(new RmpExtendedBeginRunMessage
        {
            players = players.Select(RmpLobbyPlayerState.FromLobbyPlayer).ToList(),
            seed = seed,
            act1 = act1,
            modifiers = modifiers.Select(modifier => modifier.ToSerializable()).ToList()
        });
    }

    private static void HandleConfigSync(RmpConfigSyncMessage message, ulong senderId)
    {
        if (message.ProtocolVersion != ProtocolVersion)
            Log.Warn($"[RMP] Protocol version mismatch: local={ProtocolVersion}, remote={message.ProtocolVersion} from {senderId}");
        // v0.1.7: player limit is fixed at 16; incoming MaxPlayerLimit is informational only.
        Log.Info($"[RMP] Config sync from {senderId}: v{message.ProtocolVersion}, maxPlayers={message.MaxPlayerLimit} (local fixed at {ProtocolConfig.MaxPlayerLimit})");
    }

    private static void HandleLobbySnapshot(RmpLobbySnapshotMessage message, ulong senderId)
    {
        if (message.players == null)
            return;

        StartRunLobby? lobby = SceneMonitor.FindActiveStartRunLobby();
        if (lobby == null)
            return;

        ApplyLobbySnapshot(lobby, message.players);
    }

    private static void HandleExtendedReadyState(RmpExtendedReadyStateMessage message, ulong senderId)
    {
        StartRunLobby? lobby = SceneMonitor.FindActiveStartRunLobby();
        if (lobby == null || !ExtendedLobbyModule.ShouldUseExtendedLobbyProtocol(lobby))
            return;

        if (ExtendedLobbyModule.TrySetPlayerReadyState(lobby, senderId, message.Ready, out var updatedPlayer))
        {
            ExtendedLobbyModule.NotifyPlayerChanged(lobby, updatedPlayer, false);
        }

        if (lobby.NetService.Type == NetGameType.Host)
            ExtendedLobbyModule.TryBeginExtendedRun(lobby);
    }

    private static void HandleExtendedBeginRun(RmpExtendedBeginRunMessage message, ulong senderId)
    {
        if (message.players == null)
            return;

        StartRunLobby? lobby = SceneMonitor.FindActiveStartRunLobby();
        if (lobby == null)
            return;

        ApplyLobbySnapshot(lobby, message.players);

        List<ModifierModel> modifiers = message.modifiers.Select(ModifierModel.FromSerializable).ToList();
        List<ActModel> acts = ExtendedLobbyModule.BuildActsForBeginRun(
            message.seed,
            message.act1,
            lobby,
            message.players.Select(player => player.ToLobbyPlayer()).ToList());

        lobby.LobbyListener.BeginRun(message.seed, acts, modifiers);
    }

    private static void ApplyLobbySnapshot(StartRunLobby lobby, IReadOnlyList<RmpLobbyPlayerState> snapshotPlayers)
    {
        var currentPlayers = lobby.Players.ToDictionary(player => player.id);
        var snapshot = snapshotPlayers.Select(player => player.ToLobbyPlayer()).ToList();
        var snapshotIds = snapshot.Select(player => player.id).ToHashSet();

        for (int i = lobby.Players.Count - 1; i >= 0; i--)
        {
            var existing = lobby.Players[i];
            if (!snapshotIds.Contains(existing.id))
            {
                lobby.Players.RemoveAt(i);
                if (existing.id != lobby.NetService.NetId)
                    lobby.LobbyListener.RemotePlayerDisconnected(existing);
            }
        }

        foreach (var snapshotPlayer in snapshot)
        {
            if (!currentPlayers.TryGetValue(snapshotPlayer.id, out var existing))
            {
                lobby.Players.Add(snapshotPlayer);
                if (snapshotPlayer.id != lobby.NetService.NetId)
                    lobby.LobbyListener.PlayerConnected(snapshotPlayer);
                continue;
            }

            if (!LobbyPlayersEqual(existing, snapshotPlayer))
            {
                int idx = lobby.Players.FindIndex(player => player.id == snapshotPlayer.id);
                if (idx >= 0)
                    lobby.Players[idx] = snapshotPlayer;
                ExtendedLobbyModule.NotifyPlayerChanged(lobby, snapshotPlayer, false);
            }
        }

        NGame.Instance?.RemoteCursorContainer.Initialize(lobby.InputSynchronizer, lobby.Players.Select(player => player.id));
    }

    private static bool LobbyPlayersEqual(
        MegaCrit.Sts2.Core.Entities.Multiplayer.LobbyPlayer a,
        MegaCrit.Sts2.Core.Entities.Multiplayer.LobbyPlayer b)
    {
        return a.id == b.id
            && a.slotId == b.slotId
            && a.character == b.character
            && a.maxMultiplayerAscensionUnlocked == b.maxMultiplayerAscensionUnlocked
            && a.isReady == b.isReady;
    }
}
