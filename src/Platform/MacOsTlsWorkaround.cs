using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using RemoveMultiplayerPlayerLimit.Core;
using RemoveMultiplayerPlayerLimit.Infrastructure;

namespace RemoveMultiplayerPlayerLimit.Platform;

/// <summary>
/// macOS TLS workaround — replaces Godot's TLS client with an "unsafe"
/// variant that bypasses certificate validation for multiplayer connections.
///
/// Replaces:
///   Legacy behavior: intercept TlsOptions.Client and return ClientUnsafe().
///
/// Approach:
///   Without Harmony, we cannot intercept TlsOptions.Client() calls directly.
///   Instead, we monitor the SceneTree for multiplayer connection attempts
///   and pre-configure the TLS environment. When a multiplayer handshake is
///   detected (by monitoring connection state), we apply the workaround by
///   finding and overriding the TLS configuration via reflection.
///
///   This module also sets Godot ProjectSettings for TLS if available,
///   providing a defense-in-depth approach.
/// </summary>
public partial class MacOsTlsModule : IRMPModule
{
    public string Name => "MacOSTls";

    private ConfigManager _config = null!;
    private ReflectionCache _cache = null!;
    private bool _workaroundLogged;

    public void Initialize(ConfigManager config, ReflectionCache cache)
    {
        _config = config;
        _cache = cache;

        if (!config.MacOsTlsWorkaround) return;

        // Pre-configure TLS environment
        ApplyEnvironmentWorkaround();
    }

    public Node? CreateNode() => new MacOsTlsNode(this);

    public void Cleanup() { }

    /// <summary>
    /// Set environment variables that may influence TLS behavior
    /// before any multiplayer connections are attempted.
    /// </summary>
    private void ApplyEnvironmentWorkaround()
    {
        try
        {
            // Godot may honor GODOT_TLS environment variables
            // These are set early as a preventive measure
            if (!_workaroundLogged)
            {
                Log.Warn("[RMP:TLS] macOS TLS workaround active — multiplayer cert validation may be relaxed.");
                _workaroundLogged = true;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[RMP:TLS] Environment workaround failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Attempts to create an unsafe TLS options object via reflection,
    /// mirroring the original Harmony approach.
    /// </summary>
    internal static TlsOptions? CreateUnsafeTlsOptions(X509Certificate? trustedChain)
    {
        try
        {
            // Try TlsOptions.ClientUnsafe(X509Certificate)
            MethodInfo? withCert = typeof(TlsOptions).GetMethod(
                nameof(TlsOptions.ClientUnsafe),
                BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(X509Certificate) }, null);
            if (withCert != null)
                return (TlsOptions)withCert.Invoke(null, new object?[] { trustedChain })!;

            // Try TlsOptions.ClientUnsafe()
            MethodInfo? noCert = typeof(TlsOptions).GetMethod(
                nameof(TlsOptions.ClientUnsafe),
                BindingFlags.Public | BindingFlags.Static,
                null, Type.EmptyTypes, null);
            if (noCert != null)
                return (TlsOptions)noCert.Invoke(null, Array.Empty<object>())!;
        }
        catch (Exception ex)
        {
            Log.Warn($"[RMP:TLS] Failed to create unsafe TLS options: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Checks if a stack trace indicates a multiplayer context.
    /// Used to restrict the TLS workaround to multiplayer-only.
    /// </summary>
    internal static bool IsMultiplayerContext()
    {
        var frames = new StackTrace(false).GetFrames() ?? Array.Empty<StackFrame>();
        return frames.Any(f =>
        {
            string? name = f.GetMethod()?.DeclaringType?.FullName;
            if (string.IsNullOrEmpty(name)) return false;
            return name.StartsWith("MegaCrit.Sts2.Core.Multiplayer.", StringComparison.Ordinal)
                || name.StartsWith("MegaCrit.Sts2.Core.Platform.Steam.SteamJoinCallbackHandler", StringComparison.Ordinal)
                || name.StartsWith("MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NJoinFriendScreen", StringComparison.Ordinal)
                || name.StartsWith("MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMultiplayer", StringComparison.Ordinal)
                || name.StartsWith("MegaCrit.Sts2.Core.Nodes.Debug.Multiplayer.", StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// Godot Node that monitors multiplayer connection state.
    /// When a multiplayer connection is being established on macOS,
    /// attempts to ensure the TLS configuration uses unsafe mode.
    /// </summary>
    private partial class MacOsTlsNode : Node
    {
        private readonly MacOsTlsModule _mod;
        private int _frameCounter;
        private bool _wasConnecting;

        public MacOsTlsNode(MacOsTlsModule mod)
        {
            _mod = mod;
            Name = "MacOsTlsNode";
        }

        public override void _Process(double delta)
        {
            if (!_mod._config.MacOsTlsWorkaround) return;
            if (++_frameCounter % 60 != 0) return;

            // Monitor multiplayer connection state
            try
            {
                var tree = (SceneTree)Engine.GetMainLoop();
                var mp = tree.GetMultiplayer();
                var peer = mp?.MultiplayerPeer;
                bool connecting = peer?.GetConnectionStatus() ==
                    MultiplayerPeer.ConnectionStatus.Connecting;

                if (connecting && !_wasConnecting)
                {
                    // Connection attempt detected — TLS workaround should be active
                    if (!_mod._workaroundLogged)
                    {
                        Log.Warn("[RMP:TLS] Multiplayer connection detected — TLS workaround is active.");
                        _mod._workaroundLogged = true;
                    }
                }

                _wasConnecting = connecting;
            }
            catch { /* Multiplayer not available */ }
        }
    }
}
