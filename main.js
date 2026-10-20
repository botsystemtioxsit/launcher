// БАТТЛ Лаунчер: вход в аккаунт (та же заявка с подтверждением, что в игре и
// админ-панели — без Firebase Auth), установка/обновление/запуск приложений
// проекта. Окно — локальные файлы из renderer/, без загрузки чужих страниц.
const { app, BrowserWindow, ipcMain, shell, Menu } = require('electron');
const { autoUpdater } = require('electron-updater');
const path = require('path');
const rtdb = require('./lib/rtdb');
const { createStore } = require('./lib/store');
const { APPS, byId, isAllowed } = require('./lib/catalog');
const { latestRelease, compareVersions } = require('./lib/releases');
const installer = require('./lib/installer');

const REQUEST_TTL_MS = 5 * 60 * 1000;   // как в игре и админ-панели
const POLL_MS = 2000;
const ACCOUNT_ID_RE = rtdb.SAFE_ID_RE;
const MAX_ACCOUNTS = 10;

let win = null;
let store = null;
let pendingLogin = null;   // { targetId, requestId, timer, deadline }
const busyApps = new Set(); // приложения, с которыми сейчас идёт установка/удаление

const send = (channel, payload) => {
  if (win && !win.isDestroyed()) win.webContents.send(channel, payload);
};

// ---------- аккаунты ----------
const accounts = () => store.get().accounts;
const activeAccount = () => accounts().find((a) => a.id === store.get().activeId) || null;

function saveAccount(profile) {
  const entry = { id: profile.id, name: profile.name, role: profile.role, playerId: profile.playerId, addedAt: Date.now() };
  const rest = accounts().filter((a) => a.id !== entry.id);
  const prev = accounts().find((a) => a.id === entry.id);
  if (prev) entry.addedAt = prev.addedAt;
  store.set({ accounts: [entry, ...rest].slice(0, MAX_ACCOUNTS), activeId: entry.id });
}

async function refreshActiveProfile() {
  const acc = activeAccount();
  if (!acc) return;
  try {
    const p = await rtdb.getProfile(acc.id);
    if (p.status === 'banned') {
      store.set({ accounts: accounts().filter((a) => a.id !== acc.id), activeId: null });
      return;
    }
    store.set({ accounts: accounts().map((a) => (a.id === acc.id ? { ...a, name: p.name, role: p.role, playerId: p.playerId } : a)) });
  } catch (_) { /* нет сети — остаются сохранённые данные */ }
}

function publicState() {
  return {
    version: app.getVersion(),
    platform: process.platform,
    canInstall: installer.isWindows,
    accounts: accounts(),
    activeId: store.get().activeId,
    loginPending: !!pendingLogin,
  };
}

function cancelLogin(removeRequest = true) {
  if (!pendingLogin) return;
  clearTimeout(pendingLogin.timer);
  if (removeRequest) rtdb.removeLoginRequest(pendingLogin.targetId, pendingLogin.requestId);
  pendingLogin = null;
}

function pollLogin(login) {
  login.timer = setTimeout(async () => {
    if (pendingLogin !== login) return;
    if (Date.now() > login.deadline) {
      cancelLogin();
      send('auth:result', { ok: false, error: 'Время ожидания истекло, попробуй ещё раз.' });
      return;
    }
    let status = null;
    try { status = await rtdb.getLoginRequestStatus(login.targetId, login.requestId); } catch (_) { /* сеть моргнула — спросим снова */ }
    if (pendingLogin !== login) return;
    if (status === 'approved') {
      cancelLogin(true); // заявка одноразовая — убираем за собой
      try {
        const profile = await rtdb.getProfile(login.targetId);
        if (profile.status === 'banned') throw new Error('Аккаунт заблокирован.');
        saveAccount(profile);
        send('auth:result', { ok: true });
      } catch (err) {
        send('auth:result', { ok: false, error: err.message || String(err) });
      }
    } else if (status === 'denied') {
      cancelLogin(true);
      send('auth:result', { ok: false, error: 'Вход отклонён на другом устройстве.' });
    } else {
      pollLogin(login);
    }
  }, POLL_MS);
}

