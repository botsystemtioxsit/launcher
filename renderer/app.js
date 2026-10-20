// Интерфейс лаунчера. Всё, что приходит из базы или GitHub, выводится только
// через textContent — база открыта на запись, чужой HTML сюда попасть не должен.
const L = window.launcher;
const $ = (id) => document.getElementById(id);
const state = { s: null, apps: [], progress: {} };

const ROLE_NAMES = { user: 'Игрок', moderator: 'Модератор', admin: 'Админ', superadmin: 'Суперадмин' };
const roleName = (r) => ROLE_NAMES[r] || r;
const initials = (a) => (a.name || a.playerId || '??').trim().slice(0, 2).toUpperCase();

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text !== undefined) n.textContent = text;
  return n;
}

let toastTimer = null;
function toast(msg) {
  const t = $('toast');
  t.textContent = msg;
  t.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { t.hidden = true; }, 5000);
}

function accountRow(a, { active, removable, onPick }) {
  const row = el('div', 'acc' + (active ? ' active' : ''));
  row.append(el('div', 'av', initials(a)));
  const meta = el('div', 'meta');
  meta.append(el('b', '', a.name || ('Аккаунт ' + a.id)), el('span', '', roleName(a.role) + (a.playerId ? ' · ' + a.playerId : '')));
  row.append(meta);
  if (removable) {
    const x = el('button', 'x', '×');
    x.title = 'Убрать из списка';
    x.addEventListener('click', async (e) => { e.stopPropagation(); state.s = await L.logout(a.id); render(); });
    row.append(x);
  }
  if (onPick) row.addEventListener('click', onPick);
  return row;
}

// ---------- экраны ----------
function showView(name) {
  $('view-library').hidden = name !== 'library';
  $('view-account').hidden = name !== 'account';
  document.querySelectorAll('.nav').forEach((n) => n.classList.toggle('active', n.dataset.view === name));
  if (name === 'library') loadApps(false);
}

function render() {
  const s = state.s;
  const me = s.accounts.find((a) => a.id === s.activeId);
  $('screen-login').hidden = !!me;
  $('screen-app').hidden = !me;
  $('version').textContent = 'Версия ' + s.version;
  if (!me) return renderLogin();
  $('lib-sub').textContent = 'Ты вошёл как ' + (me.name || me.playerId || me.id) + ' · ' + roleName(me.role);
  $('no-install').hidden = s.canInstall;
  const meBox = $('me');
  meBox.replaceChildren(el('div', 'av', initials(me)));
  const info = el('div');
  info.append(el('b', '', me.name || ('Аккаунт ' + me.id)), el('div', 'muted', roleName(me.role) + (me.playerId ? ' · ' + me.playerId : '')));
  meBox.append(info);
  const list = $('all-accounts');
  list.replaceChildren(...s.accounts.map((a) => accountRow(a, {
    active: a.id === s.activeId,
    removable: a.id !== s.activeId,
    onPick: async () => { if (a.id !== s.activeId) { state.s = await L.switchAccount(a.id); render(); showView('library'); } },
  })));
}

function renderLogin() {
  const saved = $('saved-accounts');
  const list = state.s.accounts;
  saved.hidden = list.length === 0;
  saved.replaceChildren(...list.map((a) => accountRow(a, {
    active: false, removable: true,
    onPick: async () => { state.s = await L.switchAccount(a.id); render(); showView('library'); },
  })));
}

// ---------- библиотека ----------
async function loadApps(force) {
  $('refresh-btn').disabled = true;
  try {
    state.apps = await L.listApps(force);
  } catch (err) {
    toast('Не удалось обновить список: ' + (err.message || err));
  }
  $('refresh-btn').disabled = false;
  renderApps();
}

function renderApps() {
  const box = $('app-list');
  box.replaceChildren(...state.apps.map(appCard));
}

