const vscode = require('vscode');
const net = require('net');
const { execFile } = require('child_process');
const path = require('path');
const fs = require('fs');

const PET_HOST = '127.0.0.1';
const PET_PORT = 27315;        // 独立版宠物（Electron）HTTP 桥
const NATIVE_PORT = 27416;     // Trae 原生悬浮窗状态/模式服务器（本扩展内置）

let enabled = true;
let typingTimer = null;
let statusItem = null;
let online = false;            // 独立版宠物在线
let nativeOnline = false;      // 原生状态服务器在线（本窗口或其它窗口实例）
let nativeMode = 'hooks';      // hooks=六态 / official=官方四态
let pingInterval = null;
let nativeTimer = null;
let out = null;
let petLaunched = false;
let launchTimer = null;

// 原生模式数据源：hook-handler 每个 Hook 事件落盘的状态/模式文件
let stateFile = '';
let modeFile = '';

function log(msg) {
  if (out) out.appendLine(`[${new Date().toLocaleTimeString()}] ${msg}`);
}

// 用 net.Socket 直接发 HTTP，绕过 http 模块可能的限制
function rawHttp(method, urlPath, body, port) {
  return new Promise((resolve, reject) => {
    const client = new net.Socket();
    let data = '';
    client.connect(port, PET_HOST, () => {
      const req = body
        ? `POST ${urlPath} HTTP/1.1\r\nHost: ${PET_HOST}\r\nContent-Type: application/json\r\nContent-Length: ${Buffer.byteLength(body)}\r\nConnection: close\r\n\r\n${body}`
        : `GET ${urlPath} HTTP/1.1\r\nHost: ${PET_HOST}\r\nConnection: close\r\n\r\n`;
      client.write(req);
    });
    client.on('data', (chunk) => (data += chunk.toString()));
    client.on('close', () => resolve(data));
    client.on('error', reject);
    client.setTimeout(3000, () => { client.destroy(); reject(new Error('超时')); });
  });
}

async function sendState(state) {
  if (!enabled) { log(`已暂停，跳过: ${state}`); return; }
  log(`推送状态: ${state}`);
  try {
    const resp = await rawHttp('POST', '/state', JSON.stringify({ state }), PET_PORT);
    if (!online) { online = true; updateStatus(); }
    log(`推送成功: ${resp.substring(0, 50)}`);
  } catch (e) {
    log(`推送错误: ${e.message}`);
    if (online) { online = false; updateStatus(); }
  }
}

async function pingPet() {
  try {
    const resp = await rawHttp('GET', '/ping', null, PET_PORT);
    online = resp.includes('200') && resp.includes('ok');
  } catch (e) {
    if (online) log(`ping 宠物错误: ${e.message}`);
    online = false;
  }
  updateStatus();
}

// 查询原生服务器（本扩展或其它窗口实例），刷新模式显示
async function pingNative() {
  try {
    const resp = await rawHttp('GET', '/state', null, NATIVE_PORT);
    if (resp.includes('200')) {
      const m = resp.match(/\r\n\r\n([\s\S]*)\s*$/);
      let j = {};
      try { j = JSON.parse(m ? m[1] : '{}'); } catch {}
      nativeOnline = true;
      nativeMode = j.mode === 'official' ? 'official' : 'hooks';
    } else {
      nativeOnline = false;
    }
  } catch (e) {
    if (nativeOnline) log(`ping 原生服务器错误: ${e.message}`);
    nativeOnline = false;
  }
  updateStatus();
}

async function toggleNativeMode() {
  const next = nativeMode === 'hooks' ? 'official' : 'hooks';
  try {
    await rawHttp('POST', '/mode', JSON.stringify({ mode: next }), NATIVE_PORT);
    nativeMode = next;
    log(`原生模式切换 → ${next}`);
    vscode.window.setStatusBarMessage(`宠物模式：${next === 'hooks' ? 'Hooks 六态' : '官方四态'}`, 3000);
  } catch (e) {
    log(`模式切换失败: ${e.message}`);
  }
  updateStatus();
}

