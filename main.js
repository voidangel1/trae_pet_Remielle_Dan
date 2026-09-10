const { app, BrowserWindow, ipcMain, Menu, screen } = require('electron');
const { execFile } = require('child_process');
const http = require('http');
const path = require('path');

const PET_SIZE = 180;
const BRIDGE_PORT = 27315; // 与 Trae 桥接扩展约定的本地端口
let win = null;
let dragOffset = null;
let dragLocked = true;

// 所有 Trae 项目共用同一个宠物进程
const gotSingleInstanceLock = app.requestSingleInstanceLock();
if (!gotSingleInstanceLock) {
  app.quit();
} else {
  app.on('second-instance', () => {
    if (win && !win.isDestroyed()) {
      if (win.isMinimized()) win.restore();
      win.focus();
    }
  });
}

function createWindow() {
  const { width, height } = screen.getPrimaryDisplay().workAreaSize;

  win = new BrowserWindow({
    width: PET_SIZE,
    height: PET_SIZE,
    x: Math.round(width - PET_SIZE - 40),
    y: Math.round(height - PET_SIZE - 40),
    transparent: true,
    frame: false,
    resizable: false,
    minWidth: PET_SIZE,
    maxWidth: PET_SIZE,
    minHeight: PET_SIZE,
    maxHeight: PET_SIZE,
    alwaysOnTop: true,
    skipTaskbar: true,
    hasShadow: false,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      sandbox: false, // preload 需要 fs 读取 theme.json，沙箱化渲染进程无法 require('fs')
    },
  });

  win.setAlwaysOnTop(true, 'screen-saver');
  win.loadFile('index.html');

  // 兜底：监听尺寸变化，一旦偏离固定值立刻拉回（防御 Electron/系统 DPI 引起的漂移）
  win.on('resize', () => enforceFixedSize());

  // F12 切换 DevTools（用于排查状态切换问题）
  win.webContents.on('before-input-event', (event, input) => {
    if (input.key === 'F12') {
      win.webContents.toggleDevTools();
      event.preventDefault();
    }
  });

  // 将渲染进程的 console.log 转发到终端
  win.webContents.on('console-message', (_e, _level, message) => {
    console.log(`[renderer] ${message}`);
  });
}

// 全局鼠标活动检测：光标移动即视为用户活跃（驱动闲置/入睡计时）
function startActivityWatcher() {
  let last = screen.getCursorScreenPoint();
  setInterval(() => {
    const pt = screen.getCursorScreenPoint();
    if (pt.x !== last.x || pt.y !== last.y) {
      last = pt;
      if (win && !win.isDestroyed()) win.webContents.send('user-activity');
    }
  }, 1000);
}

ipcMain.on('drag-start', (_e, pt) => {
  if (!win) return;
  // 拖拽开始前先复位尺寸，清除历史残留的漂移（如有）
  enforceFixedSize();
  const [wx, wy] = win.getPosition();
  dragOffset = { x: pt.x - wx, y: pt.y - wy };
});

ipcMain.on('drag-move', (_e, pt) => {
  if (!win || !dragOffset) return;
  // 不使用 setPosition：Electron 在 Windows DPI 缩放非 100% 时存在已知 bug
  // (electron#48247)，仅设置坐标也会连带改变窗口宽高，导致拖拽时窗口越来越大。
  // 改用 setBounds 并显式携带固定宽高，保证尺寸不被篡改。
  win.setBounds({
    x: Math.round(pt.x - dragOffset.x),
    y: Math.round(pt.y - dragOffset.y),
    width: PET_SIZE,
    height: PET_SIZE,
  });
});

ipcMain.on('drag-end', () => {
  dragOffset = null;
});

// 双保险：任何原因导致窗口尺寸偏离固定值时，立刻拉回 PET_SIZE
function enforceFixedSize() {
  if (!win || win.isDestroyed()) return;
  const [w, h] = win.getSize();
  if (w !== PET_SIZE || h !== PET_SIZE) {
    const [x, y] = win.getPosition();
    win.setBounds({ x, y, width: PET_SIZE, height: PET_SIZE });
  }
}

