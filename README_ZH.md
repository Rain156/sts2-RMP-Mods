<div align="center">

# 杀戮尖塔2 联机上限解锁 (Remove Multiplayer Player Limit)

[**English**](README.md) | [**更改日志**](Changelog.md)

![Version](https://img.shields.io/badge/Version-0.0.4A-blue.svg)
![Game](https://img.shields.io/badge/Slay_The_Spire_2-Mod-red.svg)
![Platform](https://img.shields.io/badge/Platform-Windows%20|%20macOS%20|%20Linux-lightgrey.svg)

*一款《杀戮尖塔2》的联机人数上限解锁模组。打破原版 4 人的限制，喊上更多的好友一起爬塔吧！*

</div>

本模组突破了《杀戮尖塔2》原版的联机限制。默认提供完美适配的 **8 人**游玩体验，并可在游戏内设置中最高解锁至 **16 人**。

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
* 📝 **游戏内设置入口：** 可直接在游戏设置界面的「游戏设置」标签页中，Modding 行下方调整房间人数上限。macOS TLS 兼容补丁可通过 `config.ini` 手动操控。


## 🎮 玩家安装说明

### Windows

1. 从 **Releases** 页面下载最新的 `sts2-RMP-[version].zip` 压缩包。
2. 解压并将内部的 `RemoveMultiplayerPlayerLimit` 文件夹整体复制到游戏的 `<Slay the Spire 2>/mods/` 目录下。
3. 启动游戏，模组将自动启用。

### macOS (Apple Silicon)

> **注意：** 部分 macOS 玩家联机时会遇到 `unknown ca` / `BadCert`。当前版本会在多人握手阶段启用一个仅限 macOS 的 TLS 兼容补丁；如果你想恢复原始证书校验行为，可以手动编辑 `config.ini` 关闭它。

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

### Linux (Ubuntu)

Linux 的模组目录结构和 Windows 基本一致，但不同发行版里游戏可执行文件和 Godot 命令名可能不同。

1. 从 **Releases** 页面下载最新的 `sts2-RMP-[version].zip` 压缩包。
2. 解压并将内部的 `RemoveMultiplayerPlayerLimit` 文件夹整体复制到游戏的：
   ```
   <Slay the Spire 2>/mods/
   ```
3. 正常通过 Steam 或本地可执行文件启动游戏即可。
4. 启动游戏后，模组将自动启用。

> **兼容性说明：** 所有玩家仍然必须使用同一版本的模组；但现在允许每个人本地设置的人数上限不同，真正生效的入房人数以上主机配置为准。

> **Linux 排错：** 如果模组启动时因为 Harmony / `mm-exhelper.so` 报 `_Unwind_RaiseException` 而初始化失败，通常是系统运行库没有被游戏进程正确看到。一般安装 `libgcc-s1`、`libstdc++6` 和 `libunwind8` 就够了。

## ⚙️ 配置说明

打开游戏内的设置界面，在「游戏设置」标签页中往下滚动到 **Modding** 行的下方，即可实时调整房间人数上限（4–16）。

macOS TLS 兼容补丁仅可通过手动编辑 `config.ini` 来开关。

配置会保存到 `mods/RemoveMultiplayerPlayerLimit/config.ini`。

示例：

```ini
[macos]
tls_workaround=true

[multiplayer]
max_player_limit=8
```

> **从旧版本升级时请注意：** 如果你的模组目录里还留着旧版生成的 `mods/RemoveMultiplayerPlayerLimit/config.json`，请先手动删除一次再启动新版本。StS2 会把模组目录下的 JSON 当成 manifest 扫描，但 `config.ini` 是安全的。

## 鸣谢

感谢以下贡献者：

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
</div>


