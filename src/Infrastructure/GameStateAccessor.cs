using System;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Runs;

namespace RemoveMultiplayerPlayerLimit.Infrastructure;

/// <summary>
/// Read-only access to game state via public APIs and cached reflection.
/// Modules use this instead of reaching into game internals directly.
/// </summary>
public static class GameStateAccessor
{
    // RunManager.State is private — access via reflection
    private static readonly PropertyInfo? RunManagerStateProperty =
        typeof(RunManager).GetProperty("State",
            BindingFlags.Instance | BindingFlags.NonPublic);

    // ── Player count ──────────────────────────────────────────────────

    public static int GetPlayerCount()
    {
        try
        {
            var rm = RunManager.Instance;
            if (rm == null) return 1;
            var state = RunManagerStateProperty?.GetValue(rm) as RunState;
            return state?.Players?.Count ?? 1;
        }
        catch { return 1; }
    }

    /// <summary>Get the current RunState via reflection (private property).</summary>
    public static RunState? GetRunState()
    {
        try
        {
            var rm = RunManager.Instance;
            if (rm == null) return null;
            return RunManagerStateProperty?.GetValue(rm) as RunState;
        }
        catch { return null; }
    }

    // ── Multiplayer ───────────────────────────────────────────────────

    public static bool IsMultiplayer()
    {
        try
        {
            var tree = (SceneTree)Engine.GetMainLoop();
            return tree.GetMultiplayer()?.GetUniqueId() != 0;
        }
        catch { return false; }
    }

    public static bool IsServer()
    {
        try
        {
            var tree = (SceneTree)Engine.GetMainLoop();
            var mp = tree.GetMultiplayer();
            return mp?.IsServer() == true;
        }
        catch { return false; }
    }

    // ── Difficulty scaling helper ─────────────────────────────────────

    /// <summary>
    /// Returns the effective player count for difficulty calculations.
    /// When scaling is disabled, clamps to vanilla 4-player max.
    /// When enabled, returns the actual player count.
    /// </summary>
    public static int GetEffectivePlayerCount(int rawCount)
    {
        return Core.ProtocolConfig.DifficultyScalingEnabled
            ? rawCount
            : Math.Min(rawCount, 4);
    }
}
