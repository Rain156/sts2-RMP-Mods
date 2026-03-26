using System;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace RemoveMultiplayerPlayerLimit.Network;

/// <summary>
/// RMP 模组独立协议层 — 与官方协议并行运行（协议并发架构）。
///
/// 设计原则：
///   1. 官方协议通道：保留官方的发包逻辑，仅对核心位宽做必要扩展（SlotId/LobbyList）
///   2. 模组协议通道：通过自定义 INetMessage / INetAction 独立发包拓展
///   3. 两条通道并行，互不干扰
///
/// 自定义消息类型通过游戏的 ReflectionHelper.GetSubtypesInMods 自动注册，
/// 自定义动作类型通过 ActionTypes 自动注册，无需手动 wire-up。
///
/// 此类负责：
///   - 协议版本声明与兼容性检查
///   - 自定义消息处理器的生命周期管理（注册/注销）
///   - 配置同步广播
/// </summary>
public static class RmpProtocol
{
	/// <summary>协议版本号。所有对端必须一致。</summary>
	public const int ProtocolVersion = 1;

	private static INetGameService? _netService;

	/// <summary>协议是否已绑定到活跃的多人会话。</summary>
	public static bool IsActive => _netService != null;

	/// <summary>
	/// 绑定到一个多人会话的网络服务。在 StartRunLobby 创建时调用。
	/// 自动注销之前绑定的会话（如有）。
	/// </summary>
	public static void Bind(INetGameService netService)
	{
		Unbind();
		_netService = netService;
		netService.RegisterMessageHandler<RmpConfigSyncMessage>(HandleConfigSync);
		Log.Info($"RMP protocol v{ProtocolVersion} bound to {netService.Type} (NetId={netService.NetId})");
	}

	/// <summary>
	/// 解除协议绑定。在多人会话结束时调用，或在 Bind 新会话前自动调用。
	/// </summary>
	public static void Unbind()
	{
		if (_netService == null)
		{
			return;
		}
		try
		{
			_netService.UnregisterMessageHandler<RmpConfigSyncMessage>(HandleConfigSync);
		}
		catch (Exception)
		{
			// 服务可能已 disposed，忽略清理异常
		}
		_netService = null;
	}

	/// <summary>
	/// Host 向所有客户端广播当前 mod 配置。
	/// 在玩家加入大厅、配置变更时调用。
	/// </summary>
	public static void BroadcastConfig(int maxPlayerLimit)
	{
		if (_netService == null || _netService.Type != NetGameType.Host)
		{
			return;
		}
		_netService.SendMessage(new RmpConfigSyncMessage
		{
			ProtocolVersion = ProtocolVersion,
			MaxPlayerLimit = maxPlayerLimit
		});
	}

	private static void HandleConfigSync(RmpConfigSyncMessage message, ulong senderId)
	{
		if (message.ProtocolVersion != ProtocolVersion)
		{
			Log.Warn($"RMP protocol version mismatch: local={ProtocolVersion}, remote={message.ProtocolVersion} from peer {senderId}");
		}
		Log.Info($"RMP config sync received from {senderId}: protocol=v{message.ProtocolVersion}, maxPlayers={message.MaxPlayerLimit}");
	}
}
