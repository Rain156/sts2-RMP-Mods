# v1.0 Refactoring Development Log

> Harmony-free rewrite of RemoveMultiplayerPlayerLimit mod
> Date: 2026-04-04
> Tool: Claude Code + Opus 4.6 Model Max

---

## 1. Project Analysis Phase

### 1.1 Documentation Review

Read all 5 doc files to establish the refactoring constraints and target architecture:

| Document | Purpose |
|---|---|
| `doc/CLAUDE.md` | Core constraints (NO Harmony), tech stack, architecture layout |
| `doc/SKILL.md` | Full skill definition — replacement patterns, code templates, module interface |
| `doc/README.md` | v1.0 README with architecture diagram, migration checklist, install guide |
| `doc/feature-specs.md` | Per-module behavior specs: polling frequency, calculation formulas, UI layout |
| `doc/migration-guide.md` | Category-by-category Harmony→Reflection mapping, performance table, null safety |

### 1.2 Original Source Analysis

Read all 16 source files in `RMP Origin Src/`:

**Entry & Config:**
- `ModEntry.cs` — `[ModInitializer]` + `new Harmony("cn.remove.multiplayer.playerlimit").PatchAll()` + INI config

**Harmony Patches (to be removed):**
- `Patches.DifficultyScaling.cs` — `[HarmonyPatch]` on `Creature.ScaleMonsterHpForMultiplayer` (Prefix) + `MultiplayerScalingModel.ModifyBlockMultiplicative` / `ModifyPowerAmountGiven` (Transpiler)
- `Patches.RestSite.cs` — `[HarmonyPatch]` on `NRestSiteRoom._Ready` (Transpiler) + 3 event handler Prefixes
- `Patches.Merchant.cs` — `[HarmonyPatch]` on `NMerchantRoom.AfterRoomIsLoaded` (Postfix)
- `Patches.Settings.cs` — `[HarmonyPatch]` on `NSettingsScreen._Ready` / `OnSubmenuClosed` (Postfix) + `NPaginator.OnIndexChanged` (Postfix)
- `Patches.Treasure.cs` — 8 `[HarmonyPatch]` on `NTreasureRoomRelicCollection` and `TreasureRoomRelicSynchronizer`
- `Patches.Tls.cs` — `[HarmonyPatch]` on `TlsOptions.Client` (Prefix, macOS only)
- `Patches.Linux.cs` — Linux Harmony dependency preloader (`dlopen` RTLD_GLOBAL)

**Network (mixed Harmony + clean code):**
- `Network/LobbyPatches.cs` — 4 `[HarmonyPatch]` on `NetHostGameService`, `StartRunLobby`
- `Network/SerializationPatches.cs` — 6 Transpiler patches changing bit widths (SlotId 2→4, LobbyList 3→5)
- `Network/TranspilerUtils.cs` — Pure IL manipulation utility
- `Network/ProtocolConfig.cs` — Clean (no Harmony)
- `Network/RmpProtocol.cs` — Clean (no Harmony)
- `Network/RmpNetMessages.cs` — Clean (no Harmony, auto-registered `INetMessage`)
- `Network/RmpNetActions.cs` — Clean (no Harmony, auto-registered `INetAction`)
- `Network/SteamLobbyHelper.cs` — Clean (pure reflection)

### 1.3 Game Source Investigation

Queried decompiled source (`Slay the Spire 2/`) for key API signatures:

- `RunManager.State` → **private** property → must access via reflection
- `Creature.ScaleMonsterHpForMultiplayer(EncounterModel?, int playerCount, int actIndex)` → 3 params
- `CombatState.AddCreature()` calls scaling with `Players.Count` and `RunState.CurrentActIndex`
- `MultiplayerApi` in Godot 4.5.1 → `MultiplayerPeer.GetConnectionStatus()` (not on MultiplayerApi directly)

---

## 2. Architecture Design

### 2.1 Module System

Designed `IRMPModule` interface per doc spec:

```csharp
public interface IRMPModule
{
    string Name { get; }
    void Initialize(ConfigManager config, ReflectionCache cache);
    Node? CreateNode();  // Injected under RMPController in SceneTree
    void Cleanup();
}
```

### 2.2 Directory Structure

```
src/
├── Core/           → ModEntry, ConfigManager, IRMPModule, ProtocolConfig
├── Infrastructure/ → ReflectionCache, SceneMonitor, GameStateAccessor, Localization
├── Features/       → 6 independent modules (Lobby, Difficulty, Campfire, Shop, Treasure, Settings)
├── Network/        → RmpProtocol, Messages, Actions, LobbyManager, SteamHelper
└── Platform/       → PlatformDetector, MacOsTlsWorkaround
```

### 2.3 Harmony Replacement Strategy

| Original Technique | Replacement | Polling Frequency |
|---|---|---|
| `[HarmonyPrefix]` ref param | `_Process()` state enforcement | 30-60 frames |
| `[HarmonyPostfix]` | `_Process()` detect-and-act | 10-30 frames |
| `[HarmonyTranspiler]` IL rewrite | RMP protocol supplemental channel | N/A |
| `harmony.PatchAll()` | `[ModInitializer]` + module loop | Once at init |

