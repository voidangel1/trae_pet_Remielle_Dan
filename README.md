# Remielle Pet for Qoder — 蕾米埃尔桌面宠物

![Remielle Qoder 桌宠](qoder-pet/preview.png)

把 [Remielle（蕾米埃尔）](Remielle_Dan-clawd-theme-v0.5.4/) 做成 **Qoder 原生桌面宠物（Petdex）**：不修改 Qoder 程序、不注入页面、不运行后台进程，只写入一个宠物包，Qoder 自己驱动待机、忙碌、等待、审查、失败，以及启动问候、悬停、拖拽和视线跟随。

基于 [6666ccc/codex_pet_Remielle_Dan](https://github.com/6666ccc/codex_pet_Remielle_Dan) 二次开发（主题作者：刘畅）。

> 本仓库的 `master` 分支是 **Trae 原生悬浮窗版**（Hooks 六态 + 主程序补丁）；当前 `qoder` 分支是 **Qoder 原生宠物版**。两者安装互不冲突，详见[与 Trae 版的区别](#与-trae-版的区别)。

## 它能做什么

Qoder 自带桌面宠物系统（Petdex），支持每个角色一套 8 列 × 11 行的精灵图集，由 Qoder 根据 IDE 状态播放对应行的动画。本项目把 Remielle 的 6 套原始 GIF 动画重新编排成 Qoder 的图集格式：

- **原生渲染**：192×208 单元格、1536×2288 图集，逐帧选取并保留原始动作节奏
- **六套素材全覆盖**：待机、编辑、等待、思考、出错、注意（attention）
- **原生交互保留**：启动挥手、悬停轻跳、左右拖拽、鼠标视线跟随
- **零侵入**：不修改 Qoder 安装目录、设置或缓存，不需要管理员权限
- **可逆**：安装器自动备份同名宠物，卸载只删除自己写入的目录

| Qoder 状态 / 交互 | 宠物表现 | 素材来源 |
|---|---|---|
| 空闲（idle） | 呼吸循环 | `idle-loop.gif` |
| Agent 运行（running） | 低头忙碌 / 编辑 | `editing.gif` |
| 等待你（waiting） | 举手等待 | `notification.gif` |
| 代码审查（review） | 思考 | `thinking.gif` |
| 任务失败（failed） | 出错懊恼 | `error.gif` |
| 启动问候（waving） | 挥手 | `notification.gif` 前 4 帧 |
| 悬停（jumping） | 轻跳 | `attention.gif` + 合成位移 |
| 拖拽（runningLeft / runningRight） | 朝拖动方向移动 | `attention.gif`（镜像） |
| 视线跟随（lookLeft / lookRight） | 左右看 | `idle-loop.gif`（镜像） |

## 安装

### 方式一：EXE 安装器（推荐）

1. 下载 [release 里的 `Remielle-QoderPet-Setup-v1.0.0.exe`](release/)（约 2.3 MB，宠物包全部内嵌）
2. 双击运行，点「安装 / 更新」（无需管理员权限）
3. 打开 Qoder → **设置 → 桌面宠物（Desktop Pet）→ 刷新** → 选择「蕾米埃尔」

卸载：重新打开安装器 → 「卸载」。只删除 `%USERPROFILE%\.petdex\pets\remielle`，不会修改 Qoder 配置；安装前的同名宠物目录会备份到 `%LOCALAPPDATA%\RemielleQoderPet\backups`。

### 方式二：手动安装（已克隆仓库）

先生成宠物包（需要 Node.js 与 `sharp`）：

```bash
npm install
npm run convert:qoder
```

然后复制到 Petdex 目录：

```powershell
$dst = "$env:USERPROFILE\.petdex\pets\remielle"
New-Item -ItemType Directory -Force $dst | Out-Null
Copy-Item qoder-pet\dist\remielle\* $dst -Force
```

回到 Qoder 的桌面宠物设置点「刷新」。目录名必须是 `remielle`，且与 `pet.json` 里的 `id` 一致。

## 使用

- 安装后在桌面宠物列表选择「蕾米埃尔」即可；Qoder 会记住选择。
- 宠物完全由 Qoder 驱动：切到其他应用、拖拽、把鼠标移到宠物上，都会触发对应原生动作。
- 更新到新版本：直接重跑安装器「安装 / 更新」；内容相同时不会重复备份。
- 恢复官方宠物：在 Qoder 桌面宠物设置中改选其他宠物；彻底移除则运行安装器卸载。

更多排查与细节见 [INSTALL.md](INSTALL.md)。

## 工作原理

```
Qoder（Petdex 运行时）
   └→ 读取 %USERPROFILE%\.petdex\pets\remielle\pet.json
        └→ 按 Qoder 状态选择 8×11 图集中的一行
             └→ 按固定帧时序播放该行动画
```

安装器只做两件事：把内嵌的 `pet.json` + `spritesheet.webp` 写入 Petdex 目录；卸载时在确认 `id` 后删除该目录。没有 Hook、扩展、端口或常驻进程。

Qoder 对宠物包的校验（本项目生成时逐项满足）：

- `id` / 目录名匹配 `^[a-z0-9](?:[a-z0-9._-]{0,127})$`
- `spriteVersionNumber` 为 2，图集为静态图
- 宽高可被 8 与 11 整除，单元格宽高比 12:13
- 只包含一个 `spritesheet.webp`（或 `.png`），不超过 32 MB

## 与 Trae 版的区别

| | Qoder 版（本分支） | Trae 版（`master`） |
|---|---|---|
| 接入方式 | Qoder 原生 Petdex 宠物包 | 补丁 Trae `main.js` / 悬浮窗页面 |
| 状态来源 | Qoder 自身状态机（idle / running / waiting / review / failed） | Trae Hooks 五事件 + 扩展状态服务器 |
| 六态语义 | 由 Qoder 状态映射，无法精确到「AI 提问 / 每次工具调用」 | 提问、编辑、通知、错误、悬停逐事件定制 |
| 安装权限 | 当前用户，无需管理员 | 需写入 Trae 安装目录，会弹 UAC |
| 维护成本 | Trae/Qoder 升级互不影响 | Trae 升级可能还原补丁，需要重跑安装器 |
| 后台进程 | 无 | Hooks 运行时 + 桥接扩展 |

如果 Qoder 后续开放更细粒度的宠物状态事件，可以在转换脚本中继续细化映射；当前优先保证原生、稳定、可逆。

## 构建

前置：Windows、Node.js、.NET Framework（系统自带 `csc.exe`）。

```bash
npm install
npm run convert:qoder   # GIF → qoder-pet/dist/remielle（图集 + pet.json + 预览）
npm run qoder:art       # 安装器预览图 + remielle.ico
npm run qoder:build     # → release/Remielle-QoderPet-Setup-v1.0.0.exe
```

转换脚本会输出 `preview.png`（带行标签的图集预览）与 `meta.json`（每行的帧选取、缩放、位置），便于核对动作映射。修改素材后重跑上述三步即可。

## 目录结构

```
qoder-pet/
├── convert-qoder-pet.js          # GIF → Petdex v2 图集
├── make-installer-art.js         # 安装器预览图与 ICO
├── installer/
│   ├── RemielleQoderInstaller.cs # WinForms 安装器（csc 编译）
│   ├── app.manifest              # asInvoker / 系统 DPI
│   └── build-qoder-installer.ps1
├── preview.png                   # README 预览图
└── dist/                         # 生成物（git 忽略）
```

## 兼容性

- 已在 **Qoder CN 0.4.8** 的 Petdex v2 格式上生成并校验宠物包；Qoder 若调整宠物校验规则或图集行数，需要重新适配。
- Windows 10/11；安装器为系统 DPI 感知，150% 缩放下正常显示。
- 宠物包同时兼容使用 `spritesheet.png` 的实现，但本项目输出无损 WebP 以控制体积。

## 分支说明

- `qoder`：Qoder 原生桌宠（当前分支）
- `master`：Trae v2.0.3 原生悬浮窗版
- `old`：v1.0–1.4 独立 Electron 宠物

## 致谢

- Remielle 主题：刘畅（`Remielle_Dan-clawd-theme-v0.5.4`，MIT）
- 上游项目：[6666ccc/codex_pet_Remielle_Dan](https://github.com/6666ccc/codex_pet_Remielle_Dan)

## License

MIT
