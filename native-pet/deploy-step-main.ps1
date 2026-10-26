# 部署/回滚的提权子脚本（由 deploy-native-pet.ps1 以管理员身份调用）
# 职责：复制 webp/png 皮肤、给 main.js 与 floatingButton.html 打补丁（或全部反向回滚）
#
# 补丁清单（v2.1，适配 Trae 3.4.7 官方 TRAE Bao 正式上线）：
#   main.js
#     P1  gate 形象开关：仅 Trae ≤3.4.4 存在（function X(t){return"ball"}），
#         3.4.7 官方已删除（右键"切换外观"原生支持 traebao），正则保留兼容旧版，
#         新版无此锚点属正常。
#     P2  尺寸表：X={ball:60,traebao:105} → 140（窗口初始尺寸；旧版 100 同样兼容）
#     P3  drag-move 高 DPI 修复：注入主进程权威光标坐标（screen 别名动态探测）
#   floatingButton.html
#     P4  禁用官方 3D 渲染：script.src './traebao/renderer.js' 改名 → 加载失败
#         自动回落 <img> 素材模式（3.4.7 空闲态被 three.js 3D 接管，换素材不可见，
#         禁 3D 后全部状态走官方 img 管线，与旧版素材方案一致）
#     P5  .icon-traebao .btn 105px → 140px；.btnContent/.btn img 85px → 113px
#         （保持官方 105:85 ≈ 140:113 比例，窗口由 ResizeObserver 自动同步）
#     P6  注入 Hooks 轮询脚本 v2.1（</body> 前，CLAWD-PET-POLL 标记）：
#         每 500ms 轮询 127.0.0.1:27416/state，mode=hooks 时驱动六态，
#         空闲态用 default.webp 动画（官方 default.png 为静态 fallback）；
#         mode=official 或桥不可用时回落官方态（含 3.4.7 新增 voice 语音态）。
#   素材
#     P7  官方位：default.png/busy/super-busy/hover-drag/voice.webp（3.4.7 文件名）
#         + default.webp（hooks 模式空闲动画）+ 六态扩展 thinking/editing/notification/error
#
# 幂等性：部署前把任意历史版本补丁归一化回官方结构，再统一打当前补丁。
param(
    [string]$MainJs,     # <Trae>\resources\app\out\main.js
    [string]$PetDir,     # ...\iCubeFloatingChat\electron-main\media\icons\traebao
    [string]$AssetDir,   # <项目>\native-pet\assets
    [string]$BackupDir,  # <项目>\native-pet\assets\official-backup
    [string]$ButtonHtml, # ...\iCubeFloatingChat\electron-main\media\floatingButton.html
    [switch]$Restore     # 回滚模式
)
$ErrorActionPreference = 'Stop'

# ---------- 补丁锚点（结构正则，不依赖混淆变量名） ----------
# gate 形象开关（≤3.4.4；3.4.7 官方已删，无匹配即跳过）
$reGate     = 'function (\w+)\((\w)\)\{return"ball"\}'
$reGatePatch = 'function $1($2){return $2==="ball"?"ball":"traebao"}'
$reGateNorm = 'function (\w+)\(\w\)\{return \w+==="ball"\?"ball":"traebao"\}'
$reGateOrig = 'function $1(t){return"ball"}'

# 尺寸表（3.4.2=100 / 3.4.7=105 两代官方值都接受；含旧 180px 补丁归一化）
$reSize      = '(\w+)=\{ball:60,traebao:(?:100|105)\}'
$reSizePatch = '$1={ball:60,traebao:140}'
$reSizeNorm  = '(\w+)=\{ball:60,traebao:(?:140|180)\}'
$reSizeOrig  = '$1={ball:60,traebao:105}'

# drag-move 拖拽修复
$reDrag     = '(\w+)\.on\("icube-floating-chat:drag-move",\((\w+),(\w+),(\w+)\)=>\{'
$reDragNorm = '\.on\("icube-floating-chat:drag-move",\((\w+),(\w+),(\w+)\)=>\{try\{const __cp=\w+\.getCursorScreenPoint\(\);\2=__cp\.x;\3=__cp\.y\}catch\(__e\)\{\}'
$reDragOrig = '.on("icube-floating-chat:drag-move",($1,$2,$3)=>{'

