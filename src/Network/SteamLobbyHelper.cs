using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace RemoveMultiplayerPlayerLimit.Network;

/// <summary>
/// Steam 大厅反射工具 — 通过反射调用 Steamworks.NET API，
/// 避免模组直接依赖 Steamworks 程序集。
/// </summary>
internal static class SteamLobbyHelper
{
	/// <summary>
	/// 尝试更新 Steam 大厅的成员上限。
	/// 通过反射链：NetHostGameService → SteamHost.LobbyId → SteamMatchmaking.SetLobbyMemberLimit。
	/// 仅在 Host 端有效，失败时静默记录警告。
	/// </summary>
	internal static void TryUpdateMemberLimit(INetGameService netService, int limit)
	{
		try
		{
			if (netService is not NetHostGameService hostService)
			{
				return;
			}
			object? netHost = hostService.NetHost;
			if (netHost == null)
			{
				return;
			}
			// SteamHost.LobbyId → CSteamID?（Steamworks.NET 类型，通过反射避免直接依赖）
			PropertyInfo? lobbyIdProp = AccessTools.Property(netHost.GetType(), "LobbyId");
			object? lobbyIdObj = lobbyIdProp?.GetValue(netHost);
			if (lobbyIdObj == null)
			{
				return;
			}
			// SteamMatchmaking.SetLobbyMemberLimit(CSteamID lobbyId, int maxMembers)
			Type? steamMatchmakingType = lobbyIdObj.GetType().Assembly.GetType("Steamworks.SteamMatchmaking");
			MethodInfo? setLimitMethod = steamMatchmakingType?.GetMethod("SetLobbyMemberLimit");
			setLimitMethod?.Invoke(null, new object[] { lobbyIdObj, limit });
		}
		catch (Exception ex)
		{
			Log.Warn($"Failed to update Steam lobby member limit: {ex.Message}");
		}
	}
}