function updateStatus() {
  if (!statusItem) return;
  let icon, label, tooltip;
  if (!enabled) {
    icon = '$(circle-slash)';
    label = '已暂停';
    tooltip = 'Pet Bridge：已暂停，点击启用';
  } else if (online) {
    icon = '$(hubot)';
    label = '已连接';
    tooltip = 'Pet Bridge：独立版宠物在线，点击暂停/启用';
  } else if (nativeOnline) {
    icon = '$(hubot)';
    label = nativeMode === 'hooks' ? '原生·六态' : '原生·四态';
    tooltip = 'Pet Bridge：Trae 原生悬浮窗，点击切换 Hooks 六态 / 官方四态';
  } else {
    icon = '$(warning)';
    label = '未检测到宠物';
    tooltip = 'Pet Bridge：未检测到独立版宠物或原生状态服务器，点击暂停/启用';
  }
  statusItem.text = `${icon} Pet: ${label}`;
  statusItem.tooltip = tooltip;
}

function markTyping() {
  sendState('attention');
  clearTimeout(typingTimer);
  typingTimer = setTimeout(() => {}, 4000);
}

// ---------- 内置原生状态服务器（27416） ----------
// v2.1 起状态服务器不再用独立进程（WMI 拉起在 Trae 沙箱/Job 环境下不稳定，
// 还会误启 Trae.exe 窗口），改由扩展宿主直接监听：
//   GET /state → {state, ts, mode}（实时读 hook-handler 落盘的 hook-state.json）
//   GET /ping  → 存活探测
//   POST /mode → {"mode":"hooks"|"official"} 切换原生悬浮窗驱动模式（持久化）
// 悬浮窗按钮页（floatingButton.html 注入的轮询脚本）每 500ms 拉取 /state。
// 多窗口时第一个 exthost 抢到端口，其余窗口只作为客户端（ping/toggle 都打到
// 同一实例）。扩展随窗口卸载时端口释放，轮询自动回落官方四态。
let nativeServer = null;

function readModeFile() {
  try {
    nativeMode = JSON.parse(fs.readFileSync(modeFile, 'utf8')).mode === 'official' ? 'official' : 'hooks';
  } catch {}
}

function writeModeFile(m) {
  nativeMode = m;
  try { fs.writeFileSync(modeFile, JSON.stringify({ mode: m })); } catch (e) { log(`模式持久化失败: ${e.message}`); }
}

function startNativeServer() {
  if (nativeServer) return;
  readModeFile();
  nativeServer = net.createServer((socket) => {
    let buf = '';
    socket.on('data', (chunk) => {
      buf += chunk.toString('utf8');
      // 请求头结束 + POST body 收齐前继续等
      const headerEnd = buf.indexOf('\r\n\r\n');
      if (headerEnd < 0) return;
      const header = buf.slice(0, headerEnd);
      const m = header.match(/^(\w+)\s+(\S+)/);
      if (!m) { socket.destroy(); return; }
      const clMatch = header.match(/Content-Length:\s*(\d+)/i);
      const bodyLen = clMatch ? parseInt(clMatch[1], 10) : 0;
      if (buf.length < headerEnd + 4 + bodyLen) return; // body 未收齐
      const body = buf.slice(headerEnd + 4, headerEnd + 4 + bodyLen);
      buf = buf.slice(headerEnd + 4 + bodyLen); // keep-alive：继续解析下一请求
      // 尊重客户端的 Connection 头：close 时响应后关闭（扩展自己的 rawHttp
      // 客户端靠 socket close 收尾；悬浮窗 fetch 用 keep-alive 长连接复用）
      handleNativeRequest(socket, m[1], m[2], body, /Connection:\s*close/i.test(header));
    });
    socket.on('error', () => {});
  });
  nativeServer.on('error', (e) => {
    if (e.code === 'EADDRINUSE') {
      log('原生服务器端口被占（其它窗口的扩展在服务），本窗口仅作客户端');
    } else {
      log(`原生服务器错误: ${e.message}`);
    }
    nativeServer = null;
  });
  nativeServer.listen(NATIVE_PORT, PET_HOST, () => {
    log(`原生状态服务器已启动：${PET_HOST}:${NATIVE_PORT}（mode=${nativeMode}）`);
  });
}

