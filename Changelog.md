## 0.1.8 Version Changelog (English)

### Fixes
* Fixed the `v0.109.0` regression where 5+ player lobbies could remain stuck after every player readied and then start immediately when one player disconnected.
* Ready/unready button interception is now continuously maintained and self-healing, preventing late vanilla signal connections from routing extended-slot players back through the unsafe vanilla lobby protocol.
* Fixed extended-run black screens by enabling vanilla network message buffering before the asynchronous run transition.
* Stopped RMP lobby snapshots from repeatedly reinitializing and disposing the game's `PeerInputSynchronizer`, preserving `PeerInputMessage` handling throughout the lobby and run transition.
* Extended-lobby maintenance operations are isolated so one reflection or scene-node failure cannot disable the remaining join, ready, and begin-run protections.
* Settings focus-chain rebuilding now supports both the older 2-parameter and `v0.109.0` 3-parameter private method signatures.

## 0.1.8 更新日志（中文）

### 修复
* 修复 `v0.109.0` 中 5 人以上全部准备后仍无法开始、退出一名玩家后立刻开局的回归问题。
* 准备/取消准备按钮接管改为持续维护和自动修复，避免游戏较晚连接的原版回调让扩展槽位玩家重新走不安全的原版大厅协议。
* 修复扩展开局时的持续黑屏：在异步切换游戏场景前启用原版网络消息缓冲。
* RMP 大厅快照不再重复初始化并销毁游戏的 `PeerInputSynchronizer`，确保大厅及开局切换期间的 `PeerInputMessage` handler 保持有效。
* 扩展大厅的各项维护操作现在分别隔离异常，单个反射或场景节点失败不会禁用其他加入、准备和开局保护。
* 设置界面的焦点链重建现在同时兼容旧版 2 参数签名与 `v0.109.0` 的 3 参数私有方法签名。

-------------------------------------------------------------------

## 0.1.8 Version Changelog (English)

### Fixes
* Fixed beta 0.106.1 mod-load failure by building release packages against the currently installed game `sts2.dll`, whose `INetMessage` interface now requires `ShouldBuffer`.

## 0.1.8 更新日志（中文）

### 修复
* 修复 beta 0.106.1 下模组加载时报 `ReflectionTypeLoadException` 的问题：发布包现在会优先使用当前已安装游戏的 `sts2.dll` 编译，以匹配新增的 `INetMessage.ShouldBuffer` 接口成员。

-------------------------------------------------------------------

## 0.1.8 Version Changelog (English)

### Fixes
* Fixed beta 0.106 treasure-room desync by removing RMP's extra remote chest reward replay and leaving vanilla's one-off chest reward synchronization in control.
* Fixed 16-player lobbies getting stuck around the vanilla-safe slots by routing 5+ player joins through RMP snapshots with 4-bit slot IDs, and by using the extended ready/begin-run flow for fixed-16 lobbies.

## 0.1.8 版本更新日志（中文）

### 修复
* 修复 beta 0.106 中第一个宝箱后容易数据不同步的问题：移除 RMP 对远端开箱奖励的额外重放，避免重复生成奖励。
* 修复 16 人房间被卡在原版安全槽位附近的问题：5 人及以上加入改用 RMP 快照同步 4-bit 槽位，并让固定 16 人房间统一走扩展准备/开局流程。

-------------------------------------------------------------------

## 0.1.4 Version Changelog (English)

### Fixes
* Fixed duplicated Settings entries. Reopening the Settings screen no longer stacks extra "Max Players" / "Difficulty Scaling" rows.

## 0.1.4 版本更新日志（中文）

### 修复
* 修复设置界面重复注入的问题。现在反复打开设置界面时，不会再不断增加“房间人数上限 / 难度缩放”选项。

-------------------------------------------------------------------

## 0.0.6 Version Changelog (English)

### Features
* Added monster difficulty scaling for 5+ players: monster HP, block, and power amounts now continue to scale beyond the vanilla 4-player cap using the official formula.
* Added a "Difficulty Scaling" toggle in the Settings screen to enable or disable this feature.

### Improvements
* Introduced a fully independent mod network protocol channel (RMP protocol) that runs concurrently alongside the official packet system without interference.

## 0.0.6 版本更改日志（中文）

### 新功能
* 新增 5 人以上怪物难度缩放：怪物血量、格挡及能力数值将在原版 4 人上限之后继续按官方公式提升。
* 在游戏设置界面新增"难度缩放"开关，可随时启用或关闭该功能。

### 改进
* 引入完全独立的模组网络协议通道（RMP 协议），与官方数据包系统并行运行，互不干扰。


-------------------------------------------------------------------

## 0.0.5A Version Changelog (English)

### Features
* Added an in-game settings entry that lets players adjust the multiplayer lobby limit in real time from the Settings screen, supporting 4-16 players.
* Added Linux platform support.
* Added macOS platform support.

### Improvements
* Migrated the configuration format to config.ini and removed unused config entries.
* When the relic pool is exhausted and treasure rooms can no longer roll a relic, the reward now falls back to Strawberry.
* Improved multiplayer compatibility.

### Fixes
* Fixed join timeouts, state desync, and handshake failures caused by mismatched protocol bit widths.

## 0.0.5A 版本更改日志（中文）

### 新功能
* 新增游戏内设置入口，可在“游戏设置”界面中实时调整联机房间人数上限，支持 4-16。
* 新增 Linux 平台支持。
* 新增 MacOS 平台支持。

### 改进
* 将配置文件优化为 config.ini 文件格式，删除无用配置项。
* 当遗物被拿完，箱子无法开出遗物时，填充为“草莓”。
* 改进联机兼容性。

### 修复
* 修复因协议位宽不一样导致的联机加入超时、状态错位和握手失败的问题。


-------------------------------------------------------------------

## 0.0.4A Version Changelog (English)

### Improvements
* **Optimize** project structure
* **Optimize** the Relic Chest selection UI for 8+ Players

### Features
* **Add** localization
* **Add** a SKIP button on Relic Chest selection screen

### Fixes
* **Fixed** mod covers do not display issues

## 0.0.4A 版本更改日志 （中文）

### Improvements
* **优化**项目结构
* **优化**遗物选择界面，现支持8+以上的玩家进行遗物选择

### Features
* **添加**本地化功能
* **添加**跳过按钮，位于遗物宝箱选择界面

### Fixes
* **修复**模组封面不显示的问题
