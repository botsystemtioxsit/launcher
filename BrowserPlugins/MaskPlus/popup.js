// Окно Mask Plus. Ничего не применяет само — пишет настройки в chrome.storage, фон (background.js) их подхватывает.
const DEFAULTS = { enabled: true, cc: 'DE', tz: true, lang: true, geo: true, webrtc: true, excluded: [] };
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
        return { tz: Intl.DateTimeFormat().resolvedOptions().timeZone, lang: navigator.language, langs: navigator.languages.join(', '),
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
  const line = (label, value, ok) => {
    const d = el('div');
    d.append(label + ': ', el('span', ok ? 'ok' : 'bad', value));
    box.append(d);
  };
  const m = Math.round(-r.offset), sign = m >= 0 ? '+' : '−', hh = Math.floor(Math.abs(m) / 60), mm = Math.abs(m) % 60;
  line('Пояс', r.tz + ' (UTC' + sign + hh + (mm ? ':' + String(mm).padStart(2, '0') : '') + ')', tzOk);
  line('Язык', r.lang, langOk);
  if (!tzOk || !langOk) {
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
  $('exclude').addEventListener('click', () => {
    const list = state.excluded.includes(host) ? state.excluded.filter(h => h !== host) : state.excluded.concat(host);
    save({ excluded: list });
  });
  $('reload').addEventListener('click', async () => { if (tab) { await chrome.tabs.reload(tab.id); window.close(); } });
}
init();
