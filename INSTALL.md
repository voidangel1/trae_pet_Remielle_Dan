# ClawdPet 安装与使用说明

ClawdPet 是一个会根据 Trae Agent 工作状态自动切换动画的桌面宠物。

v2.0 起有三种安装方式，**任选其一，不要同时开启**：

- **EXE 安装器**（最简单，推荐分发用）：`release\ClawdPet-Setup-v2.0.exe` 单文件，双击即用——完整安装（Hooks 六态 + 官方四态切换 + 安装后指引）/ 轻量安装（仅官方四态）/ 卸载（v2.0 + 旧版 1.0-1.4 全部还原官方）。
- **原生模式**（脚本部署，等效于安装器的完整安装，Trae v3.4+）：把宠物皮肤移植进 Trae 原生悬浮窗，零独立进程，随 Trae 启停。见「一 A」。
- **独立模式**（v1.x 经典）：独立 Electron 桌宠 + Hooks 驱动。见「一 B」及后续章节。

支持：

- Trae CN
- Trae 国际版
- Windows 10 / Windows 11

## 一 A、原生模式安装（推荐）

要求 Trae CN / 国际版 v3.4 及以上（含「悬浮窗」功能）。

1. 打开解压后的 `ClawdPet` 文件夹。
2. 右键 `native-pet\deploy-native-pet.ps1`。
3. 选择「使用 PowerShell 运行」，在 UAC 弹窗中点击「是」（仅写入 Trae 安装目录那一步需要管理员）。
4. 重启 Trae，宠物出现在屏幕右下角。

脚本会自动完成：

