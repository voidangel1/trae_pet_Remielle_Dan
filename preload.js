const { contextBridge, ipcRenderer } = require('electron');
const fs = require('fs');
const path = require('path');

const THEME_DIR_NAME = 'Remielle_Dan-clawd-theme-v0.5.4';
const themePath = path.join(__dirname, THEME_DIR_NAME, 'theme.json');

contextBridge.exposeInMainWorld('petAPI', {
  getTheme: () => JSON.parse(fs.readFileSync(themePath, 'utf8')),
  themeDirName: THEME_DIR_NAME,
  onUserActivity: (cb) => ipcRenderer.on('user-activity', cb),
  onTriggerState: (cb) => ipcRenderer.on('trigger-state', (_e, name) => cb(name)),
  dragStart: (pt) => ipcRenderer.send('drag-start', pt),
  dragMove: (pt) => ipcRenderer.send('drag-move', pt),
  dragEnd: () => ipcRenderer.send('drag-end'),
  showMenu: () => ipcRenderer.send('show-menu'),
});
