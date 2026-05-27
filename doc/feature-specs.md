# RMP Feature Module Specifications

## Module 1: LobbyExpander

**Purpose**: Override the hardcoded 4-player multiplayer limit.

**Behavior**:
- On mod init, find the max player count field/constant via reflection
- Continuously enforce the configured limit (host-side)
- Client-side: allow joining lobbies beyond 4
- Validation: clamp to 4–16 range

**Reflection Targets** (Claude Code must verify exact names in source):
```
Likely candidates:
- MultiplayerManager.MAX_PLAYERS or similar constant
- LobbySettings.maxPlayers or similar field
- CreateLobby() method parameter validation
- JoinLobby() method player count check
- UI lobby creation screen player cap
```

**Polling Frequency**: Every 60 frames (lobby state doesn't change rapidly)

---

## Module 2: DifficultyScaling

**Purpose**: Scale monster stats for 5+ player lobbies using the official formula.

**Behavior**:
- Monitor for combat start (state transition: non-combat → combat)
- If player count > 4 AND difficulty scaling is enabled:
  - Read all monster instances from CombatManager
  - Apply scaling formula to HP, block, and power values
  - Apply once per combat (track with boolean flag)
- Reset tracking on combat end

**Scaling Formula** (official, extrapolated from 2→3→4 player progression):
```
Claude Code must find the exact formula by analyzing:
- How HP scales from 1→2→3→4 players in the source
- The multiplier or additive factor per additional player
- Whether it's linear, multiplicative, or formula-based
- Whether block and power use the same formula or different ones
```

**Polling Frequency**: Every frame during combat state transitions, then stops

---

## Module 3: CampfireLayout

**Purpose**: Arrange character models in multiple rows at campfire for 5+ players.

**Behavior**:
- Detect campfire scene activation
- If player count > 4:
  - Calculate seat positions for front row (4 seats) and back row (overflow)
  - Move character Node2D positions accordingly
  - Spawn additional log/bench sprites for back row seating
- Reset when leaving campfire

**Seat Calculation**:
```
Front row: 4 seats at standard positions (vanilla behavior)
Back row:  (playerCount - 4) seats, offset Y position, slightly elevated
Spacing:   Equal horizontal distribution within row width
Logs:      One log sprite per 2 back-row seats
```

**Polling Frequency**: Check every 10 frames; act once per campfire visit

---

## Module 4: ShopLayout

**Purpose**: Arrange player models in a grid when visiting the merchant.

**Behavior**:
- Detect shop scene activation
- If player count > 4:
  - Calculate grid dimensions (rows × columns) to fit all players
  - Reposition player character nodes into the grid
  - Prevent overlapping and crowding
- Reset when leaving shop

**Grid Calculation**:
```
columns = ceil(sqrt(playerCount))
rows = ceil(playerCount / columns)
cell_width = available_width / columns
cell_height = available_height / rows
position[i] = (col * cell_width + offset_x, row * cell_height + offset_y)
```

**Polling Frequency**: Check every 10 frames; act once per shop visit

---

## Module 5: TreasureRoom

**Purpose**: Scale relic selection UI for large groups.

**Behavior**:
- Detect treasure/reward screen activation
- If relic slots exceed what fits in one row:
  - Split into two rows
  - Center each row horizontally
  - Adjust vertical spacing
- Reset on screen close

**Layout Calculation**:
```
max_per_row = 4 (or dynamically based on available width)
if (slot_count > max_per_row):
    row1_count = ceil(slot_count / 2)
    row2_count = slot_count - row1_count
    Center each row independently
```

**Polling Frequency**: Check every 10 frames; act once per reward screen

---

## Module 6: SettingsUI

**Purpose**: Inject mod configuration controls into the game's settings screen.

**Controls**:
1. **Max Players Paginator**: Left/right arrows to select 4–16
2. **Difficulty Scaling Toggle**: On/off checkbox

**Behavior**:
- Monitor for settings screen appearance in SceneTree
- When found, inject controls below the "Modding" section in the General tab
- Wire up Godot signals for user interaction
- Persist changes to config.ini immediately on change

**Implementation Notes**:
- Study existing game settings controls for style consistency
- Use the same Godot themes/fonts the game uses
- Paginator: use same pattern as game's volume/resolution controls
- Toggle: match game's existing checkbox style

**Polling Frequency**: Check every 30 frames; act once per settings open

---

## Module 7: RMPProtocol

**Purpose**: Independent mod network channel for RMP-specific communication.

**Messages**:
- `RMPHandshake`: Exchange mod version and config on lobby join
- `RMPConfigSync`: Host broadcasts max player limit to all clients
- `RMPDifficultySync`: Host broadcasts difficulty scaling state

**Behavior**:
- Inject a custom RPC-capable Node into the SceneTree
- Register RPC methods for RMP protocol messages
- On lobby creation: host broadcasts config to all joining players
- On lobby join: client receives and applies host config
- Runs in parallel with game's packet system — never modifies game packets

**Implementation**:
```csharp
public partial class RMPChannel : Node
{
    [Rpc(MultiplayerApi.RpcMode.Authority,
         TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void SyncConfig(int maxPlayers, bool difficultyScaling)
    {
        ConfigManager.Instance.ApplyHostConfig(maxPlayers, difficultyScaling);
    }
}
```

---

## Module 8: MacOSTlsModule

**Purpose**: Work around macOS TLS certificate validation errors in multiplayer.

**Behavior**:
- Only active on macOS (detected via PlatformDetector)
- On multiplayer connection attempt, apply TLS workaround if enabled in config
- Toggleable via config.ini `[Platform] macos_tls_workaround = true`

**Implementation Notes**:
- The original workaround likely patches the TLS certificate validation
- Without Harmony, explore:
  1. Environment variable approach (`SSL_CERT_FILE`, `GODOT_TLS_*`)
  2. Godot TLS configuration API if accessible
  3. Reflection into Godot's internal TLS settings
  4. If none work, document as a limitation and suggest system-level fix

**Claude Code must investigate**: How the original Harmony-based TLS fix worked, and what reflection-accessible alternatives exist.
