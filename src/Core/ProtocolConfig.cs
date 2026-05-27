namespace RemoveMultiplayerPlayerLimit.Core;

/// <summary>
/// Protocol configuration center — the single source of truth for all
/// protocol-related constants.
///
/// v0.1.7 change: the player limit is now a fixed 16 for every lobby in every
/// mode. The runtime setter, UI slider, and config.ini override have been
/// removed.
///
/// Layers:
///   Vanilla*  — official protocol original values (immutable)
///   Extended* — mod-extended protocol values (compile-time fixed)
/// </summary>
internal static class ProtocolConfig
{
    // ── Player count limits ───────────────────────────────────────────
    /// <summary>Fixed maximum lobby size for RMP — always 16.</summary>
    internal const int MaxPlayerLimit = 16;

    /// <summary>
    /// The capacity every hosted lobby uses. Always equals <see cref="MaxPlayerLimit"/>;
    /// kept as a distinct name for call-site readability.
    /// </summary>
    internal const int TargetPlayerLimit = MaxPlayerLimit;

    // ── Official protocol bit widths (for reference) ──────────────────
    internal const int VanillaSlotIdBits = 2;
    internal const int VanillaLobbyListLengthBits = 3;
    internal const int OfficialSerializableSlotLimit = 1 << VanillaSlotIdBits;
    internal const int OfficialSerializableLobbyLimit = (1 << VanillaLobbyListLengthBits) - 1;

    // ── Extended protocol bit widths ──────────────────────────────────
    /// <summary>SlotId 4 bits -> supports slots 0-15.</summary>
    internal const int SlotIdBits = 4;
    /// <summary>LobbyList length 5 bits -> supports up to 31 entries.</summary>
    internal const int LobbyListLengthBits = 5;

    // ── Difficulty scaling ────────────────────────────────────────────
    /// <summary>
    /// Whether to continue scaling monster HP/block/power beyond 4 players.
    /// true  = scale using actual player count (official formula extrapolation)
    /// false = clamp to 4-player difficulty (vanilla behavior)
    /// </summary>
    internal static bool DifficultyScalingEnabled { get; private set; } = true;

    internal static void SetDifficultyScalingEnabled(bool value)
    {
        DifficultyScalingEnabled = value;
    }
}