### 2.4 Serialization Bit-Width Decision

The original Transpiler patches change serialization bit widths (SlotId 2→4 bits, LobbyList 3→5 bits). Without IL manipulation:

- **Chosen approach**: Use the RMP protocol channel to transmit extended lobby data alongside vanilla protocol
- **Rationale**: Architecturally cleaner than IL patching, doesn't depend on method signatures that change between game versions
- **Limitation**: Vanilla protocol remains at original bit widths; the mod layer handles the extension

---

## 3. Implementation Phase

### 3.1 Project Skeleton

Created 3 files:
- `RemoveMultiplayerPlayerLimit.csproj` — `Godot.NET.Sdk/4.5.1`, .NET 9.0, `sts2.dll` ref, **NO** `0Harmony.dll`
- `RemoveMultiplayerPlayerLimit.json` — Mod manifest v1.0.0
- `project.godot` — Godot project config for PCK packing

### 3.2 Core Layer (4 files)

| File | Lines | Description |
|---|---|---|
| `Core/IRMPModule.cs` | 24 | Module interface contract |
| `Core/ProtocolConfig.cs` | 55 | Protocol constants + runtime config (ported from Network/) |
| `Core/ConfigManager.cs` | 130 | INI config read/write, legacy JSON migration, singleton |
| `Core/ModEntry.cs` | 75 | `[ModInitializer]`, module registration, SceneTree node injection |

Key decision: `ModEntry.Initialize()` creates all modules, calls `Initialize()` on each, then injects a root `RMPController` Node with each module's child node.

### 3.3 Infrastructure Layer (4 files)

| File | Lines | Description |
|---|---|---|
| `Infrastructure/ReflectionCache.cs` | 150 | Cached `FieldInfo`/`MethodInfo`/`PropertyInfo`/`Type` lookups with null-safe helpers |
| `Infrastructure/SceneMonitor.cs` | 55 | `FindNodeByName`, `FindNodeOfType<T>`, `IsSceneActive`, `GetRoot` |
| `Infrastructure/GameStateAccessor.cs` | 70 | Player count (via reflection on private `RunManager.State`), multiplayer checks, effective player count |
| `Infrastructure/Localization.cs` | 55 | PCK-based i18n from `res://RemoveMultiplayerPlayerLimit/localization/{lang}.json` |

Key decision: `RunManager.State` is a **private property** in the game — used `PropertyInfo` reflection instead of direct access.

### 3.4 Feature Modules (6 files)

#### LobbyExpanderModule.cs
- Polls every 60 frames for active `StartRunLobby` instance
- Enforces `MaxPlayers` via reflection on `<MaxPlayers>k__BackingField`
- Updates Steam lobby member limit via `SteamLobbyHelper`

#### DifficultyModule.cs
- Monitors combat state transitions (non-combat → combat)
- When scaling enabled + 5+ players: the game already applies the correct formula
- Known limitation: Without Harmony Prefix on `ScaleMonsterHpForMultiplayer`, cannot clamp player count *before* the method runs
- Documented with TODO for HP correction via `MonsterMaxHpBeforeModification` reflection

#### CampfireModule.cs
- Polls every 10 frames for `NRestSiteRoom`
- When found with 5+ players: creates extra character containers by cloning
- Calculates positions using left/right front/back offset system (preserved from original)
- Spawns extra log sprites via `DuplicateShiftedNode`

#### ShopModule.cs
- Polls every 10 frames for `NMerchantRoom`
- Repositions `PlayerVisuals` into row×column grid
- Layout constants preserved from original: `ForwardShiftX`, `RowStepY`, `ColumnStepX`

#### TreasureModule.cs (most complex — 250+ lines)
- Manages holder expansion, grid layout, skip button, vote resolution
- 13 cached `FieldInfo`/`MethodInfo` for game internals
- Skip button created via `PreloadManager.Cache.GetScene("ui/choice_selection_skip_button")`
- Vote resolution logic (including relic fights, consolation prizes) preserved from original

#### SettingsModule.cs
- Polls every 30 frames for `NSettingsScreen`
- Injects divider + player limit paginator + difficulty scaling toggle
- Paginator created by instantiating `screens/paginator` scene, transplanting children to real `NPaginator`
- Focus chain rebuilt via reflection on `NSettingsPanel.GetSettingsOptionsRecursive`

### 3.5 Network Layer (5 files)

| File | Change from Original |
|---|---|
| `RmpProtocol.cs` | Ported as-is (was already Harmony-free) |
| `RmpNetMessages.cs` | Ported as-is (auto-registered `INetMessage`) |
| `RmpNetActions.cs` | Ported as-is (auto-registered `INetAction`) |
| `SteamLobbyHelper.cs` | Ported as-is (pure reflection) |
| `LobbyManagerModule.cs` | **New** — replaces `LobbyPatches.cs` with `_Process()` polling |

`LobbyManagerModule` design:
- Polls every 30 frames for active lobby via `RunManager._startRunLobby` reflection
- Detects lobby creation → sets MaxPlayers, binds RMP protocol
- Detects player count changes → re-syncs, broadcasts config
- Detects lobby destruction → unbinds protocol

