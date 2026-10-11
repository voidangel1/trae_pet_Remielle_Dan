# 安装与使用说明

Remielle 桌宠 v2.0 —— 移植进 Trae 原生悬浮窗，零独立进程，随 Trae 启停。

支持：Trae CN / Trae 国际版 v3.4+，Windows 10/11。

## 一、安装（EXE 安装器，推荐）

1. 退出 Trae（托盘图标右键 → 退出，**完全退出**）。
2. 双击 `ClawdPet-Setup-v2.0.exe`（约 9MB 单文件，素材全部内嵌，无需其他准备）。
3. 按需选择：
   - **完整安装**：Hooks 六态 + 官方四态切换 + 桥接扩展 + Hooks 注册 + 安装后指引。
   - **轻量安装**：只替换官方四态皮肤与尺寸补丁，不碰 Hooks 与悬浮窗页面（最稳）。
4. 重新打开 Trae，屏幕右下角出现 140px 的 Remielle。

> **重要**：Trae 只在启动时读取 Hooks 与设置配置，安装后必须**完全重启 Trae**（重载窗口无效）。

## 二、脚本方式（克隆仓库 / 解压发布包）

1. 右键 `native-pet\deploy-native-pet.ps1` → 「使用 PowerShell 运行」（弹一次 UAC，用于写入 Trae 安装目录）。
2. 重启 Trae。

脚本完成：皮肤 webp 部署 → 官方素材备份（`native-pet\assets\official-backup\`）→ main.js `qF` 补丁（解锁官方灰度关闭的宠物形象）→ 悬浮窗页面注入轮询脚本 → `buttonIcon` 切为 `traebao`。

> 脚本方式不安装桥接扩展与 Hooks（六态需要它们）。完整六态请用 EXE 安装器，脚本主要面向「Trae 升级后重新部署」的维护场景。

## 三、使用

| 事件 | 宠物状态 |
|---|---|
| 空闲 | idle 呼吸循环 |
| 发送消息（UserPromptSubmit） | thinking |
| AI 执行工具 / 编辑文件（PreToolUse） | editing |
| AI 回复以问句结尾（Stop） | notification，常驻等你回复 |
| 出错（Stop 错误字段） | error |
| 悬停 / 拖拽 | attention |

- 单击宠物：开关对话浮窗；右键：菜单；`Alt+Space`：快捷唤起。
- 尺寸：Trae 设置搜 `trae.floatingChat.size`（0.5–2 倍）。
- **状态栏 Pet 图标**：显示当前模式（`原生·六态` / `原生·四态`），点击切换；桥不可用时宠物自动回落官方四态，永不消失。

## 四、常见问题

### 1. 安装后宠物没出现 / 还是官方圆球
- 确认 Trae **完全重启**过（不是重载窗口）。
- 用 EXE 安装器重跑一次「完整安装」（幂等）。

### 2. 状态不变化（一直同一种动画）
依次检查 `%USERPROFILE%\.trae-cn\hooks.json`（国际版为 `.trae`）：

- 每个事件必须是 **数组**：`[ { "matcher": "*", "hooks": [...] } ]`（纯对象会被静默丢弃）。
- `command` 不能带引号（`"node" "handler.js"` 会静默失效）。
- 文件不能带 BOM。
- Trae 设置 → Hooks：全局开关已打开，执行模式为「本地自动运行」（沙箱模式会静默跳过 Hook）。

改完必须完全重启 Trae。

### 3. Trae 升级后宠物变回官方形象
升级会还原官方素材与 main.js。重跑 `deploy-native-pet.ps1`（或 EXE 完整安装）即可，脚本幂等。

### 4. 想换回官方原样
- 皮肤与补丁：`powershell -File native-pet\deploy-native-pet.ps1 -Restore`
- 全部（含扩展、Hooks、设置、旧版 1.0–1.4 清理）：EXE 安装器 → 卸载

### 5. 更换动画素材
替换 `Remielle_Dan-clawd-theme-v0.5.4\assets\` 下的 GIF → `npm install && npm run convert` 重新生成 webp → 重跑部署脚本。

## 五、给发布者的说明

- 主分发物：`release\ClawdPet-Setup-v2.0.exe`（`installer\build-installer.ps1` 或 `release.ps1` 构建）。
- `release.ps1` 同时产出 zip（皮肤 + 部署脚本 + 文档），适合脚本方式使用。
- 仓库须包含：`installer\`、`native-pet\`（含 8 张 webp）、`trae-pet-bridge\`、`hook-handler.js`、主题目录——安装器构建时从这些位置嵌入资源。
