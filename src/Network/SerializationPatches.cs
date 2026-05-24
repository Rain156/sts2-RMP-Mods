using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace RemoveMultiplayerPlayerLimit.Network;

// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  序列化位宽 Transpiler 补丁                                             ║
// ║                                                                        ║
// ║  修改官方协议消息的 SlotId / LobbyList 序列化位宽：                       ║
// ║    • LobbyPlayer.slotId           : 2 → 4 bits                         ║
// ║    • Lobby message player lists   : 3 → 5 bits                         ║
// ║                                                                        ║
// ║  Lobby 列表补丁扫描官方 Lobby 消息，避免漏掉 8 人时才触发的消息。          ║
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

// ── Lobby message player lists ─────────────────────────────────────────

[HarmonyPatch]
internal static class LobbyMessageSerializeListPatch
{
	private static IEnumerable<MethodBase> TargetMethods()
		=> LobbyMessagePatchTargets.GetPacketMethods(nameof(IPacketSerializable.Serialize), typeof(PacketWriter));

	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
		=> LobbyMessagePatchTargets.ReplaceLobbyListBits(instructions,
			SerializationMethods.WriteListWithBits,
			nameof(LobbyMessageSerializeListPatch),
			__originalMethod);
}

[HarmonyPatch]
internal static class LobbyMessageDeserializeListPatch
{
	private static IEnumerable<MethodBase> TargetMethods()
		=> LobbyMessagePatchTargets.GetPacketMethods(nameof(IPacketSerializable.Deserialize), typeof(PacketReader));

	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
		=> LobbyMessagePatchTargets.ReplaceLobbyListBits(instructions,
			SerializationMethods.ReadListWithBits,
			nameof(LobbyMessageDeserializeListPatch),
			__originalMethod);
}

internal static class LobbyMessagePatchTargets
{
	private const string LobbyMessagesNamespace = "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby";

	internal static IEnumerable<MethodBase> GetPacketMethods(string methodName, Type packetType)
	{
		foreach (Type type in GetSts2Types())
		{
			if (type.Namespace != LobbyMessagesNamespace || type.IsAbstract)
			{
				continue;
			}
			MethodInfo? method = type.GetMethod(methodName,
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
				null,
				new[] { packetType },
				null);
			if (method == null || method.IsAbstract || method.DeclaringType != type)
			{
				continue;
			}
			yield return method;
		}
	}

	internal static IEnumerable<CodeInstruction> ReplaceLobbyListBits(
		IEnumerable<CodeInstruction> instructions,
		MethodInfo? targetMethod,
		string patchName,
		MethodBase originalMethod)
	{
		IEnumerable<CodeInstruction> result = TranspilerUtils.ReplaceBitWidthBeforeCall(instructions,
			targetMethod,
			ProtocolConfig.VanillaLobbyListLengthBits,
			ProtocolConfig.LobbyListLengthBits,
			patchName,
			requireReplacement: false,
			out int replacementCount);

		if (replacementCount > 0)
		{
			Log.Info($"{patchName}: widened {replacementCount} lobby list bit-width operand(s) in {originalMethod.DeclaringType?.Name}.{originalMethod.Name}.");
		}
		return result;
	}

	private static IEnumerable<Type> GetSts2Types()
	{
		try
		{
			return typeof(LobbyPlayer).Assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException ex)
		{
			return ex.Types.Where(type => type != null).Cast<Type>();
		}
	}
}
