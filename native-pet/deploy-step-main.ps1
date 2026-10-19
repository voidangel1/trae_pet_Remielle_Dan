# 部署/回滚的提权子脚本（由 deploy-native-pet.ps1 以管理员身份调用）
# 职责：复制 webp 皮肤、给 main.js 与 floatingButton.html 打补丁（或全部反向回滚）
#
# 补丁清单（v2.0）：
#   main.js
#     P1  qF 图标开关：Trae CN 3.4.2 硬编码 return "ball"（traebao 形象灰度关闭，
#         且会把 settings.json 的 buttonIcon 改回 ball）→ 改为尊重 ball、默认 traebao
#     P2  XF 尺寸表：traebao 100 → 140（窗口初始尺寸）
#   floatingButton.html
#     P3  .icon-traebao .btn 100px → 140px（按钮窗口内容尺寸）
#     P4  .btn img 80px → 140px（立绘显示尺寸，实际窗口由 html 内 ResizeObserver
#         → button-resize IPC 自动同步）
#     P5  注入 Hooks 轮询脚本 v2（</body> 前，CLAWD-PET-POLL 标记）：
#         每 500ms 轮询 127.0.0.1:27416/state，mode=hooks 时驱动六态
#         （notification=模型提问，常驻等待回答；其余 25s 过期），
#         mode=official 或桥不可用时回落官方四态。模式由状态服务器持久化，
#         可点击 Trae 状态栏 Pet 图标切换。
#   素材
#     P6  8 张 webp：官方四态位 + 六态扩展位（thinking/editing/notification/error）
#
# 幂等性：部署前先把新旧两代补丁（含旧 180px 尺寸版）归一化回官方锚点，
# 再统一打当前版本补丁——支持任意版本状态升级部署与回滚。
param(
    [string]$MainJs,     # <Trae>\resources\app\out\main.js
    [string]$PetDir,     # ...\iCubeFloatingChat\electron-main\media\icons\traebao
    [string]$AssetDir,   # <项目>\native-pet\assets
    [string]$BackupDir,  # <项目>\native-pet\assets\official-backup
    [string]$ButtonHtml, # ...\iCubeFloatingChat\electron-main\media\floatingButton.html
    [switch]$Restore     # 回滚模式
)
$ErrorActionPreference = 'Stop'

# ---------- 补丁锚点 ----------
# v2.0.3：Trae 每个版本都会重命名混淆变量（qF/XF/an → tB/oB → 3.4.4 的 TB/PB/sn），
# 硬编码锚点每次升级必失效，现改为按代码结构正则匹配，不依赖具体变量名：
#   gate  形象开关：  function X(t){return"ball"} → function X(t){return t==="ball"?"ball":"traebao"}
#   size  尺寸表：    X={ball:60,traebao:100} → X={ball:60,traebao:140}
#   drag 拖拽修复：   X.on("icube-floating-chat:drag-move",(e,i,r)=>{ → 注入主进程权威光标坐标
$reGate     = 'function (\w+)\((\w)\)\{return"ball"\}'
$reGatePatch = 'function $1($2){return $2==="ball"?"ball":"traebao"}'
$reGateNorm = 'function (\w+)\(\w\)\{return \w+==="ball"\?"ball":"traebao"\}'
$reGateOrig = 'function $1(t){return"ball"}'

$reSize      = '(\w+)=\{ball:60,traebao:100\}'
$reSizePatch = '$1={ball:60,traebao:140}'
$reSizeNorm  = '(\w+)=\{ball:60,traebao:(?:140|180)\}'   # 含 v2.0 早期 180px 版
$reSizeOrig  = '$1={ball:60,traebao:100}'

