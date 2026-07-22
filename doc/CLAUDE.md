# CLAUDE.md

## Project
sts2-RMP-Mods: Slay the Spire 2 — Remove Multiplayer Player Limit.
Increases the vanilla 4-player lobby cap to 4–16 players, with campfire/shop/treasure UI adaptation, difficulty scaling, RMP network protocol, and macOS TLS fix.

## CRITICAL — Refactoring in Progress
This project is being refactored to COMPLETELY REMOVE Harmony/MonoMod.
The v1.0 target is a 100% Harmony-free codebase.

## Core Constraints — ABSOLUTE RULES
- **NEVER** use `[HarmonyPatch]`, `[HarmonyPrefix]`, `[HarmonyPostfix]`, `[HarmonyTranspiler]`
- **NEVER** import `HarmonyLib`, `0Harmony`, `MonoMod`
- **NEVER** call `harmony.PatchAll()` or any runtime method replacement
- **NEVER** add `Lib.Harmony` as a NuGet/DLL reference
- **NEVER** use `.json` for config files (game scans .json as manifests → use `.ini`)

## Replacement Techniques
- `[ModInitializer("Initialize")]` as sole entry point
- `ReflectionCache` for cached field/method access to game internals
- Godot `SceneTree` Node injection for UI and scene modifications
- `_Process()` polling for state change detection (replaces Prefix/Postfix hooks)
- Custom Godot RPC for mod-to-mod network communication

## Tech Stack
- C# / .NET 9.0+ / Godot 4.5.1 (MegaDot)
- Game assembly: sts2.dll (reference only)
- Entry: `[ModInitializer("Initialize")]`
- Config: config.ini (INI format)
- PCK: Must use Godot 4.5.1 to pack

## Architecture
- `src/Core/` → ModEntry, lifecycle, config
- `src/Infrastructure/` → ReflectionCache, SceneMonitor, GameStateAccessor
- `src/Features/` → Each feature is an independent module (LobbyExpander, DifficultyScaling, CampfireLayout, ShopLayout, TreasureRoom, SettingsUI)
- `src/Network/` → RMP protocol (independent mod network channel)
- `src/Platform/` → Platform detection, macOS TLS workaround

## Game Source Code
Decompiled source: [USER SET THIS PATH]
Priority analysis targets:
1. Multiplayer max player limit field/constant
2. Monster HP/block/power initialization with player count scaling
3. Campfire/shop/treasure room scene node structure
4. Settings screen UI tree structure
5. Multiplayer lobby creation/join validation

## Build
```bash
dotnet build              # Debug
dotnet build -c Release   # Release → auto-copies to game mods/
```

## Key Facts
- Mod ID: RemoveMultiplayerPlayerLimit
- Current Harmony-based version: 0.0.6
- Target Harmony-free version: 1.0.0
- Platforms: Windows, macOS (ARM64 + x64), Linux
- All players must use same mod version
- Only host's limit config determines lobby size
