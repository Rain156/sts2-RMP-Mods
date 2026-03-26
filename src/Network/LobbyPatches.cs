using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Runs;

namespace RemoveMultiplayerPlayerLimit.Network;

// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  大厅容量补丁                                                           ║
// ║                                                                        ║
// ║  修改 Host 创建的 ENet/Steam 服务器容量上限，                             ║
// ║  并在玩家连接时动态同步 StartRunLobby.MaxPlayers，                        ║
// ║  同时通过模组协议通道广播配置给所有客户端。                                 ║
// ╚══════════════════════════════════════════════════════════════════════════╝

[HarmonyPatch(typeof(NetHostGameService), nameof(NetHostGameService.StartENetHost))]
internal static class StartENetHostPatch
{
	private static void Prefix(ref int maxClients)
		=> maxClients = Math.Max(maxClients, ProtocolConfig.TargetPlayerLimit);
}

[HarmonyPatch(typeof(NetHostGameService), nameof(NetHostGameService.StartSteamHost))]
internal static class StartSteamHostPatch
{
	private static void Prefix(ref int maxClients)
		=> maxClients = Math.Max(maxClients, ProtocolConfig.TargetPlayerLimit);
}

[HarmonyPatch(typeof(StartRunLobby), MethodType.Constructor,
	typeof(GameMode), typeof(INetGameService), typeof(IStartRunLobbyListener), typeof(int))]
internal static class StartRunLobbyConstructorPatch
{
	private static void Postfix(StartRunLobby __instance, INetGameService netService)
	{
		if (netService.Type == NetGameType.Host
			&& __instance.MaxPlayers < ProtocolConfig.TargetPlayerLimit
			&& LobbySync.MaxPlayersField != null)
		{
			LobbySync.MaxPlayersField.SetValue(__instance, ProtocolConfig.TargetPlayerLimit);
		}
		// 绑定模组协议通道到当前多人会话
		if (netService.Type is NetGameType.Host or NetGameType.Client)
		{
			RmpProtocol.Bind(netService);
		}
	}
}

// ── 动态同步 MaxPlayers：当玩家尝试加入时，确保 MaxPlayers 与当前设置一致 ──

[HarmonyPatch(typeof(StartRunLobby), "OnConnectedToClientAsHost")]
internal static class OnConnectedToClientAsHostPatch
{
	private static void Prefix(StartRunLobby __instance) => LobbySync.SyncLobbyMaxPlayers(__instance);
}

[HarmonyPatch(typeof(StartRunLobby), "HandleClientLobbyJoinRequestMessage")]
internal static class HandleClientLobbyJoinRequestMessagePatch
{
	private static void Prefix(StartRunLobby __instance) => LobbySync.SyncLobbyMaxPlayers(__instance);
}

/// <summary>
/// MaxPlayers 同步逻辑 — 对外提供 <see cref="MaxPlayersField"/> 和 <see cref="SyncLobbyMaxPlayers"/>。
/// </summary>
internal static class LobbySync
{
	internal static readonly FieldInfo? MaxPlayersField =
		AccessTools.Field(typeof(StartRunLobby), "<MaxPlayers>k__BackingField");

	internal static void SyncLobbyMaxPlayers(StartRunLobby lobby)
	{
		if (MaxPlayersField == null || lobby.NetService.Type != NetGameType.Host)
		{
			return;
		}
		if (lobby.MaxPlayers != ProtocolConfig.TargetPlayerLimit)
		{
			MaxPlayersField.SetValue(lobby, ProtocolConfig.TargetPlayerLimit);
			SteamLobbyHelper.TryUpdateMemberLimit(lobby.NetService, ProtocolConfig.TargetPlayerLimit);
		}
		// 通过模组协议通道广播配置给所有客户端
		RmpProtocol.BroadcastConfig(ProtocolConfig.TargetPlayerLimit);
	}
}
