# ClawdPet 发布打包脚本
# 用法: 在项目根目录运行 .\release.ps1
# 会生成 release\ClawdPet-v2.0.2.zip，可直接发给他人
$ErrorActionPreference = "Stop"
$projectDir = $PSScriptRoot
$releaseDir = Join-Path $projectDir "release"
$releaseName = "ClawdPet-v2.0.2"
$stagingDir = Join-Path $releaseDir $releaseName

Write-Host "=== ClawdPet 发布打包 ===" -ForegroundColor Cyan

# 清理旧目录
if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stagingDir | Out-Null

# ============================================================
# 1. 复制 v2.0 文件（原生桌宠）
# ============================================================
Write-Host "`n[1/5] 复制文件..." -ForegroundColor Yellow

$petFiles = @("package.json", "hook-handler.js", "INSTALL.md", "README.md")
foreach ($f in $petFiles) {
    Copy-Item (Join-Path $projectDir $f) (Join-Path $stagingDir $f) -Force
}
Write-Host "  → 根文件 ($($petFiles.Count) 个)" -ForegroundColor Green

# 主题资源（复制内容而非目录本身，避免目标已存在时形成嵌套）
$themeSrc = Join-Path $projectDir "Remielle_Dan-clawd-theme-v0.5.4"
$themeDst = Join-Path $stagingDir "Remielle_Dan-clawd-theme-v0.5.4"
New-Item -ItemType Directory -Force -Path $themeDst | Out-Null
Copy-Item "$themeSrc\*" $themeDst -Recurse -Force
Write-Host "  → 主题资源" -ForegroundColor Green

# ============================================================
# 2. 复制桥接扩展
# ============================================================
Write-Host "`n[2/5] 复制桥接扩展..." -ForegroundColor Yellow
$extSrc = Join-Path $projectDir "trae-pet-bridge"
$extDst = Join-Path $stagingDir "trae-pet-bridge"
New-Item -ItemType Directory -Force -Path $extDst | Out-Null
Copy-Item "$extSrc\*" $extDst -Recurse -Force
Write-Host "  → trae-pet-bridge" -ForegroundColor Green

# ============================================================
# 2.5 复制 Trae 原生桌宠皮肤（v2.0 新增）
# ============================================================
Write-Host "`n[2.5/5] 复制原生桌宠皮肤..." -ForegroundColor Yellow
$nativeSrc = Join-Path $projectDir "native-pet"
$nativeDst = Join-Path $stagingDir "native-pet"
New-Item -ItemType Directory -Force -Path $nativeDst | Out-Null
Copy-Item "$nativeSrc\convert-native-pet.js" $nativeDst -Force
Copy-Item "$nativeSrc\deploy-native-pet.ps1" $nativeDst -Force
Copy-Item "$nativeSrc\deploy-step-main.ps1" $nativeDst -Force
New-Item -ItemType Directory -Force -Path "$nativeDst\assets" | Out-Null
foreach ($f in @("default.webp", "busy.webp", "super-busy.webp", "hover-drag.webp",
                 "thinking.webp", "editing.webp", "notification.webp", "error.webp")) {
    Copy-Item "$nativeSrc\assets\$f" "$nativeDst\assets" -Force
}
# official-backup 为本机专属备份（含本机 main.js 原件），不随发布包分发
Write-Host "  → native-pet（8 张皮肤 webp + 部署/转换脚本）" -ForegroundColor Green

# ============================================================
# 2.7 打包 EXE 安装器（v2.0 新增：双模式安装 + 全版本卸载还原）
# ============================================================
Write-Host "`n[2.7/5] 打包 EXE 安装器..." -ForegroundColor Yellow
$setupExe = Join-Path $releaseDir "ClawdPet-Setup-v2.0.2.exe"
# 安装器源或皮肤有更新时重新编译（无改动时直接复用）
$installerSrc = Join-Path $projectDir "installer\ClawdPetInstaller.cs"
$needsBuild = -not (Test-Path $setupExe)
if (-not $needsBuild) {
    $exeTime = (Get-Item $setupExe).LastWriteTime
    foreach ($dep in @($installerSrc, (Join-Path $projectDir "hook-handler.js"), (Join-Path $projectDir "trae-pet-bridge\extension.js"))) {
        if ((Get-Item $dep).LastWriteTime -gt $exeTime) { $needsBuild = $true; break }
    }
    if (-not $needsBuild) {
        foreach ($f in @("default.webp", "busy.webp", "super-busy.webp", "hover-drag.webp", "thinking.webp", "editing.webp", "notification.webp", "error.webp")) {
            if ((Get-Item "$nativeSrc\assets\$f").LastWriteTime -gt $exeTime) { $needsBuild = $true; break }
        }
    }
}
if ($needsBuild) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $projectDir "installer\build-installer.ps1")
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $setupExe)) { throw "安装器编译失败" }
}
Copy-Item $setupExe (Join-Path $releaseDir "tmp-setup.exe") -Force  # 仅用于体积显示
Remove-Item (Join-Path $releaseDir "tmp-setup.exe") -Force
$setupSize = [math]::Round((Get-Item $setupExe).Length / 1MB, 1)
Write-Host "  → ClawdPet-Setup-v2.0.2.exe（$setupSize MB，已生成在 release\ 下，单文件可直接分发）" -ForegroundColor Green

# ============================================================
# 3. 复制安装脚本和配置
# ============================================================
Write-Host "`n[3/5] 复制文档..." -ForegroundColor Yellow
Write-Host "  → INSTALL.md / README.md（已在步骤 1 复制）" -ForegroundColor Green

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

# 用 PowerShell 内置压缩（无需加载 .NET 程序集，兼容受限环境）
Compress-Archive -Path "$stagingDir\*" -DestinationPath $zipPath -Force

$zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "  → $zipPath ($zipSize MB)" -ForegroundColor Green

# ============================================================
# 完成
# ============================================================
Write-Host @"

=== 打包完成 ===

发布包: $zipPath ($zipSize MB)

给他人安装（两种方式任选）:
  ★ EXE 安装器（推荐，单文件，无需解压）:
    直接发送 release\ClawdPet-Setup-v2.0.2.exe
    → 完整安装：Hooks 六态 + 官方四态切换 + 安装后指引
    → 轻量安装：仅官方四态皮肤（不动悬浮窗页面/Hooks，最稳定）
    → 卸载：v2.0 + 旧版 1.0-1.4 全部还原官方
  脚本部署（Trae 升级后的重部署 / 无扩展的皮肤模式）:
    1. 解压 zip 到任意目录
    2. 右键 native-pet\deploy-native-pet.ps1 → "使用 PowerShell 运行"（会弹 UAC）
    3. 重启 Trae IDE

注意:
  - 脚本方式不含桥接扩展与 Hooks，完整六态请用 EXE 完整安装
  - 不要删除解压后的目录（皮肤部署与备份都在其中）
  - Trae 升级会还原原生皮肤与 main.js 补丁：重跑安装器（或 deploy-native-pet.ps1）即可
  - 如果更换了宠物主题，重新运行 release.ps1 打包即可
"@ -ForegroundColor Cyan
