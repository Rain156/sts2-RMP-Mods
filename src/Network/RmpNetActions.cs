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
/// 遗物跳过 GameAction — 模组协议通道的独立动作（替代 PickRelicAction(player, -1) 黑客）。
///
/// 旧方案问题：
///   PickRelicAction 的 relicIndex 字段用 8-bit 序列化，-1 被截断为 255，
///   接收端需要特殊处理 255→-1 的映射，侵入了官方 NetPickRelicAction 的协议空间。
///
/// 新方案：
///   通过 ActionTypes 自动注册 RmpSkipRelicNetAction 作为独立的网络动作类型，
///   类型本身即表达 "跳过" 语义，无需编码 -1 到官方字段中。
///
/// 网络流转:
///   发起端: RmpSkipRelicGameAction.ToNetAction() → RmpSkipRelicNetAction
///   网络层: ActionTypes 编码类型ID + Serialize（空载荷）
///   接收端: RmpSkipRelicNetAction.ToGameAction(player) → RmpSkipRelicGameAction
///   执行:   RmpSkipRelicGameAction.ExecuteAction() → OnPicked(player, -1)
///
/// Host 广播时也通过 GameAction.ToNetAction() 回路保持类型一致:
///   Host: INetAction → GameAction → ToNetAction() → RmpSkipRelicNetAction (保持类型)
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
/// 遗物跳过 INetAction — 模组独立的网络动作类型。
///
/// 通过 ActionTypes (NetTypeCache&lt;INetAction&gt;) 自动发现与注册。
/// 无载荷序列化：类型ID本身即为 "跳过" 信号。
/// </summary>
public struct RmpSkipRelicNetAction : INetAction, IPacketSerializable
{
	public readonly GameAction ToGameAction(Player player) => new RmpSkipRelicGameAction(player);

	public readonly void Serialize(PacketWriter writer)
	{
		// 无载荷 — 类型本身即为 "跳过" 语义
	}

	public void Deserialize(PacketReader reader)
	{
		// 无载荷
	}

	public override readonly string ToString() => nameof(RmpSkipRelicNetAction);
}
