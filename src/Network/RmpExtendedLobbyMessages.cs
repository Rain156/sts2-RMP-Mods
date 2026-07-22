using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace RemoveMultiplayerPlayerLimit.Network;

public struct RmpLobbyPlayerState : IPacketSerializable
{
    public ulong id;
    public int slotId;
    public CharacterModel character;
    public SerializableUnlockState unlockState;
    public int maxMultiplayerAscensionUnlocked;
    public bool isReady;

    public readonly LobbyPlayer ToLobbyPlayer()
    {
        return new LobbyPlayer
        {
            id = id,
            slotId = slotId,
            character = character,
            unlockState = unlockState,
            maxMultiplayerAscensionUnlocked = maxMultiplayerAscensionUnlocked,
            isReady = isReady
        };
    }

    public static RmpLobbyPlayerState FromLobbyPlayer(LobbyPlayer lobbyPlayer)
    {
        return new RmpLobbyPlayerState
        {
            id = lobbyPlayer.id,
            slotId = lobbyPlayer.slotId,
            character = lobbyPlayer.character,
            unlockState = lobbyPlayer.unlockState,
            maxMultiplayerAscensionUnlocked = lobbyPlayer.maxMultiplayerAscensionUnlocked,
            isReady = lobbyPlayer.isReady
        };
    }

    public readonly void Serialize(PacketWriter writer)
    {
        writer.WriteULong(id);
        writer.WriteInt(slotId, 4);
        writer.WriteModel(character);
        writer.Write(unlockState);
        writer.WriteInt(maxMultiplayerAscensionUnlocked);
        writer.WriteBool(isReady);
    }

    public void Deserialize(PacketReader reader)
    {
        id = reader.ReadULong();
        slotId = reader.ReadInt(4);
        character = reader.ReadModel<CharacterModel>();
        unlockState = reader.Read<SerializableUnlockState>();
        maxMultiplayerAscensionUnlocked = reader.ReadInt();
        isReady = reader.ReadBool();
    }
}

public struct RmpLobbySnapshotMessage : INetMessage, IPacketSerializable
{
    public List<RmpLobbyPlayerState>? players;

    public readonly bool ShouldBroadcast => false;
    public readonly bool ShouldBuffer => false;
    public readonly NetTransferMode Mode => NetTransferMode.Reliable;
    public readonly LogLevel LogLevel => LogLevel.Info;

    public readonly void Serialize(PacketWriter writer)
    {
        if (players == null)
            throw new InvalidOperationException("players must not be null");

        writer.WriteList(players, RemoveMultiplayerPlayerLimit.Core.ProtocolConfig.LobbyListLengthBits);
    }

    public void Deserialize(PacketReader reader)
    {
        players = reader.ReadList<RmpLobbyPlayerState>(RemoveMultiplayerPlayerLimit.Core.ProtocolConfig.LobbyListLengthBits);
    }

    public override readonly string ToString() => $"RmpLobbySnapshot(players={players?.Count ?? 0})";
}

public struct RmpExtendedReadyStateMessage : INetMessage, IPacketSerializable
{
    public bool Ready;

    public readonly bool ShouldBroadcast => true;
    public readonly bool ShouldBuffer => false;
    public readonly NetTransferMode Mode => NetTransferMode.Reliable;
    public readonly LogLevel LogLevel => LogLevel.Debug;

    public readonly void Serialize(PacketWriter writer)
    {
        writer.WriteBool(Ready);
    }

    public void Deserialize(PacketReader reader)
    {
        Ready = reader.ReadBool();
    }

    public override readonly string ToString() => $"RmpExtendedReady(ready={Ready})";
}

public struct RmpExtendedBeginRunMessage : INetMessage, IPacketSerializable
{
    public List<RmpLobbyPlayerState>? players;
    public string seed;
    public string act1;
    public List<SerializableModifier> modifiers;

    public readonly bool ShouldBroadcast => false;
    public readonly bool ShouldBuffer => false;
    public readonly NetTransferMode Mode => NetTransferMode.Reliable;
    public readonly LogLevel LogLevel => LogLevel.Info;

    public readonly void Serialize(PacketWriter writer)
    {
        if (players == null)
            throw new InvalidOperationException("players must not be null");

        writer.WriteList(players, RemoveMultiplayerPlayerLimit.Core.ProtocolConfig.LobbyListLengthBits);
        writer.WriteString(seed);
        writer.WriteString(act1);
        writer.WriteList(modifiers);
    }

    public void Deserialize(PacketReader reader)
    {
        players = reader.ReadList<RmpLobbyPlayerState>(RemoveMultiplayerPlayerLimit.Core.ProtocolConfig.LobbyListLengthBits);
        seed = reader.ReadString();
        act1 = reader.ReadString();
        modifiers = reader.ReadList<SerializableModifier>();
    }

    public override readonly string ToString() => $"RmpExtendedBeginRun(players={players?.Count ?? 0}, seed={seed})";
}
