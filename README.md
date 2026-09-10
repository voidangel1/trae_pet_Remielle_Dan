# ClawdPet — 蕾米埃尔 · Clawd 桌面宠物（Trae IDE）

本项目基于 `https://github.com/6666ccc/codex_pet_Remielle_Dan` 进行二次开发。

一个运行在 Windows 桌面上的 Electron 桌宠，通过本地 HTTP 桥（端口 27315）接收 Trae IDE 扩展与 Hooks 推送的事件，实时反映 Agent 的工作状态（空闲 / 注意 / 思考 / 编辑 / 通知 / 错误）。

## 功能

- 六种状态动画（360×360 GIF）：idle / attention / thinking / editing / notification / error
- Agent 工作时持续显示 thinking / editing，完成后自动回到 idle
- 拖拽移动（右键菜单「可移动」解锁）、单击、双击（摇晃 + error 表情）等交互
- 单实例运行，多个 Trae 项目共享同一只宠物
- F12 打开 DevTools 调试

## 安装

1. 解压发布包（或直接克隆本仓库）
2. 右键 `install.ps1` → 「使用 PowerShell 运行」
3. 重启 Trae IDE

脚本会自动：
- 安装桥接扩展到 Trae（CN / 国际版自适应）
- 用国内镜像安装 Electron 运行时
- 配置 Trae 设置（自动启动 + 项目路径）
- 注册全局 Hooks（UserPromptSubmit / PreToolUse / PostToolUse / Notification / Stop）

## 开发

```bash
npm install
npm start      # 运行宠物
npm run build  # 打包便携版 exe
.\release.ps1  # 生成可分发的 zip 发布包
```

## 目录结构

```
main.js      Electron 主进程（窗口、拖拽、本地桥接服务器）
pet.js       渲染进程（状态机、交互）
preload.js   上下文桥接
index.html   页面入口
style.css    样式
hook-handler.js   Trae Hooks 处理器
trae-pet-bridge/  Trae 扩展（转发事件到本地桥）
Remielle_Dan-clawd-theme-v0.5.4/  主题资源（GIF + theme.json）
```

## 已知问题修复（v1.4）

- **彻底修复宠物随窗口关闭退出的问题（v1.2 修复无效的真正根因）**：v1.2 移除 taskkill + 看门狗的方案部署后实测宠物依然精确跟随「启动它的窗口」死亡。深入排查发现真正根因是 **Windows Job Object 连坐**：Trae 的扩展宿主（exthost）运行在 kill-on-close 的 Job Object 内，exthost 直接 spawn 的宠物进程自动继承 Job 成员资格，窗口关闭、Trae 释放 Job 句柄时，宠物被系统连坐杀死——`detached: true` 和删除 taskkill 都无法幸免（已用 `IsProcessInJob` API 实测确认）。修复：扩展改用 **WMI `Win32_Process.Create`** 启动宠物——由系统服务进程 WmiPrvSE（在危险 Job 之外）创建进程，宠物不继承 exthost 的 Job，窗口关闭后存活，生命周期完全交给自带看门狗（所有 Trae 窗口关闭后约 20 秒内自行退出）。因 WMI 无法传递环境变量，看门狗开关从 `CLAWD_PET_FOLLOW_TRAE` 环境变量改为 `--follow-trae` 命令行参数（环境变量方式保留兼容手动测试）。
- install.ps1 生成的 hooks.json 改用 `[ordered]` 保持键序与 Trae Hooks UI 生成的格式一致。

## 已知问题修复（v1.3）

