// Окно Mask Plus. Ничего не применяет само — пишет настройки в chrome.storage, фон (background.js) их подхватывает.
const DEFAULTS = { enabled: true, cc: 'DE', tz: true, lang: true, geo: true, webrtc: true, ua: false, os: 'win11', br: 'chrome', excluded: [] };
const $ = id => document.getElementById(id);
let state = Object.assign({}, DEFAULTS);
let tab = null;      // активная вкладка
let host = '';       // сайт активной вкладки (только http/https)

const country = () => MASK_COUNTRIES.find(c => c.cc === state.cc) || MASK_COUNTRIES[0];

// ---------- язык окна (русский / English / Deutsch) ----------
// Русские строки остаются в разметке и в коде, а перевод — словарь «русская строка → [English, Deutsch]» (как в игре и в лаунчере).
const I18N = {
  'создано нейросетью': ['made by AI', 'von einer KI erstellt'],
  'Включить или выключить маскировку': ['Turn the masking on or off', 'Maskierung ein- oder ausschalten'],
  'Страна, под которую маскируемся': ['The country to mask as', 'Land, als das du dich ausgibst'],
  'Что подменять': ['What to fake', 'Was gefälscht wird'],
  'Время и часовой пояс': ['Time and time zone', 'Uhrzeit und Zeitzone'],
  'Язык браузера и сайтов': ['Browser and site language', 'Sprache von Browser und Webseiten'],
  'Местоположение (GPS)': ['Location (GPS)', 'Standort (GPS)'],
  'Защита адреса от утечки через WebRTC': ['Protect the address from leaking through WebRTC', 'Adresse vor Lecks über WebRTC schützen'],
  'Защиту WebRTC держит другое расширение — здесь она не включится.': ['Another extension controls WebRTC protection — it will not turn on here.', 'Der WebRTC-Schutz wird von einer anderen Erweiterung verwaltet – hier lässt er sich nicht einschalten.'],
  'Система и браузер': ['System and browser', 'System und Browser'],
  'Подменять систему и браузер': ['Fake the system and the browser', 'System und Browser fälschen'],
  'Операционная система': ['Operating system', 'Betriebssystem'],
  'Браузер': ['Browser', 'Browser'],
  'Сайты могут заметить расхождение (шрифты, экран). Если сайт плохо работает — выключите.': ['Sites may notice a mismatch (fonts, screen). If a site works badly — turn it off.', 'Webseiten können eine Abweichung bemerken (Schriften, Bildschirm). Funktioniert eine Seite schlecht, schalte es aus.'],
  'Этот сайт': ['This site', 'Diese Webseite'],
  'Что видит эта вкладка': ['What this tab sees', 'Was dieser Tab sieht'],
  'Обновить вкладку': ['Reload the tab', 'Tab neu laden'],
  'IP-адрес это расширение не меняет.': ['This extension does not change the IP address.', 'Diese Erweiterung ändert die IP-Adresse nicht.'],
  'Сайт видит адрес, с которого пришёл запрос; чтобы он был из нужной страны, включите шлюз (Gateway Plus) на сервере в этой стране. После смены страны обновите открытые вкладки.': ['A site sees the address the request came from; for it to be from the country you need, turn on the gateway (Gateway Plus) on a server in that country. After changing the country, reload the open tabs.', 'Eine Webseite sieht die Adresse, von der die Anfrage kam; damit sie aus dem gewünschten Land stammt, schalte das Gateway (Gateway Plus) auf einem Server in diesem Land ein. Lade nach einem Ländertausch die geöffneten Tabs neu.'],
  'Страна': ['Country', 'Land'],
  'Пояс': ['Time zone', 'Zeitzone'],
  'Язык': ['Language', 'Sprache'],
  'Система': ['System', 'System'],
  'сейчас': ['now', 'jetzt'],
  'здесь маскировка не применяется (служебная страница)': ['masking is not applied here (a service page)', 'hier wird nicht maskiert (Dienstseite)'],
  'Снова маскировать этот сайт': ['Mask this site again', 'Diese Webseite wieder maskieren'],
  'Не маскировать этот сайт': ['Do not mask this site', 'Diese Webseite nicht maskieren'],
  'откройте обычный сайт — тогда здесь будет видно, что он о вас узнаёт': ['open an ordinary site — then you will see here what it learns about you', 'öffne eine normale Webseite – dann siehst du hier, was sie über dich erfährt'],
  'эту вкладку проверить нельзя — обновите её или откройте другую': ['this tab cannot be checked — reload it or open another one', 'dieser Tab lässt sich nicht prüfen – lade ihn neu oder öffne einen anderen'],
  'Вкладка открыта до последней смены настроек.': ['The tab was opened before the last settings change.', 'Der Tab wurde vor der letzten Änderung der Einstellungen geöffnet.'],
  'Для этого сайта маскировка выключена.': ['Masking is turned off for this site.', 'Für diese Webseite ist die Maskierung ausgeschaltet.']
};
let UI = 'ru';
function detectUI() {
  let l = 'ru';
  try { l = (chrome.i18n && chrome.i18n.getUILanguage ? chrome.i18n.getUILanguage() : navigator.language || 'ru'); } catch (e) { /* нет */ }
  l = String(l).slice(0, 2).toLowerCase();
  return ['ru', 'uk', 'be', 'kk'].includes(l) ? 'ru' : l === 'de' ? 'de' : 'en';
}
const norm = s => s.replace(/\s+/g, ' ').trim();
function t(ru) { if (UI === 'ru') return ru; const e = I18N[norm(ru)]; return e ? e[UI === 'en' ? 0 : 1] : ru; }
const cname = c => UI === 'en' ? c.name_en : UI === 'de' ? c.name_de : c.name;
const ccity = c => UI === 'en' ? c.city_en : UI === 'de' ? c.city_de : c.city;
const orig = new WeakMap();   // исходный русский текст узлов и атрибутов, чтобы переключать язык туда и обратно
function translateStatic() {
  const w = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
  for (let n = w.nextNode(); n; n = w.nextNode()) {
    if (!orig.has(n)) { if (!/[А-Яа-яЁё]/.test(n.nodeValue)) continue; orig.set(n, n.nodeValue); }
    const src = orig.get(n);
    const lead = src.match(/^\s*/)[0], trail = src.match(/\s*$/)[0];
    n.nodeValue = UI === 'ru' ? src : lead + t(src) + trail;
  }
  for (const e of document.querySelectorAll('[title],[aria-label]')) {
    for (const a of ['title', 'aria-label']) {
      if (!e.hasAttribute(a)) continue;
      const key = a + '@';
      const m = orig.get(e) || {};
      if (!(key in m)) { if (!/[А-Яа-яЁё]/.test(e.getAttribute(a))) continue; m[key] = e.getAttribute(a); orig.set(e, m); }
      e.setAttribute(a, UI === 'ru' ? m[key] : t(m[key]));
    }
  }
  document.documentElement.lang = UI;
}
function fillCountries() {
  const sel = $('country'), cur = sel.value || state.cc;
  sel.replaceChildren();
  const sorted = MASK_COUNTRIES.slice().sort((a, b) => a.cc === 'DE' ? -1 : b.cc === 'DE' ? 1 : cname(a).localeCompare(cname(b), UI));
  for (const c of sorted) {
    const o = el('option', '', cname(c) + ' · ' + c.cc);
    o.value = c.cc;
    sel.append(o);
  }
  sel.value = cur;
}
function renderLangButtons() {
  const box = $('uilang');
  box.replaceChildren();
  for (const code of ['ru', 'en', 'de']) {
    const b = el('button', code === UI ? 'on' : '', code.toUpperCase());
    b.type = 'button';
    b.title = code === 'ru' ? 'Русский' : code === 'en' ? 'English' : 'Deutsch';
    b.addEventListener('click', async () => {
      UI = code;
      try { localStorage.setItem('maskUiLang', code); } catch (e) { /* нет хранилища */ }
      try { await chrome.storage.local.set({ uiLang: code }); } catch (e) { /* ничего */ }
      applyLang();
    });
    box.append(b);
  }
}
function applyLang() {
  translateStatic();
  fillCountries();
  renderLangButtons();
  render();
  checkTab();
}

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
    [t('Страна'), cname(c) + ' (' + c.cc + ') · ' + ccity(c)],
    [t('Пояс'), c.tz + ' · ' + t('сейчас') + ' ' + clockIn(c.tz, c.locale)],
    [t('Язык'), c.langs.join(', ')]
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
  if (!host) { $('site').textContent = t('здесь маскировка не применяется (служебная страница)'); btn.hidden = true; return; }
  const off = state.excluded.includes(host);
  $('site').textContent = host;
  btn.hidden = false;
  btn.textContent = off ? t('Снова маскировать этот сайт') : t('Не маскировать этот сайт');
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
  if (!tab || !host) { box.append(el('span', 'dim', t('откройте обычный сайт — тогда здесь будет видно, что он о вас узнаёт'))); return; }
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
  if (!r) { box.append(el('span', 'dim', t('эту вкладку проверить нельзя — обновите её или откройте другую'))); return; }
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
  line(t('Пояс'), r.tz + ' (UTC' + sign + hh + (mm ? ':' + String(mm).padStart(2, '0') : '') + ')', tzOk);
  line(t('Язык'), r.lang, langOk);
  line(t('Система'), osOf(r.ua), uaOk);
  line(t('Браузер'), brOf(r.ua), uaOk);
  if (!tzOk || !langOk || !uaOk) {
    box.append(el('div', 'dim', t('Вкладка открыта до последней смены настроек.')));
    reload.hidden = false;
  } else if (!want) {
    box.append(el('div', 'dim', t('Для этого сайта маскировка выключена.')));
  }
}

async function init() {
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

  // язык окна: выбор человека (кнопки RU/EN/DE), иначе — по языку браузера
  try { const saved = localStorage.getItem('maskUiLang'); UI = ['ru', 'en', 'de'].includes(saved) ? saved : detectUI(); } catch (e) { UI = detectUI(); }
  applyLang();

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
