---
name: sts2-rmp-mod
description: |
  Skill for developing and refactoring the sts2-RMP-Mods project — a Slay the Spire 2 multiplayer mod that removes the 4-player lobby limit, supporting up to 16 players. Use this skill whenever working on: multiplayer player limit expansion, lobby size modification, campfire/shop/treasure room UI layout for large groups, monster difficulty scaling, in-game settings injection, RMP network protocol, macOS TLS workaround, or any refactoring from Harmony patches to reflection-based approaches. Also trigger for: RMP, RemoveMultiplayerPlayerLimit, 大厅上限, 人数限制, multiplayer limit, player cap, lobby expansion, campfire seating, shop layout grid, difficulty scaling. CRITICAL CONSTRAINT: This project is being refactored to REMOVE all Harmony/MonoMod patches — every code suggestion must use [ModInitializer] + reflection + SceneTree injection ONLY.
---

# STS2-RMP Mod Development Skill

Guide for Claude Code to develop and refactor the Remove Multiplayer Player Limit mod for Slay the Spire 2.

---

## Absolute Constraints

### NEVER
- ❌ Use `[HarmonyPatch]`, `[HarmonyPrefix]`, `[HarmonyPostfix]`, `[HarmonyTranspiler]`
- ❌ Import `HarmonyLib`, `0Harmony`, `MonoMod`, or any patching framework
- ❌ Call `harmony.PatchAll()` or any runtime method replacement
- ❌ Add `Lib.Harmony` as a NuGet or DLL reference
- ❌ Use IL-level code manipulation

### ALWAYS
- ✅ Use `[ModInitializer("Initialize")]` as the sole entry point
- ✅ Access game internals via `ReflectionCache` (cached FieldInfo/MethodInfo)
- ✅ Modify game UI via Godot SceneTree node injection
- ✅ Observe game state via `_Process()` polling in injected Nodes
- ✅ Isolate each feature into independent, composable Module classes
- ✅ Support Windows, macOS (native ARM64), and Linux without platform workarounds

---

## Project Context

### What This Mod Does
1. **Lobby Expansion**: Increases multiplayer player limit from 4 to 4–16
2. **Difficulty Scaling**: Monster HP/block/power scale for 5+ players using the official formula
3. **Campfire Layout**: Auto front/back row seating with extra logs for 5+ players
4. **Shop Layout**: Player model grid arrangement for large groups
5. **Treasure Room**: Relic slot auto-scaling into two centered rows
6. **Settings UI**: In-game paginator for player limit + difficulty toggle
7. **RMP Protocol**: Independent mod network channel alongside official packet system
8. **macOS TLS Fix**: Resolves multiplayer certificate errors on macOS

### Tech Stack
- **Engine**: Godot 4.5.1 (MegaDot) / C# / .NET 9.0+
- **Core Assembly**: `sts2.dll`
- **Entry Point**: `[ModInitializer("Initialize")]` from `MegaCrit.Sts2.Core.Modding`
- **Config**: `config.ini` (INI format — NOT .json, game scans .json as manifests)
- **Resources**: `.pck` packed with Godot 4.5.1 specifically
- **Dev Stack**: Claude Code + Opus 4.6 Model Max

---

## Module Architecture

Each feature is an independent module that registers with the mod lifecycle:

