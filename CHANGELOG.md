# 更新日志

本项目所有对 Trae 主程序（`main.js` / `floatingButton.html`）的修改均为**补丁式**修改：
Trae 升级会还原官方文件，重新运行安装器（完整或轻量均可）即可一键恢复全部补丁。

## [v2.0.3] - 2026-10-19

### 修复

- **Trae 3.4.4 升级后宠物第三次变回官方小球**。3.4.4 把形象 gate `tB` 改名 `TB`
  （`function TB(t){return"ball"}`）、尺寸表 `oB` 改名 `PB`、`drag-move` 处理者改名为
  `sn`，v2.0.2 携带的新旧两套硬编码锚点全部失配；且旧代码用 `String.Replace` 静默无操作、
  日志无条件报 `[OK]`，故障被完全掩盖。
- **锚点全面改用结构正则，不再依赖混淆变量名**。三处锚点按代码结构匹配：
  `function X(t){return"ball"}`、`X={ball:60,traebao:100}`、
  `X.on("icube-floating-chat:drag-move",(e,i,r)=>{`——Trae 未来再改名也无需适配。
  拖拽补丁的 `screen` 别名改为动态探测（取离 handler 最近的 `getCursorScreenPoint` 引用，
  3.4.4 实测为 `lm`）。安装/卸载前先正则归一化任意历史版本补丁 → 官方结构，跨版本重装幂等。
- **锚点失配时明确告警**，不再假报成功。
- 部署脚本（`deploy-native-pet.ps1` / `deploy-step-main.ps1`）多重 BOM 叠加导致
  `param()` 绑定失效的问题（同类问题 v2.0.1 只修了 `build-installer.ps1`）。

## [v2.0.2] - 2026-10-16

### 修复

- **Trae 3.4.3 升级后宠物变回官方小球**。Trae 3.4.3（2026-10）重构把形象 gate `qF` 改名为
  `tB`（`function tB(t){return"ball"}`，无论 `settings.json` 写什么都归一成 `ball` 并写回，
  所以手动改配置永远无效）、尺寸表 `XF` 改名为 `oB`，旧版锚点全部失配。安装器现同时携带
  新旧两套锚点（`qF`/`XF` 与 `tB`/`oB`），各自命中才生效，升级后重跑安装器即可恢复
  140px 的 Remielle。卸载/重装归一化同步兼容两套锚点。
- **非 16:9 屏（高 DPI）拖拽错位与点击失灵**。此类屏幕（16:10、3:2 等）Windows 默认缩放
  为 125%/150%，渲染进程 `MouseEvent.screenX` 与主进程 DIP 坐标错位：按住宠物四角拖动时
  窗口会加速跑离鼠标（表现为"宠物自行往反方向移动"），鼠标甩出窗口后 `mouseup` 丢失、
  拖拽状态残留，松开后点击无法呼出 Trae。`drag-move` 处理现改用主进程
  `screen.getCursorScreenPoint()` 权威 DIP 坐标，任何缩放/多屏下精准跟手。补丁内置
  try/catch 兜底：Trae 升级改混淆变量名时自动退回官方原始逻辑，不会引入新问题。

### 变更

- 版本号统一升至 2.0.2（安装器 / 桥接扩展 / package.json / 构建脚本 / 文档）。
- 官方素材备份逻辑加固：本地已有完整备份时不再重复备份，防止把皮肤误存为"官方还原基线"。
- 安装日志会标注本次命中的是旧版锚点还是 3.5 新锚点，便于排查升级适配问题。

## [v2.0.1] - 2026-10-11

### 新增

- **Trae 自定义安装路径检测**。旧版只认进程路径与三个 C 盘固定位置；现注册表改大小写不敏感
  匹配（实测 Trae CN 写入的 DisplayName 为 "TRAE CN (User)"，旧代码匹配 "Trae CN" 必然漏掉）、
  补 WOW6432Node 视图与 DisplayIcon 推导、枚举所有盘符的 Program Files；仍失败时点「安装」
  会弹文件选择框手动指认 `Trae CN.exe`。
- **无 node 环境自动安装 Node LTS**。查找顺序：Trae 托管 node → 系统 PATH → 自动从
  npmmirror（原淘宝镜像，国内直连）下载 Node LTS（约 30MB zip）解压到
  `%LOCALAPPDATA%\ClawdPet-v2\node`，官方 nodejs.org 兜底；zip 魔数校验 + 下载进度日志，
  卸载时随运行时目录一并清理。
- **桥接扩展改走 VSIX + Trae CLI 官方注册**。直接往 `extensions` 目录拷贝的"旁载"方式已被
  Trae 封锁：启动扫描会执行 "Marked extension as removed"，把目录写入 `.obsolete` 延迟删除
  清单并拒绝加载（状态栏与六态服务器全部不出现）。安装器现现场打包 VSIX 并调用 Trae 自带
  CLI（`ELECTRON_RUN_AS_NODE=1 Trae CN.exe cli.js --install-extension --force`）走官方注册
  流程，写入 `extensions.json` 并自动解除移除标记，注册结果硬校验；CLI 不可用或 Trae 运行中
  导致失败时回退目录拷贝，并在日志明确提示重跑。

### 修复

- `build-installer.ps1` 多重 BOM 叠加导致的 PowerShell 解析报错。

## [v2.0] - 2026-10

- 移植进 Trae 原生悬浮窗：Remielle 六态（Trae Hooks 驱动：提问 / 编辑 / 通知 / 错误 / 忙碌 /
  悬停拖拽）+ 官方四态可切换（状态栏点击切换，记住选择）。
- EXE 安装器（WinForms，csc 编译，零第三方依赖，素材全部内嵌）：完整 / 轻量双模式，
  安装后指引；卸载一键还原官方原样，同时清理 v1.0–1.4 独立宠物、扩展、hooks.json、
  自启动与运行时目录。
