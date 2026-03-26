using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace RemoveMultiplayerPlayerLimit.Network;

/// <summary>
/// IL Transpiler 工具类 — 纯 IL 操作，零游戏依赖。
///
/// 核心能力：在 IL 指令流中定位特定方法调用前的常量加载指令，
/// 并将其替换为新值。用于修改官方序列化方法的位宽参数。
///
/// 设计：
///   所有方法都是静态无状态的，可安全并发调用。
///   不引用任何 MegaCrit / Godot / Harmony 命名空间。
/// </summary>
internal static class TranspilerUtils
{
	private static readonly int LdcI4MinOpcodeValue = OpCodes.Ldc_I4_M1.Value;

	private static readonly int LdcI4MaxOpcodeValue = OpCodes.Ldc_I4_8.Value;

	private static readonly int LdcI4SOpcodeValue = OpCodes.Ldc_I4_S.Value;

	private static readonly int LdcI4OpcodeValue = OpCodes.Ldc_I4.Value;

	/// <summary>
	/// 在 IL 指令流中，找到所有对 <paramref name="targetMethod"/> 的调用，
	/// 将其前方的 <paramref name="sourceBitWidth"/> 常量加载指令替换为 <paramref name="targetBitWidth"/>。
	/// </summary>
	/// <exception cref="InvalidOperationException">未找到任何可替换的位宽操作数。</exception>
	internal static IEnumerable<CodeInstruction> ReplaceBitWidthBeforeCall(
		IEnumerable<CodeInstruction> instructions,
		MethodInfo? targetMethod,
		int sourceBitWidth,
		int targetBitWidth,
		string patchName)
	{
		MethodInfo resolvedTargetMethod = targetMethod
			?? throw new InvalidOperationException($"{patchName}: target method is null.");

		List<CodeInstruction> list = new List<CodeInstruction>(instructions);
		int count = 0;

		for (int i = 0; i < list.Count; i++)
		{
			if (!IsCallToMethod(list[i], resolvedTargetMethod))
			{
				continue;
			}
			int loadIndex = FindBitWidthLoadIndex(list, i, sourceBitWidth);
			if (loadIndex < 0)
			{
				continue;
			}
			list[loadIndex] = CloneWithNewIntOperand(list[loadIndex], targetBitWidth);
			count++;
		}

		if (count == 0)
		{
			throw new InvalidOperationException(
				$"{patchName}: no bit-width operand replaced for method " +
				$"{resolvedTargetMethod.Name} ({sourceBitWidth}->{targetBitWidth}), game code may have changed.");
		}

		return list;
	}

	/// <summary>
	/// 从 <paramref name="callIndex"/> 向前回溯最多 8 条指令，
	/// 查找值等于 <paramref name="expectedValue"/> 的 ldc.i4 指令。
	/// </summary>
	/// <returns>找到的指令索引，或 -1。</returns>
	private static int FindBitWidthLoadIndex(IReadOnlyList<CodeInstruction> instructions, int callIndex, int expectedValue)
	{
		int searchStart = Math.Max(0, callIndex - 8);
		for (int i = callIndex - 1; i >= searchStart; i--)
		{
			if (instructions[i].opcode == OpCodes.Nop)
			{
				continue;
			}
			int? ldcI4Value = ReadLdcI4Nullable(instructions[i]);
			if (ldcI4Value.HasValue)
			{
				return ldcI4Value.Value == expectedValue ? i : -1;
			}
			if (IsTerminatingOpcode(instructions[i].opcode))
			{
				return -1;
			}
		}
		return -1;
	}

	/// <summary>判断操作码是否为控制流终止指令（分支/返回/抛出/调用）。</summary>
	private static bool IsTerminatingOpcode(OpCode opcode)
	{
		FlowControl flowControl = opcode.FlowControl;
		return flowControl is FlowControl.Branch or FlowControl.Cond_Branch
			or FlowControl.Return or FlowControl.Throw or FlowControl.Call;
	}

	/// <summary>创建一个新的 ldc.i4 指令，保留原指令的 labels 和 blocks。</summary>
	private static CodeInstruction CloneWithNewIntOperand(CodeInstruction source, int newValue)
	{
		CodeInstruction result = new CodeInstruction(OpCodes.Ldc_I4, newValue);
		result.labels.AddRange(source.labels);
		result.blocks.AddRange(source.blocks);
		return result;
	}

	/// <summary>判断指令是否为对 <paramref name="targetMethod"/> 的调用（含泛型方法匹配）。</summary>
	internal static bool IsCallToMethod(CodeInstruction instruction, MethodInfo targetMethod)
	{
		if ((instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt)
			|| instruction.operand is not MethodInfo methodInfo)
		{
			return false;
		}
		if (methodInfo == targetMethod)
		{
			return true;
		}
		MethodInfo resolvedMethod = methodInfo.IsGenericMethod ? methodInfo.GetGenericMethodDefinition() : methodInfo;
		MethodInfo resolvedTarget = targetMethod.IsGenericMethod ? targetMethod.GetGenericMethodDefinition() : targetMethod;
		return resolvedMethod == resolvedTarget;
	}

	/// <summary>解析 ldc.i4 系列操作码的整数值。非 ldc.i4 返回 null。</summary>
	internal static int? ReadLdcI4Nullable(CodeInstruction instruction)
	{
		int opcodeValue = instruction.opcode.Value;
		return opcodeValue switch
		{
			_ when opcodeValue >= LdcI4MinOpcodeValue && opcodeValue <= LdcI4MaxOpcodeValue
				=> opcodeValue - (LdcI4MinOpcodeValue + 1),
			_ when opcodeValue == LdcI4SOpcodeValue && instruction.operand is sbyte sb
				=> sb,
			_ when opcodeValue == LdcI4OpcodeValue && instruction.operand is int num
				=> num,
			_ => null
		};
	}
}
