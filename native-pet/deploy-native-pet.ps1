# ClawdPet → Trae 原生桌宠（TRAE Bao / 悬浮窗）皮肤部署脚本
#
# 原理: Trae v3.4+ 的原生悬浮窗宠物（TRAE Bao）素材位于安装目录
#   <Trae>\resources\app\out\vs\platform\iCubeFloatingChat\electron-main\media\icons\traebao\
# 共 4 个状态位: default(静立) / busy(会话运行中) / super-busy(忙满3分钟每180s播10s) / hover-drag(悬停拖拽)
# 本脚本把 Remielle 的 4 张动画 webp（node convert-native-pet.js 生成）替换进去，
# 把设置 trae.floatingChat.buttonIcon 切回 "traebao"，并给 main.js 打 qF 补丁——
# Trae CN 3.4.2 把图标归一化函数硬编码为 return "ball"（traebao 形象被灰度关闭，
# 且会把设置改回 ball），必须打补丁解锁，见 deploy-step-main.ps1。
#
# 用法:
#   右键本文件 → 「使用 PowerShell 运行」        首次部署 / Trae 升级覆盖后重新部署
#   powershell -File deploy-native-pet.ps1 -Restore    恢复官方 TRAE Bao 素材与部署前设置
#   powershell -File deploy-native-pet.ps1 -KeepClawd  部署皮肤但保留 ClawdPet 独立版自动启动（双宠并存）
#
# 注意:
# - 素材复制与 main.js 补丁需要管理员权限（写入 Program Files），脚本会弹 UAC，仅此一步提权。
# - 部署后需重启 Trae 生效。
# - Trae 每次升级会还原官方素材与 main.js，重跑本脚本即可。
param(
    [switch]$Restore,   # 恢复官方素材与部署前设置
    [switch]$KeepClawd, # 部署后不禁用 ClawdPet 独立版
    [switch]$NoPause    # 自动化调用时不暂停等待回车
)

$ErrorActionPreference = "Stop"
$assetDir  = Join-Path $PSScriptRoot "assets"
$backupDir = Join-Path $assetDir "official-backup"
$infoFile  = Join-Path $backupDir "backup-info.json"
$skinFiles = @("default.webp", "busy.webp", "super-busy.webp", "hover-drag.webp",
    "thinking.webp", "editing.webp", "notification.webp", "error.webp")

function Wait-Exit([int]$code) {
    if (-not $NoPause) { Read-Host "按回车退出" | Out-Null }
    exit $code
}

Write-Host "=== ClawdPet 原生桌宠皮肤部署 ===" -ForegroundColor Cyan

# ---------- 1. 定位 Trae（v3.4+ 含原生悬浮窗） ----------
$trae = $null
foreach ($candidate in @(
    @{ Icons = "C:\Program Files\Trae CN\resources\app\out\vs\platform\iCubeFloatingChat\electron-main\media\icons"; MainJs = "C:\Program Files\Trae CN\resources\app\out\main.js"; Settings = Join-Path $env:APPDATA "Trae CN\User\settings.json"; Name = "Trae CN" },
    @{ Icons = "C:\Program Files\Trae\resources\app\out\vs\platform\iCubeFloatingChat\electron-main\media\icons"; MainJs = "C:\Program Files\Trae\resources\app\out\main.js"; Settings = Join-Path $env:APPDATA "Trae\User\settings.json"; Name = "Trae" }
)) {
    if (Test-Path (Join-Path $candidate.Icons "traebao")) { $trae = $candidate; break }
}
if (-not $trae) {
    Write-Host "[错误] 未找到 Trae 原生悬浮窗（需 Trae v3.4+）" -ForegroundColor Red
    Wait-Exit 1
}
$petDir = Join-Path $trae.Icons "traebao"
$mainJs = $trae.MainJs
$buttonHtml = Join-Path $trae.Icons "..\floatingButton.html"
Write-Host "目标: $($trae.Name)"
Write-Host "目录: $petDir`n"

# ---------- 2. 素材检查 ----------
if (-not $Restore) {
    foreach ($f in $skinFiles) {
        if (-not (Test-Path (Join-Path $assetDir $f))) {
            Write-Host "[错误] 缺少 assets\$f" -ForegroundColor Red
            Write-Host "  请先运行: node convert-native-pet.js （需 npm install sharp）" -ForegroundColor Yellow
            Wait-Exit 1
        }
    }
}

