<div align="center">

# Remove Multiplayer Player Limit

[**简体中文**](README_ZH.md) | [**Changelog**](Changelog.md)

![Version](https://img.shields.io/badge/Version-0.0.6-blue.svg)
![Game](https://img.shields.io/badge/Slay_The_Spire_2-Mod-red.svg)
![Platform](https://img.shields.io/badge/Platform-Windows%20|%20macOS%20|%20Linux-lightgrey.svg)

*A Slay the Spire 2 mod that increases the vanilla 4-player multiplayer lobby limit. Gather more friends and climb the Spire together!*

</div>

This mod elegantly increases the multiplayer lobby limit. By default, it perfectly supports **8 players** and can be configured up to **16 players** from the in-game settings screen.

<br>

<div align="center">
  <img src="img/combat.png" alt="Screenshot 1" width="800"/>
  <br><br>
  <img src="img/shop.png" alt="Screenshot 2" width="800"/>
  <br><br>
  <img src="img/campfire.png" alt="Screenshot 3" width="800"/>
  <br><br>
  <img src="img/defect.png" alt="Screenshot 4" width="800"/>
</div>

<br>

## ✨ Core Features

* 👥 **Expanded Multiplayer:** Increases the maximum lobby size up to 16 players (default is 8). **Note: Lobbies with more than 8 players may experience rendering errors and UI issues.**
* 🏕️ **Expanded Campfire Seating:** When there are more than 4 players, character models will not overlap. Campfires automatically generate front and back rows, complete with additional background logs for everyone to sit on.
* 💰 **Organized Shop Layout:** When visiting the merchant with a large group, player models are automatically arranged into neat grids (rows and columns) to prevent crowding and overlapping.
* 🎁 **Smart Treasure Room:** The relic selection screen automatically scales, intelligently splitting **relic slots** into two perfectly centered rows when needed.
* 📝 **Customizable Limit:** Adjust the maximum player count (4–16) directly from the in-game settings screen, under the General tab below the Modding section. The macOS TLS workaround can be toggled via `config.ini`.
* ⚔️ **Difficulty Scaling:** When enabled, monster HP, block, and power amounts continue to scale beyond the vanilla 4-player cap, keeping combat challenging for game. Can be toggled from the settings screen.

## 🎮 Installation

### Windows

1. Download the latest `sts2-RMP-[version].zip` from the **Releases** page.
2. Extract the archive and copy the inner `RemoveMultiplayerPlayerLimit` folder to your game directory: `<Slay the Spire 2>/mods/`.
3. Launch the game. The mod will be enabled automatically.

### macOS (Apple Silicon)

macOS requires placing the mod inside the `.app` bundle and running the game under Rosetta 2.

> **Note:** Some macOS players hit `unknown ca` / `BadCert` errors when joining multiplayer. This mod now enables a macOS-only TLS workaround during multiplayer handshakes. If you need the original behavior, edit `config.ini` to disable the workaround.

1. Download the latest `sts2-RMP-[version].zip` from the **Releases** page.
2. Extract the archive and copy the inner `RemoveMultiplayerPlayerLimit` folder to:
   ```
   <Slay the Spire 2>/SlayTheSpire2.app/Contents/MacOS/mods/
   ```
3. Both launch methods below bypass Steam's normal launch flow. To avoid a **"Steam failed to initialize"** error, make sure the **Steam client is running** in the background and create a `steam_appid.txt` file next to the game executable:
   ```bash
   echo "2868840" > "$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/steam_appid.txt"
   ```
4. Run the game under Rosetta 2 using **one** of these methods:

   **Option A — Finder (recommended):** Navigate to `SlayTheSpire2.app`, right-click > **Get Info**, and check **"Open using Rosetta"**. Then double-click `SlayTheSpire2.app` directly to launch (do **not** launch through Steam, as it may override the Rosetta setting).

   **Option B — Terminal:** Open Terminal and run:
   ```bash
   cd "$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS"
   arch -x86_64 "./Slay the Spire 2"
   ```

5. The mod will be enabled automatically on launch.

### Linux (Ubuntu)

Linux uses the same mod folder layout as Windows, but the game executable and Godot binary names vary by distro/package.

1. Download the latest `sts2-RMP-[version].zip` from the **Releases** page.
2. Extract the archive and copy the inner `RemoveMultiplayerPlayerLimit` folder to:
   ```
   <Slay the Spire 2>/mods/
   ```
3. Start the game normally from Steam or your local executable.
4. The mod will be enabled automatically on launch.

> **Compatibility note:** All players must use the same mod version. Local settings may differ safely; only the host's configured limit determines how many players can actually join the lobby.

> **Linux troubleshooting:** If the mod fails during startup with a Harmony / `mm-exhelper.so` error mentioning `_Unwind_RaiseException`, make sure your system runtime libraries are available to the game process. Installing `libgcc-s1`, `libstdc++6`, and `libunwind8` is usually sufficient.

## ⚙️ Configuration

The **Max Players** paginator lets you adjust the lobby player limit (4–16) in real time. A **Difficulty Scaling** toggle is also available to control whether monster stats scale beyond 4 players.

The macOS TLS compatibility workaround can only be changed by editing `config.ini` manually.

Values are saved to `mods/RemoveMultiplayerPlayerLimit/config.ini`.

Example:

```ini
[macos]
tls_workaround=true

[multiplayer]
max_player_limit=8
difficulty_scaling=true
```

> **Important for upgrading from older releases:** If you already have `mods/RemoveMultiplayerPlayerLimit/config.json` from an older version, delete that file once before launching the new build. StS2 scans JSON files in the mod folder as manifests, but `config.ini` is safe.

## Contributors

Special thanks to the following contributors:

<div align="center">
   <a href="https://github.com/Guchen1">
      <img src="https://github.com/Guchen1.png?size=96" alt="Guchen1" width="96" height="96" />
   </a>
   <a href="https://github.com/Lemon2ee">
      <img src="https://github.com/Lemon2ee.png?size=96" alt="Lemon2ee" width="96" height="96" />
   </a>
   <a href="https://github.com/DawningW">
      <img src="https://github.com/DawningW.png?size=96" alt="DawningW" width="96" height="96" />
   </a>
   <a href="https://github.com/VariianWrynn">
      <img src="https://github.com/VariianWrynn.png?size=96" alt="VariianWrynn" width="96" height="96" />
   </a>
</div>

