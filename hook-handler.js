// hook-handler.js — Trae Hook 事件 → 桌面宠物状态桥接
// 用 net.Socket 直接发 HTTP（快速、不阻塞 Agent），不输出 stdout
// v2.1: 原生悬浮窗的状态服务器由 trae-pet-bridge 扩展内置（27416），
// hook-handler 只负责落盘状态（writeLastPush）+ 推送独立版宠物（27315 桥），
// 不再通过 WMI 拉起任何进程（沙箱/Job 环境下不稳定且会误启 Trae.exe 窗口）。
const net = require('net');
const fs = require('fs');
const path = require('path');

const LOG_FILE = path.join(__dirname, 'hook-debug.log');
// 最近一次推送的状态（每次 Hook 都是独立进程，需要落盘跨进程共享；
// 原生悬浮窗模式：扩展内置服务器每次请求实时读取此文件）
const STATE_FILE = path.join(__dirname, 'hook-state.json');

function log(msg) {
  const line = `[${new Date().toISOString()}] ${msg}\n`;
  try { fs.appendFileSync(LOG_FILE, line); } catch {}
}

function readLastPush() {
  try {
    return JSON.parse(fs.readFileSync(STATE_FILE, 'utf8'));
  } catch {
    return null;
  }
}

function writeLastPush(state) {
  try {
    fs.writeFileSync(STATE_FILE, JSON.stringify({ state, ts: Date.now() }));
  } catch {}
}

function detectError(data, stopReason) {
  const values = [];
  // v2.0 修复：text_content / last_assistant_message 是完整的助手回复文本，
  // 里面出现「错误/失败」等字样（如日志、排障总结）会被误判成 error 态，
  // 必须跳过；错误判定只看结构化字段（stopReason、error 对象等）。
  const TEXT_KEYS = new Set(['text_content', 'last_assistant_message']);
  const collect = (value, key = '', depth = 0) => {
    if (depth > 6 || values.length > 200) return;
    if (typeof value === 'string') {
      if (!TEXT_KEYS.has(key.trim())) values.push(`${key} ${value.substring(0, 2000)}`);
    } else if (Array.isArray(value)) {
      value.slice(0, 50).forEach((item) => collect(item, key, depth + 1));
    } else if (value && typeof value === 'object') {
      Object.entries(value).slice(0, 100).forEach(([childKey, childValue]) => {
        collect(childValue, `${key} ${childKey}`, depth + 1);
      });
    }
  };
  collect(data);
  collect(stopReason, 'stopReason');
  const text = values.join(' ').toLowerCase();
  return /error|fail(?:ed|ure)?|exception|abort(?:ed)?|cancel(?:led)?|interrupt(?:ed)?|crash(?:ed)?|timeout|timed.?out|websocket|http status 5\d\d|模型异常|模型错误|模型中断|中断|失败|异常|错误/.test(text);
}

// v2.0：判定 Stop 时模型是否在向用户提问（notification 态的新语义）。
// 看 last_assistant_message 的结尾：问题通常出现在消息末尾；
// 先剔除代码块（代码里的 ?/吗 是误报源），再匹配问句特征。
function detectQuestion(data) {
  const text = `${data.last_assistant_message || ''}\n${data.text_content || ''}`.trim();
  if (!text) return false;
  const stripped = text.replace(/```[\s\S]*?```/g, '').replace(/`[^`\n]*`/g, '');
  const tail = stripped.trimEnd().slice(-400);
  return /[？?]\s*$/.test(tail)
    || /(吗|呢)[？?；;。]?\s*$/.test(tail)
    || /(?:请确认|请选择|是否需要|需要我|告诉我)/.test(tail.slice(-120));
}

function pushState(state) {
  // 1) 落盘（原生悬浮窗：扩展内置服务器实时读取；独立版：hook-state 机制）
  writeLastPush(state);
  // 2) 推送独立版宠物（27315 桥，原生模式下桥不在 → 静默失败即可）
  const body = JSON.stringify({ state });
  const client = new net.Socket();
  client.connect(27315, '127.0.0.1', () => {
    client.write(
      `POST /state HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Type: application/json\r\nContent-Length: ${Buffer.byteLength(body)}\r\nConnection: close\r\n\r\n${body}`
    );
    log(`HTTP 已发送: ${state}`);
    client.destroy();
    process.exit(0);
  });
  client.on('error', (e) => {
    log(`HTTP 错误: ${e.message}`);
    process.exit(0);
  });
  setTimeout(() => { log('HTTP 超时'); process.exit(0); }, 2000);
}

let input = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', (chunk) => (input += chunk));
process.stdin.on('end', () => {
  let event = '';
  let stopReason = '';
  let data = {};
  try {
    data = JSON.parse(input);
    event = data.hook_event_name || '';
    stopReason = data.stopReason || data.stop_reason || '';
    log(`收到事件: ${event} | stopReason: ${stopReason}`);
    // Notification / Stop 的 payload 内容对排查状态误判最有价值，截断记录
    if (event === 'Notification' || event === 'Stop') {
      log(`payload: ${JSON.stringify(data).substring(0, 1500)}`);
    }
  } catch {
    log(`JSON 解析失败: ${input.substring(0, 200)}`);
    // 即使解析失败，也尝试从原始文本判断异常并兜底恢复
    if (/error|fail|exception|abort|interrupt|模型异常|异常|失败|错误/i.test(input)) {
      pushState('error');
    } else {
      pushState('idle');
    }
    return;
  }

  let state = 'idle';
  try {
    switch (event) {
      case 'UserPromptSubmit': state = 'thinking'; break;
      case 'PreToolUse':       state = 'editing'; break;
      case 'PostToolUse':      state = 'thinking'; break;
      case 'Notification': {
        // Stop 判定为 error 后约 300ms，Trae 会紧接着弹「任务已中断」系统通知，
        // 其 payload 不含错误关键词，会把宠物刚展示的 error 覆盖成 notification。
        // 15 秒窗口内直接忽略这类通知，保证 error 表情可见。
        const last = readLastPush();
        if (last && last.state === 'error' && Date.now() - last.ts < 15000) {
          log('error 后 15s 内的 Notification，忽略（避免覆盖错误状态）');
          process.exit(0);
        }
        state = detectError(data, stopReason) ? 'error' : 'notification';
        break;
      }
      case 'Stop':
        if (detectError(data, stopReason)) {
          state = 'error';
        } else if (detectQuestion(data)) {
          // v2.0 新语义：notification = 模型向用户提问、等待回答
          state = 'notification';
          log('Stop 判定为模型提问 → notification');
        } else {
          state = 'idle';
        }
        break;
      default:                 state = 'idle'; break;
    }
  } catch (e) {
    log(`状态映射异常: ${e.message}，回退到 error`);
    state = 'error';
  }

  log(`映射: ${event} → ${state} | stopReason: ${stopReason}`);
  pushState(state);
});

// 兜底：3 秒后强制退出，避免阻塞 Agent
setTimeout(() => { log('stdin 超时，强制退出'); process.exit(0); }, 3000);