function handleNativeRequest(socket, method, url, body, closeConn) {
  const headers = {
    'Access-Control-Allow-Origin': '*',
    'Cache-Control': 'no-store',
    'Content-Type': 'application/json',
    Connection: closeConn ? 'close' : 'keep-alive',
  };
  const send = (code, payload) => {
    const text = JSON.stringify(payload);
    socket.write(`HTTP/1.1 ${code}\r\n${Object.entries(headers).map(([k, v]) => `${k}: ${v}`).join('\r\n')}\r\nContent-Length: ${Buffer.byteLength(text)}\r\n\r\n${text}`);
    if (closeConn) socket.end();
  };
  if (method === 'GET' && url === '/ping') {
    send(200, { ok: true });
    return;
  }
  if (method === 'GET' && url === '/state') {
    let j = { state: 'idle', ts: 0 };
    try { j = JSON.parse(fs.readFileSync(stateFile, 'utf8')); } catch {}
    j.mode = nativeMode;
    send(200, j);
    return;
  }
  if (method === 'POST' && url === '/mode') {
    try {
      const j = JSON.parse(body);
      if (j.mode === 'hooks' || j.mode === 'official') {
        writeModeFile(j.mode);
        send(200, { mode: nativeMode });
      } else {
        send(400, { error: 'invalid mode' });
      }
    } catch {
      send(400, { error: 'bad request' });
    }
    return;
  }
  send(404, {});
}

// ---------- 独立版宠物启动（WMI，Job 外） ----------
// 关键：扩展宿主（exthost）及其直接 spawn 的子进程处于 Trae 的 kill-on-close
// Job Object 内，窗口关闭时会被连坐杀死。通过 WMI 的 Win32_Process.Create 让
// 系统服务进程 WmiPrvSE（Job 外）创建宠物进程，不继承 Job；生命周期由宠物自带
// 的看门狗（--follow-trae）管理。
function launchPet() {
  const config = vscode.workspace.getConfiguration('traePetBridge');
  const autoLaunch = config.get('autoLaunch', true);
  if (!autoLaunch) { log('自动启动已禁用，跳过'); return; }
  if (petLaunched) { log('本窗口已启动过宠物，跳过'); return; }
  if (online) { log('宠物已在线，跳过启动'); return; }

  let petPath = config.get('petPath', '');

  // 未配置路径时尝试自动发现
  if (!petPath) {
    const candidates = [
      path.join(__dirname, '..', 'ClawdPet.exe'),          // 源码目录下的构建产物
      path.join(__dirname, 'ClawdPet.exe'),                 // 扩展目录内
    ];
    for (const c of candidates) {
      if (fs.existsSync(c)) { petPath = c; break; }
    }
  }

  let cmdLine, cwd;
  if (petPath && fs.existsSync(petPath)) {
    if (petPath.toLowerCase().endsWith('.exe')) {
      cmdLine = `"${petPath}" --follow-trae`;
      cwd = path.dirname(petPath);
    } else {
      const electronPath = path.join(petPath, 'node_modules', 'electron', 'dist', 'electron.exe');
      if (!fs.existsSync(electronPath)) {
        log(`未找到项目 Electron: ${electronPath}`);
        return;
      }
      cmdLine = `"${electronPath}" . --follow-trae`;
      cwd = petPath;
    }
  } else {
    const projectDir = path.join(__dirname, '..');
    const electronPath = path.join(projectDir, 'node_modules', 'electron', 'dist', 'electron.exe');
    if (fs.existsSync(path.join(projectDir, 'main.js')) && fs.existsSync(electronPath)) {
      log(`自动发现宠物项目: ${projectDir}`);
      cmdLine = `"${electronPath}" . --follow-trae`;
      cwd = projectDir;
    } else {
      log('未找到宠物路径，请在设置中配置 traePetBridge.petPath');
      return;
    }
  }

  try {
    const psScript =
      `Invoke-CimMethod -ClassName Win32_Process -MethodName Create ` +
      `-Arguments @{CommandLine='${cmdLine.replace(/'/g, "''")}';CurrentDirectory='${cwd.replace(/'/g, "''")}'}`;
    const encoded = Buffer.from(psScript, 'utf16le').toString('base64');
    log(`通过 WMI 启动宠物: ${cmdLine}`);
    execFile('powershell.exe', ['-NoProfile', '-EncodedCommand', encoded], { windowsHide: true, timeout: 15000 }, (err) => {
      if (err) log(`WMI 启动器错误: ${err.message}`);
    });
    petLaunched = true;
    log('WMI 启动命令已发出');
  } catch (e) {
    log(`启动宠物失败: ${e.message}`);
  }
}