```csharp
// All modules implement this interface
public interface IRMPModule
{
    string Name { get; }
    void Initialize(ConfigManager config, ReflectionCache cache);
    Node CreateNode();  // Returns a Godot Node to inject into SceneTree
    void Cleanup();
}

// ModEntry registers and manages all modules
[ModInitializer("Initialize")]
public static class ModEntry
{
    private static readonly List<IRMPModule> _modules = new();
    private static Node _root;

    public static void Initialize()
    {
        Log.Warn("[RMP] Initializing...");
        var config = new ConfigManager();
        var cache = new ReflectionCache();

        // Register modules
        _modules.Add(new LobbyExpanderModule());
        _modules.Add(new DifficultyModule());
        _modules.Add(new CampfireModule());
        _modules.Add(new ShopModule());
        _modules.Add(new TreasureModule());
        _modules.Add(new SettingsModule());
        _modules.Add(new RMPProtocolModule());

        if (PlatformDetector.IsMacOS)
            _modules.Add(new MacOSTlsModule());

        // Initialize all modules
        foreach (var module in _modules)
            module.Initialize(config, cache);

        // Inject root node into SceneTree
        var tree = (SceneTree)Engine.GetMainLoop();
        _root = new Node { Name = "RMPController" };
        foreach (var module in _modules)
        {
            var node = module.CreateNode();
            if (node != null)
                _root.AddChild(node);
        }
        tree.Root.CallDeferred("add_child", _root);

        Log.Warn("[RMP] All modules loaded");
    }
}
```

---

## Refactoring Patterns — Harmony → Reflection

### Pattern 1: Value Override (was Postfix that changes return value)

**Before (Harmony)**:
```csharp
[HarmonyPatch(typeof(MultiplayerManager), "get_MaxPlayers")]
class Patch { static void Postfix(ref int __result) => __result = 8; }
```

**After (Reflection + Polling)**:
```csharp
public partial class LobbyExpanderNode : Node
{
    private FieldInfo _maxPlayersField;
    private object _target;
    private int _desiredMax;

    public override void _Ready()
    {
        _maxPlayersField = ReflectionCache.GetField(
            "MegaCrit.Sts2.Core.Multiplayer.SomeClass", "maxPlayers");
        _desiredMax = ConfigManager.Instance.MaxPlayerLimit;
    }

    public override void _Process(double delta)
    {
        // Find the target instance
        _target ??= FindMultiplayerManagerInstance();
        if (_target == null) return;

        // Continuously enforce our value
        var current = (int)_maxPlayersField.GetValue(_target);
        if (current != _desiredMax)
        {
            _maxPlayersField.SetValue(_target, _desiredMax);
            GD.Print($"[RMP] Max players set to {_desiredMax}");
        }
    }
}
```

### Pattern 2: Scene Modification (was Prefix/Postfix that rearranges UI)

**Before (Harmony)**:
```csharp
[HarmonyPatch(typeof(CampfireRoom), "ArrangeCharacters")]
class Patch { static bool Prefix(CampfireRoom __instance) { /* rewrite */ return false; } }
```

**After (SceneTree Injection)**:
```csharp
public partial class CampfireNode : Node
{
    private bool _hasArranged = false;
    private string _lastScenePath = "";

    public override void _Process(double delta)
    {
        var currentScene = GetCurrentSceneName();

        // Reset when leaving campfire
        if (currentScene != "campfire") { _hasArranged = false; return; }

        // Arrange once when entering campfire
        if (!_hasArranged && GetPlayerCount() > 4)
        {
            ArrangeSeats();
            _hasArranged = true;
        }
    }

    private void ArrangeSeats()
    {
        var campfireNode = FindCampfireRoomNode();
        if (campfireNode == null) return;

        var characters = GetCharacterNodes(campfireNode);
        var positions = SeatCalculator.Calculate(characters.Count);

        for (int i = 0; i < characters.Count; i++)
        {
            // Move character nodes to calculated positions
            if (characters[i] is Node2D node2d)
                node2d.Position = positions[i];
        }

        // Spawn extra log nodes for back row
        SpawnBackRowLogs(campfireNode, positions);
    }
}
```

### Pattern 3: Settings UI Injection (was Postfix on settings build)

**Before (Harmony)**:
```csharp
[HarmonyPatch(typeof(SettingsScreen), "OnReady")]
class Patch { static void Postfix(SettingsScreen __instance) { /* add controls */ } }
```

