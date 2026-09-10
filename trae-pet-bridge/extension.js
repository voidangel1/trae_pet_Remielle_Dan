const vscode = require('vscode');
const net = require('net');
const { execFile } = require('child_process');
const path = require('path');
const fs = require('fs');

const PET_HOST = '127.0.0.1';
const PET_PORT = 27315;

let enabled = true;
let typingTimer = null;
let statusItem = null;
let online = false;
let pingInterval = null;
let out = null;
let petLaunched = false;
let launchTimer = null;

function log(msg) {
  if (out) out.appendLine(`[${new Date().toLocaleTimeString()}] ${msg}`);
}

// 用 net.Socket 直接发 HTTP，绕过 http 模块可能的限制
function rawHttp(method, path, body) {
  return new Promise((resolve, reject) => {
    const client = new net.Socket();
    let data = '';
    client.connect(PET_PORT, PET_HOST, () => {
      const req = body
        ? `POST ${path} HTTP/1.1\r\nHost: ${PET_HOST}\r\nContent-Type: application/json\r\nContent-Length: ${Buffer.byteLength(body)}\r\nConnection: close\r\n\r\n${body}`
        : `GET ${path} HTTP/1.1\r\nHost: ${PET_HOST}\r\nConnection: close\r\n\r\n`;
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
    const resp = await rawHttp('POST', '/state', JSON.stringify({ state }));
    if (!online) { online = true; updateStatus(); }
    log(`推送成功: ${resp.substring(0, 50)}`);
  } catch (e) {
    log(`推送错误: ${e.message}`);
    if (online) { online = false; updateStatus(); }
  }
}

async function pingPet() {
  log('ping 宠物...');
  try {
    const resp = await rawHttp('GET', '/ping');
    online = resp.includes('200') && resp.includes('ok');
    log(`ping 结果: online=${online}`);
    updateStatus();
  } catch (e) {
    log(`ping 错误: ${e.message}`);
    if (online) { online = false; updateStatus(); }
  }
}

function updateStatus() {
  if (!statusItem) return;
  const icon = online ? '$(hubot)' : '$(warning)';
  const label = enabled ? (online ? '已连接' : '未检测到宠物') : '已暂停';
  statusItem.text = `${icon} Pet: ${label}`;
  statusItem.tooltip = '点击切换桌面宠物桥接';
}

function markTyping() {
  sendState('attention');
  clearTimeout(typingTimer);
  typingTimer = setTimeout(() => {}, 4000);
}

// 自动启动宠物 Electron 进程
// 关键：扩展宿主（exthost）及其直接 spawn 的子进程全部处于 Trae 的 kill-on-close
// Job Object 内，宿主所在窗口关闭时会被连坐杀死（实测确认，detached: true 也无法
// 逃逸）。因此这里必须通过 WMI 的 Win32_Process.Create 让系统服务进程 WmiPrvSE
// （在 Job 外）创建宠物进程，宠物不继承 Job，窗口关闭后仍存活；其生命周期由
// 宠物自带的看门狗（--follow-trae）管理，所有 Trae 窗口关闭后自行退出。
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
      // 开发模式：直接调用项目本地 Electron，不经过 portable exe 或 npx
      const electronPath = path.join(petPath, 'node_modules', 'electron', 'dist', 'electron.exe');
      if (!fs.existsSync(electronPath)) {
        log(`未找到项目 Electron: ${electronPath}`);
        return;
      }
      cmdLine = `"${electronPath}" . --follow-trae`;
      cwd = petPath;
    }
  } else {
    // 兜底：假设扩展源码同级目录就是宠物项目
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
    // WMI Create 无法传递环境变量，改用 --follow-trae 命令行参数开启看门狗
    const psScript =
      `Invoke-CimMethod -ClassName Win32_Process -MethodName Create ` +
      `-Arguments @{CommandLine='${cmdLine.replace(/'/g, "''")}';CurrentDirectory='${cwd.replace(/'/g, "''")}'}`;
    // 用 -EncodedCommand 传递脚本，避开多层引号转义问题
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

function activate(context) {
  out = vscode.window.createOutputChannel('Pet Bridge');
  // 不自动弹出输出面板，仅静默记录日志（可在输出面板手动查看）
  log('=== Pet Bridge 扩展已激活 ===');

  statusItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 100);
  statusItem.command = 'trae-pet-bridge.toggle';
  updateStatus();
  statusItem.show();
  log('状态栏已创建');

  context.subscriptions.push(
    vscode.commands.registerCommand('trae-pet-bridge.toggle', () => {
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
    })
  );

  pingPet();
  pingInterval = setInterval(pingPet, 30000);
  context.subscriptions.push({ dispose: () => clearInterval(pingInterval) });

  // 等待扩展宿主完成初始化后再启动，避免 Trae 启动阶段拒绝 GUI 子进程
  launchTimer = setTimeout(launchPet, 1500);
}

function stopPet() {
  if (launchTimer) clearTimeout(launchTimer);
  if (pingInterval) clearInterval(pingInterval);
  // 不做任何宠物进程处理：宠物由 WMI 在 Job 外创建，生命周期由其自带看门狗管理
}

function deactivate() {
  stopPet();
}

module.exports = { activate, deactivate };