# ---------- 3. 备份管理 ----------
if ($Restore) {
    if (-not (Test-Path (Join-Path $backupDir "default.webp"))) {
        Write-Host "[错误] 没有可恢复的官方备份（native-pet-assets\official-backup\）" -ForegroundColor Red
        Wait-Exit 1
    }
} else {
    $backupInfo = $null
    if (Test-Path $infoFile) {
        try { $backupInfo = Get-Content $infoFile -Raw -Encoding UTF8 | ConvertFrom-Json } catch {}
    }
    if (-not $backupInfo) {
        # 首次部署: 备份官方素材 + 记录部署前的 buttonIcon
        New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
        Copy-Item (Join-Path $petDir "*.webp") $backupDir -Force
        $prevIcon = "traebao"
        if (Test-Path $trae.Settings) {
            try {
                $s = Get-Content $trae.Settings -Raw -Encoding UTF8 | ConvertFrom-Json
                $v = $s.PSObject.Properties['trae.floatingChat.buttonIcon'].Value
                if ($v) { $prevIcon = $v }
            } catch {}
        }
        $info = [ordered]@{ previousButtonIcon = $prevIcon; backedUpAt = (Get-Date -Format "yyyy-MM-dd HH:mm:ss") }
        [System.IO.File]::WriteAllText($infoFile, ($info | ConvertTo-Json))
        Write-Host "[OK] 已备份官方 TRAE Bao 素材 → native-pet-assets\official-backup\（此前 buttonIcon: $prevIcon）" -ForegroundColor Green
    } else {
        # 已有备份: 检测 Trae 升级是否换了官方素材（既不像备份也不像我们的皮肤 → 刷新备份）
        $curHash  = (Get-FileHash (Join-Path $petDir "default.webp") -ErrorAction SilentlyContinue).Hash
        $bakHash  = (Get-FileHash (Join-Path $backupDir "default.webp") -ErrorAction SilentlyContinue).Hash
        $skinHash = (Get-FileHash (Join-Path $assetDir "default.webp") -ErrorAction SilentlyContinue).Hash
        if ($curHash -and $bakHash -and $skinHash -and $curHash -ne $bakHash -and $curHash -ne $skinHash) {
            Copy-Item (Join-Path $petDir "*.webp") $backupDir -Force
            Write-Host "[OK] 检测到 Trae 升级更换了官方素材，备份已刷新" -ForegroundColor Green
        }
    }
}

# ---------- 4. 更新 settings.json（无需管理员，UTF-8 无 BOM） ----------
$settingsPath = $trae.Settings
$settings = New-Object PSObject
if (Test-Path $settingsPath) {
    $settings = Get-Content $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
}
function Set-Setting([PSObject]$obj, [string]$key, $value) {
    if ($obj.PSObject.Properties[$key]) { $obj.$key = $value }
    else { $obj | Add-Member -NotePropertyName $key -NotePropertyValue $value }
}
if ($Restore) {
    $prevIcon = "traebao"
    try { $prevIcon = (Get-Content $infoFile -Raw -Encoding UTF8 | ConvertFrom-Json).previousButtonIcon } catch {}
    Set-Setting $settings 'trae.floatingChat.buttonIcon' $prevIcon
    # 恢复独立版 ClawdPet 的自动启动
    Set-Setting $settings 'traePetBridge.autoLaunch' $true
    Write-Host "[OK] buttonIcon → $prevIcon，ClawdPet 独立版自动启动已恢复" -ForegroundColor Green
} else {
    Set-Setting $settings 'trae.floatingChat.buttonIcon' 'traebao'
    if (-not $KeepClawd) {
        Set-Setting $settings 'traePetBridge.autoLaunch' $false
        Write-Host "[OK] buttonIcon → traebao，ClawdPet 独立版自动启动已关闭（避免双宠；-KeepClawd 可保留）" -ForegroundColor Green
    } else {
        Write-Host "[OK] buttonIcon → traebao（保留 ClawdPet 独立版，双宠并存）" -ForegroundColor Green
    }
}
$json = $settings | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText($settingsPath, $json)

