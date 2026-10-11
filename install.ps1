# ClawdPet 安装脚本 — 自动配置 Trae 扩展和 Hooks
# 用法: 解压后右键此文件 -> "使用 PowerShell 运行"
$ErrorActionPreference = "Stop"
$installDir = $PSScriptRoot
Write-Host "=== ClawdPet 安装程序 ===" -ForegroundColor Cyan
Write-Host "安装目录: $installDir`n"

# ============================================================
# 1. 检测 Trae 版本（CN / 国际版）
# ============================================================
$traeExtRoot = $null
$traeUserDir = $null
$traeHooksPath = $null
$traeSettingsPath = $null

foreach ($candidate in @(
    @{ ext = "$env:USERPROFILE\.trae-cn\extensions"; user = "$env:APPDATA\Trae CN\User"; hooks = "$env:USERPROFILE\.trae-cn\hooks.json"; name = "Trae CN" },
    @{ ext = "$env:USERPROFILE\.trae\extensions"; user = "$env:APPDATA\Trae\User"; hooks = "$env:USERPROFILE\.trae\hooks.json"; name = "Trae" }
)) {
    if (Test-Path $candidate.ext) {
        $traeExtRoot = $candidate.ext
        $traeUserDir = $candidate.user
        $traeHooksPath = $candidate.hooks
        $traeSettingsPath = Join-Path $candidate.user "settings.json"
        Write-Host "[OK] 检测到 $($candidate.name)" -ForegroundColor Green
        break
    }
}

if (-not $traeExtRoot) {
    Write-Host "[错误] 未找到 Trae IDE，请先安装 Trae CN 或 Trae 国际版" -ForegroundColor Red
    Write-Host "  CN: https://www.trae.com.cn" -ForegroundColor Yellow
    Write-Host "  国际版: https://www.trae.ai" -ForegroundColor Yellow
    exit 1
}

# ============================================================
# 2. 安装桥接扩展
# ============================================================
Write-Host "`n[1/5] 安装 Trae 桥接扩展..." -ForegroundColor Yellow
$extDir = Join-Path $traeExtRoot "remielle.trae-pet-bridge-2.0.0"