### 3.6 Platform Layer (2 files)

| File | Description |
|---|---|
| `PlatformDetector.cs` | Static `IsMacOS` / `IsLinux` / `IsWindows` / `IsMacOSArm64` |
| `MacOsTlsWorkaround.cs` | TLS workaround module — monitors multiplayer connection state, provides `CreateUnsafeTlsOptions()` via reflection |

`Patches.Linux.cs` was **completely removed** — it only existed to preload Harmony's native dependencies (`libunwind`, `libgcc_s`). Without Harmony, no native libraries are needed.

---

## 4. Build & Verification

### 4.1 First Build — 14 Errors

| Error Category | Count | Root Cause |
|---|---|---|
| GD0002 Missing `partial` | 8 | Outer classes with nested Godot Node subclasses need `partial` modifier |
| CS1061 `RunManager.State` | 3 | `State` is private — used reflection PropertyInfo |
| CS7036 `ScaleMonsterHpForMultiplayer` | 1 | Method takes 3 params, not 1 |
| CS1061 `MultiplayerApi.GetConnectionStatus` | 1 | API is on `MultiplayerPeer`, not `MultiplayerApi` |

### 4.2 Fixes Applied

1. Added `partial` to all 8 module classes containing nested Godot Node classes
2. Rewrote `GameStateAccessor` to use reflection for `RunManager.State`
3. Rewrote `DifficultyModule` to document the HP scaling limitation
4. Fixed `MacOsTlsWorkaround` to use `MultiplayerPeer.GetConnectionStatus()`
5. Cleaned up unused imports in `DifficultyModule`

### 4.3 Final Build — Success

```
dotnet build → 0 warnings, 0 errors
```

### 4.4 Harmony-Free Verification

```bash
grep -r "^using HarmonyLib\|^\[HarmonyPatch\|\bharmony\.\w\|AccessTools\." src/
# Result: No matches found
```

All "Harmony" mentions in the codebase are exclusively in `///` XML doc comments documenting what each module replaces.

---

## 5. Files Removed (from original)

| File | Reason |
|---|---|
| `Patches.Linux.cs` | Only preloaded Harmony native deps — no longer needed |
| `Network/TranspilerUtils.cs` | Pure IL manipulation utility — no longer needed |
| `Network/SerializationPatches.cs` | Transpiler patches — replaced by RMP protocol approach |
| `0Harmony.dll` reference | Intentionally removed from .csproj |

---

## 6. Build Tools Updated

Updated `tools/build_release.ps1` and `tools/build_release.sh`:
- Godot path: auto-detect from `libs/` directory (was hardcoded to external path)
- Fallback chain: `$env:GODOT_PATH` → `libs/` → `PATH`
- Added banner, step progress, and final summary with file sizes
- Added `config.ini` template copy to release directory
- Preserved one-click installer generation (Windows only, in ZIP)

---

## 7. Known Limitations & Future Work

### 7.1 Difficulty Scaling (HP)
The original Harmony Prefix intercepted `playerCount` before `ScaleMonsterHpForMultiplayer` was called, allowing it to clamp to 4 when scaling was disabled. Without Harmony, the game applies the full player count. When scaling is disabled with 5+ players, monster HP will still be scaled for the actual count.

**Fix path**: Read `Creature.MonsterMaxHpBeforeModification` via reflection after combat init, then re-apply `ScaleMonsterHpForMultiplayer` with clamped count.

### 7.2 Block/Power Scaling
The original Transpiler intercepted `_runState.Players.Count` inside `MultiplayerScalingModel` methods. Without IL modification, the player count used for block/power scaling cannot be clamped.

**Fix path**: Override the result after the scaling method returns, or maintain a shadow `MultiplayerScalingModel` that uses the effective count.

### 7.3 Serialization Bit Widths
The original Transpiler patches expanded `LobbyPlayer.slotId` from 2→4 bits and lobby list length from 3→5 bits. Without IL modification, the vanilla protocol serialization cannot be changed.

**Current state**: The RMP protocol channel can supplement vanilla data, but the full integration is pending.

### 7.4 Rest Site Event Handlers
The original Harmony Prefixes on `OnPlayerChangedHoveredRestSiteOption`, `OnBeforePlayerSelectedRestSiteOption`, and `OnAfterPlayerSelectedRestSiteOption` prevented index-out-of-bounds crashes when more than 4 players were at the campfire. The current SceneTree approach creates the extra containers, but doesn't intercept the event handlers.

**Fix path**: Monitoring these events via Godot signals or _Process polling for error states.

---

## 8. Summary

| Metric | Value |
|---|---|
| Source files created | 21 |
| Total modules | 9 (6 feature + 1 network + 1 platform + 1 lobby manager) |
| Harmony references | 0 (in code), comments only |
| Build result | 0 errors, 0 warnings |
| Files removed from original | 3 (Linux.cs, TranspilerUtils.cs, SerializationPatches.cs) |
| Platform support | Windows, macOS (native ARM64), Linux |