$reDrag     = '(\w+)\.on\("icube-floating-chat:drag-move",\((\w+),(\w+),(\w+)\)=>\{'
$reDragNorm = '\.on\("icube-floating-chat:drag-move",\((\w+),(\w+),(\w+)\)=>\{try\{const __cp=\w+\.getCursorScreenPoint\(\);\2=__cp\.x;\3=__cp\.y\}catch\(__e\)\{\}'
$reDragOrig = '.on("icube-floating-chat:drag-move",($1,$2,$3)=>{'

$cssBtnGate      = ".icon-traebao .btn {`n`twidth: 100px;`n`theight: 100px;`n}"
$cssBtnPatch     = ".icon-traebao .btn {`n`twidth: 140px;`n`theight: 140px;`n}"
$cssBtnLegacy180 = ".icon-traebao .btn {`n`twidth: 180px;`n`theight: 180px;`n}"

$cssImgGate      = '.btn img { object-fit: cover; pointer-events: none; width: 80px; height: 80px; }'
$cssImgPatch     = '.btn img { object-fit: cover; pointer-events: none; width: 140px; height: 140px; }'
$cssImgLegacy180 = '.btn img { object-fit: cover; pointer-events: none; width: 180px; height: 180px; }'

$pollMarkStart = '<!--CLAWD-PET-POLL-START-->'
$pollMarkEnd   = '<!--CLAWD-PET-POLL-END-->'
$pollScript = @'
<!--CLAWD-PET-POLL-START-->
<script>
/* ClawdPet v2.0: Hooks 六态轮询 + 模式开关（deploy-step-main.ps1 注入，勿手改） */
(function () {
  var STATE_URL = 'http://127.0.0.1:27416/state';
  var STALE_MS = 25000;
  var EXTRA = { attention: 'hover-drag.webp', thinking: 'thinking.webp', editing: 'editing.webp', notification: 'notification.webp', error: 'error.webp' };
  var origGetTraebaoSrc = getTraebaoSrc;
  var hookState = null;

  function stateDir() {
    var p = currentStaticPath || '';
    var m = p.match(/^(.*[\\/])/);
    return m ? m[1] : '';
  }

  getTraebaoSrc = function () {
    if ((isHovering || isDragging) && runningTasks.length === 0 && currentHoverPath) return currentHoverPath;
    if (hookState && currentIconName === 'traebao') {
      var extra = EXTRA[hookState.state];
      var dir = stateDir();
      if (extra && dir) return dir + extra;
      return currentStaticPath; // idle / 未知状态
    }
    return origGetTraebaoSrc();
  };

  var lastState = null;
  setInterval(function () {
    fetch(STATE_URL, { cache: 'no-store' })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (j) {
        var next = null;
        if (j && j.mode !== 'official' && typeof j.state === 'string' && j.ts) {
          var age = Date.now() - j.ts;
          // notification（模型提问、等待用户回答）常驻；其余状态 25s 过期回落官方逻辑
          if (j.state === 'notification' || age < STALE_MS) next = j;
        }
        hookState = next;
        var s = next ? next.state : null;
        if (s !== lastState) {
          lastState = s;
          if (currentIconName === 'traebao') updateTraebaoSrc();
        }
      })
      .catch(function () {
        if (hookState) { hookState = null; lastState = null; if (currentIconName === 'traebao') updateTraebaoSrc(); }
      });
  }, 500);
})();
</script>
<!--CLAWD-PET-POLL-END-->
'@

$skinFiles  = @('default.webp', 'busy.webp', 'super-busy.webp', 'hover-drag.webp')
$extraFiles = @('thinking.webp', 'editing.webp', 'notification.webp', 'error.webp')

function Read-Text([string]$p) { [System.IO.File]::ReadAllText($p) }
function Write-Text([string]$p, [string]$t) { [System.IO.File]::WriteAllText($p, $t) } # UTF-8 无 BOM