# 清理旧版本扩展目录，避免新旧共存被 Trae 重复加载
Get-ChildItem $traeExtRoot -Directory -Filter "remielle.trae-pet-bridge-*" -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne "remielle.trae-pet-bridge-2.0.0" } |
    ForEach-Object {
        Write-Host "  清理旧版扩展: $($_.Name)" -ForegroundColor Gray
        Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
New-Item -ItemType Directory -Force -Path $extDir | Out-Null
Copy-Item "$installDir\trae-pet-bridge\*" -Destination $extDir -Recurse -Force
Write-Host "  -> $extDir" -ForegroundColor Green

# ============================================================
# 3. 检查并安装依赖（Electron）
# ============================================================
Write-Host "`n[2/5] 检查依赖..." -ForegroundColor Yellow
$electronExe = Join-Path $installDir "node_modules\electron\dist\electron.exe"

if (Test-Path $electronExe) {
    Write-Host "  [OK] Electron 运行时已就绪" -ForegroundColor Green
} else {
    Write-Host "  未找到 Electron，正在用国内源安装..." -ForegroundColor Yellow

    # 配置国内镜像
    $env:ELECTRON_MIRROR = "https://npmmirror.com/mirrors/electron/"
    $env:ELECTRON_BUILDER_BINARIES_MIRROR = "https://npmmirror.com/mirrors/electron-builder-binaries/"
    $env:npm_config_registry = "https://registry.npmmirror.com"

    Push-Location $installDir
    try {
        # 只安装 electron（跳过 electron-builder，减小体积）
        npm install electron@37 --registry=https://registry.npmmirror.com --save-dev
        if ($LASTEXITCODE -ne 0) { throw "npm install 失败" }
        Write-Host "  [OK] Electron 安装完成" -ForegroundColor Green
    } catch {
        Write-Host "  [错误] Electron 安装失败: $_" -ForegroundColor Red
        Write-Host "  请手动运行: npm install electron@37 --registry=https://registry.npmmirror.com" -ForegroundColor Yellow
        exit 1
    } finally {
        Pop-Location
    }
}

# ============================================================
# 4. 配置 Trae 设置（自动启动 + 项目路径）
# ============================================================
Write-Host "`n[3/5] 配置 Trae 设置..." -ForegroundColor Yellow
New-Item -ItemType Directory -Force -Path $traeUserDir | Out-Null

$settings = @{}
if (Test-Path $traeSettingsPath) {
    $existingSettings = Get-Content $traeSettingsPath -Raw | ConvertFrom-Json
    if ($existingSettings) {
        foreach ($property in $existingSettings.PSObject.Properties) {
            $settings[$property.Name] = $property.Value
        }
    }
}
$settings["traePetBridge.autoLaunch"] = $true
$settings["traePetBridge.petPath"] = $installDir
# 注意：必须写无 BOM 的 UTF-8。PowerShell 5.1 的 Out-File -Encoding utf8 会带 BOM，
# Trae 对带 BOM 的 JSON 解析会失败（hooks 会被整体静默忽略）。
$settingsJson = $settings | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText($traeSettingsPath, $settingsJson, [System.Text.UTF8Encoding]::new($false))
Write-Host "  -> $traeSettingsPath" -ForegroundColor Green
Write-Host "  petPath = $installDir" -ForegroundColor Gray

# ============================================================
# 5. 配置 Hooks
# ============================================================
Write-Host "`n[4/5] 配置 Hooks..." -ForegroundColor Yellow

$nodePath = $null

# 优先使用 Trae 自带的 Node（始终随 Trae 存在，比依赖系统安装更可靠）
foreach ($traeNodeRoot in @("$env:USERPROFILE\.trae-cn\binaries\node\versions", "$env:USERPROFILE\.trae\binaries\node\versions")) {
    if (Test-Path $traeNodeRoot) {
        $traeNode = Get-ChildItem $traeNodeRoot -Directory -ErrorAction SilentlyContinue |
            Sort-Object { try { [version]$_.Name } catch { [version]'0.0' } } -Descending |
            ForEach-Object { Join-Path $_.FullName "node.exe" } |
            Where-Object { Test-Path $_ } |
            Select-Object -First 1
        if ($traeNode) { $nodePath = $traeNode; break }
    }
}

# 备选：项目本地 Electron 自带的 node.exe（部分发行版包含）
if (-not $nodePath) {
    $localNode = Join-Path $installDir "node_modules\electron\dist\node.exe"
    if (Test-Path $localNode) { $nodePath = $localNode }
}

# 备选：系统 PATH / 常见安装位置
if (-not $nodePath) {
    foreach ($candidate in @(
        (Get-Command node -ErrorAction SilentlyContinue).Source,
        "C:\Program Files\nodejs\node.exe"
    )) {
        if ($candidate -and (Test-Path $candidate)) { $nodePath = $candidate; break }
    }
}

if (-not $nodePath) {
    Write-Host "  [警告] 未找到 Node.js，Hooks 将不可用。请安装 Node.js 后重新运行本脚本。" -ForegroundColor Red
    Write-Host "  https://nodejs.org" -ForegroundColor Yellow
    $nodePath = "node"
} else {
    Write-Host "  使用 Node: $nodePath" -ForegroundColor Gray
}

# 注意：必须写入完整路径，禁止使用 8.3 短路径（如 TRAE-C~1）。
# Trae 升级自带 Node 版本后短路径会失效，Hooks 将被静默跳过。
# 路径含空格时加引号，避免 shell 解析错误。
$hookScript = "$installDir\hook-handler.js" -replace '\\', '/'
$nodeCmd = if ($nodePath -match ' ') { '"{0}"' -f $nodePath } else { $nodePath }
$scriptCmd = if ($hookScript -match ' ') { '"{0}"' -f $hookScript } else { $hookScript }
$cmd = "$nodeCmd $scriptCmd"

$hooksConfig = [ordered]@{
    version = 1
    hooks = [ordered]@{
        # matcher 字段与 Trae Hooks UI 启用后生成的格式对齐，避免覆盖后需要重新启用；
        # [ordered] 保持键序（type/command/timeout）与 UI 生成的结构一致
        UserPromptSubmit = @([ordered]@{ hooks = @([ordered]@{ type = "command"; command = $cmd; timeout = 10 }) })
        PreToolUse       = @([ordered]@{ matcher = "*"; hooks = @([ordered]@{ type = "command"; command = $cmd; timeout = 10 }) })
        PostToolUse      = @([ordered]@{ matcher = "*"; hooks = @([ordered]@{ type = "command"; command = $cmd; timeout = 10 }) })
        Notification     = @([ordered]@{ matcher = "*"; hooks = @([ordered]@{ type = "command"; command = $cmd; timeout = 10 }) })
        Stop             = @([ordered]@{ hooks = @([ordered]@{ type = "command"; command = $cmd; timeout = 10 }) })
    }
}

# 写入全局 hooks（无 BOM UTF-8，BOM 会导致 Trae 解析失败、Hooks 被静默忽略）
$hooksJson = $hooksConfig | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText($traeHooksPath, $hooksJson, [System.Text.UTF8Encoding]::new($false))
Write-Host "  全局 Hooks -> $traeHooksPath" -ForegroundColor Green

# 同时写入项目级 hooks（如果存在 .trae 目录）
$projectHooksDir = Join-Path $installDir ".trae"
if (Test-Path $projectHooksDir) {
    $projectHooksPath = Join-Path $projectHooksDir "hooks.json"
    [System.IO.File]::WriteAllText($projectHooksPath, $hooksJson, [System.Text.UTF8Encoding]::new($false))
    Write-Host "  项目 Hooks -> $projectHooksPath" -ForegroundColor Green
}

# ============================================================
# 6. 验证 Electron 运行时
# ============================================================
Write-Host "`n[5/5] 验证运行时..." -ForegroundColor Yellow
$electronExe = Join-Path $installDir "node_modules\electron\dist\electron.exe"
if (Test-Path $electronExe) {
    Write-Host "  [OK] Electron 运行时就绪" -ForegroundColor Green
} else {
    Write-Host "  [错误] Electron 运行时仍不存在，安装未完成" -ForegroundColor Red
    exit 1
}

# ============================================================
# 完成
# ============================================================
Write-Host ""

Write-Host "=== 安装完成 ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "下一步:" -ForegroundColor Cyan
Write-Host "  1. 重启 Trae IDE" -ForegroundColor Cyan
Write-Host "  2. 宠物会自动启动并出现在屏幕右下角" -ForegroundColor Cyan
Write-Host "  3. 手动启用 Hooks（必须！Trae 1.107+ 未启用时 Hooks 完全不会执行）:" -ForegroundColor Yellow
Write-Host "     - 设置 → Hooks → 已配置的 Hooks → 全局 → 打开开关，在安全警示中确认「启用」" -ForegroundColor Yellow
Write-Host "     - 设置 → Hooks → 执行模式 → 切换为「本地自动运行」" -ForegroundColor Yellow
Write-Host "     （默认的沙箱模式下 Hook 命令可能被静默跳过，宠物不会有任何反应）" -ForegroundColor DarkYellow
Write-Host "  4. 向 Agent 发送消息，宠物会自动切换状态" -ForegroundColor Cyan
Write-Host ""
Write-Host "状态说明:" -ForegroundColor Cyan
Write-Host "  idle         空闲" -ForegroundColor Cyan
Write-Host "  thinking     Agent 思考中" -ForegroundColor Cyan
Write-Host "  editing      Agent 执行命令/改文件" -ForegroundColor Cyan
Write-Host "  notification Agent 提问/任务完成" -ForegroundColor Cyan
Write-Host "  error        Agent 异常" -ForegroundColor Cyan
Write-Host "  attention    单击宠物" -ForegroundColor Cyan
Write-Host ""
Write-Host "自定义:" -ForegroundColor Cyan
Write-Host "  - 宠物动画: 替换 Remielle_Dan-clawd-theme-v0.5.4\assets\ 中的 GIF" -ForegroundColor Cyan
Write-Host "  - 宠物配置: 编辑 Remielle_Dan-clawd-theme-v0.5.4\theme.json" -ForegroundColor Cyan
Write-Host "  - 自动启动: Trae 设置 -> traePetBridge.autoLaunch" -ForegroundColor Cyan
Write-Host "  - 宠物路径: Trae 设置 -> traePetBridge.petPath" -ForegroundColor Cyan
