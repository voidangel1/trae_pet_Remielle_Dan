const vscode = require('vscode');
const net = require('net');
const { spawn, execFile } = require('child_process');
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
let petProcess = null;
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
function launchPet() {
  const config = vscode.workspace.getConfiguration('traePetBridge');
  const autoLaunch = config.get('autoLaunch', true);
  if (!autoLaunch) { log('自动启动已禁用，跳过'); return; }
  if (petProcess) { log('宠物进程已在运行，跳过'); return; }

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

  try {
    if (petPath && fs.existsSync(petPath)) {
      if (petPath.toLowerCase().endsWith('.exe')) {
        // 直接调用 Windows 可执行文件，避免 shell/cmd 的路径和退出行为影响 Electron
        log(`启动宠物 exe: ${petPath}`);
        petProcess = execFile(petPath, [], {
          cwd: path.dirname(petPath),
          windowsHide: true,
          detached: true,
          stdio: 'ignore',
        });
      } else {
        // 开发模式：直接调用项目本地 Electron，不经过 portable exe 或 npx
        const electronPath = path.join(petPath, 'node_modules', 'electron', 'dist', 'electron.exe');
        if (!fs.existsSync(electronPath)) {
          log(`未找到项目 Electron: ${electronPath}`);
          return;
        }
        log(`启动宠物开发模式: ${petPath}`);
        const childEnv = { ...process.env };
        delete childEnv.ELECTRON_RUN_AS_NODE;
        delete childEnv.VSCODE_PID;
        petProcess = execFile(electronPath, ['.'], {
          cwd: petPath,
          env: childEnv,
          windowsHide: true,
          detached: true,
          stdio: 'ignore',
        });
      }
      petProcess.on('error', (error) => log(`宠物进程错误: ${error.message}`));
      petProcess.on('exit', (code) => log(`宠物进程退出: code=${code}`));
    } else {
      // 兜底：假设扩展源码同级目录就是宠物项目
      const projectDir = path.join(__dirname, '..');
      if (fs.existsSync(path.join(projectDir, 'main.js'))) {
        log(`自动发现宠物项目: ${projectDir}`);
        petProcess = spawn('npx', ['electron', '.'], { cwd: projectDir, detached: true, stdio: 'ignore', shell: true });
      } else {
        log('未找到宠物路径，请在设置中配置 trae2PetBridge.petPath');
        return;
      }
    }
    petProcess.unref();
    log('宠物进程已启动');
  } catch (e) {
    log(`启动宠物失败: ${e.message}`);
    petProcess = null;
  }
}

function activate(context) {
  out = vscode.window.createOutputChannel('Pet Bridge');
  out.show();
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
  if (!petProcess || !petProcess.pid) return;

  const pid = petProcess.pid;
  log(`Trae 已关闭，正在关闭宠物进程: ${pid}`);
  execFile('taskkill', ['/PID', String(pid), '/T', '/F'], { windowsHide: true }, () => {});
  petProcess = null;
}

function deactivate() {
  stopPet();
}

module.exports = { activate, deactivate };
