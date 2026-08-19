# ClawdPet 安装与使用说明

ClawdPet 是一个会根据 Trae Agent 工作状态自动切换动画的桌面宠物。

支持：

- Trae CN
- Trae 国际版
- Windows 10 / Windows 11

## 一、安装前准备

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

然后重新运行 `install.ps1`，再重启 Trae。

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
%USERPROFILE%\.trae-cn\extensions\remielle.trae-pet-bridge-0.1.0
```

Trae 国际版：

```text
%USERPROFILE%\.trae\extensions\remielle.trae-pet-bridge-0.1.0
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

使用者不需要安装 Node.js，因为发布包已经包含 Electron 运行时。安装脚本会优先使用发布包内的 Node 运行时配置 Hooks。