**After (SceneTree Monitor + Injection)**:
```csharp
public partial class SettingsNode : Node
{
    private bool _injected = false;

    public override void _Process(double delta)
    {
        if (_injected) return;

        // Wait for settings screen to appear in the SceneTree
        var settingsScreen = FindSettingsScreen();
        if (settingsScreen == null) return;

        InjectControls(settingsScreen);
        _injected = true;
    }

    private void InjectControls(Node settingsScreen)
    {
        // Find the General tab container
        var generalTab = FindChild(settingsScreen, "GeneralTab");
        if (generalTab == null) return;

        // Create and add paginator
        var paginator = MaxPlayerPaginator.Create(
            ConfigManager.Instance.MaxPlayerLimit,
            OnMaxPlayersChanged
        );
        generalTab.AddChild(paginator);

        // Create and add difficulty toggle
        var toggle = ScalingToggle.Create(
            ConfigManager.Instance.DifficultyScaling,
            OnDifficultyToggled
        );
        generalTab.AddChild(toggle);
    }
}
```

### Pattern 4: Combat Start Hook (was Postfix on combat init)

**Before (Harmony)**:
```csharp
[HarmonyPatch(typeof(CombatManager), "StartCombat")]
class Patch { static void Postfix() { /* apply difficulty */ } }
```

**After (State Transition Detection)**:
```csharp
public partial class DifficultyNode : Node
{
    private bool _wasInCombat = false;
    private bool _scalingApplied = false;

    public override void _Process(double delta)
    {
        bool inCombat = GameStateAccessor.IsCombatActive();

        // Detect combat start transition
        if (inCombat && !_wasInCombat)
        {
            _scalingApplied = false;
        }

        // Apply scaling once per combat, when state is ready
        if (inCombat && !_scalingApplied && GameStateAccessor.AreMonstersReady())
        {
            int playerCount = GameStateAccessor.GetPlayerCount();
            if (playerCount > 4 && ConfigManager.Instance.DifficultyScaling)
            {
                ApplyScaling(playerCount);
            }
            _scalingApplied = true;
        }

        // Reset on combat end
        if (!inCombat && _wasInCombat)
        {
            _scalingApplied = false;
        }

        _wasInCombat = inCombat;
    }

    private void ApplyScaling(int playerCount)
    {
        var monsters = GameStateAccessor.GetAllMonsters();
        foreach (var monster in monsters)
        {
            ScalingFormula.ApplyTo(monster, playerCount);
        }
    }
}
```

---

## ReflectionCache Implementation

Central to the entire refactoring — caches all reflection lookups for performance:

```csharp
public class ReflectionCache
{
    private readonly Dictionary<string, FieldInfo> _fields = new();
    private readonly Dictionary<string, MethodInfo> _methods = new();
    private readonly Dictionary<string, PropertyInfo> _properties = new();
    private readonly Dictionary<string, Type> _types = new();

    // Game assembly reference
    private readonly Assembly _gameAssembly;

    public ReflectionCache()
    {
        _gameAssembly = typeof(MegaCrit.Sts2.Core.Modding.ModInitializerAttribute).Assembly;
    }

    public Type GetType(string fullName)
    {
        if (!_types.TryGetValue(fullName, out var type))
        {
            type = _gameAssembly.GetType(fullName);
            _types[fullName] = type;
        }
        return type;
    }

    public FieldInfo GetField(string typeName, string fieldName)
    {
        var key = $"{typeName}.{fieldName}";
        if (!_fields.TryGetValue(key, out var field))
        {
            var type = GetType(typeName);
            field = type?.GetField(fieldName,
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic);
            _fields[key] = field;
        }
        return field;
    }

    public MethodInfo GetMethod(string typeName, string methodName, Type[] paramTypes = null)
    {
        var key = paramTypes == null
            ? $"{typeName}.{methodName}"
            : $"{typeName}.{methodName}({string.Join(",", paramTypes.Select(t => t.Name))})";

        if (!_methods.TryGetValue(key, out var method))
        {
            var type = GetType(typeName);
            method = paramTypes == null
                ? type?.GetMethod(methodName,
                    BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic)
                : type?.GetMethod(methodName,
                    BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic,
                    null, paramTypes, null);
            _methods[key] = method;
        }
        return method;
    }

    // Helper: set value with null safety
    public bool TrySetField(object target, string typeName, string fieldName, object value)
    {
        var field = GetField(typeName, fieldName);
        if (field == null)
        {
            GD.PrintErr($"[RMP] Field not found: {typeName}.{fieldName}");
            return false;
        }
        field.SetValue(target, value);
        return true;
    }

    // Helper: get value with null safety
    public T TryGetField<T>(object target, string typeName, string fieldName, T fallback = default)
    {
        var field = GetField(typeName, fieldName);
        if (field == null) return fallback;
        return (T)(field.GetValue(target) ?? fallback);
    }
}
```