ipcMain.handle('state:get', async () => {
  await refreshActiveProfile();
  return publicState();
});

ipcMain.handle('auth:start', async (_e, email) => {
  if (pendingLogin) cancelLogin();
  if (!rtdb.isValidEmail(email)) return { ok: false, error: 'Впиши email целиком.' };
  try {
    const targetId = await rtdb.findAccountByEmail(email);
    if (!targetId) return { ok: false, error: 'Эта почта не привязана ни к одному аккаунту. Привяжи её в игре: Настройки → Аккаунт.' };
    const requestId = await rtdb.createLoginRequest(targetId);
    pendingLogin = { targetId, requestId, timer: null, deadline: Date.now() + REQUEST_TTL_MS };
    pollLogin(pendingLogin);
    return { ok: true, expiresInMs: REQUEST_TTL_MS };
  } catch (err) {
    return { ok: false, error: 'Не удалось отправить заявку: ' + (err.message || err) };
  }
});

ipcMain.handle('auth:cancel', () => { cancelLogin(); return publicState(); });

ipcMain.handle('auth:switch', async (_e, id) => {
  if (!ACCOUNT_ID_RE.test(String(id)) || !accounts().some((a) => a.id === id)) return publicState();
  store.set({ activeId: id });
  await refreshActiveProfile();
  return publicState();
});

ipcMain.handle('auth:logout', (_e, id) => {
  // id не передан — выходим из текущего; иначе убираем указанный из списка
  const target = id || store.get().activeId;
  if (target && ACCOUNT_ID_RE.test(String(target))) {
    const left = accounts().filter((a) => a.id !== target);
    store.set({ accounts: left, activeId: store.get().activeId === target ? null : store.get().activeId });
  }
  return publicState();
});

ipcMain.handle('auth:add', () => { // «Добавить аккаунт» — просто показать вход, сессии не трогаем
  store.set({ activeId: null });
  return publicState();
});

// ---------- приложения ----------
async function describeApp(def, role, force) {
  const out = {
    id: def.id, name: def.name, desc: def.desc,
    allowed: isAllowed(def, role),
    installed: null, latest: null, updateAvailable: false, busy: busyApps.has(def.id), error: null,
  };
  const [found, release] = await Promise.allSettled([
    installer.findInstalled(def.productName),
    latestRelease(def.repo, { force }),
  ]);
  if (found.status === 'fulfilled' && found.value) out.installed = { version: found.value.version };
  if (release.status === 'fulfilled') {
    out.latest = { version: release.value.version };
  } else {
    out.error = 'Не удалось узнать последнюю версию (нет связи с GitHub?)';
  }
  if (out.installed && out.latest && out.installed.version) {
    out.updateAvailable = compareVersions(out.latest.version, out.installed.version) > 0;
  }
  return out;
}

ipcMain.handle('apps:list', async (_e, force) => {
  const acc = activeAccount();
  if (!acc) return [];
  return Promise.all(APPS.map((def) => describeApp(def, acc.role, !!force)));
});

// Роль проверяем заново прямо перед действием, а не по тому, что нарисовано:
// сохранённая в файле роль могла устареть.
async function requireAllowed(def) {
  const acc = activeAccount();
  if (!acc) throw new Error('Сначала войди в аккаунт.');
  await refreshActiveProfile();
  const fresh = activeAccount();
  if (!fresh) throw new Error('Аккаунт больше недоступен — войди заново.');
  if (!isAllowed(def, fresh.role)) throw new Error('Для этого приложения нет доступа у твоего аккаунта.');
}

ipcMain.handle('apps:install', async (_e, id) => {
  const def = byId(id);
  if (!def) return { ok: false, error: 'Неизвестное приложение' };
  if (busyApps.has(id)) return { ok: false, error: 'Уже выполняется' };
  busyApps.add(id);
  send('apps:progress', { id, phase: 'download', percent: 0 });
  try {
    await requireAllowed(def);
    const release = await latestRelease(def.repo, { force: true });
    await installer.install(def, release, (percent) => {
      send('apps:progress', percent === null ? { id, phase: 'install' } : { id, phase: 'download', percent });
    });
    return { ok: true };
  } catch (err) {
    return { ok: false, error: err.message || String(err) };
  } finally {
    busyApps.delete(id);
    send('apps:progress', { id, phase: 'done' });
  }
});

