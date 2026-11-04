// Окно Mask Plus. Ничего не применяет само — пишет настройки в chrome.storage, фон (background.js) их подхватывает.
const DEFAULTS = { enabled: true, cc: 'DE', tz: true, lang: true, geo: true, webrtc: true, ua: false, os: 'win11', br: 'chrome', excluded: [] };
const $ = id => document.getElementById(id);
let state = Object.assign({}, DEFAULTS);
let tab = null;      // активная вкладка
let host = '';       // сайт активной вкладки (только http/https)

const country = () => MASK_COUNTRIES.find(c => c.cc === state.cc) || MASK_COUNTRIES[0];

function clockIn(tz, locale) {
  try { return new Intl.DateTimeFormat(locale, { timeZone: tz, hour: '2-digit', minute: '2-digit', weekday: 'short' }).format(new Date()); }
  catch (e) { return ''; }
}

function el(tag, cls, text) {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  if (text !== undefined) e.textContent = text;
  return e;
}

function renderShows() {
  const c = country(), box = $('shows');
  box.replaceChildren();
  const rows = [
    ['Страна', c.name + ' (' + c.cc + ') · ' + c.city],
    ['Пояс', c.tz + ' · сейчас ' + clockIn(c.tz, c.locale)],
    ['Язык', c.langs.join(', ')]
  ];
  for (const [k, v] of rows) {
    const line = el('div');
    line.append(k + ': ', el('b', '', v));
    box.append(line);
  }
}

// настоящий браузер (это окно — страница расширения, подмена на неё не ставится) → то, что увидят сайты при выбранных ОС и браузере
function realBrowser() {
  let brands = null;
  try { const b = navigator.userAgentData && navigator.userAgentData.brands; if (b) brands = b.map(x => ({ brand: x.brand, version: x.version })); } catch (e) { /* нет */ }
  return { ua: navigator.userAgent, brands };
}
const wantUA = () => MaskUA.build(state.os, state.br, realBrowser());
const osOf = ua => /Windows NT/.test(ua) ? 'Windows' : /Macintosh/.test(ua) ? 'macOS' : /Android/.test(ua) ? 'Android' : /Linux|X11/.test(ua) ? 'Linux' : '?';
function brOf(ua) {
  let m;
  if ((m = /Edg\/(\d+)/.exec(ua))) return 'Edge ' + m[1];
  if ((m = /Firefox\/(\d+)/.exec(ua))) return 'Firefox ' + m[1];
  if ((m = /Chrome\/(\d+)/.exec(ua))) return 'Chrome ' + m[1];
  if ((m = /Version\/([\d.]+).*Safari/.exec(ua))) return 'Safari ' + m[1];
  return '?';
}

function renderUA() {
  const off = !state.enabled || !state.ua;
  $('ua').checked = state.ua;
  $('ua').disabled = !state.enabled;
  $('os').value = state.br === 'safari' ? 'mac' : state.os;
  $('br').value = state.br;
  $('os').disabled = off || state.br === 'safari';
  $('br').disabled = off;
  $('uaWarn').hidden = off;
  $('uaStr').textContent = state.ua ? wantUA().ua : '';
}

function renderSite() {
  const btn = $('exclude');
  if (!host) { $('site').textContent = 'здесь маскировка не применяется (служебная страница)'; btn.hidden = true; return; }
  const off = state.excluded.includes(host);
  $('site').textContent = host;
  btn.hidden = false;
  btn.textContent = off ? 'Снова маскировать этот сайт' : 'Не маскировать этот сайт';
}

function render() {
  $('enabled').checked = state.enabled;
  document.body.classList.toggle('off', !state.enabled);
  for (const k of ['tz', 'lang', 'geo', 'webrtc']) { $(k).checked = state[k]; $(k).disabled = !state.enabled; }
  $('country').value = state.cc;
  $('country').disabled = !state.enabled;
  renderUA();
  renderShows();
  renderSite();
}

async function save(patch) {
  Object.assign(state, patch);
  render();
  await chrome.storage.local.set(patch);
  setTimeout(checkTab, 400);
}

