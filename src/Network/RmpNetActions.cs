using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;

namespace RemoveMultiplayerPlayerLimit.Network;

/// <summary>
/// Relic skip GameAction — independent mod action type.
///
/// Replaces the old hack of sending PickRelicAction(player, -1) which
/// truncated -1 to 255 in the 8-bit serialization field.
///
/// Network flow:
///   Sender:   RmpSkipRelicGameAction.ToNetAction() → RmpSkipRelicNetAction
///   Wire:     ActionTypes encodes type ID + Serialize (empty payload)
///   Receiver: RmpSkipRelicNetAction.ToGameAction(player) → RmpSkipRelicGameAction
///   Execute:  RmpSkipRelicGameAction.ExecuteAction() → OnPicked(player, -1)
/// </summary>
public class RmpSkipRelicGameAction : GameAction
{
    private readonly Player _player;

    public override ulong OwnerId => _player.NetId;
    public override GameActionType ActionType => GameActionType.NonCombat;

    public RmpSkipRelicGameAction(Player player)
    {
        _player = player;
    }

    protected override Task ExecuteAction()
    {
        RunManager.Instance.TreasureRoomRelicSynchronizer.OnPicked(_player, -1);
        return Task.CompletedTask;
    }

    public override INetAction ToNetAction() => new RmpSkipRelicNetAction();

    public override string ToString() => $"RmpSkipRelicAction for player {_player.NetId}";
}

/// <summary>
/// Relic skip INetAction — mod-independent network action type.
/// Auto-discovered via ActionTypes (NetTypeCache&lt;INetAction&gt;).
/// Zero payload — the type itself signals "skip".
/// </summary>
public struct RmpSkipRelicNetAction : INetAction, IPacketSerializable
{
    public readonly GameAction ToGameAction(Player player) => new RmpSkipRelicGameAction(player);

    public readonly void Serialize(PacketWriter writer) { /* No payload */ }
    public void Deserialize(PacketReader reader) { /* No payload */ }

    public override readonly string ToString() => nameof(RmpSkipRelicNetAction);
}
