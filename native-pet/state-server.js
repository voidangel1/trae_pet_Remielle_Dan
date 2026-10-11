// state-server.js — Trae 原生悬浮窗的 Hooks 状态 + 模式服务器（独立调试工具）
//
// 【v2.1 起不再是必需组件】生产环境由 trae-pet-bridge 扩展内置同款服务器
// （exthost 直接监听 27416，随扩展启停），hook-handler 只落盘状态文件。
// 保留本文件用于脱离 Trae 扩展手动调试原生悬浮窗轮询：
//
//   node state-server.js <hook-state.json> [hook-mode.json]
//
//   GET /state → {state, ts, mode}
//   GET /ping  → 存活探测
//   POST /mode → {"mode":"hooks"|"official"}
const http = require('http');
const fs = require('fs');

const PORT = 27416;
const STATE_FILE = process.argv[2];
const MODE_FILE = process.argv[3]; // 可选：模式持久化
const IDLE_EXIT_MS = 10 * 60 * 1000;

if (!STATE_FILE) process.exit(1);

let mode = 'hooks';
if (MODE_FILE) {
  try {
    mode = JSON.parse(fs.readFileSync(MODE_FILE, 'utf8')).mode === 'official' ? 'official' : 'hooks';
  } catch {}
}

function saveMode(m) {
  mode = m;
  if (MODE_FILE) {
    try { fs.writeFileSync(MODE_FILE, JSON.stringify({ mode: m })); } catch {}
  }
}

let lastRequest = Date.now();

const server = http.createServer((req, res) => {
  lastRequest = Date.now();
  const headers = {
    'Access-Control-Allow-Origin': '*',
    'Cache-Control': 'no-store',
    'Content-Type': 'application/json',
  };
  if (req.method === 'GET' && req.url === '/ping') {
    res.writeHead(200, headers);
    res.end('{"ok":true}');
    return;
  }
  if (req.method === 'GET' && req.url === '/state') {
    let body = '{"state":"idle","ts":0}';
    try {
      const raw = fs.readFileSync(STATE_FILE, 'utf8');
      const j = JSON.parse(raw); // 校验合法再下发，损坏时回落默认值
      j.mode = mode;
      body = JSON.stringify(j);
    } catch {
      body = JSON.stringify({ state: 'idle', ts: 0, mode });
    }
    res.writeHead(200, headers);
    res.end(body);
    return;
  }
  if (req.method === 'POST' && req.url === '/mode') {
    let data = '';
    req.on('data', (c) => { data += c; if (data.length > 4096) req.destroy(); });
    req.on('end', () => {
      try {
        const j = JSON.parse(data);
        if (j.mode === 'hooks' || j.mode === 'official') {
          saveMode(j.mode);
          res.writeHead(200, headers);
          res.end(JSON.stringify({ mode }));
        } else {
          res.writeHead(400, headers);
          res.end('{"error":"invalid mode"}');
        }
      } catch {
        res.writeHead(400, headers);
        res.end('{"error":"bad request"}');
      }
    });
    return;
  }
  res.writeHead(404, headers);
  res.end();
});

server.on('error', () => process.exit(0)); // 端口被占：已有实例在跑
server.listen(PORT, '127.0.0.1', () => {
  // detached + stdio ignore，输出无人消费，仅写 stderr 便于手动调试
  console.error(`[clawd-state-server] listening on 127.0.0.1:${PORT}, state: ${STATE_FILE}, mode: ${mode}`);
});

// 无请求自杀（避免残留后台进程）
setInterval(() => {
  if (Date.now() - lastRequest > IDLE_EXIT_MS) process.exit(0);
}, 60 * 1000).unref();
