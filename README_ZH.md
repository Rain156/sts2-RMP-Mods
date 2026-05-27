<div align="center">

# 杀戮尖塔2 联机上限解锁 Reforge

[**English**](README.md) | [**更新日志**](Changelog.md)

![Version](https://img.shields.io/badge/Version-0.1.8--beta-blue.svg)
![Game](https://img.shields.io/badge/Slay_The_Spire_2-Mod-red.svg)
![Platform](https://img.shields.io/badge/Platform-Windows%20|%20macOS%20|%20Linux-lightgrey.svg)
![Runtime](https://img.shields.io/badge/Runtime-Harmony--free-green.svg)

*一款无 Harmony / MonoMod 的《杀戮尖塔2》联机人数上限解锁模组，将原版 4 人房间扩展到 16 人。*

</div>

Reforge 版不是旧 Harmony 补丁的继续堆叠，而是一次重写：它保留 RMP 的目标，让更多玩家一起联机，同时改善多人营地、商店、宝箱房布局，并为 4 人以上队伍提供可选难度缩放。

`0.1.8-beta` 是面向《杀戮尖塔2》`beta 0.106.x` 附近版本的玩家测试版。

本测试版包含以下关键修复：

- 修复 `beta 0.106.1` 下因为游戏新增 `INetMessage.ShouldBuffer` 接口成员导致的模组加载失败。
- 修复 `beta 0.106` 中第一个宝箱后容易数据不同步的问题：移除 RMP 对远端开箱奖励的重复重放，交回原版一次性宝箱同步流程处理。
- 修复 5-16 人大厅流程：超过原版安全槽位的加入、准备状态和开局同步会走 RMP 扩展大厅协议。
- 玩家槽位使用 4-bit 表示 `0-15`，玩家列表长度使用 5-bit 表示，覆盖当前最多 16 人的目标。

<br>

<div align="center">
  <img src="img/combat.png" alt="战斗截图" width="800"/>
  <br><br>
  <img src="img/shop.png" alt="商店截图" width="800"/>
  <br><br>
  <img src="img/campfire.png" alt="营地截图" width="800"/>
  <br><br>
  <img src="img/defect.png" alt="角色排列截图" width="800"/>
</div>

<br>

## ✨ 核心功能

* 👥 **突破人数限制：** 将联机房间人数上限从 4 人提升到 16 人。`0.1.8-beta` 中所有房间固定按 16 人容量创建。
* 🏕️ **营地座位扩容：** 超过 4 人时，角色不会重叠在一起，而是自动排列到额外座位和队列中。
* 💰 **商店阵列排布：** 多人同屏时，商店里的角色模型会自动排列成更清晰的网格，减少拥挤和穿模。
* 🎁 **宝箱房自适应布局：** 遗物分配界面会根据人数缩放和重排，同时宝箱奖励同步交由游戏原版一次性同步流程处理。
* 📝 **游戏内设置入口：** 在游戏设置界面中加入难度缩放开关。旧版人数上限分页器已移除，因为当前测试版固定使用 16 人房间。
* ⚔️ **难度缩放：** 开启后，怪物血量、格挡及能力数值将在超过原版 4 人上限后继续提升。
* 🌐 **RMP 扩展大厅协议：** 为大房间快照、准备状态和开局流程提供模组网络协议，避免继续依赖原版不安全的大房间消息。
* 🍎 **macOS TLS 兼容补丁：** 通过 `config.ini` 为 macOS 联机中的 `unknown ca` / `BadCert` 问题提供开关。
* 🚫 **无 Harmony / MonoMod：** 不使用 `[HarmonyPatch]`、Transpiler 或运行时方法替换。Reforge 使用官方 Mod 入口、反射和注入的 Godot 节点。

## 🎮 玩家安装说明

### Windows

1. 下载 `sts2-RMP-0.1.8-beta.zip`。
2. 解压压缩包。
3. 将内部的 `RemoveMultiplayerPlayerLimit` 文件夹复制到：

   ```text
   <Slay the Spire 2>/mods/
   ```

4. 启动游戏，模组会由游戏的模组加载器载入。

### macOS (Apple Silicon)

macOS 版游戏可能需要将模组放入 `.app` 包内部，并通过 Rosetta 2 运行游戏。

> **注意：** 部分 macOS 玩家联机时会遇到 `unknown ca` / `BadCert`。Reforge 包含仅限 macOS 的 TLS 兼容补丁；如果你想恢复原始证书校验行为，可以编辑 `config.ini` 并设置 `tls_workaround=false`。

1. 下载 `sts2-RMP-0.1.8-beta.zip`。
2. 解压并将内部的 `RemoveMultiplayerPlayerLimit` 文件夹复制到：

   ```text
   <Slay the Spire 2>/SlayTheSpire2.app/Contents/MacOS/mods/
   ```

3. 如果直接启动游戏时出现 **"Steam failed to initialize"**，请保持 Steam 客户端在后台运行，并在游戏可执行文件旁创建 `steam_appid.txt`：

   ```bash
   echo "2868840" > "$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/steam_appid.txt"
   ```

4. 通过 Rosetta 2 运行游戏，任选其一：

   **方式 A - 访达：** 找到 `SlayTheSpire2.app`，右键 > **显示简介**，勾选 **"以 Rosetta 方式打开"**，然后直接启动该 app。

   **方式 B - 终端：**

   ```bash
   cd "$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS"
   arch -x86_64 "./Slay the Spire 2"
   ```

### Linux

Linux 使用与 Windows 相同的模组目录结构：

```text
<Slay the Spire 2>/mods/
```

之后正常通过 Steam 或本地可执行文件启动游戏即可。

> **兼容性说明：** 同一房间内的所有玩家应使用同一个模组版本。`0.1.8-beta` 的房间容量固定为 16；本地配置只控制难度缩放和 macOS TLS 兼容补丁。

## ⚙️ 配置说明

运行时配置保存在：

```text
mods/RemoveMultiplayerPlayerLimit/config.ini
```

当前可配置项：

* `tls_workaround`：macOS TLS 兼容补丁，仅对 macOS 有意义。
* `difficulty_scaling`：是否让怪物数值在 4 人以上继续缩放。

示例：

```ini
[macos]
tls_workaround=true

[multiplayer]
difficulty_scaling=true
```

旧配置中的 `max_player_limit` 在当前 Reforge 测试版中会被有意忽略。当前房间人数上限固定为 16。

> **从旧版本升级时请注意：** 如果你的模组目录里还留着旧版生成的 `mods/RemoveMultiplayerPlayerLimit/config.json`，请先删除它再启动 Reforge。《杀戮尖塔2》会把模组目录里的 JSON 文件当成 manifest 扫描，但 `config.ini` 是安全的。

## 🛠️ 构建

需要：

* .NET 9 SDK
* Godot 4.5.1，用于打包 PCK
* 《杀戮尖塔2》的 `sts2.dll` 和 `Steamworks.NET.dll`

只构建 DLL：

```bash
dotnet build -c Release
```

构建完整发布包：

```powershell
pwsh tools/build_release.ps1 -Configuration Release
```

如果要针对 beta 分支构建，建议显式使用当前已安装游戏的 `sts2.dll`：

```powershell
pwsh tools/build_release.ps1 -Configuration Release -Sts2AssemblyPath "<Slay the Spire 2>/data_sts2_windows_x86_64/sts2.dll"
```

发布脚本也会尝试自动查找当前安装的游戏程序集。这样做很重要，因为游戏 beta 更新可能改变 `INetMessage` 这类模组接口。

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
   <a href="https://github.com/VariianWrynn">
      <img src="https://github.com/VariianWrynn.png?size=96" alt="VariianWrynn" width="96" height="96" />
   </a>
</div>
