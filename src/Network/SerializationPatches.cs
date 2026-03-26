using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace RemoveMultiplayerPlayerLimit.Network;

// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  序列化位宽 Transpiler 补丁                                             ║
// ║                                                                        ║
// ║  修改官方协议消息的 SlotId / LobbyList 序列化位宽：                       ║
// ║    • LobbyPlayer.slotId           : 2 → 4 bits                         ║
// ║    • ClientLobbyJoinResponse.list : 3 → 5 bits                         ║
// ║    • LobbyBeginRunMessage.list    : 3 → 5 bits                         ║
// ║                                                                        ║
// ║  每个消息的 Serialize / Deserialize 各一个补丁，成对保证位宽一致。         ║
// ╚══════════════════════════════════════════════════════════════════════════╝

/// <summary>
/// 持有通过反射获取的 PacketWriter / PacketReader 序列化方法引用。
/// 供各 Transpiler 补丁作为匹配目标使用。
/// </summary>
internal static class SerializationMethods
{
	internal static readonly MethodInfo? WriteIntWithBits =
		AccessTools.Method(typeof(PacketWriter), nameof(PacketWriter.WriteInt), new[] { typeof(int), typeof(int) });

	internal static readonly MethodInfo? ReadIntWithBits =
		AccessTools.Method(typeof(PacketReader), nameof(PacketReader.ReadInt), new[] { typeof(int) });

	internal static readonly MethodInfo? WriteListWithBits =
		typeof(PacketWriter).GetMethods(BindingFlags.Public | BindingFlags.Instance)
			.FirstOrDefault(m => m.Name == nameof(PacketWriter.WriteList)
				&& m.IsGenericMethodDefinition
				&& m.GetParameters().Length == 2
				&& m.GetParameters()[1].ParameterType == typeof(int));

	internal static readonly MethodInfo? ReadListWithBits =
		typeof(PacketReader).GetMethods(BindingFlags.Public | BindingFlags.Instance)
			.FirstOrDefault(m => m.Name == nameof(PacketReader.ReadList)
				&& m.IsGenericMethodDefinition
				&& m.GetParameters().Length == 1
				&& m.GetParameters()[0].ParameterType == typeof(int));
}

// ── LobbyPlayer SlotId ─────────────────────────────────────────────────

[HarmonyPatch(typeof(LobbyPlayer), nameof(LobbyPlayer.Serialize))]
internal static class LobbyPlayerSerializePatch
{
	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		=> TranspilerUtils.ReplaceBitWidthBeforeCall(instructions,
			SerializationMethods.WriteIntWithBits,
			ProtocolConfig.VanillaSlotIdBits, ProtocolConfig.SlotIdBits,
			nameof(LobbyPlayerSerializePatch));
}

[HarmonyPatch(typeof(LobbyPlayer), nameof(LobbyPlayer.Deserialize))]
internal static class LobbyPlayerDeserializePatch
{
	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		=> TranspilerUtils.ReplaceBitWidthBeforeCall(instructions,
			SerializationMethods.ReadIntWithBits,
			ProtocolConfig.VanillaSlotIdBits, ProtocolConfig.SlotIdBits,
			nameof(LobbyPlayerDeserializePatch));
}

// ── ClientLobbyJoinResponseMessage ─────────────────────────────────────

[HarmonyPatch(typeof(ClientLobbyJoinResponseMessage), nameof(ClientLobbyJoinResponseMessage.Serialize))]
internal static class ClientLobbyJoinResponseSerializePatch
{
	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		=> TranspilerUtils.ReplaceBitWidthBeforeCall(instructions,
			SerializationMethods.WriteListWithBits,
			ProtocolConfig.VanillaLobbyListLengthBits, ProtocolConfig.LobbyListLengthBits,
			nameof(ClientLobbyJoinResponseSerializePatch));
}

[HarmonyPatch(typeof(ClientLobbyJoinResponseMessage), nameof(ClientLobbyJoinResponseMessage.Deserialize))]
internal static class ClientLobbyJoinResponseDeserializePatch
{
	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		=> TranspilerUtils.ReplaceBitWidthBeforeCall(instructions,
			SerializationMethods.ReadListWithBits,
			ProtocolConfig.VanillaLobbyListLengthBits, ProtocolConfig.LobbyListLengthBits,
			nameof(ClientLobbyJoinResponseDeserializePatch));
}

// ── LobbyBeginRunMessage ───────────────────────────────────────────────

[HarmonyPatch(typeof(LobbyBeginRunMessage), nameof(LobbyBeginRunMessage.Serialize))]
internal static class LobbyBeginRunSerializePatch
{
	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		=> TranspilerUtils.ReplaceBitWidthBeforeCall(instructions,
			SerializationMethods.WriteListWithBits,
			ProtocolConfig.VanillaLobbyListLengthBits, ProtocolConfig.LobbyListLengthBits,
			nameof(LobbyBeginRunSerializePatch));
}

[HarmonyPatch(typeof(LobbyBeginRunMessage), nameof(LobbyBeginRunMessage.Deserialize))]
internal static class LobbyBeginRunDeserializePatch
{
	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		=> TranspilerUtils.ReplaceBitWidthBeforeCall(instructions,
			SerializationMethods.ReadListWithBits,
			ProtocolConfig.VanillaLobbyListLengthBits, ProtocolConfig.LobbyListLengthBits,
			nameof(LobbyBeginRunDeserializePatch));
}