- 部署 4 张动画 webp 皮肤（静立 / 忙碌 / 卖力 / 悬停拖拽）。
- 备份 Trae 官方素材到 `native-pet\assets\official-backup\`。
- 给 Trae 主进程 `main.js` 打 `qF` 补丁（解锁官方灰度关闭的 traebao 宠物形象）。
- 把 `trae.floatingChat.buttonIcon` 切换为 `traebao`，并关闭独立模式的自动启动（`-KeepClawd` 参数可保留双开）。

原生模式常见问题：

- **Trae 升级后宠物变回官方形象**：升级会还原官方素材与 main.js，重跑一次 `deploy-native-pet.ps1` 即可。
- **想换回官方形象 / 独立模式**：`powershell -File native-pet\deploy-native-pet.ps1 -Restore`。
- **想调整宠物大小**：Trae 设置中搜 `trae.floatingChat.size`（0.5–2 倍）。
- 状态语义与独立版一致：Trae Hooks 驱动六态（提问→thinking、编辑/查看/调用工具→editing、**模型提问→notification 常驻等待回答**、异常→error）。状态服务器（127.0.0.1:27416）由首个 Hook 事件自动拉起，不可用时回落官方四态（会话忙碌）。宠物尺寸默认 140px（可在 Trae 设置 `trae.floatingChat.size` 0.5–2 倍微调）。**点击 Trae 状态栏 Pet 图标可切换 Hooks 六态 / 官方四态**。

## 一 B、安装前准备（独立模式）

1. 安装 Trae CN 或 Trae 国际版。
2. 完全退出 Trae，包括后台仍在运行的 Trae 窗口。
3. 解压收到的 `ClawdPet` 压缩包到一个固定目录。
4. 不要在安装完成后删除或移动这个目录。宠物运行时需要使用其中的 Electron 运行时和主题文件。

建议解压到没有特殊权限限制的目录，例如：

```text
D:\Apps\ClawdPet
```

## 二、一键安装

1. 打开解压后的 `ClawdPet` 文件夹。
2. 右键点击 `install.ps1`。
3. 选择“使用 PowerShell 运行”。
4. 等待窗口显示“安装完成”。
5. 重新启动 Trae。

安装脚本会自动完成：

- 检测 Trae CN 或 Trae 国际版。
- 安装 `Trae Pet Bridge` 扩展。
- 配置 Trae 全局 Hooks。
- 配置桌面宠物自动启动。
- 将宠物路径设置为当前解压目录。

### 重要：手动启用 Hooks（Trae 1.107 及以上版本必须）

新版 Trae 出于安全考虑，**不会自动执行 Hooks 配置文件中的命令**。安装完成后必须手动启用一次，否则宠物不会有任何状态变化：

1. 打开 Trae **设置 → Hooks**。
2. 在「已配置的 Hooks」→ **全局** 下，打开开关，并在弹出的安全警示中点击「启用」。
3. 在「执行模式」中，将「沙箱内运行」切换为 **「本地自动运行」**（沙箱模式下 Hook 命令可能被静默跳过）。

只需要设置一次，之后重启 Trae 均会保持生效。

如果右键菜单中没有“使用 PowerShell 运行”，可以在该文件夹空白处右键，选择“在终端中打开”，然后执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

## 三、Trae 中需要确认的设置

安装后打开 Trae 设置，搜索以下两个配置项：

```text
traePetBridge.autoLaunch
traePetBridge.petPath
```

应当是以下状态：

```json
{
  "traePetBridge.autoLaunch": true,
  "traePetBridge.petPath": "解压后的 ClawdPet 文件夹路径"
}
```

例如：

```json
{
  "traePetBridge.autoLaunch": true,
  "traePetBridge.petPath": "D:\\Apps\\ClawdPet"
}
```

其中：

- `traePetBridge.autoLaunch`：是否在 Trae 启动时自动启动宠物，设为 `true`。
- `traePetBridge.petPath`：解压后的 ClawdPet 文件夹路径，不是 `ClawdPet.exe` 路径。

### Trae CN 设置文件位置

```text
%APPDATA%\Trae CN\User\settings.json
```

### Trae 国际版设置文件位置

```text
%APPDATA%\Trae\User\settings.json
```

安装脚本会自动识别版本并写入对应位置，通常不需要手动修改。

## 四、如何使用

启动 Trae 后，宠物会自动出现在屏幕右下角附近。

不同事件对应的状态：

| 状态 | 触发条件 |
| --- | --- |
| `idle` | 空闲状态 |
| `thinking` | Agent 思考中 |
| `editing` | Agent 执行工具、修改文件 |
| `notification` | Trae 发送通知，显示约 5 秒后回到空闲 |
| `error` | Agent 或模型发生异常 |
| `attention` | 点击宠物或代码编辑活动 |

关闭 Trae 时，宠物会同步关闭。这个功能不需要配置 Windows 启动项。

## 五、常见问题

### 1. Trae 启动后宠物没有出现

请依次检查：

1. 确认 `traePetBridge.autoLaunch` 为 `true`。
2. 确认 `traePetBridge.petPath` 指向解压后的文件夹。
3. 确认该文件夹没有被移动或删除。
4. 完全退出并重新启动 Trae。
5. 打开 Trae 的输出面板，查看 `Pet Bridge` 输出频道。

正常情况下可以看到：

```text
启动宠物开发模式: <ClawdPet 文件夹路径>
宠物进程已启动
```

### 2. 状态不随 Trae 变化

确认 Hooks 文件存在：

Trae CN：

```text
%USERPROFILE%\.trae-cn\hooks.json
```

Trae 国际版：

```text
%USERPROFILE%\.trae\hooks.json
```

然后确认 Hooks 已在 Trae 中启用（Trae 1.107+ 必须手动启用，见「二、一键安装」中的说明）：

1. 设置 → Hooks → 已配置的 Hooks → 全局 → 开关已打开。
2. 执行模式为「本地自动运行」。

最后重新运行 `install.ps1`，再重启 Trae。

### 3. PowerShell 阻止脚本运行

在解压目录打开 PowerShell，执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

### 4. 只想手动启动宠物

在 Trae 设置中将以下配置改为 `false`：

```json
"traePetBridge.autoLaunch": false
```

之后可以手动启动项目中的 `ClawdPet.exe`，或者使用项目开发模式启动。

### 5. 如何更换动画

进入以下目录：

```text
Remielle_Dan-clawd-theme-v0.5.4\assets
```

替换对应的 GIF 文件即可。替换后重新启动宠物。

## 六、卸载

1. 在 Trae 设置中删除或禁用：

```text
traePetBridge.autoLaunch
```

2. 删除 Trae 扩展目录中的：

Trae CN：

```text
%USERPROFILE%\.trae-cn\extensions\remielle.trae-pet-bridge-2.0.0
```

Trae 国际版：

```text
%USERPROFILE%\.trae\extensions\remielle.trae-pet-bridge-2.0.0
```

3. 删除对应的全局 Hooks 文件中的 ClawdPet 配置。
4. 删除解压后的 ClawdPet 文件夹。
5. 重启 Trae。

## 七、给发布者的说明

将整个发布目录压缩后发送给使用者。发布目录至少应包含：

- `install.ps1`
- `trae-pet-bridge\`
- `hook-handler.js`
- `main.js`
- `pet.js`
- `preload.js`
- `index.html`
- `style.css`
- `package.json`
- `node_modules\electron\`
- `Remielle_Dan-clawd-theme-v0.5.4\`
- `native-pet\`（原生模式：转换/部署脚本 + `assets\` 下的 4 张 webp 皮肤；本机 `official-backup\` 不打包）

使用者不需要安装 Node.js，因为发布包已经包含 Electron 运行时。安装脚本会优先使用发布包内的 Node 运行时配置 Hooks。