# ---------- 5. 提权执行：复制素材 + main.js / floatingButton.html 补丁（仅此一步需要管理员，弹 UAC） ----------
$stepScript = Join-Path $PSScriptRoot "deploy-step-main.ps1"
if (-not (Test-Path $stepScript)) {
    Write-Host "[错误] 缺少 deploy-step-main.ps1" -ForegroundColor Red
    Wait-Exit 1
}
$elevArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$stepScript`"",
    '-MainJs', "`"$mainJs`"", '-PetDir', "`"$petDir`"",
    '-AssetDir', "`"$assetDir`"", '-BackupDir', "`"$backupDir`"",
    '-ButtonHtml', "`"$buttonHtml`"")
if ($Restore) { $elevArgs += '-Restore' }
Start-Process powershell -Verb RunAs -Wait -ArgumentList $elevArgs

# ---------- 6. 校验 ----------
$srcDir = if ($Restore) { $backupDir } else { $assetDir }
$ok = $true
foreach ($f in $skinFiles) {
    $a = (Get-FileHash (Join-Path $srcDir $f) -ErrorAction SilentlyContinue).Hash
    $b = (Get-FileHash (Join-Path $petDir $f) -ErrorAction SilentlyContinue).Hash
    if ($a -ne $b) { $ok = $false; Write-Host "[校验失败] $f" -ForegroundColor Red }
}
# v2.0.3：锚点改结构正则（Trae 各版本混淆变量名不同：qF/XF → tB/oB → 3.4.4 TB/PB/sn）
$patchRe    = 'function \w+\(\w\)\{return \w+==="ball"\?"ball":"traebao"\}'
$gateRe     = 'function \w+\(\w\)\{return"ball"\}'
$xfPatchMark = '{ball:60,traebao:140}'
$pollMark = '<!--CLAWD-PET-POLL-START-->'
$mainText = ""
if (Test-Path $mainJs) { $mainText = [IO.File]::ReadAllText($mainJs) }
$htmlText = ""
if (Test-Path $buttonHtml) { $htmlText = [IO.File]::ReadAllText($buttonHtml) }
if ($Restore) {
    if ([regex]::IsMatch($mainText, $patchRe) -or $mainText.Contains($xfPatchMark)) {
        $ok = $false
        Write-Host "[校验失败] main.js 补丁未回滚" -ForegroundColor Red
    }
    if ($htmlText.Contains($pollMark)) {
        $ok = $false
        Write-Host "[校验失败] floatingButton.html 轮询脚本未移除" -ForegroundColor Red
    }
} else {
    if ([regex]::IsMatch($mainText, $patchRe)) {
        Write-Host "[OK] main.js 形象补丁已生效（traebao 形象已解锁）" -ForegroundColor Green
    } elseif ([regex]::IsMatch($mainText, $gateRe)) {
        $ok = $false
        Write-Host "[校验失败] main.js 锁定点仍在（补丁未写入），请以管理员身份重试" -ForegroundColor Red
    } else {
        Write-Host "[警告] 未找到形象锁定点（Trae 版本结构已变化），皮肤可能不生效，仅完成素材替换" -ForegroundColor Yellow
    }
    if ($mainText.Contains($xfPatchMark)) {
        Write-Host "[OK] main.js 尺寸补丁已生效（traebao 窗口 140px）" -ForegroundColor Green
    }
    if ($mainText.Contains('__cp=')) {
        Write-Host "[OK] main.js 拖拽高 DPI 修复已生效" -ForegroundColor Green
    }
    if ($htmlText.Contains($pollMark) -and $htmlText.Contains('width: 140px')) {
        Write-Host "[OK] floatingButton.html 已注入 Hooks 六态轮询 v2 + 140px 尺寸" -ForegroundColor Green
    } elseif (-not $htmlText.Contains('</body>')) {
        Write-Host "[警告] floatingButton.html 结构已变化，六态轮询与尺寸补丁未注入" -ForegroundColor Yellow
    } else {
        $ok = $false
        Write-Host "[校验失败] floatingButton.html 补丁未写入，请以管理员身份重试" -ForegroundColor Red
    }
}
if ($ok) {
    Write-Host "`n[完成] $($(if ($Restore) { '官方素材与补丁已全部回滚' } else { 'Remielle 皮肤 + 全部补丁已部署（六态 + 140px）' }))，重启 Trae 后生效。" -ForegroundColor Green
    if (-not $Restore) {
        Write-Host "  Hooks 六态: 提问→thinking  编辑/查看/调用工具→editing  模型提问→notification  异常→error  完成→idle"
        Write-Host "  状态由 Trae Hooks 驱动（首个事件自动拉起 127.0.0.1:27416 状态服务器）"
        Write-Host "  点击 Trae 状态栏 Pet 图标可在 Hooks 六态 / 官方四态之间切换（持久化）"
        Write-Host "  桥不可用时回落官方四态: 静立 / 会话忙碌 / 卖力 / 悬停拖拽"
    }
    Wait-Exit 0
} else {
    Write-Host "`n[错误] 校验失败，请以管理员身份重试" -ForegroundColor Red
    Wait-Exit 1
}
