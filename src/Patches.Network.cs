using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;
using RemoveMultiplayerPlayerLimit.src;
using RemoveMultiplayerPlayetLimit.src.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace RemoveMultiplayerPlayerLimit;

public static partial class ModEntry
{
	private static readonly MethodInfo WriterWriteIntWithBitsMethod = typeof(PacketWriter).GetMethod(nameof(PacketWriter.WriteInt));

	private static readonly MethodInfo ReaderReadIntWithBitsMethod = typeof(PacketReader).GetMethod(nameof(PacketReader.ReadInt));

	private static readonly MethodInfo WriterWriteListWithBitsMethod = typeof(PacketWriter).GetMethod(nameof(PacketWriter.WriteList)).MakeGenericMethod([typeof(LobbyPlayer)]);

	private static readonly MethodInfo ReaderReadListWithBitsMethod = typeof(PacketReader).GetMethod(nameof(PacketReader.ReadList)).MakeGenericMethod([typeof(LobbyPlayer)]);

    [HarmonyPatch(typeof(NetHostGameService), nameof(NetHostGameService.StartENetHost))]
	private static class StartENetHostPatch
	{
		private static void Prefix(ref int maxClients) => maxClients = Math.Max(maxClients, Option.PlayerLimit);
	}

	[HarmonyPatch(typeof(NetHostGameService), nameof(NetHostGameService.StartSteamHost))]
	private static class StartSteamHostPatch
	{
		private static void Prefix(ref int maxClients) => maxClients = Math.Max(maxClients, Option.PlayerLimit);
	}

	[HarmonyPatch(typeof(StartRunLobby), MethodType.Constructor, typeof(GameMode), typeof(INetGameService), typeof(IStartRunLobbyListener), typeof(int))]
	private static class StartRunLobbyConstructorPatch
	{
		private static void Postfix(StartRunLobby __instance, INetGameService netService)
		{
			if (netService.Type == NetGameType.Host && __instance.MaxPlayers < Option.PlayerLimit)
				typeof(StartRunLobby).GetProperty(nameof(StartRunLobby.MaxPlayers)).GetSetMethod(true)?.Invoke(__instance, [ Option.PlayerLimit ]);
		}
	}

	[HarmonyPatch(typeof(LobbyPlayer), nameof(LobbyPlayer.Serialize))]
	private static class LobbyPlayerSerializePatch
	{
		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => ReplaceBitWidthBeforeCall(instructions, WriterWriteIntWithBitsMethod, VanillaSlotIdBits, SlotIdBits, nameof(LobbyPlayerSerializePatch));
	}

	[HarmonyPatch(typeof(LobbyPlayer), nameof(LobbyPlayer.Deserialize))]
	private static class LobbyPlayerDeserializePatch
	{
		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => ReplaceBitWidthBeforeCall(instructions, ReaderReadIntWithBitsMethod, VanillaSlotIdBits, SlotIdBits, nameof(LobbyPlayerDeserializePatch));
	}

	[HarmonyPatch(typeof(ClientLobbyJoinResponseMessage), nameof(ClientLobbyJoinResponseMessage.Serialize))]
	[HarmonyDebug]
    private static class ClientLobbyJoinResponseSerializePatch
	{
		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => ReplaceBitWidthBeforeCall(instructions, WriterWriteListWithBitsMethod, VanillaLobbyListLengthBits, LobbyListLengthBits, nameof(ClientLobbyJoinResponseSerializePatch));
	}

	[HarmonyPatch(typeof(ClientLobbyJoinResponseMessage), nameof(ClientLobbyJoinResponseMessage.Deserialize))]
	private static class ClientLobbyJoinResponseDeserializePatch
	{
		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => ReplaceBitWidthBeforeCall(instructions, ReaderReadListWithBitsMethod, VanillaLobbyListLengthBits, LobbyListLengthBits, nameof(ClientLobbyJoinResponseDeserializePatch));
	}

	[HarmonyPatch(typeof(LobbyBeginRunMessage), nameof(LobbyBeginRunMessage.Serialize))]
	private static class LobbyBeginRunSerializePatch
	{
		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => ReplaceBitWidthBeforeCall(instructions, WriterWriteListWithBitsMethod, VanillaLobbyListLengthBits, LobbyListLengthBits, nameof(LobbyBeginRunSerializePatch));
	}

	[HarmonyPatch(typeof(LobbyBeginRunMessage), nameof(LobbyBeginRunMessage.Deserialize))]
	private static class LobbyBeginRunDeserializePatch
	{
		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => ReplaceBitWidthBeforeCall(instructions, ReaderReadListWithBitsMethod, VanillaLobbyListLengthBits, LobbyListLengthBits, nameof(LobbyBeginRunDeserializePatch));
	}

	private static IEnumerable<CodeInstruction> ReplaceBitWidthBeforeCall(IEnumerable<CodeInstruction> instructions, MethodInfo targetMethod, int sourceBitWidth, int targetBitWidth, string patchName)
	{
		var resolvedTargetMethod = targetMethod ?? throw new InvalidOperationException($"{patchName}: target method is null.");

        List<CodeInstruction> codes = [.. instructions];

		var replace = false;

        foreach (var instruction in codes)
		{
			if (instruction.Calls(resolvedTargetMethod) && codes.TryGetLast(instruction, out var last) && TestLdcI4(last, sourceBitWidth))
			{
                last.opcode = OpCodes.Ldc_I4;

				last.operand = targetBitWidth;

				replace = true;
            }
		}

		if (!replace)
            throw new InvalidOperationException($"{patchName}: no bit-width operand replaced, game code may have changed.");

        return codes;
	}

    private static bool TryReadLdcI4(CodeInstruction instruction, out int? value)
    {
        var v = instruction.opcode.Value;

        value = v switch
        {
            >= 21 and <= 30 => v - 22,
            31 => (sbyte)instruction.operand,
            32 => (int)instruction.operand,
            _ => null
        };

        return value != null;
    }

	private static bool TestLdcI4(CodeInstruction instruction, int value) => TryReadLdcI4(instruction, out var v) && v == value;
}