// что страница видит на самом деле — спрашиваем у неё самой (в её собственном мире, как увидел бы любой сайт)
async function checkTab() {
  const box = $('seen'), reload = $('reload');
  box.replaceChildren();
  reload.hidden = true;
  if (!tab || !host) { box.append(el('span', 'dim', 'откройте обычный сайт — тогда здесь будет видно, что он о вас узнаёт')); return; }
  let r;
  try {
    const res = await chrome.scripting.executeScript({
      target: { tabId: tab.id }, world: 'MAIN',
      func: () => {
        const t = new Date(Date.UTC(2024, 6, 1, 12, 0, 0));
        return { ua: navigator.userAgent, tz: Intl.DateTimeFormat().resolvedOptions().timeZone, lang: navigator.language, langs: navigator.languages.join(', '),
          offset: new Date().getTimezoneOffset(), noon: t.getHours() };
      }
    });
    r = res && res[0] && res[0].result;
  } catch (e) { r = null; }
  if (!r) { box.append(el('span', 'dim', 'эту вкладку проверить нельзя — обновите её или откройте другую')); return; }
  // это окно — страница расширения, на неё подмена не ставится: здесь видно настоящие значения
  const realTz = Intl.DateTimeFormat().resolvedOptions().timeZone, realLang = navigator.language;
  const c = country();
  const want = state.enabled && !state.excluded.includes(host);
  const tzOk = r.tz === (want && state.tz ? c.tz : realTz);
  const langOk = r.lang === (want && state.lang ? c.locale : realLang);
  const uaOk = r.ua === (want && state.ua ? wantUA().ua : navigator.userAgent);
  const line = (label, value, ok) => {
    const d = el('div');
    d.append(label + ': ', el('span', ok ? 'ok' : 'bad', value));
    box.append(d);
  };
  const m = Math.round(-r.offset), sign = m >= 0 ? '+' : '−', hh = Math.floor(Math.abs(m) / 60), mm = Math.abs(m) % 60;
  line('Пояс', r.tz + ' (UTC' + sign + hh + (mm ? ':' + String(mm).padStart(2, '0') : '') + ')', tzOk);
  line('Язык', r.lang, langOk);
  line('Система', osOf(r.ua), uaOk);
  line('Браузер', brOf(r.ua), uaOk);
  if (!tzOk || !langOk || !uaOk) {
    box.append(el('div', 'dim', 'Вкладка открыта до последней смены настроек.'));
    reload.hidden = false;
  } else if (!want) {
    box.append(el('div', 'dim', 'Для этого сайта маскировка выключена.'));
  }
}

async function init() {
  const sorted = MASK_COUNTRIES.slice().sort((a, b) => a.cc === 'DE' ? -1 : b.cc === 'DE' ? 1 : a.name.localeCompare(b.name, 'ru'));
  for (const c of sorted) {
    const o = el('option', '', c.name + ' · ' + c.cc);
    o.value = c.cc;
    $('country').append(o);
  }
  state = Object.assign({}, DEFAULTS, await chrome.storage.local.get(Object.keys(DEFAULTS)));
  if (!Array.isArray(state.excluded)) state.excluded = [];
  if (!['win11', 'win10', 'mac', 'linux'].includes(state.os)) state.os = DEFAULTS.os;
  if (!['chrome', 'edge', 'firefox', 'safari'].includes(state.br)) state.br = DEFAULTS.br;
  if (state.br === 'safari') state.os = 'mac';

  try {
    [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    const u = new URL(tab && tab.url ? tab.url : '');
    if (u.protocol === 'http:' || u.protocol === 'https:') host = u.hostname.toLowerCase().replace(/^www\./, '');
  } catch (e) { host = ''; }

  try {
    const d = await chrome.privacy.network.webRTCIPHandlingPolicy.get({});
    $('webrtcWarn').hidden = !(d.levelOfControl === 'controlled_by_other_extensions' || d.levelOfControl === 'not_controllable');
  } catch (e) { /* нет права — просто не показываем */ }

  render();
  checkTab();

  $('enabled').addEventListener('change', e => save({ enabled: e.target.checked }));
  $('country').addEventListener('change', e => save({ cc: e.target.value }));
  for (const k of ['tz', 'lang', 'geo', 'webrtc']) $(k).addEventListener('change', e => save({ [k]: e.target.checked }));
  $('ua').addEventListener('change', e => save({ ua: e.target.checked }));
  $('os').addEventListener('change', e => save({ os: e.target.value }));
  $('br').addEventListener('change', e => save(e.target.value === 'safari' ? { br: 'safari', os: 'mac' } : { br: e.target.value }));
  $('exclude').addEventListener('click', () => {
    const list = state.excluded.includes(host) ? state.excluded.filter(h => h !== host) : state.excluded.concat(host);
    save({ excluded: list });
  });
  $('reload').addEventListener('click', async () => { if (tab) { await chrome.tabs.reload(tab.id); window.close(); } });
}
init();
