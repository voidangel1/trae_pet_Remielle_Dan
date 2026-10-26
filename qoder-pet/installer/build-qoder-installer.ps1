# build-qoder-installer.ps1 — 编译 Remielle Qoder 桌宠安装器 EXE（csc，零第三方依赖）
#
# 产出: ..\release\Remielle-QoderPet-Setup-v1.0.0.exe（单文件，宠物包/预览图全部嵌入）
# 前置: qoder-pet\dist\remielle\{pet.json, spritesheet.webp}
#       qoder-pet\dist\installer-preview.png、remielle.ico
#       （运行 node qoder-pet\convert-qoder-pet.js 与 node qoder-pet\make-installer-art.js）
param(
    [string]$Output = ""
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $here
$dist = Join-Path $root 'dist'
$build = Join-Path $here 'build'
if (-not $Output) { $Output = Join-Path (Join-Path (Split-Path -Parent $root) 'release') 'Remielle-QoderPet-Setup-v1.0.0.exe' }

# ---------- 1. 暂存资源（统一命名） ----------
Remove-Item $build -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $build | Out-Null

$petJson = Join-Path (Join-Path $dist 'remielle') 'pet.json'
$sheet = Join-Path (Join-Path $dist 'remielle') 'spritesheet.webp'
$preview = Join-Path $dist 'installer-preview.png'
$icon = Join-Path $dist 'remielle.ico'
foreach ($f in @($petJson, $sheet, $preview, $icon)) {
    if (-not (Test-Path $f)) { throw "缺少资源 $f（先运行 qoder-pet 下的转换与制图脚本）" }
}
Copy-Item $petJson (Join-Path $build 'pet_pet.json') -Force
Copy-Item $sheet (Join-Path $build 'pet_spritesheet.webp') -Force
Copy-Item $preview (Join-Path $build 'ui_preview.png') -Force

# ---------- 2. csc 编译 ----------
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (-not (Test-Path $csc)) { throw "未找到 .NET Framework csc 编译器" }

$resources = Get-ChildItem $build -File | ForEach-Object { "/resource:$($_.FullName),$($_.Name)" }
$srcFile = Join-Path $here 'RemielleQoderInstaller.cs'

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Output) | Out-Null
$argv = @(
    '/nologo', '/target:winexe', '/platform:anycpu',
    "/out:$Output",
    '/codepage:65001',
    "/win32manifest:$(Join-Path $here 'app.manifest')",
    "/win32icon:$icon",
    '/r:System.dll', '/r:System.Core.dll',
    '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll',
    $srcFile
) + $resources

& $csc @argv
if ($LASTEXITCODE -ne 0) { throw "编译失败（exit $LASTEXITCODE）" }

$size = [math]::Round((Get-Item $Output).Length / 1MB, 1)
Write-Host "[完成] $Output（${size} MB）" -ForegroundColor Green
Write-Host "  安装 = 写入 %USERPROFILE%\.petdex\pets\remielle；卸载 = 仅删除该目录（校验 id）。"
