// Mask Plus — фоновая часть (service worker). Следит за настройками и приводит браузер в соответствие:
//   1) подключает на страницы движок подмены (страна + флаги + engine.js) — до скриптов самой страницы;
//   2) ставит заголовок Accept-Language у всех запросов, а при включённой подмене системы и браузера — User-Agent и Client Hints (sec-ch-ua…);
//   3) включает защиту WebRTC от утечки адресов;
//   4) рисует на значке код страны.
// Само окно (popup) только пишет настройки в chrome.storage — отсюда всё подхватывается.

const DEFAULTS = { enabled: true, cc: 'DE', tz: true, lang: true, geo: true, webrtc: true, ua: false, os: 'win11', br: 'chrome', excluded: [] };
const OS_LIST = ['win11', 'win10', 'mac', 'linux'];
const BR_LIST = ['chrome', 'edge', 'firefox', 'safari'];
const SCRIPT_ID = 'mask-plus';
const RULE_ID = 1;      // Accept-Language
const UA_RULE_ID = 2;   // User-Agent и Client Hints
const TYPES = ['main_frame', 'sub_frame', 'stylesheet', 'script', 'image', 'font', 'object', 'xmlhttprequest', 'ping', 'csp_report', 'media', 'websocket', 'other'];

importScripts('countries.js', 'ua.js');

async function load() {
  const s = Object.assign({}, DEFAULTS, await chrome.storage.local.get(Object.keys(DEFAULTS)));
  if (!MASK_COUNTRIES.some(c => c.cc === s.cc)) s.cc = DEFAULTS.cc;
  s.ua = s.ua === true;
  if (!OS_LIST.includes(s.os)) s.os = DEFAULTS.os;
  if (!BR_LIST.includes(s.br)) s.br = DEFAULTS.br;
  if (s.br === 'safari') s.os = 'mac';   // Safari бывает только на macOS
  s.excluded = (Array.isArray(s.excluded) ? s.excluded : []).filter(h => typeof h === 'string' && /^[a-z0-9.-]{1,253}$/.test(h));
  return s;
}

// Chrome шлёт «de-DE,de;q=0.9,en-US;q=0.8,en;q=0.7» — делаем так же
function acceptLanguage(langs) {
  return langs.map((l, i) => i === 0 ? l : l + ';q=' + Math.max(0.1, 1 - i * 0.1).toFixed(1)).join(',');
}

function pattern(host) {
  return /^[\d.]+$/.test(host) || !host.includes('.') ? `*://${host}/*` : `*://*.${host}/*`;
}

async function applyScripts(s, country) {
  try { await chrome.scripting.unregisterContentScripts({ ids: [SCRIPT_ID] }); } catch (e) { /* ещё не было */ }
  if (!s.enabled || !(s.tz || s.lang || s.geo || s.ua)) return;
  const flags = (s.tz ? '1' : '0') + (s.lang ? '1' : '0') + (s.geo ? '1' : '0');
  const script = {
    id: SCRIPT_ID,
    matches: ['<all_urls>'],
    js: [`c/${country.cc}.js`, `f/${flags}.js`].concat(s.ua ? [`u/${s.os}-${s.br}.js`, 'ua.js'] : [], ['engine.js']),
    runAt: 'document_start',
    world: 'MAIN',
    allFrames: true,
    matchOriginAsFallback: true,
    persistAcrossSessions: true
  };
  if (s.excluded.length) script.excludeMatches = s.excluded.map(pattern);
  await chrome.scripting.registerContentScripts([script]);
}

// «Паспорт» настоящего браузера — из него собираются подменённые заголовки (тот же код, что и в движке на странице)
function realBrowser() {
  let brands = null;
  try { const b = navigator.userAgentData && navigator.userAgentData.brands; if (b) brands = b.map(x => ({ brand: x.brand, version: x.version })); } catch (e) { /* нет — возьмём стандартные */ }
  return { ua: navigator.userAgent, brands };
}

async function applyHeaders(s, country) {
  const remove = [RULE_ID, UA_RULE_ID];
  if (!s.enabled || !(s.lang || s.ua)) { await chrome.declarativeNetRequest.updateDynamicRules({ removeRuleIds: remove }); return; }
  const condition = { resourceTypes: TYPES };
  if (s.excluded.length) { condition.excludedRequestDomains = s.excluded; condition.excludedInitiatorDomains = s.excluded; }
  const addRules = [];
  if (s.lang) {
    addRules.push({
      id: RULE_ID, priority: 1,
      action: { type: 'modifyHeaders', requestHeaders: [{ header: 'Accept-Language', operation: 'set', value: acceptLanguage(country.langs) }] },
      condition
    });
  }
  if (s.ua) {
    const U = MaskUA.build(s.os, s.br, realBrowser());
    const headers = [{ header: 'User-Agent', operation: 'set', value: U.ua }];
    // Chrome и Edge шлют краткие подсказки (sec-ch-ua…) — ставим те же значения, что видны странице; Firefox и Safari их не шлют — убираем
    if (U.hasUAD) {
      headers.push({ header: 'sec-ch-ua', operation: 'set', value: U.chUa });
      headers.push({ header: 'sec-ch-ua-mobile', operation: 'set', value: '?0' });
      headers.push({ header: 'sec-ch-ua-platform', operation: 'set', value: U.chPlatform });
    }
    for (const h of U.strip) headers.push({ header: h, operation: 'remove' });
    addRules.push({ id: UA_RULE_ID, priority: 1, action: { type: 'modifyHeaders', requestHeaders: headers }, condition });
  }
  await chrome.declarativeNetRequest.updateDynamicRules({ removeRuleIds: remove, addRules });
}

async function applyWebrtc(s) {
  const api = chrome.privacy && chrome.privacy.network && chrome.privacy.network.webRTCIPHandlingPolicy;
  if (!api) return;
  // «только основной выход в интернет»: не светит адрес из домашней сети и адреса других подключений (в том числе настоящий, если включён шлюз)
  if (s.enabled && s.webrtc) await api.set({ value: 'default_public_interface_only' });
  else await api.clear({});
}

async function applyBadge(s) {
  await chrome.action.setBadgeBackgroundColor({ color: '#FF2A9D' });
  if (chrome.action.setBadgeTextColor) await chrome.action.setBadgeTextColor({ color: '#06050D' });
  await chrome.action.setBadgeText({ text: s.enabled ? s.cc : '' });
  await chrome.action.setTitle({ title: s.enabled ? 'Mask Plus — сейчас: ' + s.cc : 'Mask Plus — выключено' });
}

let chain = Promise.resolve();
function apply() {
  chain = chain.then(async () => {
    const s = await load();
    const country = MASK_COUNTRIES.find(c => c.cc === s.cc);
    for (const step of [applyScripts, applyHeaders, applyWebrtc, applyBadge]) {
      try { await step(s, country); } catch (e) { console.warn('Mask Plus:', step.name, e && e.message); }
    }
  });
  return chain;
}

chrome.runtime.onInstalled.addListener(async () => {
  const have = await chrome.storage.local.get(Object.keys(DEFAULTS));
  const fill = {};
  for (const k of Object.keys(DEFAULTS)) if (have[k] === undefined) fill[k] = DEFAULTS[k];
  if (Object.keys(fill).length) await chrome.storage.local.set(fill);
  await apply();
});
chrome.runtime.onStartup.addListener(apply);
chrome.storage.onChanged.addListener((changes, area) => { if (area === 'local') apply(); });