# 移除注入的轮询脚本块（连同前后换行）
function Remove-PollBlock([string]$t) {
    $s = $t.IndexOf($pollMarkStart)
    if ($s -lt 0) { return $t }
    $e = $t.IndexOf($pollMarkEnd, $s)
    if ($e -lt 0) { return $t }
    $end = $e + $pollMarkEnd.Length
    while ($end -lt $t.Length -and ($t[$end] -eq "`n" -or $t[$end] -eq "`r")) { $end++ }
    while ($s -gt 0 -and ($t[$s - 1] -eq "`n" -or $t[$s - 1] -eq "`r")) { $s-- }
    return $t.Substring(0, $s) + $t.Substring($end)
}

# 把所有已知补丁（任意版本的 gate/尺寸/拖拽补丁）归一化回官方结构
function Normalize-MainJs([string]$t) {
    $t = [regex]::Replace($t, $reGateNorm, $reGateOrig)
    $t = [regex]::Replace($t, $reSizeNorm, $reSizeOrig)
    $t = [regex]::Replace($t, $reDragNorm, $reDragOrig)
    return $t
}
# 给 main.js 打补丁：gate 解锁 + 140px + 拖拽高 DPI 修复（screen 别名动态探测，
# 取离 drag-move handler 最近的 getCursorScreenPoint 引用，随版本混淆名自适应）
function Patch-MainJs([string]$t) {
    $t = [regex]::Replace($t, $reGate, $reGatePatch)
    $t = [regex]::Replace($t, $reSize, $reSizePatch)
    $h = [regex]::Match($t, $reDrag)
    if ($h.Success) {
        $alias = $null; $best = [int]::MaxValue
        foreach ($m in [regex]::Matches($t, '(\w+)\.getCursorScreenPoint\(')) {
            $d = [math]::Abs($m.Index - $h.Index)
            if ($d -lt $best) { $best = $d; $alias = $m.Groups[1].Value }
        }
        if ($alias) {
            $g = $h.Groups
            $new = "$($g[1].Value).on(`"icube-floating-chat:drag-move`",($($g[2].Value),$($g[3].Value),$($g[4].Value))=>{try{const __cp=$alias.getCursorScreenPoint();$($g[3].Value)=__cp.x;$($g[4].Value)=__cp.y}catch(__e){}"
            $t = $t.Replace($h.Value, $new)
        }
    }
    return $t
}

function Normalize-Html([string]$t) {
    $t = $t.Replace($cssBtnPatch, $cssBtnGate).Replace($cssBtnLegacy180, $cssBtnGate)
    $t = $t.Replace($cssImgPatch, $cssImgGate).Replace($cssImgLegacy180, $cssImgGate)
    return Remove-PollBlock $t
}

if ($Restore) {
    # ---------- 回滚：素材 + 全部补丁归一化 ----------
    Copy-Item "$BackupDir\*.webp" $PetDir -Force
    foreach ($f in $extraFiles) {
        Remove-Item (Join-Path $PetDir $f) -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path $MainJs) {
        Write-Text $MainJs (Normalize-MainJs (Read-Text $MainJs))
    }
    if (Test-Path $ButtonHtml) {
        Write-Text $ButtonHtml (Normalize-Html (Read-Text $ButtonHtml))
    }
} else {
    # ---------- 部署：素材 + 补丁（先归一化再打，幂等且支持版本升级） ----------
    foreach ($f in ($skinFiles + $extraFiles)) {
        Copy-Item "$AssetDir\$f" $PetDir -Force
    }
    if (Test-Path $MainJs) {
        $t = Patch-MainJs (Normalize-MainJs (Read-Text $MainJs))
        Write-Text $MainJs $t
    }
    if (Test-Path $ButtonHtml) {
        $t = Normalize-Html (Read-Text $ButtonHtml)
        $t = $t.Replace($cssBtnGate, $cssBtnPatch).Replace($cssImgGate, $cssImgPatch)
        if ($t.Contains('</body>')) {
            $t = $t.Replace('</body>', $pollScript + "`n</body>")
        }
        Write-Text $ButtonHtml $t
    }
}