---

## Source Code Analysis — What Claude Code Must Find

When the user provides decompiled source code, Claude Code should answer these questions to complete the refactoring:

### Lobby Limit (P0 — Core Feature)
1. Where is the max player constant/field defined? (`4` or `MAX_PLAYERS`)
2. Is it a constant, static field, property, or method return value?
3. Where is it checked during lobby creation / join?
4. Is it validated server-side, client-side, or both?

### Difficulty Scaling (P0)
5. What is the official HP/block/power scaling formula for 2→3→4 players?
6. Where is `Monster.InitHP()` or equivalent called?
7. How are block and power values initialized per monster?
8. Is there a `playerCount` parameter passed to scaling methods?

### Campfire Scene (P1)
9. What is the SceneTree path to the campfire room node?
10. How are character models positioned — absolute coords or relative offsets?
11. What node type are the seat positions (Node2D, Sprite2D, Marker2D)?
12. How are the background log/bench sprites managed?

### Shop Scene (P1)
13. What is the shop room node structure?
14. How are player models arranged when visiting merchant?
15. Is there a grid system already, or is it purely positional?

### Treasure Room (P1)
16. How are relic choice slots laid out?
17. What determines the number of slots per row?
18. Where is the layout calculation performed?

### Settings UI (P1)
19. What is the settings screen SceneTree structure?
20. Where is the General tab's child container?
21. What Godot Control types does the game use for similar settings?
22. How do existing paginator/toggle controls work (signals, methods)?

### Network (P1)
23. What multiplayer API does the game use (ENet via Godot, Steam Networking, custom)?
24. How does the existing packet system serialize game state?
25. Can we add custom RPC methods to existing multiplayer nodes?
26. Where is lobby creation/join logic with the player cap validation?

### macOS TLS (P2)
27. Where does the multiplayer handshake occur?
28. What TLS settings/certificates are configured?
29. Is TLS handled by Godot's built-in SSL or custom code?

---

## SceneTree Navigation Helpers

Common patterns for finding game nodes:

```csharp
// Find nodes by type
public static T FindNodeOfType<T>(Node root) where T : Node
{
    if (root is T target) return target;
    foreach (var child in root.GetChildren())
    {
        var result = FindNodeOfType<T>(child);
        if (result != null) return result;
    }
    return null;
}

// Find node by name pattern
public static Node FindNodeByName(Node root, string nameContains)
{
    if (root.Name.ToString().Contains(nameContains)) return root;
    foreach (var child in root.GetChildren())
    {
        var result = FindNodeByName(child, nameContains);
        if (result != null) return result;
    }
    return null;
}

// Get current active scene
public static string GetCurrentSceneName()
{
    var tree = (SceneTree)Engine.GetMainLoop();
    return tree.CurrentScene?.Name.ToString() ?? "";
}

// Detect scene transitions
public static bool IsSceneActive(string nameContains)
{
    var tree = (SceneTree)Engine.GetMainLoop();
    return tree.CurrentScene?.Name.ToString().Contains(nameContains) == true;
}
```

---

## Config.ini Format

```csharp
public class ConfigManager
{
    private readonly string _configPath;

    public int MaxPlayerLimit { get; set; } = 8;
    public bool DifficultyScaling { get; set; } = true;
    public bool MacOSTlsWorkaround { get; set; } = true;

    public ConfigManager()
    {
        // Config lives next to the mod DLL
        var modDir = Path.GetDirectoryName(
            typeof(ConfigManager).Assembly.Location);
        _configPath = Path.Combine(modDir, "config.ini");
        Load();
    }

    // IMPORTANT: Use .ini NOT .json
    // Game scans .json files in mod folders as manifests
    // .ini files are safe and ignored by the game loader
}
```