ipcMain.handle('apps:launch', async (_e, id) => {
  const def = byId(id);
  if (!def) return { ok: false, error: 'Неизвестное приложение' };
  try {
    await requireAllowed(def);
    const found = await installer.findInstalled(def.productName);
    if (!found) return { ok: false, error: 'Приложение не установлено' };
    installer.launch(found.exe);
    return { ok: true };
  } catch (err) {
    return { ok: false, error: err.message || String(err) };
  }
});

ipcMain.handle('apps:uninstall', async (_e, id) => {
  const def = byId(id);
  if (!def) return { ok: false, error: 'Неизвестное приложение' };
  if (busyApps.has(id)) return { ok: false, error: 'Уже выполняется' };
  busyApps.add(id);
  send('apps:progress', { id, phase: 'install' });
  try {
    const found = await installer.findInstalled(def.productName);
    if (!found) return { ok: true };
    await installer.uninstall(found);
    return { ok: true };
  } catch (err) {
    return { ok: false, error: err.message || String(err) };
  } finally {
    busyApps.delete(id);
    send('apps:progress', { id, phase: 'done' });
  }
});

ipcMain.handle('shell:openIssues', () => shell.openExternal('https://github.com/botsystemtioxsit/launcher/releases'));

// ---------- окно ----------
function createWindow() {
  win = new BrowserWindow({
    width: 980,
    height: 680,
    minWidth: 720,
    minHeight: 520,
    title: 'БАТТЛ Лаунчер',
    icon: path.join(__dirname, 'build', 'icon.png'),
    backgroundColor: '#0b0b0f', // без этого при запуске на миг мелькает белый экран
    autoHideMenuBar: true,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  });
  win.loadFile(path.join(__dirname, 'renderer', 'index.html'));
  // Окно показывает только свои файлы: любые переходы и новые окна — в обычный браузер или запрет.
  win.webContents.on('will-navigate', (e) => e.preventDefault());
  win.webContents.setWindowOpenHandler(({ url }) => {
    if (/^https:\/\/github\.com\/botsystemtioxsit\//.test(url)) shell.openExternal(url);
    return { action: 'deny' };
  });
}

// ---------- самообновление лаунчера ----------
function setupSelfUpdate() {
  if (!app.isPackaged) return;
  autoUpdater.autoDownload = true;
  autoUpdater.on('update-available', (i) => send('self:update', { phase: 'downloading', version: i.version }));
  autoUpdater.on('update-downloaded', (i) => send('self:update', { phase: 'ready', version: i.version }));
  autoUpdater.on('error', (err) => console.error('Автообновление лаунчера:', err));
  const check = () => autoUpdater.checkForUpdates().catch((err) => console.error('Проверка обновления лаунчера:', err));
  check();
  setInterval(check, 60 * 60 * 1000);
}
ipcMain.handle('self:install', () => { autoUpdater.quitAndInstall(); });

// ---------- запуск ----------
if (!app.requestSingleInstanceLock()) {
  app.quit();
} else {
  app.on('second-instance', () => {
    if (win) { if (win.isMinimized()) win.restore(); win.focus(); }
  });
  app.whenReady().then(() => {
    Menu.setApplicationMenu(null);
    store = createStore(app.getPath('userData'), 'launcher.json', { accounts: [], activeId: null });
    // файл могли править руками — выбрасываем всё, что не похоже на наш формат
    store.set({
      accounts: (store.get().accounts || []).filter((a) => a && ACCOUNT_ID_RE.test(String(a.id))).slice(0, MAX_ACCOUNTS)
        .map((a) => ({ id: String(a.id), name: String(a.name || '').slice(0, 40), role: String(a.role || 'user').slice(0, 20), playerId: String(a.playerId || '').slice(0, 24), addedAt: Number(a.addedAt) || 0 })),
    });
    createWindow();
    setupSelfUpdate();
    app.on('activate', () => { if (BrowserWindow.getAllWindows().length === 0) createWindow(); });
  });
  app.on('window-all-closed', () => { cancelLogin(); app.quit(); });
}
