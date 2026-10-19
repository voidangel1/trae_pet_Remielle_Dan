# build-installer.ps1 — 编译 ClawdPet 安装器 EXE（csc，零第三方依赖）
#
# 产出: ..\release\ClawdPet-Setup-v2.0.3.exe（单文件，素材/扩展/脚本全部嵌入）
# 前置: native-pet\assets\*.webp（皮肤 8 张 + official-backup 官方 4 张）已生成
#       （运行 native-pet\convert-native-pet.js；官方备份来自首次部署）
param(
    [string]$Output = ""
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $here
$build = Join-Path $here 'build'
if (-not $Output) { $Output = Join-Path (Join-Path $root 'release') 'ClawdPet-Setup-v2.0.3.exe' }

# ---------- 1. 暂存资源（统一命名） ----------
Remove-Item $build -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $build | Out-Null

$assets = Join-Path (Join-Path $root 'native-pet') 'assets'
$skinMap = @{
    'default.webp' = 'skin_default.webp'; 'busy.webp' = 'skin_busy.webp'
    'super-busy.webp' = 'skin_super-busy.webp'; 'hover-drag.webp' = 'skin_hover-drag.webp'
    'thinking.webp' = 'skin_thinking.webp'; 'editing.webp' = 'skin_editing.webp'
    'notification.webp' = 'skin_notification.webp'; 'error.webp' = 'skin_error.webp'
}
foreach ($k in $skinMap.Keys) {
    $src = Join-Path $assets $k
    if (-not (Test-Path $src)) { throw "缺少皮肤素材 $k（先运行 native-pet\convert-native-pet.js）" }
    Copy-Item $src (Join-Path $build $skinMap[$k]) -Force
}

# 官方素材（卸载还原用）
$officialBackup = Join-Path $assets 'official-backup'
foreach ($f in @('busy.webp', 'default.webp', 'hover-drag.webp', 'super-busy.webp')) {
    $src = Join-Path $officialBackup $f
    if (-not (Test-Path $src)) { throw "缺少官方素材备份 $f（需要一份原始 Trae 的 traebao 素材）" }
    Copy-Item $src (Join-Path $build "official_$f") -Force
}

Copy-Item (Join-Path $root 'hook-handler.js') (Join-Path $build 'rt_hook-handler.js') -Force
Copy-Item (Join-Path $here 'poller.html') (Join-Path $build 'rt_poller.html') -Force
Copy-Item (Join-Path (Join-Path $root 'trae-pet-bridge') 'extension.js') (Join-Path $build 'ext_extension.js') -Force
Copy-Item (Join-Path (Join-Path $root 'trae-pet-bridge') 'package.json') (Join-Path $build 'ext_package.json') -Force

# ---------- 2. csc 编译 ----------
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (-not (Test-Path $csc)) { throw "未找到 .NET Framework csc 编译器" }

# 每个资源一个数组元素（/resource:完整路径,资源名），
# PowerShell 传参时自动整体加引号，无需手写内嵌引号
$resources = Get-ChildItem $build -File | ForEach-Object { "/resource:$($_.FullName),$($_.Name)" }
$srcFile = Join-Path $here 'ClawdPetInstaller.cs'

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Output) | Out-Null
$argv = @(
    '/nologo', '/target:winexe', '/platform:anycpu',
    "/out:$Output",
    '/codepage:65001',                      # 源码 UTF-8（含中文）
    "/win32manifest:$(Join-Path $here 'app.manifest')",
    '/r:System.dll', '/r:System.Core.dll',
    '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll',
    '/r:System.Web.Extensions.dll',
    '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll',  # Node zip 解压（.NET 4.5+ 自带）
    $srcFile
) + $resources

& $csc @argv
if ($LASTEXITCODE -ne 0) { throw "编译失败（exit $LASTEXITCODE）" }

$size = [math]::Round((Get-Item $Output).Length / 1MB, 1)
Write-Host "[完成] $Output（${size} MB）" -ForegroundColor Green
Write-Host "  完整安装 = 原生六态 + 官方四态切换；轻量安装 = 仅官方四态；卸载 = 全版本还原官方。"
