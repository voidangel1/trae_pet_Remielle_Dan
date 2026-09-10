// Clawd 主题运行时：解析 theme.json，驱动状态机与交互反应
const theme = window.petAPI.getTheme();
const ASSETS = `${window.petAPI.themeDirName}/assets/`;
const T = theme.timings || {};

const MIN_DISPLAY = T.minDisplay || {};
const AUTO_RETURN = T.autoReturn || {};

const img = document.getElementById('pet');

let current = 'idle';
let stateToken = 0; // 递增使过期的定时回调失效
let notificationTimer = null;
// Agent 是否活跃（thinking/editing 期间为 true，优先级高于编辑器 attention）
let agentActive = false;
const PERSISTENT_AGENT_STATES = ['thinking', 'editing'];
// error 展示截止时间：此期间到达的 notification 视为 Trae 异常打断后的系统通知，忽略
let errorShownUntil = 0;
// 最近一次状态事件时间，用于检测「工具已提出但长时间无后续事件」的等待场景
let lastEventAt = Date.now();

function play(file) {
  // 追加时间戳强制重新加载，让 GIF 从头播放；加载失败时退回无参数路径兜底
  img.onerror = () => {
    img.onerror = null;
    img.src = `${ASSETS}${file}`;
  };
  img.src = `${ASSETS}${file}?t=${Date.now()}`;
}

function setState(name) {
  const files = theme.states[name];
  if (!files || files.length === 0) {
    console.warn(`[pet] 状态 "${name}" 无对应动画`);
    return;
  }
  current = name;
  console.log(`[pet] → ${name}: ${files[0]}`);
  play(files[Math.floor(Math.random() * files.length)]);
}

function toIdle() {
  setState('idle');
}

// 临时状态（通知/错误等）：按 minDisplay/autoReturn 展示后回到待机
function triggerTemporary(name, fallbackHold = 2000) {
  const hold = MIN_DISPLAY[name] || AUTO_RETURN[name] || fallbackHold;
  const token = ++stateToken;
  setState(name);
  setTimeout(() => {
    if (token === stateToken && !agentActive) toIdle();
  }, hold);
}

// ---------- 随机待机动画 ----------
function scheduleRandomIdle() {
  const delay = 15000 + Math.random() * 20000;
  setTimeout(() => {
    const list = theme.idleAnimations || [];
    if (!agentActive && current === 'idle' && list.length > 0) {
      const pick = list[Math.floor(Math.random() * list.length)];
      const token = ++stateToken;
      play(pick.file);
      setTimeout(() => {
        if (token === stateToken && !agentActive) toIdle();
      }, pick.duration || 5000);
    }
    scheduleRandomIdle();
  }, delay);
}

// 主进程检测到全局光标移动（保留接口）
window.petAPI.onUserActivity(() => {});

// ---------- 审批 / 提问等待检测 ----------
// 实测（hook-debug.log）：Trae 在「命令运行审批」「Agent 向用户提问」时不会触发任何
// Hook 事件（Notification 仅在任务完成/异常打断时触发）。表现为 PreToolUse(editing)
// 后长时间无任何后续事件。超过 45 秒视为在等待用户操作，切换到 notification 提醒；
// 后续事件（审批通过 / 用户回答 / 任务结束）到达时会自然接管状态。
const EDITING_STALL_MS = 45000;
setInterval(() => {
  if (agentActive && current === 'editing' && Date.now() - lastEventAt > EDITING_STALL_MS) {
    console.log('[pet] editing 停滞超时，判定为等待用户审批/回答 → notification');
    setState('notification'); // 持续显示直到下一个事件，不自动回 idle
  }
}, 5000);

// 右键菜单 / Hook / 扩展触发的状态
window.petAPI.onTriggerState((name) => {
  // error 展示期间到达的 notification 是 Trae 在异常打断后紧跟的系统通知，
  // 忽略以免覆盖 error（hook-handler 层已抑制，此处为双保险，防 Hook 状态文件失效）
  if (name === 'notification' && Date.now() < errorShownUntil) {
    console.log('[pet] error 展示中，忽略随后的 notification');
    return;
  }
  ++stateToken; // 使所有之前的定时器失效，防止旧定时器覆盖新状态
  lastEventAt = Date.now();
  if (name === 'lock-toggle') {
    dragEnabled = !dragEnabled;
    return;
  }
  if (PERSISTENT_AGENT_STATES.includes(name)) {
    // thinking / editing：持续显示，直到收到 idle
    agentActive = true;
    setState(name);
  } else if (name === 'error') {
    // Agent 异常：显示 3 秒后回到 idle
    agentActive = true;
    setState('error');
    errorShownUntil = Date.now() + 3000;
    const token = stateToken;
    setTimeout(() => {
      if (token === stateToken) { agentActive = false; toIdle(); }
    }, 3000);
  } else if (name === 'notification') {
    // 完成通知：5 秒后强制回到 idle，即使期间又被其他事件打断
    clearTimeout(notificationTimer);
    agentActive = false;
    setState('notification');
    notificationTimer = setTimeout(() => {
      notificationTimer = null;
      toIdle();
    }, 5000);
  } else if (name === 'attention') {
    // 对话输入事件：短暂显示注意状态
    agentActive = false;
    triggerTemporary('attention', 2000);
  } else if (name === 'idle') {
    // Stop 事件：Agent 完成，回到 idle
    agentActive = false;
    toIdle();
  } else if (!agentActive) {
    // attention 等编辑器状态：仅在 Agent 不活跃时响应
    triggerTemporary(name);
  }
});

// ---------- 拖拽 / 点击 / 双击 ----------
let dragging = false;
let moved = false;
let dragEnabled = false; // 锁定位置开关，默认锁定

img.addEventListener('mousedown', (e) => {
  if (e.button !== 0 || !dragEnabled) return;
  dragging = true;
  moved = false;
  img.classList.add('dragging');
  e.preventDefault();
  window.petAPI.dragStart({ x: e.screenX, y: e.screenY });
});

img.addEventListener('dragstart', (e) => e.preventDefault());

window.addEventListener('mousemove', (e) => {
  if (!dragging || !dragEnabled) return;
  if (Math.abs(e.movementX) > 1 || Math.abs(e.movementY) > 1) moved = true;
  window.petAPI.dragMove({ x: e.screenX, y: e.screenY });
});

window.addEventListener('mouseup', () => {
  if (!dragging) return;
  dragging = false;
  img.classList.remove('dragging');
  window.petAPI.dragEnd();
  if (moved) toIdle();
});

// 单击（与双击去抖）：单击 → 注意；双击 → annoyed（摇晃 + error 表情）
let clickTimer = null;
img.addEventListener('click', () => {
  if (moved || agentActive) return;
  clearTimeout(clickTimer);
  clickTimer = setTimeout(() => triggerTemporary('attention', 2000), 260);
});

img.addEventListener('dblclick', () => {
  clearTimeout(clickTimer);
  if (agentActive) return;
  const token = ++stateToken;
  img.classList.add('annoyed');
  setState('error');
  setTimeout(() => {
    img.classList.remove('annoyed');
    if (token === stateToken) toIdle();
  }, 1000);
});

img.addEventListener('contextmenu', (e) => {
  e.preventDefault();
  window.petAPI.showMenu();
});

// ---------- 启动 ----------
toIdle();
scheduleRandomIdle();
