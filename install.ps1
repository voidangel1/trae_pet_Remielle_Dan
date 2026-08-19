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
$extDir = Join-Path $traeExtRoot "remielle.trae-pet-bridge-0.1.0"
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
$settings | ConvertTo-Json -Depth 10 | Out-File $traeSettingsPath -Encoding utf8 -Force
Write-Host "  -> $traeSettingsPath" -ForegroundColor Green
Write-Host "  petPath = $installDir" -ForegroundColor Gray

# ============================================================
# 5. 配置 Hooks
# ============================================================
Write-Host "`n[4/5] 配置 Hooks..." -ForegroundColor Yellow

$nodePath = $null
$localNode = Join-Path $installDir "node_modules\electron\dist\node.exe"
if (Test-Path $localNode) {
    $nodePath = $localNode
} else {
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
}

$fso = New-Object -ComObject Scripting.FileSystemObject
try { $nodeShort = $fso.GetFile($nodePath).ShortPath } catch { $nodeShort = $nodePath }
$hookScript = "$installDir\hook-handler.js" -replace '\\', '/'
$cmd = "$nodeShort $hookScript"

$hooksConfig = @{
    version = 1
    hooks = @{
        UserPromptSubmit = @(@{ hooks = @(@{ type = "command"; command = $cmd; timeout = 5 }) })
        PreToolUse       = @(@{ hooks = @(@{ type = "command"; command = $cmd; timeout = 5 }) })
        PostToolUse      = @(@{ hooks = @(@{ type = "command"; command = $cmd; timeout = 5 }) })
        Notification     = @(@{ hooks = @(@{ type = "command"; command = $cmd; timeout = 5 }) })
        Stop             = @(@{ hooks = @(@{ type = "command"; command = $cmd; timeout = 5 }) })
    }
}

# 写入全局 hooks
$hooksJson = $hooksConfig | ConvertTo-Json -Depth 10
$hooksJson | Out-File $traeHooksPath -Encoding utf8 -Force
Write-Host "  全局 Hooks -> $traeHooksPath" -ForegroundColor Green

# 同时写入项目级 hooks（如果存在 .trae 目录）
$projectHooksDir = Join-Path $installDir ".trae"
if (Test-Path $projectHooksDir) {
    $projectHooksPath = Join-Path $projectHooksDir "hooks.json"
    $hooksJson | Out-File $projectHooksPath -Encoding utf8 -Force
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
Write-Host "  3. 向 Agent 发送消息，宠物会自动切换状态" -ForegroundColor Cyan
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
