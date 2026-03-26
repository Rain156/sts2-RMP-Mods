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