- **修复 Agent 异常打断时「错误」状态几乎无法显示的问题**：实测发现 `Stop` 事件的错误检测本身工作正常，但 Trae 判定异常打断后会紧接着（约 300ms）弹出一条不含错误关键词的系统通知，把宠物刚切换到的 error 覆盖成 notification。现在 hook-handler 通过状态文件（`hook-state.json`）跨进程记录最近推送的状态，error 后 15 秒内的 Notification 直接忽略；pet.js 同步做了双保险（error 展示的 3 秒内忽略 notification）。
- **修复「命令审批 / Agent 提问」场景「通知」状态不触发的问题**：分析 hook-debug.log 发现这两种场景下 Trae 完全不触发任何 Hook 事件（Notification 仅在任务完成/异常打断时触发），宠物会一直卡在 editing。现在 pet.js 增加等待检测：`PreToolUse(editing)` 后超过 45 秒无任何后续事件，判定为等待用户操作，持续显示 notification 直到下一个事件到达。
- hook-handler 现在会记录 `Notification` / `Stop` 事件的完整 payload（截断 1500 字符），便于后续排查状态误判。
- **修复 install.ps1 在 Windows PowerShell 5.1 下行为错乱的问题**：脚本原为 UTF-8 无 BOM 编码，PowerShell 5.1 会按 GBK 解码，中文注释的 UTF-8 字节序列在错位解码时会吞掉紧跟的换行符，导致下一行代码被并入注释而**静默跳过**（表现为：扩展未安装、Hook 命令被写成空命令、`$projectHooksDir` 为 null 报错等）。install.ps1 / release.ps1 已转为 UTF-8 with BOM。
- **修复 install.ps1 写入的 hooks.json 带 BOM 导致 Hooks 被静默忽略的问题**：PowerShell 5.1 的 `Out-File -Encoding utf8` 会写入带 BOM 的 UTF-8，而 Trae 解析 hooks.json 时不接受 BOM，整个文件被静默忽略——表现为 Hooks 开关已开启但任何事件都不触发（本机 10:59 起 Hooks 整体静默近两小时，即由首次成功运行 install.ps1 引入）。已改为 `[System.IO.File]::WriteAllText` 无 BOM 写入（settings.json / hooks.json 共 3 处），并清除了本机两份文件的存量 BOM。**注意：hooks.json 是 Trae 启动时加载的，修复后需重启 Trae 生效。**
- **修复 install.ps1 的 Node 检测从未成功的问题**：检测链首选的 `node_modules\electron\dist\node.exe` 在 Electron 发行包中并不存在，系统 PATH 也可能没有 Node，导致写入的 Hook 命令无效。现在优先使用 Trae 自带的 Node（`~\.trae-cn\binaries\node\versions\*\node.exe`，随 Trae 始终存在），再回退到 Electron 本地 node / 系统 Node。Hook 配置同时补齐 `matcher` 字段并统一 timeout 为 10，与 Trae Hooks UI 启用后的格式对齐，覆盖后无需重新启用。

## 已知问题修复（v1.2）

- **修复关闭第一个 Trae 窗口会导致宠物退出的问题**：此前宠物进程由最先打开的窗口的扩展托管，该窗口一关闭，宠物就被连带结束。现在扩展停用时不再杀死宠物，宠物通过自带看门狗自管生命周期——桥接扩展启动宠物时打上「跟随 Trae」标记（环境变量 `CLAWD_PET_FOLLOW_TRAE=1`），宠物每 10 秒检测一次系统中是否还有 Trae 主进程（`Trae.exe` / `Trae CN.exe`，排除 `trae-sandbox` 等辅助进程），连续两次未检出（约 20 秒容忍期，可扛住 Trae 重启/升级时的进程空窗）才自行退出。手动 `npm start` 或双击 exe 启动的宠物不带标记，不会自动退出。

## 已知问题修复（v1.1）

- **修复 Trae 1.107+ 更新后状态全部失效的问题**：新版 Trae 要求 Hooks 必须在「设置 → Hooks」中手动启用，且执行模式默认为沙箱（会静默跳过 Hook 命令）。安装后需按 INSTALL.md 手动启用一次。
- Hooks 命令改用完整路径，不再使用 8.3 短路径（Trae 升级自带 Node 后短路径会失效导致 Hooks 被静默跳过）。
- Pet Bridge 扩展不再在 Trae 启动时自动弹出日志面板。
- install.ps1 会自动清理旧版本扩展目录，避免新旧共存重复加载。

## 已知问题修复（v1.0）

修复了 Windows 显示缩放非 100%（如 125% / 150%）的屏幕上，拖拽宠物时窗口不断变大的问题（Electron 上游 bug electron#48247）。拖拽现在通过 `setBounds` 显式携带固定宽高实现，并附带 `resize` 事件兜底，窗口尺寸恒定 180×180。
