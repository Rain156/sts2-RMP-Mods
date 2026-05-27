using System;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Transport.Steam;
using Steamworks;

namespace RemoveMultiplayerPlayerLimit.Network;

/// <summary>
/// Steam lobby helper — calls Steamworks.NET APIs directly to update
/// the Steam lobby member limit after lobby creation.
///
/// Path: NetHostGameService → NetHost (cast to SteamHost) → LobbyId
///       → SteamMatchmaking.SetLobbyMemberLimit(lobbyId, limit)
/// </summary>
internal static class SteamLobbyHelper
{
    internal static bool TryUpdateMemberLimit(INetGameService netService, int limit)
    {
        try
        {
            if (netService is not NetHostGameService hostService) return false;

            if (hostService.NetHost is not SteamHost steamHost) return false;

            CSteamID? lobbyId = steamHost.LobbyId;
            if (!lobbyId.HasValue) return false;

            bool result = SteamMatchmaking.SetLobbyMemberLimit(lobbyId.Value, limit);
            if (result)
            {
                Log.Info($"[RMP] Steam lobby member limit set to {limit} (lobby={lobbyId.Value.m_SteamID})");
            }
            else
            {
                Log.Warn($"[RMP] SteamMatchmaking.SetLobbyMemberLimit({limit}) returned false");
            }
            return result;
        }
        catch (Exception ex)
        {
            Log.Warn($"[RMP] Failed to update Steam lobby limit: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gets the current Steam lobby member limit, or -1 if unavailable.
    /// </summary>
    internal static int GetCurrentMemberLimit(INetGameService netService)
    {
        try
        {
            if (netService is not NetHostGameService hostService) return -1;
            if (hostService.NetHost is not SteamHost steamHost) return -1;
            CSteamID? lobbyId = steamHost.LobbyId;
            if (!lobbyId.HasValue) return -1;
            return SteamMatchmaking.GetLobbyMemberLimit(lobbyId.Value);
        }
        catch { return -1; }
    }
}
