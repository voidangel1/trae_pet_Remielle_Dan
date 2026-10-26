# 更新日志

本分支（`qoder`）为 **Qoder 原生桌面宠物（Petdex）** 版本，不修改 Qoder 程序。
Trae 版（v1.0–v2.1.0，Hooks 六态 / 主程序补丁）的更新记录见 `master` 分支的 CHANGELOG。

## [Qoder v1.0.0] - 2026-10-20

### 新增

- **Qoder 原生桌宠包**。按 Qoder CN 0.4.8 的 Petdex sprite v2 规格，把 Remielle 的
  6 套 GIF 动画重排为 8 列 × 11 行、192×208 单元格、1536×2288 的无损 WebP 图集，
  共使用 73 帧。不修改 Qoder 程序、设置或缓存，无 Hooks、扩展、端口与后台进程。
- **动作映射**：
  - Agent 运行 → `editing`（忙碌）
  - 等待你 → `notification`（举手等待）
  - 代码审查 → `thinking`（思考）
  - 任务失败 → `error`（懊恼）
  - 空闲 → `idle-loop`（呼吸）
  - 启动问候 → `notification` 前 4 帧（挥手）
  - 悬停 → `attention` + 合成轻跳
  - 左右拖拽 → `attention` 镜像
  - 视线跟随 → `idle-loop` 左右镜像
- **转换工具** `qoder-pet/convert-qoder-pet.js`：
  - 逐帧提取 GIF、按 alpha 包围盒对齐，并以各状态中位尺寸统一缩放（避免单帧飞纸导致整体缩小）；
  - 按状态完整包围盒水平居中、底部对齐，支持镜像与合成的跳跃/抖动位移；
  - 输出 `pet.json`、`spritesheet.webp`、带行标签的 `preview.png` 和记录每行帧选取的 `meta.json`；
  - 输出前校验图集尺寸、单元格比例、透明通道、单页静态图与 32 MB 上限。
- **EXE 安装器** `Remielle-QoderPet-Setup-v1.0.0.exe`（WinForms / csc，零第三方依赖，约 2.3 MB）：
  - 宠物包与预览图全部内嵌，单文件分发；
  - 每用户安装，无需管理员权限，不写 Qoder 安装目录；
  - 安装前校验内嵌包，目标目录内容不同时自动备份，失败自动回滚；
  - 卸载前校验 `pet.json` 的 `id`，只删除 `%USERPROFILE%\.petdex\pets\remielle`，保留备份与日志；
  - 安装后指引、重新检测、打开宠物目录；系统 DPI 感知，150% 缩放下布局正常。
- **Phase 0 兼容性测试包**：彩色编号图集（`remielle-test`），用于确认 Qoder 的 8×11 行列解析。

### 说明

- Qoder 端状态由 IDE 自身状态机驱动，无法像 Trae Hooks 那样逐事件定制；当前覆盖 Qoder
  暴露的 idle / running / waiting / review / failed 与全部原生交互。
- 安装器与转换脚本已在 Windows 11（150% DPI）上完成安装、覆盖更新、备份、卸载与资源完整性验证；
  Qoder 内的最终显示效果以桌面宠物设置中的实际预览为准。
