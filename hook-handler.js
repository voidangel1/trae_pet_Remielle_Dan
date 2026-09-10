// hook-handler.js — Trae Hook 事件 → 桌面宠物状态桥接
// 用 net.Socket 直接发 HTTP（快速、不阻塞 Agent），不输出 stdout
const net = require('net');
const fs = require('fs');
const path = require('path');

const LOG_FILE = path.join(__dirname, 'hook-debug.log');
// 最近一次推送的状态（每次 Hook 都是独立进程，需要落盘跨进程共享）
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
  const collect = (value, key = '', depth = 0) => {
    if (depth > 6 || values.length > 200) return;
    if (typeof value === 'string') {
      values.push(`${key} ${value.substring(0, 2000)}`);
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

function pushState(state) {
  writeLastPush(state);
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
        state = detectError(data, stopReason) ? 'error' : 'idle';
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
