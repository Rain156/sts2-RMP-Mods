using Godot;

namespace RemoveMultiplayerPlayerLimit.Core;

/// <summary>
/// Contract for every independent RMP feature module.
/// Each module is registered at mod init, receives shared infrastructure,
/// and optionally returns a Godot Node for SceneTree injection.
/// </summary>
public interface IRMPModule
{
    string Name { get; }

    /// <summary>
    /// One-time setup: receive config and reflection cache.
    /// Called before the module's node enters the SceneTree.
    /// </summary>
    void Initialize(ConfigManager config, Infrastructure.ReflectionCache cache);

    /// <summary>
    /// Return a Godot Node to inject under RMPController.
    /// Return null if the module needs no per-frame logic.
    /// </summary>
    Node? CreateNode();

    /// <summary>
    /// Cleanup when the mod is shutting down.
    /// </summary>
    void Cleanup();
}
