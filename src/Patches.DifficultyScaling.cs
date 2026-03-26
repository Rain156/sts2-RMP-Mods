using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Singleton;
using RemoveMultiplayerPlayerLimit.Network;

namespace RemoveMultiplayerPlayerLimit;

public static partial class ModEntry
{
	// ── 怪物难度缩放：超过 4 人后继续按官方公式提升 ──────────────────────────
	//
	// 官方公式：Value × PlayerCount × ActMultiplier
	// 原版仅支持 4 人，该公式自然只跑到 4。
	// 本补丁在「难度缩放」关闭时将 playerCount 钳制到 4（保留原版体验），
	// 开启时直接使用真实玩家数（官方公式自然延伸到更多人）。

	internal static int GetEffectivePlayerCount(int rawCount)
	{
		return ProtocolConfig.DifficultyScalingEnabled ? rawCount : Math.Min(rawCount, 4);
	}

	// ── HP 缩放 ──────────────────────────────────────────────────────────

	[HarmonyPatch(typeof(Creature), nameof(Creature.ScaleMonsterHpForMultiplayer))]
	private static class ScaleMonsterHpPatch
	{
		private static void Prefix(ref int playerCount)
		{
			playerCount = GetEffectivePlayerCount(playerCount);
		}
	}

	// ── 格挡缩放 ─────────────────────────────────────────────────────────

	[HarmonyPatch(typeof(MultiplayerScalingModel), nameof(MultiplayerScalingModel.ModifyBlockMultiplicative))]
	private static class ModifyBlockScalingPatch
	{
		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			return PatchPlayersCountInScaling(instructions);
		}
	}

	// ── 能力数值缩放 ─────────────────────────────────────────────────────

	[HarmonyPatch(typeof(MultiplayerScalingModel), nameof(MultiplayerScalingModel.ModifyPowerAmountGiven))]
	private static class ModifyPowerScalingPatch
	{
		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			return PatchPlayersCountInScaling(instructions);
		}
	}

	/// <summary>
	/// 通用 Transpiler：在 MultiplayerScalingModel 方法中，找到 _runState.Players.Count
	/// 的 get_Count 调用，在其后插入 GetEffectivePlayerCount 以实现钳制。
	/// </summary>
	private static IEnumerable<CodeInstruction> PatchPlayersCountInScaling(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo helper = AccessTools.Method(typeof(ModEntry), nameof(GetEffectivePlayerCount));
		FieldInfo? runStateField = AccessTools.Field(typeof(MultiplayerScalingModel), "_runState");

		bool foundRunStateLoad = false;

		foreach (CodeInstruction instruction in instructions)
		{
			yield return instruction;

			// 检测 ldfld _runState
			if (!foundRunStateLoad && runStateField != null && instruction.LoadsField(runStateField))
			{
				foundRunStateLoad = true;
				continue;
			}

			// 在 _runState 之后寻找 get_Count（即 _runState.Players.Count）
			if (foundRunStateLoad
				&& (instruction.opcode == OpCodes.Callvirt || instruction.opcode == OpCodes.Call)
				&& instruction.operand is MethodInfo mi
				&& mi.Name == "get_Count"
				&& mi.ReturnType == typeof(int))
			{
				yield return new CodeInstruction(OpCodes.Call, helper);
				foundRunStateLoad = false;
			}
		}
	}
}
