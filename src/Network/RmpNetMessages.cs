using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace RemoveMultiplayerPlayerLimit.Network;

/// <summary>
/// RMP config sync message — custom mod protocol message.
///
/// Broadcast by Host to all clients carrying current mod config.
/// Auto-registered via ReflectionHelper.GetSubtypesInMods&lt;INetMessage&gt;().
///
/// Packet format:
///   [8 bits] ProtocolVersion
///   [8 bits] MaxPlayerLimit (4-16)
/// </summary>
public struct RmpConfigSyncMessage : INetMessage, IPacketSerializable
{
    public int ProtocolVersion;
    public int MaxPlayerLimit;

    public readonly bool ShouldBroadcast => true;
    public readonly bool ShouldBuffer => false;
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
        => $"RmpConfigSync(v{ProtocolVersion}, maxPlayers={MaxPlayerLimit})";
}
