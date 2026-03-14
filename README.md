<div align="center">

# Remove Multiplayer Player Limit

[**简体中文**](README_ZH.md) | [**Changelog**](Changelog.md)

![Version](https://img.shields.io/badge/Version-0.0.4A-blue.svg)
![Game](https://img.shields.io/badge/Slay_The_Spire_2-Mod-red.svg)
![Platform](https://img.shields.io/badge/Platform-Windows%20|%20macOS-lightgrey.svg)

*A Slay the Spire 2 mod that increases the vanilla 4-player multiplayer lobby limit. Gather more friends and climb the Spire together!*

</div>

This mod elegantly increases the multiplayer lobby limit. By default, it perfectly supports **8 players** and can be configured to support up to **16 players**.

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
* 📝 **Customizable Limit:** Easily adjust your preferred maximum player count (4-16) via an auto-generated `config.json` file.

## 🎮 Installation

### Windows

1. Download the latest `sts2-RMP-[version].zip` from the **Releases** page.
2. Extract the archive and copy the inner `RemoveMultiplayerPlayerLimit` folder to your game directory: `<Slay the Spire 2>/mods/`.
3. Launch the game. The mod will be enabled automatically.

### macOS (Apple Silicon)

macOS requires placing the mod inside the `.app` bundle and running the game under Rosetta 2.

> **Note:** Some macOS players hit `unknown ca` / `BadCert` errors when joining multiplayer. This mod now enables a macOS-only TLS workaround during multiplayer handshakes. If you need the original behavior, set `macos_tls_workaround` to `false` in `config.json`.

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

## ⚙️ Configuration

After launching the game with the mod enabled for the first time, a `config.json` file will be generated in the mod's folder (`mods/RemoveMultiplayerPlayerLimit/config.json`).

```json
{
  "max_player_limit": 8,
  "macos_tls_workaround": true,
  "min_supported": 4,
  "max_supported": 16
}
```