---

## Build Configuration

```xml
<!-- RemoveMultiplayerPlayerLimit.csproj -->
<Project Sdk="Godot.NET.Sdk/4.4.0">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <RootNamespace>RemoveMultiplayerPlayerLimit</RootNamespace>
    <EnableDynamicLoading>true</EnableDynamicLoading>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="sts2">
      <HintPath>$(STS2GamePath)\data_sts2_windows_x86_64\sts2.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <!-- NO Lib.Harmony reference — intentionally removed -->
  </ItemGroup>

  <Target Name="CopyToMods" AfterTargets="Build"
          Condition="Exists('$(STS2GamePath)\mods')">
    <MakeDir Directories="$(STS2GamePath)\mods\RemoveMultiplayerPlayerLimit" />
    <Copy SourceFiles="$(OutputPath)RemoveMultiplayerPlayerLimit.dll"
          DestinationFolder="$(STS2GamePath)\mods\RemoveMultiplayerPlayerLimit\" />
    <Copy SourceFiles="mod_manifest.json"
          DestinationFolder="$(STS2GamePath)\mods\RemoveMultiplayerPlayerLimit\"
          Condition="Exists('mod_manifest.json')" />
  </Target>
</Project>
```

---

## Testing Checklist

### Per-Module Testing

| Module | Test | Pass Criteria |
|--------|------|--------------|
| LobbyExpander | Host lobby with 8 players | All 8 can join and play |
| LobbyExpander | Set limit to 16 via settings | Up to 16 can join |
| DifficultyScaling | 6-player combat | Monster HP is visibly higher than 4-player |
| DifficultyScaling | Toggle OFF | Monster HP matches vanilla 4-player values |
| CampfireModule | 6 players at campfire | Two rows, no overlap, extra logs visible |
| ShopModule | 8 players at shop | Grid layout, no overlap |
| TreasureModule | 6+ players, relic choice | Two rows of relic slots, centered |
| SettingsModule | Open settings screen | Paginator and toggle visible, functional |
| RMPProtocol | Host + 4 clients | Protocol messages delivered correctly |
| MacOSTlsModule | macOS multiplayer join | No BadCert / unknown ca errors |

### Platform Verification

| Platform | Critical Check |
|----------|----------------|
| Windows x64 | Full functionality baseline |
| macOS ARM64 (Apple Silicon) | Native launch — no Rosetta needed, no Harmony crash |
| macOS x64 (Intel) | Works same as ARM64 |
| Linux x64 | No `mm-exhelper.so` error, no extra library installs |

---

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Using `.json` for config | Game treats all `.json` as manifests — use `.ini` |
| Reflection target renamed in game update | Wrap all reflection in `try/catch`, log missing targets |
| _Process polling too expensive | Use frame counters to throttle checks (every 30-60 frames) |
| SceneTree node not found on first frame | Use `CallDeferred` or wait a few frames before injection |
| Godot 4.5.1 vs newer versions | PCK must be packed with 4.5.1 exactly — newer versions rejected |
| Multiple module nodes conflict | Each module gets its own Node child under RMPController |
| Setting changes not persisted | Write config.ini on every setting change, not just on exit |

---

## Quick Reference

```csharp
// Game singletons
CombatManager.Instance
RunManager.Instance
NGame.Instance
NCombatRoom.Instance

// Godot SceneTree
((SceneTree)Engine.GetMainLoop()).Root
((SceneTree)Engine.GetMainLoop()).CurrentScene

// Logging
Log.Warn("[RMP] message");     // In-game log
GD.Print("[RMP] debug");       // Godot console
GD.PrintErr("[RMP] error");    // Godot error

// Multiplayer checks
Multiplayer.IsServer()          // Am I the host?
Multiplayer.GetUniqueId()       // My peer ID
Multiplayer.GetPeers()          // All connected peer IDs
```
