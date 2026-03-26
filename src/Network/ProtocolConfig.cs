using System;

namespace RemoveMultiplayerPlayerLimit.Network;

/// <summary>
/// 模组协议配置中心 — 所有协议相关常量与运行时配置的唯一权威来源。
///
/// 分层：
///   Vanilla*  — 官方协议的原始值（不可更改）
///   Extended* — 模组扩展的协议值（编译期固定）
///   Target*   — 运行时用户配置（可通过设置面板修改）
/// </summary>
internal static class ProtocolConfig
{
	// ── 玩家人数限制 ───────────────────────────────────────────────────────

	internal const int DefaultPlayerLimit = 8;

	internal const int MinPlayerLimit = 4;

	internal const int MaxPlayerLimit = 16;

	// ── 官方协议位宽（用于 Transpiler 匹配源值） ──────────────────────────

	internal const int VanillaSlotIdBits = 2;

	internal const int VanillaLobbyListLengthBits = 3;

	// ── 扩展协议位宽（Transpiler 替换目标值） ─────────────────────────────

	/// <summary>SlotId 4 bits → 支持 0-15 号槽位。</summary>
	internal const int SlotIdBits = 4;

	/// <summary>LobbyList 长度 5 bits → 支持最多 31 人列表。</summary>
	internal const int LobbyListLengthBits = 5;

	// ── 运行时配置（可由设置面板 / 配置文件修改） ──────────────────────────

	internal static int TargetPlayerLimit { get; set; } = DefaultPlayerLimit;

	/// <summary>clamped 赋值，确保值始终在 [Min, Max] 范围内。</summary>
	internal static void SetTargetPlayerLimit(int value)
	{
		TargetPlayerLimit = Math.Clamp(value, MinPlayerLimit, MaxPlayerLimit);
	}

	// ── 难度缩放 ─────────────────────────────────────────────────────────

	/// <summary>
	/// 是否启用超过 4 人后的怪物难度继续缩放。
	/// true  = 怪物 HP / 格挡 / 能力数值按实际玩家数缩放（官方公式延伸）
	/// false = 钳制到 4 人难度（与原版一致）
	/// </summary>
	internal static bool DifficultyScalingEnabled { get; private set; } = true;

	internal static void SetDifficultyScalingEnabled(bool value)
	{
		DifficultyScalingEnabled = value;
	}
}