function appCard(a) {
  const card = el('div', 'card app');
  const info = el('div');
  info.append(el('h3', '', a.name), el('p', 'muted', a.desc));
  const badges = el('div');
  if (!a.allowed) badges.append(el('span', 'badge', 'Нет доступа для твоей роли'));
  else {
    if (a.installed) badges.append(el('span', 'badge ok', 'Установлено' + (a.installed.version ? ' ' + a.installed.version : '')));
    else badges.append(el('span', 'badge', 'Не установлено'));
    if (a.latest) badges.append(el('span', 'badge' + (a.updateAvailable ? ' upd' : ''), (a.updateAvailable ? 'Доступно ' : 'Последняя ') + a.latest.version));
  }
  info.append(badges);
  if (a.error) info.append(el('p', 'error', a.error));
  card.append(info);

  const actions = el('div', 'actions');
  const prog = state.progress[a.id];
  if (prog) {
    const bar = el('div', 'bar' + (prog.phase === 'install' || prog.percent == null ? ' indeterminate' : ''));
    const fill = el('i');
    if (prog.phase === 'download') fill.style.width = (prog.percent || 0) + '%';
    bar.append(fill);
    actions.append(el('span', 'muted', prog.phase === 'install' ? 'Работаю…' : 'Скачиваю ' + (prog.percent || 0) + '%'), bar);
  } else if (a.allowed) {
    const canInstall = state.s.canInstall;
    if (a.installed) {
      const play = el('button', 'btn primary', 'Запустить');
      play.addEventListener('click', () => act(a, () => L.launch(a.id), false));
      actions.append(play);
      if (a.updateAvailable && canInstall) {
        const up = el('button', 'btn', 'Обновить');
        up.addEventListener('click', () => act(a, () => L.install(a.id), true, 'Обновлено'));
        actions.append(up);
      }
      if (canInstall) {
        const rm = el('button', 'btn ghost danger', 'Удалить');
        rm.addEventListener('click', () => {
          if (window.confirm('Удалить «' + a.name + '» с этого компьютера?')) act(a, () => L.uninstall(a.id), true, 'Удалено');
        });
        actions.append(rm);
      }
    } else {
      const inst = el('button', 'btn primary', 'Установить');
      inst.disabled = !canInstall || !a.latest;
      inst.addEventListener('click', () => act(a, () => L.install(a.id), true, 'Установлено'));
      actions.append(inst);
    }
  }
  card.append(actions);
  return card;
}

async function act(a, fn, reload, okText) {
  if (reload) { state.progress[a.id] = { phase: 'download', percent: 0 }; renderApps(); }
  const res = await fn();
  delete state.progress[a.id];
  if (!res.ok) toast(res.error || 'Не получилось');
  else if (okText) toast(okText + ': ' + a.name);
  if (reload) await loadApps(true); else renderApps();
}

L.onProgress((p) => {
  if (p.phase === 'done') delete state.progress[p.id];
  else state.progress[p.id] = p;
  renderApps();
});

// ---------- вход ----------
function setLoginBusy(busy) {
  $('login-btn').disabled = busy;
  $('login-email').disabled = busy;
  $('login-cancel').hidden = !busy;
  $('login-btn').textContent = busy ? 'Ждём подтверждения…' : 'Запросить вход';
}

$('login-form').addEventListener('submit', async (e) => {
  e.preventDefault();
  $('login-error').hidden = true;
  $('login-status').hidden = true;
  setLoginBusy(true);
  const res = await L.login($('login-email').value);
  if (!res.ok) {
    setLoginBusy(false);
    $('login-error').textContent = res.error;
    $('login-error').hidden = false;
    return;
  }
  $('login-status').textContent = '⏳ Заявка отправлена. Нажми «Разрешить» в сообщении Telegram-бота, в игре (Сообщество → Запросы) или в админ-панели (Аккаунт). Ждём 5 минут.';
  $('login-status').hidden = false;
});

$('login-cancel').addEventListener('click', async () => {
  await L.cancelLogin();
  setLoginBusy(false);
  $('login-status').hidden = true;
});

L.onAuthResult(async (res) => {
  setLoginBusy(false);
  $('login-status').hidden = true;
  if (!res.ok) {
    $('login-error').textContent = res.error;
    $('login-error').hidden = false;
    return;
  }
  $('login-email').value = '';
  state.s = await L.getState();
  render();
  showView('library');
});

// ---------- прочее ----------
document.querySelectorAll('.nav').forEach((n) => n.addEventListener('click', () => showView(n.dataset.view)));
$('refresh-btn').addEventListener('click', () => loadApps(true));
$('add-account').addEventListener('click', async () => { state.s = await L.addAccount(); render(); });
$('logout-btn').addEventListener('click', async () => { state.s = await L.logout(); render(); });

L.onSelfUpdate((u) => {
  $('update-bar').hidden = false;
  $('update-text').textContent = u.phase === 'ready' ? 'Лаунчер ' + u.version + ' готов к установке' : 'Скачиваю обновление лаунчера ' + u.version + '…';
  $('update-btn').hidden = u.phase !== 'ready';
});
$('update-btn').addEventListener('click', () => L.installSelfUpdate());

(async function init() {
  state.s = await L.getState();
  render();
  if (state.s.activeId) showView('library');
  // свежие версии приложений — раз в 30 минут, пока лаунчер открыт
  setInterval(() => { if (state.s.activeId && !$('view-library').hidden) loadApps(false); }, 30 * 60 * 1000);
})();
