using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using RemoveMultiplayerPlayerLimit.Features.CampfireLayout;
using RemoveMultiplayerPlayerLimit.Features.DifficultyScaling;
using RemoveMultiplayerPlayerLimit.Features.SettingsUI;
using RemoveMultiplayerPlayerLimit.Features.ShopLayout;
using RemoveMultiplayerPlayerLimit.Features.TreasureRoom;
using RemoveMultiplayerPlayerLimit.Features.VictoryFlow;
using RemoveMultiplayerPlayerLimit.Infrastructure;
using RemoveMultiplayerPlayerLimit.Network;
using RemoveMultiplayerPlayerLimit.Platform;

namespace RemoveMultiplayerPlayerLimit.Core;

/// <summary>
/// Sole entry point — [ModInitializer] with NO Harmony.
/// Initializes all modules and injects a root Node into the SceneTree.
/// </summary>
[ModInitializer("Initialize")]
public static class ModEntry
{
    internal const int VanillaMultiplayerHolderCount = 4;

    private static readonly List<IRMPModule> Modules = new();
    private static Node? _root;

    public static void Initialize()
    {
        Log.Warn("[RMP] Initializing v0.1.8...");

        Modules.Clear();

        var config = new ConfigManager();
        var cache = new ReflectionCache();

        int slotCapacity = 1 << ProtocolConfig.SlotIdBits;
        int lobbyCapacity = 1 << ProtocolConfig.LobbyListLengthBits;

        // Register feature modules
        Modules.Add(new DifficultyModule());
        Modules.Add(new CampfireModule());
        Modules.Add(new ShopModule());
        Modules.Add(new TreasureModule());
        Modules.Add(new SettingsModule());
        Modules.Add(new VictoryModule());

        // Register network module
        Modules.Add(new HostBootstrapModule());
        Modules.Add(new ExtendedLobbyModule());
        Modules.Add(new LobbyManagerModule());

        // Register platform modules
        if (PlatformDetector.IsMacOS)
            Modules.Add(new MacOsTlsModule());

        // Initialize all modules
        foreach (var module in Modules)
        {
            try
            {
                module.Initialize(config, cache);
            }
            catch (Exception ex)
            {
                Log.Warn($"[RMP] Failed to initialize module {module.Name}: {ex}");
            }
        }

        // Inject root node into SceneTree
        var tree = (SceneTree)Engine.GetMainLoop();
        _root = new Node { Name = "RMPController" };
        _root.AddChild(SceneMonitor.CreateRegistryNode());
        foreach (var module in Modules)
        {
            try
            {
                var node = module.CreateNode();
                if (node != null)
                    _root.AddChild(node);
            }
            catch (Exception ex)
            {
                Log.Warn($"[RMP] Failed to create node for module {module.Name}: {ex}");
            }
        }
        tree.Root.CallDeferred("add_child", _root);

        Log.Warn($"[RMP] All modules loaded. Player limit fixed at {ProtocolConfig.MaxPlayerLimit}, " +
                 $"slot bits: {ProtocolConfig.SlotIdBits} (cap {slotCapacity}), " +
                 $"lobby bits: {ProtocolConfig.LobbyListLengthBits} (cap {lobbyCapacity}), " +
                 $"difficulty scaling: {ProtocolConfig.DifficultyScalingEnabled}, " +
                 $"macOS TLS: {config.MacOsTlsWorkaround}");
    }
}
