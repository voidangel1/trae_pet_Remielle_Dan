# Remielle Pet for Trae — 蕾米埃尔桌面宠物

把 [Remielle（蕾米埃尔）](Remielle_Dan-clawd-theme-v0.5.4/) 接进 **Trae IDE 原生悬浮窗**：你的 AI 助手在思考、写代码、等你确认时，右下角的小人会实时做出对应动作。零独立进程、随 Trae 启停、一个 EXE 装好。

基于 [6666ccc/codex_pet_Remielle_Dan](https://github.com/6666ccc/codex_pet_Remielle_Dan) 二次开发（主题作者：刘畅）。

| 待机 | 思考 | 编辑 |
|---|---|---|
| ![idle](Remielle_Dan-clawd-theme-v0.5.4/assets/idle-loop.gif) | ![thinking](Remielle_Dan-clawd-theme-v0.5.4/assets/thinking.gif) | ![editing](Remielle_Dan-clawd-theme-v0.5.4/assets/editing.gif) |

## 它能做什么

Trae v3.4+ 自带一个原生悬浮宠物（TRAE Bao），但默认形象被灰度关闭、只剩一个圆球，且状态只有「空闲/忙碌/超忙碌/拖拽」四种静态图。本项目：

- **解锁官方宠物形象**，并把 Remielle 的 6 套动画换上去（逐帧时序 1:1 保留，比原 GIF 更精确）
- **恢复完整的 Hooks 六态状态机**——你发消息她开始思考、AI 改文件她低头干活、AI 向你提问她会举手等你、出错她有专属表情
- 状态栏 **Pet 图标一键切换** Hooks 六态 / 官方四态（桥挂了自动回落官方模式，宠物永不消失）
- 官方悬浮窗的全部交互保留：单击开关对话浮窗、右键菜单、拖拽、`Alt+Space`、0.5–2 倍缩放

| 你的操作 / Trae 事件 | 宠物状态 |
|---|---|
| 无事发生 | idle 呼吸循环 |
| 发送消息（UserPromptSubmit） | thinking |
| AI 执行工具/编辑文件（PreToolUse） | editing |
| AI 回复以问句结尾、等你确认（Stop） | notification（常驻直到你回复） |
| 出错（Stop 错误字段） | error |
| 悬停 / 拖拽 | attention |

## 安装

**方式一：EXE 安装器（推荐）**

1. 下载 [release 里的 `ClawdPet-Setup-v2.1.0.exe`](release/)（约 9MB，素材全部内嵌，无需克隆仓库）
2. 退出 Trae（托盘右键 → 退出），双击运行
3. 三种模式任选：
   - **完整安装**：六态 + 官方四态切换 + Hooks + 扩展 + 安装后指引
   - **轻量安装**：只换官方四态皮肤，不碰 Hooks 与页面注入（最稳）
   - **卸载**：一键还原官方原样，**同时能清理本项目的旧版本（v1.0–1.4 独立宠物）**
4. 重新打开 Trae，右下角出现 140px 的 Remielle

**方式二：脚本部署（适合 Trae 升级后的重部署 / 无扩展皮肤模式）**

```powershell
右键 native-pet\deploy-native-pet.ps1 → 使用 PowerShell 运行   # 会弹一次 UAC
```

> 脚本方式不含桥接扩展与 Hooks 注册（六态需要它们）——首次安装请用 EXE 安装器，脚本主要面向升级后的重部署。完整说明见 [INSTALL.md](INSTALL.md)。

## 工作原理

```
Trae Hooks（五事件）
   └→ hook-handler.js 落盘状态（hook-state.json）
        └→ trae-pet-bridge 扩展内置状态服务器（127.0.0.1:27416，随扩展启停）
             └→ floatingButton.html 注入的轮询脚本（500ms）
                  └→ 按状态名切换 webp 动画
```

## Trae 升级后

升级会还原 `main.js` 和官方素材 → 右键重跑 `deploy-native-pet.ps1`（或重跑安装器完整安装）即可，脚本幂等、自动刷新官方素材备份。

## 兼容性

- Windows 10/11 + Trae CN 3.4.2+（v2.1.0 适配 3.4.7：官方已原生支持右键「切换外观」，本项目禁用官方 3D 渲染回落素材模式；锚点全部结构正则，Trae 大版本重构若再失配，安装日志会明确告警）
- Trae v3.4+（CN / 国际版）；旧版 Trae 或想用 v1.x 独立 Electron 版 → 切到 [`old` 分支](../../tree/old)；Qoder 桌宠版 → 切到 [`qoder` 分支](../../tree/qoder)
- 需要 Trae 自带 Node（Hooks 执行用，安装器自动探测）

## 开发与构建

```bash
npm install                      # 安装 sharp（GIF → webp 转换用）
.\installer\build-installer.ps1   # csc 编译单文件 EXE 安装器
.\release.ps1                     # 打 release zip + EXE
npm run convert                   # 主题 GIF → 动画 webp（改素材后重跑）
```

## 分支说明

- `master`：v2.1.0 当前版本（原生悬浮窗移植 + EXE 安装器；v2.0.1：自定义路径检测、VSIX+CLI 扩展注册；v2.0.2：适配 Trae 3.5 锚点；v2.0.3：锚点全面改结构正则；v2.1.0：适配 Trae 3.4.7——官方 TRAE Bao 上线 + 禁 3D 回落素材模式 + voice/default.png 新素材）
- `old`：v1.0–1.4 独立 Electron 宠物（旧版 Trae 与独立模式用户看这里）

## 致谢

- Remielle 主题：刘畅（`Remielle_Dan-clawd-theme-v0.5.4`，MIT）
- 上游项目：[6666ccc/codex_pet_Remielle_Dan](https://github.com/6666ccc/codex_pet_Remielle_Dan)

## License

MIT
