using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace RemoveMultiplayerPlayerLimit.Network;

/// <summary>
/// RMP 配置同步消息 — 模组协议通道的自定义消息。
///
/// 由 Host 向所有客户端广播，携带当前 mod 配置。
/// 通过游戏的 ReflectionHelper.GetSubtypesInMods&lt;INetMessage&gt;() 自动注册，
/// MessageTypes 分配类型ID，NetMessageBus 处理序列化/分发。
///
/// 数据包格式:
///   [8 bits] ProtocolVersion  — 协议版本，用于兼容性检查
///   [8 bits] MaxPlayerLimit   — 最大玩家人数上限 (4-16)
/// </summary>
public struct RmpConfigSyncMessage : INetMessage, IPacketSerializable
{
	public int ProtocolVersion;
	public int MaxPlayerLimit;

	public readonly bool ShouldBroadcast => true;
	public readonly NetTransferMode Mode => NetTransferMode.Reliable;
	public readonly LogLevel LogLevel => LogLevel.Info;

	public readonly void Serialize(PacketWriter writer)
	{
		writer.WriteInt(ProtocolVersion, 8);
		writer.WriteInt(MaxPlayerLimit, 8);
	}

	public void Deserialize(PacketReader reader)
	{
		ProtocolVersion = reader.ReadInt(8);
		MaxPlayerLimit = reader.ReadInt(8);
	}

	public override readonly string ToString()
	{
		return $"RmpConfigSync(v{ProtocolVersion}, maxPlayers={MaxPlayerLimit})";
	}
}
