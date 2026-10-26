# 安装与使用说明（Qoder 版）

Remielle 桌宠 v1.0.0 —— Qoder 原生桌面宠物（Petdex），零补丁、零后台进程、无需管理员权限。

支持：Qoder CN 0.4.8+（Windows 10/11）。宠物包格式为 Petdex sprite v2（8 列 × 11 行）。

## 一、安装

### 1. EXE 安装器（推荐）

1. 关闭正在运行的 Qoder（不是必须，但建议，避免刷新列表时读到旧目录）。
2. 双击 `release\Remielle-QoderPet-Setup-v1.0.0.exe`（约 2.3 MB，单文件，宠物包全部内嵌）。
3. 点「安装 / 更新」，等待日志出现 `[OK] 已安装 ...`。
4. 打开 Qoder → **设置 → 桌面宠物（Desktop Pet）→ 刷新** → 选择「蕾米埃尔」。

安装器只写入：

```
%USERPROFILE%\.petdex\pets\remielle\
├── pet.json
└── spritesheet.webp
```

不会修改 Qoder 安装目录、`settings.json`、扩展或缓存，也不会注册服务、开机启动或后台进程。

### 2. 手动安装（开发者）

```bash
npm install
npm run convert:qoder
```

```powershell
$dst = "$env:USERPROFILE\.petdex\pets\remielle"
New-Item -ItemType Directory -Force $dst | Out-Null
Copy-Item qoder-pet\dist\remielle\* $dst -Force
```

然后回到 Qoder 桌面宠物设置点「刷新」。

> 目录名（`remielle`）必须与 `pet.json` 中 `id` 完全一致，否则 Qoder 会忽略这个包。

### 3. 验证

安装器「重新检测」显示 `已安装：...\remielle（2.0 MB）` 即写入成功。Qoder 端按以下顺序确认：

1. 设置里的桌面宠物总开关已打开。
2. 宠物列表中出现「蕾米埃尔」。
3. 选中后，桌面上出现约 192px 宽的角色，空闲时播放呼吸动画。
4. 鼠标悬停、左右拖拽、启动 Qoder，分别触发轻跳、左右移动、挥手。

## 二、使用

宠物由 Qoder 驱动，不需要额外操作：

| Qoder 状态 / 交互 | 表现 |
|---|---|
| 空闲 | 呼吸循环 |
| Agent 运行中 | 低头忙碌 |
| 等待你操作 | 举手等待 |
| 代码审查 | 思考 |
| 任务失败 | 懊恼 |
| 启动 | 挥手问候 |
| 悬停 | 轻跳 |
| 左右拖拽 | 朝拖动方向移动 |
| 鼠标移动 | 视线跟随 |

在桌面宠物设置中切换其他角色，或关闭总开关，即可暂停 Remielle，不会影响已安装的包。

## 三、卸载

### 安装器

打开安装器 → 「卸载」→ 确认。安装器会：

- 校验目标目录的 `pet.json` 确为 `id: remielle`，避免误删其他宠物；
- 只删除 `%USERPROFILE%\.petdex\pets\remielle`；
- 保留 `%LOCALAPPDATA%\RemielleQoderPet\backups` 中的历史备份和 `install.log`。

卸载后在 Qoder 桌面宠物设置刷新或改选其他角色。若 Qoder 仍显示蕾米埃尔，刷新列表或重启 Qoder 即可。

### 手动卸载

```powershell
Remove-Item "$env:USERPROFILE\.petdex\pets\remielle" -Recurse -Force
```

## 四、文件与备份

