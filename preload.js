// Узкий мост между страницей лаунчера и основным процессом: только эти методы,
// без доступа к Node.js и без произвольных каналов.
const { contextBridge, ipcRenderer } = require('electron');

const on = (channel) => (cb) => {
  const handler = (_e, payload) => cb(payload);
  ipcRenderer.on(channel, handler);
  return () => ipcRenderer.removeListener(channel, handler);
};

contextBridge.exposeInMainWorld('launcher', {
  getState: () => ipcRenderer.invoke('state:get'),
  login: (email) => ipcRenderer.invoke('auth:start', email),
  cancelLogin: () => ipcRenderer.invoke('auth:cancel'),
  switchAccount: (id) => ipcRenderer.invoke('auth:switch', id),
  addAccount: () => ipcRenderer.invoke('auth:add'),
  logout: (id) => ipcRenderer.invoke('auth:logout', id),
  listApps: (force) => ipcRenderer.invoke('apps:list', !!force),
  install: (id) => ipcRenderer.invoke('apps:install', id),
  launch: (id) => ipcRenderer.invoke('apps:launch', id),
  uninstall: (id) => ipcRenderer.invoke('apps:uninstall', id),
  installSelfUpdate: () => ipcRenderer.invoke('self:install'),
  onAuthResult: on('auth:result'),
  onProgress: on('apps:progress'),
  onSelfUpdate: on('self:update'),
});