// ---------- 跟随 Trae 退出看门狗 ----------
// 桥接扩展启动宠物时开启此功能（--follow-trae 参数或 CLAWD_PET_FOLLOW_TRAE 环境变量）；
// 手动 npm start 或双击 exe 启动的宠物不带标记，不会自动退出。
// 注意：扩展必须通过 WMI 创建宠物（见 extension.js），直接 spawn 的子进程会
// 落入 Trae 的 kill-on-close Job Object，随窗口关闭被连坐杀死。
const FOLLOW_TRAE = process.env.CLAWD_PET_FOLLOW_TRAE === '1' || process.argv.includes('--follow-trae');
// 仅匹配 Trae 主程序（Trae.exe / Trae CN.exe），排除 trae-sandbox 等辅助进程
const TRAE_PROCESS_RE = /^trae( cn|-cn)?\.exe$/i;

function hasTraeProcess(callback) {
  execFile('tasklist', ['/FO', 'CSV', '/NH'], { windowsHide: true }, (err, stdout) => {
    if (err) {
      callback(true); // 检测失败时按「仍在运行」处理，避免误杀
      return;
    }
    const found = stdout.split('\n').some((line) => {
      const m = line.match(/^"([^"]+)"/);
      return !!m && TRAE_PROCESS_RE.test(m[1]);
    });
    callback(found);
  });
}

function startTraeWatchdog() {
  if (!FOLLOW_TRAE) return;
  let misses = 0;
  const timer = setInterval(() => {
    hasTraeProcess((found) => {
      if (found) {
        misses = 0;
      } else if (++misses >= 2) {
        // 连续两次（约 20 秒）未检测到 Trae：所有窗口均已关闭，宠物自行退出。
        // 容忍期可扛住 Trae 重启/升级时的短暂进程空窗。
        console.log('[watchdog] 所有 Trae 窗口已关闭，宠物退出');
        clearInterval(timer);
        app.quit();
      }
    });
  }, 10000);
  timer.unref();
}

ipcMain.on('show-menu', () => {
  const send = (name) => () => win.webContents.send('trigger-state', name);
  const menu = Menu.buildFromTemplate([
    { label: '空闲', click: send('idle') },
    { label: '注意', click: send('attention') },
    { label: '思考', click: send('thinking') },
    { label: '编辑', click: send('editing') },
    { label: '通知', click: send('notification') },
    { label: '错误', click: send('error') },
    { type: 'separator' },
    { label: dragLocked ? '锁定位置' : '可移动', click: () => {
      dragLocked = !dragLocked;
      win.webContents.send('trigger-state', 'lock-toggle');
    } },
    { type: 'separator' },
    { label: '退出', click: () => app.quit() },
  ]);
  menu.popup({ window: win });
});

// 本地 HTTP 桥：接收 Trae 扩展推送的任务事件，驱动宠物状态
function startBridgeServer() {
  const server = http.createServer((req, res) => {
    if (req.method === 'GET' && req.url === '/ping') {
      res.writeHead(200, { 'Content-Type': 'text/plain' });
      res.end('ok');
      return;
    }
    if (req.method === 'POST' && req.url === '/state') {
      let body = '';
      req.on('data', (chunk) => {
        body += chunk;
        if (body.length > 4096) req.destroy(); // 防御异常大包
      });
      req.on('end', () => {
        try {
          const { state } = JSON.parse(body);
          console.log(`[bridge] 收到状态: ${state}`);
          if (typeof state === 'string' && win && !win.isDestroyed()) {
            win.webContents.send('trigger-state', state);
          }
          res.writeHead(200, { 'Content-Type': 'text/plain' });
          res.end('ok');
        } catch {
          res.writeHead(400);
          res.end('bad request');
        }
      });
      return;
    }
    res.writeHead(404);
    res.end();
  });
  server.on('error', () => {}); // 端口被占用（如多开）时不影响宠物本体
  server.listen(BRIDGE_PORT, '127.0.0.1');
}

app.whenReady().then(() => {
  createWindow();
  startActivityWatcher();
  startBridgeServer();
  startTraeWatchdog();
});

app.on('window-all-closed', () => app.quit());
