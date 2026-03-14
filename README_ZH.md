<div align="center">

# 杀戮尖塔2 联机上限解锁 (Remove Multiplayer Player Limit)

[**English**](README.md) | [**更改日志**](Changelog.md)

![Version](https://img.shields.io/badge/Version-0.0.4A-blue.svg)
![Game](https://img.shields.io/badge/Slay_The_Spire_2-Mod-red.svg)
![Platform](https://img.shields.io/badge/Platform-Windows%20|%20macOS-lightgrey.svg)

*一款《杀戮尖塔2》的联机人数上限解锁模组。打破原版 4 人的限制，喊上更多的好友一起爬塔吧！*

</div>

本模组突破了《杀戮尖塔2》原版的联机限制。默认提供完美适配的 **8 人**游玩体验，并可通过配置文件最高解锁至 **16 人**。

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

## ✨ 核心功能

* 👥 **突破人数限制：** 最高支持 16 人同时联机游玩（默认推荐 8 人）。**注意：超过 8 人时可能会出现渲染错误等问题。**
* 🏕️ **营地座位扩容：** 超过 4 人时，角色不会重叠在一起。营地会自动增加额外的前后排座位，并生成对应的“原木”背景，确保每个玩家都有位置。
* 💰 **商店阵列排布：** 多人同屏时，商店里的角色模型会自动排列成多行多列，告别拥挤和模型穿模。
* 🎁 **宝箱房自适应布局：** 遗物分配界面会根据当前房间的人数自动缩放，智能拆分为双排并居中对齐，让每个人都能清晰地选择遗物。
* 📝 **自定义房间大小：** 首次运行会自动生成 `config.json` 配置文件，允许你自由设置房间的人数上限（4-16人）。

## 🎮 玩家安装说明

### Windows

1. 从 **Releases** 页面下载最新的 `sts2-RMP-[version].zip` 压缩包。
2. 解压并将内部的 `RemoveMultiplayerPlayerLimit` 文件夹整体复制到游戏的 `<Slay the Spire 2>/mods/` 目录下。
3. 启动游戏，模组将自动启用。

### macOS (Apple Silicon)

> **注意：** 部分 macOS 玩家联机时会遇到 `unknown ca` / `BadCert`。当前版本会在多人握手阶段启用一个仅限 macOS 的 TLS 兼容补丁；如果你想恢复原始证书校验行为，可以在 `config.json` 里把 `macos_tls_workaround` 设为 `false`。

macOS 需要将模组放入 `.app` 包内部，并通过 Rosetta 2 运行游戏。

> **注意：** 尚未进行联机实测，仅能保证正确打开游戏并且右下角显示正常载入模组。

1. 从 **Releases** 页面下载最新的 `sts2-RMP-[version].zip` 压缩包。
2. 解压并将内部的 `RemoveMultiplayerPlayerLimit` 文件夹复制到：
   ```
   <Slay the Spire 2>/SlayTheSpire2.app/Contents/MacOS/mods/
   ```
3. 以下两种启动方式都绕过了 Steam 的正常启动流程。为避免出现 **"Steam failed to initialize"** 错误，请确保 **Steam 客户端正在后台运行**，并在游戏可执行文件旁创建 `steam_appid.txt` 文件：
   ```bash
   echo "2868840" > "$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/steam_appid.txt"
   ```
4. 通过 Rosetta 2 运行游戏，任选**其一**：

   **方式 A — 访达（推荐）：** 找到 `SlayTheSpire2.app`，右键 > **显示简介**，勾选 **"以 Rosetta 方式打开"**，然后直接双击 `SlayTheSpire2.app` 启动（**不要**通过 Steam 启动，Steam 可能会忽略 Rosetta 设置）。

   **方式 B — 终端：** 打开"终端"应用，输入以下命令：
   ```bash
   cd "$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS"
   arch -x86_64 "./Slay the Spire 2"
   ```

5. 启动游戏，模组将自动启用。

## ⚙️ 配置文件说明

首次启用模组并启动游戏后，模组目录下会自动生成 `config.json` 文件（路径：`mods/RemoveMultiplayerPlayerLimit/config.json`）。

```json
{
  "max_player_limit": 8,
  "macos_tls_workaround": true,
  "min_supported": 4,
  "max_supported": 16
}
```