# HTML：禁用官方 3D 渲染（3.4.7 空闲态 three.js 接管，禁用后回落 img 素材模式）
$rendererGate  = "script.src = './traebao/renderer.js';"
$rendererPatch = "script.src = './traebao/renderer.clawdpet-disabled.js';"

# HTML：尺寸 CSS（保持官方 105:85 比例 → 140:113）
$reCssBtn     = '(\.icon-traebao \.btn \{\s*width: )105px(;\s*height: )105px(;)'
$reCssBtnP    = '${1}140px${2}140px${3}'
$reCssBtnN    = '(\.icon-traebao \.btn \{\s*width: )(?:140|113|100|180)px(;\s*height: )(?:140|113|100|180)px(;)'
$reCssBtnO    = '${1}105px${2}105px${3}'

$reCssImg     = '(\.btn img \{[^}]*?width: )85px(;\s*height: )85px(;)'
$reCssImgP    = '${1}113px${2}113px${3}'
$reCssImgN    = '(\.btn img \{[^}]*?width: )(?:113|140|80|180)px(;\s*height: )(?:113|140|80|180)px(;)'
$reCssImgO    = '${1}85px${2}85px${3}'

$reCssContent = '(\.icon-traebao \.btnContent \{[^}]*?width: )85px(;\s*height: )85px(;)'
$reCssContentP = '${1}113px${2}113px${3}'
$reCssContentN = '(\.icon-traebao \.btnContent \{[^}]*?width: )(?:113|140|80|180)px(;\s*height: )(?:113|140|80|180)px(;)'
$reCssContentO = '${1}85px${2}85px${3}'

$pollMarkStart = '<!--CLAWD-PET-POLL-START-->'
$pollMarkEnd   = '<!--CLAWD-PET-POLL-END-->'
$pollScript = @'
<!--CLAWD-PET-POLL-START-->
<script>
/* ClawdPet v2.1: Hooks 六态轮询 + 模式开关（deploy-step-main.ps1 注入，勿手改） */
(function () {
  var STATE_URL = 'http://127.0.0.1:27416/state';
  var STALE_MS = 25000;
  var EXTRA = { idle: 'default.webp', attention: 'hover-drag.webp', thinking: 'thinking.webp', editing: 'editing.webp', notification: 'notification.webp', error: 'error.webp' };
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
      return currentStaticPath; // 未知状态
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

# 官方位素材（3.4.7 文件名）+ hooks 空闲动画 + 六态扩展
$skinFiles  = @('default.png', 'busy.webp', 'super-busy.webp', 'hover-drag.webp', 'voice.webp', 'default.webp')
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
# 给 main.js 打补丁：gate 解锁（旧版）+ 140px + 拖拽高 DPI 修复
# （screen 别名动态探测：取离 drag-move handler 最近的 getCursorScreenPoint 引用）
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
    $t = $t.Replace($rendererPatch, $rendererGate)
    $t = [regex]::Replace($t, $reCssBtnN, $reCssBtnO)
    $t = [regex]::Replace($t, $reCssImgN, $reCssImgO)
    $t = [regex]::Replace($t, $reCssContentN, $reCssContentO)
    return Remove-PollBlock $t
}

if ($Restore) {
    # ---------- 回滚：素材 + 全部补丁归一化 ----------
    Copy-Item "$BackupDir\*.webp" $PetDir -Force
    Copy-Item "$BackupDir\*.png" $PetDir -Force
    foreach ($f in $extraFiles) {
        Remove-Item (Join-Path $PetDir $f) -Force -ErrorAction SilentlyContinue
    }
    # default.webp 是我们加的 hooks 空闲动画（3.4.7 官方无此文件）；旧版官方备份里有则上面已恢复
    if (-not (Test-Path "$BackupDir\default.webp")) {
        Remove-Item (Join-Path $PetDir "default.webp") -Force -ErrorAction SilentlyContinue
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
        # 禁用官方 3D 渲染（3.4.7+；旧版无此行则跳过）
        $t = $t.Replace($rendererGate, $rendererPatch)
        # 尺寸 CSS：105/85 → 140/113
        $t = [regex]::Replace($t, $reCssBtn, $reCssBtnP)
        $t = [regex]::Replace($t, $reCssImg, $reCssImgP)
        $t = [regex]::Replace($t, $reCssContent, $reCssContentP)
        if ($t.Contains('</body>')) {
            $t = $t.Replace('</body>', $pollScript + "`n</body>")
        }
        Write-Text $ButtonHtml $t
    }
}