function resolveNativeFiles() {
  const config = vscode.workspace.getConfiguration('traePetBridge');
  let petPath = config.get('petPath', '');
  if (!petPath || !fs.existsSync(path.join(petPath, 'hook-handler.js'))) {
    petPath = path.join(__dirname, '..'); // 兜底：扩展源码同级即项目目录
  }
  stateFile = path.join(petPath, 'hook-state.json');
  modeFile = path.join(petPath, 'hook-mode.json');
}

function activate(context) {
  out = vscode.window.createOutputChannel('Pet Bridge');
  log('=== Pet Bridge 扩展已激活 ===');

  resolveNativeFiles();
  startNativeServer();

  statusItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 100);
  statusItem.command = 'trae-pet-bridge.toggle';
  updateStatus();
  statusItem.show();
  log('状态栏已创建');

  context.subscriptions.push(
    vscode.commands.registerCommand('trae-pet-bridge.toggle', () => {
      // 原生模式在线：点击 = 切换 Hooks 六态 / 官方四态；否则 = 暂停/启用
      if (!online && nativeOnline) {
        toggleNativeMode();
        return;
      }
      enabled = !enabled;
      if (!enabled) sendState('idle');
      updateStatus();
      log(`桥接 ${enabled ? '启用' : '暂停'}`);
    }),

    vscode.workspace.onDidChangeTextDocument((e) => {
      if (e.document.uri.scheme === 'file' && e.contentChanges.length > 0) {
        markTyping();
      }
    }),

    vscode.window.onDidChangeTextEditorSelection((e) => {
      if (e.textEditor.document.uri.scheme === 'file') {
        markTyping();
      }
    }),

    { dispose: () => { if (nativeServer) { nativeServer.close(); nativeServer = null; } } }
  );

  pingPet();
  pingNative();
  pingInterval = setInterval(pingPet, 30000);
  nativeTimer = setInterval(pingNative, 5000);
  context.subscriptions.push({ dispose: () => { clearInterval(pingInterval); clearInterval(nativeTimer); } });

  // 等待扩展宿主完成初始化后再启动，避免 Trae 启动阶段拒绝 GUI 子进程
  launchTimer = setTimeout(launchPet, 1500);
}

function stopPet() {
  if (launchTimer) clearTimeout(launchTimer);
  if (pingInterval) clearInterval(pingInterval);
  if (nativeTimer) clearInterval(nativeTimer);
  // 原生服务器随扩展 dispose 关闭；独立版宠物由 WMI 在 Job 外创建，自管生命周期
}

function deactivate() {
  stopPet();
}

module.exports = { activate, deactivate };
