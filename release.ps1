# ClawdPet 发布打包脚本
# 用法: 在项目根目录运行 .\release.ps1
# 会生成 release\ClawdPet-v0.5.4.zip，可直接发给他人
$ErrorActionPreference = "Stop"
$projectDir = $PSScriptRoot
$releaseDir = Join-Path $projectDir "release"
$releaseName = "ClawdPet-v0.5.4"
$stagingDir = Join-Path $releaseDir $releaseName

Write-Host "=== ClawdPet 发布打包 ===" -ForegroundColor Cyan

# 清理旧目录
if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stagingDir | Out-Null

# ============================================================
# 1. 复制宠物运行时文件
# ============================================================
Write-Host "`n[1/5] 复制运行时文件..." -ForegroundColor Yellow

# 宠物源码
$petFiles = @("main.js", "pet.js", "preload.js", "index.html", "style.css", "package.json", "hook-handler.js")
foreach ($f in $petFiles) {
    Copy-Item (Join-Path $projectDir $f) (Join-Path $stagingDir $f) -Force
}
Write-Host "  → 宠物源码 ($($petFiles.Count) 个文件)" -ForegroundColor Green

# 主题资源
$themeSrc = Join-Path $projectDir "Remielle_Dan-clawd-theme-v0.5.4"
$themeDst = Join-Path $stagingDir "Remielle_Dan-clawd-theme-v0.5.4"
Copy-Item $themeSrc $themeDst -Recurse -Force
Write-Host "  → 主题资源" -ForegroundColor Green

# ============================================================
# 2. 复制桥接扩展
# ============================================================
Write-Host "`n[2/5] 复制桥接扩展..." -ForegroundColor Yellow
$extSrc = Join-Path $projectDir "trae-pet-bridge"
$extDst = Join-Path $stagingDir "trae-pet-bridge"
Copy-Item $extSrc $extDst -Recurse -Force
Write-Host "  → trae-pet-bridge" -ForegroundColor Green

# ============================================================
# 3. 复制安装脚本和配置
# ============================================================
Write-Host "`n[3/5] 复制安装脚本..." -ForegroundColor Yellow
Copy-Item (Join-Path $projectDir "install.ps1") (Join-Path $stagingDir "install.ps1") -Force
Copy-Item (Join-Path $projectDir "INSTALL.md") (Join-Path $stagingDir "INSTALL.md") -Force
Write-Host "  → install.ps1 / INSTALL.md" -ForegroundColor Green

# ============================================================
# 4. 清理不需要的文件
# ============================================================
Write-Host "`n[4/5] 清理..." -ForegroundColor Yellow
Write-Host "  → 无需清理" -ForegroundColor Gray

# ============================================================
# 5. 生成压缩包
# ============================================================
Write-Host "`n[5/5] 生成压缩包..." -ForegroundColor Yellow
$zipPath = Join-Path $releaseDir "$releaseName.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

# 用 .NET 压缩（支持大文件）
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stagingDir, $zipPath)

$zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "  → $zipPath ($zipSize MB)" -ForegroundColor Green

# ============================================================
# 完成
# ============================================================
Write-Host @"

=== 打包完成 ===

发布包: $zipPath ($zipSize MB)

给他人安装:
  1. 解压 zip 到任意目录
  2. 右键 install.ps1 → "使用 PowerShell 运行"
  3. 重启 Trae IDE

注意:
  - 发布包体积很小（不含 Electron 运行时），接收方安装时会自动用国内源下载
  - 不要删除解压后的目录，宠物运行时需要它
  - 如果更换了宠物主题，重新运行 release.ps1 打包即可
"@ -ForegroundColor Cyan