| 路径 | 用途 |
|---|---|
| `%USERPROFILE%\.petdex\pets\remielle\` | 宠物包本体（安装器唯一写入位置） |
| `%LOCALAPPDATA%\RemielleQoderPet\backups\` | 安装前同名目录的备份（`remielle-yyyyMMdd-HHmmss`） |
| `%LOCALAPPDATA%\RemielleQoderPet\install.log` | 安装 / 卸载日志 |

安装器写入前会：

1. 校验内嵌 `pet.json` 的 `id` 与 `spriteVersionNumber`；
2. 校验 `spritesheet.webp` 大小（100 KB–32 MB）与 RIFF/WEBP 文件头；
3. 若目标目录已存在且内容不同，先完整备份；内容相同则跳过备份；
4. 复制失败时自动从备份回滚。

## 五、常见问题

### 1. 刷新后列表里没有「蕾米埃尔」

- 确认目录是 `%USERPROFILE%\.petdex\pets\remielle`（不是 `%APPDATA%`，也没有多套一层目录）。
- 确认 `pet.json` 的 `id` 为 `remielle`，文件名严格为 `pet.json`。
- 确认目录里只有一个精灵图文件，且名为 `spritesheet.webp`（或 `.png`），不能同时存在两个。
- 在设置里点「刷新」；仍不出现则完全退出并重启 Qoder。
- 检查 Qoder 版本是否为 0.4.8+，桌面宠物功能是否可用。

### 2. 宠物出现了，但只有一种动作

- Qoder 的 Agent 状态需要 IDE 内实际发生对应事件才会切换；没有任务时停在 idle 是正常的。
- 打开 `qoder-pet/dist/preview.png` 核对图集：每一行的前 N 格应有连续动作，未使用格保持透明。
- 若只有启动挥手、悬停和拖拽正常，说明图集本身可用，只是当前没有触发 Agent 状态。

### 3. 为什么有「Remielle Test」和「蕾米埃尔」两个宠物？

`remielle-test` 是 Phase 0 的兼容性测试卡（彩色编号格子，用于确认 Qoder 的行列解析），没有艺术素材。不需要时直接删除：

```powershell
Remove-Item "$env:USERPROFILE\.petdex\pets\remielle-test" -Recurse -Force
```

然后在桌面宠物设置里刷新。

### 4. Qoder 升级后宠物变了 / 不见了

Qoder 升级可能调整 Petdex 校验规则或图集规格。先重新运行安装器；若仍异常，请用 `qoder-pet/dist/preview.png` 核对图集，或按[构建](#六构建)重新生成。安装器不会修改 Qoder 程序，因此升级不会破坏 Qoder 本身。

### 5. 安装器提示目录已存在但不是本安装器的包

说明 `%USERPROFILE%\.petdex\pets\remielle` 被其他来源占用。安装器会先备份再覆盖；卸载时只识别 `id: remielle`。若要保留原包，请先手动备份该目录。

### 6. 想恢复官方宠物

在 Qoder 桌面宠物设置里选择官方角色即可。Remielle 的包可以保留，随时切回。

### 7. 高 DPI 下宠物模糊 / 太大太小

图集单元格固定为 192×208，由 Qoder 按界面缩放渲染；安装器本身已做 DPI 适配。不要手动缩放 `spritesheet.webp`，否则会破坏 12:13 的单元格比例。

## 六、构建

前置：Windows、Node.js 18+、.NET Framework 4.x（系统自带 `csc.exe`）。

```bash
npm install
npm run convert:qoder   # qoder-pet/dist/remielle/{pet.json,spritesheet.webp} + preview.png + meta.json
npm run qoder:art       # qoder-pet/dist/installer-preview.png + remielle.ico
npm run qoder:build     # release/Remielle-QoderPet-Setup-v1.0.0.exe
```

转换脚本会校验：

- 图集 1536×2288，单元格 192×208，比例 12:13；
- 8 列 × 11 行，静态单页，带透明通道；
- 文件不超过 32 MB；
- `pet.json` 的 `id` 与目录名一致。

构建脚本使用 `csc /resource` 把 `pet.json`、`spritesheet.webp`、安装器预览图嵌入 EXE，输出单文件安装器。

## 七、限制

- 状态由 Qoder 提供，无法像 Trae Hooks 那样精确到「AI 提问」「每次工具调用」级别；当前映射覆盖 Qoder 暴露的四个 Agent 阶段和全部原生交互。
- Qoder 的 Petdex 规格可能随版本变化；升级后若图集不被接受，需要重新生成。
- 宠物包为静态图集，不含音频、特效层或独立窗口。
- Trae 版（Hooks 六态、可切换官方四态）仍在 `master` 分支。
