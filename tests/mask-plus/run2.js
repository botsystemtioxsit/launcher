// Запуск: cd tests/mask-plus && npm i playwright && node run2.js   (нужен Chromium с поддержкой расширений; Windows не нужен)
// Функциональные проверки Mask Plus: заголовок языка, геолокация, iframe, исключения, выключение, WebRTC, окно.
const { chromium } = require('playwright');
const http = require('http'), fs = require('fs'), os = require('os'), path = require('path');
const EXT = path.resolve(__dirname, '../../BrowserPlugins/MaskPlus');
let okN = 0, badN = 0;
const check = (name, cond, extra) => { (cond ? okN++ : badN++); console.log((cond ? 'ok   ' : 'ПЛОХО') + ' ' + name + (cond ? '' : '  → ' + extra)); };

const server = http.createServer((req, res) => {
  if (req.url.startsWith('/echo')) { res.setHeader('content-type', 'application/json'); res.setHeader('access-control-allow-origin', '*'); res.end(JSON.stringify({ al: req.headers['accept-language'] || null, ua: req.headers['user-agent'] || null, chua: req.headers['sec-ch-ua'] || null,
    chplat: req.headers['sec-ch-ua-platform'] || null, chpv: req.headers['sec-ch-ua-platform-version'] || null, charch: req.headers['sec-ch-ua-arch'] || null })); return; }
  res.setHeader('content-type', 'text/html; charset=utf-8');
  res.end('<!doctype html><title>t</title><body>test</body>');
});
const sleep = ms => new Promise(r => setTimeout(r, ms));
async function setState(worker, patch) {
  await worker.evaluate(p => chrome.storage.local.set(p), patch);
  await sleep(700);   // фон успевает перерегистрировать скрипты и правило
}
const tzOf = p => p.evaluate(() => Intl.DateTimeFormat().resolvedOptions().timeZone + '|' + navigator.language);

(async () => {
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const port = server.address().port, base = 'http://127.0.0.1:' + port, other = 'http://localhost:' + port;
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'maskplus2-'));
  const ctx = await chromium.launchPersistentContext(dir, { channel: 'chromium', headless: true,
    args: ['--disable-extensions-except=' + EXT, '--load-extension=' + EXT], locale: 'en-US', timezoneId: 'UTC' });
  let worker = ctx.serviceWorkers()[0] || await ctx.waitForEvent('serviceworker');
  await sleep(800);
  const id = new URL(worker.url()).host;

  // --- по умолчанию (после установки): включено, Германия ---
  let page = await ctx.newPage(); await page.goto(base + '/p');
  check('после установки включена Германия', (await tzOf(page)) === 'Europe/Berlin|de-DE', await tzOf(page));

  // --- смена страны ---
  await setState(worker, { cc: 'JP' });
  page = await ctx.newPage(); await page.goto(base + '/p');
  check('смена на Японию', (await tzOf(page)) === 'Asia/Tokyo|ja-JP', await tzOf(page));
  const eight = await page.evaluate(() => [navigator.languages.join(), Object.isFrozen(navigator.languages), navigator.languages === navigator.languages, new Date(Date.UTC(2024, 0, 1, 0, 0)).getHours()].join('|'));
  check('языки Японии, массив заморожен и один и тот же', eight === 'ja-JP,ja,en-US,en|true|true|9', eight);

  // --- заголовок Accept-Language ---
  const al = async (pg, u) => pg.evaluate(async u => (await (await fetch(u)).json()).al, u);
  check('Accept-Language у fetch', (await al(page, base + '/echo')) === 'ja-JP,ja;q=0.9,en-US;q=0.8,en;q=0.7', await al(page, base + '/echo'));
  await page.goto(base + '/echo');
  const navAl = JSON.parse(await page.textContent('body')).al;
  check('Accept-Language у перехода по ссылке', navAl === 'ja-JP,ja;q=0.9,en-US;q=0.8,en;q=0.7', navAl);

  // --- геолокация ---
  await page.goto(base + '/p');
  const geo = await page.evaluate(() => new Promise((res, rej) => {
    const t0 = performance.now();
    navigator.geolocation.getCurrentPosition(p => res({ ms: Math.round(performance.now() - t0), lat: p.coords.latitude, lon: p.coords.longitude, acc: p.coords.accuracy,
      inst: p instanceof GeolocationPosition && p.coords instanceof GeolocationCoordinates, json: JSON.stringify(p).length > 20, alt: p.coords.altitude, ts: typeof p.timestamp }), e => rej(e.message));
    setTimeout(() => rej('нет ответа'), 4000);
  }));
  check('геолокация: рядом с Токио, быстро, настоящие типы', Math.abs(geo.lat - 35.6762) < 0.03 && Math.abs(geo.lon - 139.6503) < 0.03 && geo.ms < 800 && geo.inst && geo.json && geo.alt === null, JSON.stringify(geo));
  const watch = await page.evaluate(() => new Promise(res => { let n = 0; const id = navigator.geolocation.watchPosition(() => { n++; }, null); setTimeout(() => { navigator.geolocation.clearWatch(id); const at = n; setTimeout(() => res({ id, at, after: n }), 300); }, 600); }));
  check('watchPosition даёт позицию, clearWatch останавливает', watch.at >= 1 && watch.after === watch.at && typeof watch.id === 'number', JSON.stringify(watch));
  const geoErr = await page.evaluate(() => { try { navigator.geolocation.getCurrentPosition(); } catch (e) { return e.constructor.name + ': ' + e.message; } return 'нет ошибки'; });
  check('getCurrentPosition() без аргументов бросает TypeError как родной', /^TypeError: Failed to execute 'getCurrentPosition' on 'Geolocation': 1 argument required/.test(geoErr), geoErr);

  // --- iframe ---
  await page.goto(base + '/p');
  const fr = await page.evaluate(async () => {
    const out = {};
    const f = document.createElement('iframe'); document.body.appendChild(f);
    out.blankSync = f.contentWindow.Intl.DateTimeFormat().resolvedOptions().timeZone + '|' + new f.contentWindow.Date(Date.UTC(2024, 0, 1, 0)).getHours() + '|' + f.contentWindow.Date.prototype.getHours.toString().includes('native code');
    out.docSync = f.contentDocument.defaultView.navigator.language;
    const g = document.createElement('iframe'); g.srcdoc = '<p>x</p>'; document.body.appendChild(g);
    await new Promise(r => g.onload = r);
    out.srcdoc = g.contentWindow.Intl.DateTimeFormat().resolvedOptions().timeZone + '|' + g.contentWindow.navigator.language;
    const h = document.createElement('iframe'); h.src = '/p'; document.body.appendChild(h);
    await new Promise(r => h.onload = r);
    out.same = h.contentWindow.Intl.DateTimeFormat().resolvedOptions().timeZone;
    return out;
  });
  check('iframe about:blank (синхронно)', fr.blankSync === 'Asia/Tokyo|9|true' && fr.docSync === 'ja-JP', JSON.stringify(fr));
  check('iframe srcdoc и тот же источник', fr.srcdoc === 'Asia/Tokyo|ja-JP' && fr.same === 'Asia/Tokyo', JSON.stringify(fr));
  await page.evaluate(o => { const f = document.createElement('iframe'); f.src = o + '/p'; document.body.appendChild(f); return new Promise(r => f.onload = r); }, other);
  const cross = page.frames().find(f => f.url().startsWith(other));
  check('iframe другого сайта получает свою копию', cross && (await tzOf(cross)) === 'Asia/Tokyo|ja-JP', cross ? await tzOf(cross) : 'нет кадра');

  // --- «как родное» ---
  const nat = await page.evaluate(() => [Date.prototype.getHours.toString(), Function.prototype.toString.toString(), Object.getOwnPropertyDescriptor(Navigator.prototype, 'language').get.toString(), Date.toString(), navigator.geolocation.getCurrentPosition.toString()].join(' || '));
  check('подмены выглядят родными в toString()', !/=>|engine|__gsmask/.test(nat) && (nat.match(/\[native code\]/g) || []).length === 5, nat);
  check('в window не осталось следов', await page.evaluate(() => !('__gsmask' in globalThis)), 'остался __gsmask');

  // --- отдельные переключатели ---
  await setState(worker, { tz: false });
  page = await ctx.newPage(); await page.goto(base + '/p');
  check('только язык (время настоящее)', (await tzOf(page)) === 'UTC|ja-JP', await tzOf(page));
  await setState(worker, { tz: true, lang: false });
  page = await ctx.newPage(); await page.goto(base + '/p');
  check('только время (язык настоящий)', (await tzOf(page)) === 'Asia/Tokyo|en-US', await tzOf(page));
  check('язык выключен — заголовок не трогается', (await al(page, base + '/echo')) !== 'ja-JP,ja;q=0.9,en-US;q=0.8,en;q=0.7', await al(page, base + '/echo'));
  await setState(worker, { lang: true, geo: false });
  page = await ctx.newPage(); await page.goto(base + '/p');
  const g2 = await page.evaluate(() => navigator.geolocation.getCurrentPosition.toString());
  check('геолокацию можно не подменять', /native code/.test(g2) && (await page.evaluate(() => Math.abs(new Date().getTimezoneOffset()) === 540)), g2);
  await setState(worker, { geo: true });


  // --- система и браузер ---
  const echo = pg => pg.evaluate(async u => (await fetch(u)).json(), base + '/echo');
  const realUA = await page.evaluate(() => navigator.userAgent);
  const major = (/Chrome\/(\d+)/.exec(realUA) || [])[1];
  check('настоящий браузер — Chromium (для проверок ниже)', !!major, realUA);
  const brandHeader = list => list.map(b => '"' + b.brand + '";v="' + b.version + '"').join(', ');

  await setState(worker, { ua: true, os: 'mac', br: 'safari' });
  page = await ctx.newPage(); await page.goto(base + '/p');
  const sf = await page.evaluate(() => ({ ua: navigator.userAgent, app: navigator.appVersion, plat: navigator.platform, vendor: navigator.vendor, ps: navigator.productSub,
    uad: typeof navigator.userAgentData, chrome: typeof window.chrome, oscpu: typeof navigator.oscpu, cl: typeof NavigatorUAData }));
  check('Safari/macOS: navigator.userAgent/platform/vendor, нет userAgentData и window.chrome',
    /^Mozilla\/5\.0 \(Macintosh; Intel Mac OS X 10_15_7\) AppleWebKit\/605\.1\.15 \(KHTML, like Gecko\) Version\/\d+\.\d Safari\/605\.1\.15$/.test(sf.ua) && sf.plat === 'MacIntel'
    && sf.vendor === 'Apple Computer, Inc.' && sf.ps === '20030107' && sf.uad === 'undefined' && sf.chrome === 'undefined' && sf.oscpu === 'undefined' && sf.cl === 'undefined' && sf.app === sf.ua.slice(8), JSON.stringify(sf));
  let h = await echo(page);
  check('Safari/macOS: заголовок User-Agent тот же, подсказки sec-ch-ua убраны', h.ua === sf.ua && h.chua === null && h.chplat === null && h.chpv === null, JSON.stringify(h));
  await page.goto(base + '/echo');
  check('Safari/macOS: и при переходе по ссылке', JSON.parse(await page.textContent('body')).ua === sf.ua, await page.textContent('body'));

  await setState(worker, { os: 'win11', br: 'edge' });
  page = await ctx.newPage(); await page.goto(base + '/p');
  const ed = await page.evaluate(async () => {
    const u = navigator.userAgentData;
    const he = await u.getHighEntropyValues(['platformVersion', 'architecture', 'bitness', 'model', 'uaFullVersion', 'fullVersionList', 'wow64', 'formFactors']);
    return { ua: navigator.userAgent, app: navigator.appVersion, plat: navigator.platform, vendor: navigator.vendor, brands: u.brands.map(b => ({ brand: b.brand, version: b.version })),
      mobile: u.mobile, platform: u.platform, he, json: u.toJSON(), fresh: u.brands !== u.brands, frozen: Object.isFrozen(u.brands), itemFrozen: Object.isFrozen(u.brands[0]) };
  });
  check('Edge/Windows 11: navigator.userAgent и platform',
    ed.ua === 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/' + major + '.0.0.0 Safari/537.36 Edg/' + major + '.0.0.0' && ed.plat === 'Win32' && ed.vendor === 'Google Inc.' && ed.app === ed.ua.slice(8), JSON.stringify(ed));
  check('Edge/Windows 11: userAgentData — бренды (каждый раз новый замороженный массив, как у родного), платформа, mobile',
    ed.brands.some(b => b.brand === 'Microsoft Edge' && b.version === major) && ed.brands.some(b => b.brand === 'Chromium' && b.version === major) && !ed.brands.some(b => /Google Chrome/.test(b.brand))
    && ed.platform === 'Windows' && ed.mobile === false && ed.fresh && ed.frozen && !ed.itemFrozen, JSON.stringify({ b: ed.brands, p: ed.platform, m: ed.mobile, fresh: ed.fresh, frozen: ed.frozen, itemFrozen: ed.itemFrozen }));
  check('Edge/Windows 11: getHighEntropyValues',
    ed.he.platformVersion === '19.0.0' && ed.he.architecture === 'x86' && ed.he.bitness === '64' && ed.he.model === '' && ed.he.wow64 === false && /^\d+\.\d+\.\d+\.\d+$/.test(ed.he.uaFullVersion)
    && ed.he.fullVersionList.some(b => b.brand === 'Microsoft Edge' && /^\d+\.\d+\.\d+\.\d+$/.test(b.version)) && !ed.he.fullVersionList.some(b => /Google Chrome/.test(b.brand))
    && JSON.stringify(ed.he.formFactors) === '["Desktop"]' && ed.he.platform === 'Windows' && ed.he.brands.length === ed.brands.length && JSON.stringify(ed.json.brands) === JSON.stringify(ed.brands), JSON.stringify(ed.he));
  h = await echo(page);
  check('Edge/Windows 11: заголовки совпадают со страницей (User-Agent, sec-ch-ua, platform), лишних подсказок нет',
    h.ua === ed.ua && h.chua === brandHeader(ed.brands) && h.chplat === '"Windows"' && h.chpv === null && h.charch === null, JSON.stringify(h) + ' | ' + brandHeader(ed.brands));
  const fr2 = await page.evaluate(async () => {
    const f = document.createElement('iframe'); document.body.appendChild(f);
    const w = f.contentWindow;
    const he = await w.navigator.userAgentData.getHighEntropyValues(['platformVersion']);
    return { ua: w.navigator.userAgent === navigator.userAgent, plat: w.navigator.userAgentData.platform, pv: he.platformVersion, nat: [Object.getOwnPropertyDescriptor(Navigator.prototype, 'userAgent').get.toString(), NavigatorUAData.prototype.getHighEntropyValues.toString(), Object.getOwnPropertyDescriptor(NavigatorUAData.prototype, 'brands').get.toString()].join(' || ') };
  });
  check('Edge/Windows 11: iframe получает ту же подмену', fr2.ua && fr2.plat === 'Windows' && fr2.pv === '19.0.0', JSON.stringify(fr2));
  check('подмена navigator выглядит родной в toString()', (fr2.nat.match(/\[native code\]/g) || []).length === 3 && !/=>|engine|UAI/.test(fr2.nat), fr2.nat);
  const bad = await page.evaluate(async () => { try { await navigator.userAgentData.getHighEntropyValues(); return 'нет ошибки'; } catch (e) { return e.constructor.name; } });
  check('getHighEntropyValues() без аргумента отклоняется как родной (TypeError)', bad === 'TypeError', bad);

  await setState(worker, { os: 'win10', br: 'chrome' });
  page = await ctx.newPage(); await page.goto(base + '/p');
  const ch = await page.evaluate(async () => ({ ua: navigator.userAgent, brands: navigator.userAgentData.brands.map(b => b.brand), pv: (await navigator.userAgentData.getHighEntropyValues(['platformVersion'])).platformVersion }));
  check('Chrome/Windows 10: версия 10.0.0, бренд Google Chrome', ch.pv === '10.0.0' && ch.brands.includes('Google Chrome') && !ch.brands.includes('Microsoft Edge') && !/Edg\//.test(ch.ua), JSON.stringify(ch));
  h = await echo(page);
  check('Chrome/Windows 10: sec-ch-ua содержит Google Chrome', /"Google Chrome";v="\d+"/.test(h.chua || '') && h.chplat === '"Windows"', JSON.stringify(h));

  await setState(worker, { os: 'linux', br: 'firefox' });
  page = await ctx.newPage(); await page.goto(base + '/p');
  const ff = await page.evaluate(() => ({ ua: navigator.userAgent, app: navigator.appVersion, plat: navigator.platform, vendor: navigator.vendor, ps: navigator.productSub,
    oscpu: navigator.oscpu, build: navigator.buildID, uad: typeof navigator.userAgentData, chrome: typeof window.chrome }));
  const ffv = (/Firefox\/(\d+)\.0$/.exec(ff.ua) || [])[1];
  check('Firefox/Linux: navigator.userAgent, appVersion, platform, oscpu',
    ffv === String(+major + 3) && ff.ua === 'Mozilla/5.0 (X11; Linux x86_64; rv:' + ffv + '.0) Gecko/20100101 Firefox/' + ffv + '.0' && ff.app === '5.0 (X11)' && ff.plat === 'Linux x86_64' && ff.vendor === '' && ff.ps === '20100101'
    && ff.oscpu === 'Linux x86_64' && ff.build === '20181001000000' && ff.uad === 'undefined' && ff.chrome === 'undefined', JSON.stringify(ff));
  h = await echo(page);
  check('Firefox/Linux: заголовок User-Agent, подсказки убраны', h.ua === ff.ua && h.chua === null && h.chplat === null, JSON.stringify(h));

  await setState(worker, { os: 'win11', br: 'chrome', excluded: ['127.0.0.1'] });
  page = await ctx.newPage(); await page.goto(base + '/p');
  h = await echo(page);
  check('исключённый сайт: настоящие User-Agent и navigator', (await page.evaluate(() => navigator.userAgent)) === realUA && h.ua === realUA, JSON.stringify(h));
  await setState(worker, { excluded: [] });
  await setState(worker, { ua: false });
  page = await ctx.newPage(); await page.goto(base + '/p');
  h = await echo(page);
  check('подмена системы выключена — настоящие User-Agent, navigator и заголовки', (await page.evaluate(() => navigator.userAgent)) === realUA && h.ua === realUA && h.chua !== null, JSON.stringify(h));
  const rules0 = await worker.evaluate(async () => (await chrome.declarativeNetRequest.getDynamicRules()).map(r => r.id).join());
  check('выключено — остаётся только правило языка', rules0 === '1', rules0);
  await setState(worker, { lang: false });
  const rules1 = await worker.evaluate(async () => (await chrome.declarativeNetRequest.getDynamicRules()).length);
  await setState(worker, { lang: true });
  check('язык и подмена системы выключены — правил нет', rules1 === 0, rules1);

  // --- исключённый сайт ---
  await setState(worker, { excluded: ['127.0.0.1'] });
  page = await ctx.newPage(); await page.goto(base + '/p');
  check('исключённый сайт не маскируется', (await tzOf(page)) === 'UTC|en-US', await tzOf(page));
  check('исключённому сайту заголовок не меняется', (await al(page, base + '/echo')) !== 'ja-JP,ja;q=0.9,en-US;q=0.8,en;q=0.7', await al(page, base + '/echo'));
  page = await ctx.newPage(); await page.goto(other + '/p');
  check('другие сайты по-прежнему маскируются', (await tzOf(page)) === 'Asia/Tokyo|ja-JP', await tzOf(page));
  await setState(worker, { excluded: [] });

  // --- WebRTC ---
  const rtc = () => worker.evaluate(async () => { const d = await chrome.privacy.network.webRTCIPHandlingPolicy.get({}); return d.value + '|' + d.levelOfControl; });
  check('WebRTC: защита включена', (await rtc()) === 'default_public_interface_only|controlled_by_this_extension', await rtc());
  await setState(worker, { webrtc: false });
  check('WebRTC: галочка снята — настройка возвращена', !(await rtc()).includes('controlled_by_this_extension'), await rtc());
  await setState(worker, { webrtc: true });

  // --- значок ---
  const badge = await worker.evaluate(() => chrome.action.getBadgeText({}));
  check('на значке код страны', badge === 'JP', badge);

  // --- выключение ---
  await setState(worker, { enabled: false });
  page = await ctx.newPage(); await page.goto(base + '/p');
  check('выключено — всё как есть', (await tzOf(page)) === 'UTC|en-US', await tzOf(page));
  check('выключено — заголовок не меняется', (await al(page, base + '/echo')) !== 'ja-JP,ja;q=0.9,en-US;q=0.8,en;q=0.7', await al(page, base + '/echo'));
  check('выключено — WebRTC возвращён', !(await rtc()).includes('controlled_by_this_extension'), await rtc());
  check('выключено — значок пустой', (await worker.evaluate(() => chrome.action.getBadgeText({}))) === '', 'есть текст');
  const regs = await worker.evaluate(async () => (await chrome.scripting.getRegisteredContentScripts()).length + '|' + (await chrome.declarativeNetRequest.getDynamicRules()).length);
  check('выключено — скрипты и правило сняты', regs === '0|0', regs);
  await setState(worker, { enabled: true, cc: 'AU' });
  page = await ctx.newPage(); await page.goto(base + '/p?target');
  check('включили снова — Австралия', (await tzOf(page)) === 'Australia/Sydney|en-AU', await tzOf(page));

  // --- окно расширения: скриншоты и логика «что видит вкладка» ---
  const tabId = await worker.evaluate(async b => (await chrome.tabs.query({ url: b + '/p?target' }))[0].id, base);
  const shots = process.env.SHOTS_DIR ? process.env.SHOTS_DIR + '/' : os.tmpdir() + '/';
  for (const [name, patch] of [['popup-on', { enabled: true, cc: 'AU' }], ['popup-off', { enabled: false }]]) {
    await setState(worker, patch);
    const pop = await ctx.newPage();
    await pop.addInitScript(tab => { const q = chrome.tabs.query.bind(chrome.tabs); chrome.tabs.query = async () => [{ id: tab, url: 'http://127.0.0.1/p' }]; }, tabId);
    await pop.setViewportSize({ width: 340, height: 640 });
    await pop.goto('chrome-extension://' + id + '/popup.html');
    await sleep(700);
    if (name === 'popup-on') {
      const txt = await pop.textContent('#seen');
      check('окно: показывает пояс и язык вкладки', /Australia\/Sydney/.test(txt) && /en-AU/.test(txt), txt);
      const sel = await pop.inputValue('#country');
      check('окно: выбрана текущая страна', sel === 'AU', sel);
      await pop.selectOption('#country', 'FR');
      await sleep(800);
      const st = await worker.evaluate(() => chrome.storage.local.get('cc'));
      check('окно: выбор страны сохраняется', st.cc === 'FR', JSON.stringify(st));
      await pop.click('#exclude');
      await sleep(600);
      const st2 = await worker.evaluate(() => chrome.storage.local.get('excluded'));
      check('окно: «Не маскировать этот сайт» добавляет в список', JSON.stringify(st2.excluded) === '["127.0.0.1"]', JSON.stringify(st2));
      await pop.click('#exclude'); await sleep(500);
      await pop.check('#ua'); await sleep(600);
      await pop.selectOption('#br', 'safari'); await sleep(700);
      const st3 = await worker.evaluate(() => chrome.storage.local.get(['ua', 'os', 'br']));
      check('окно: «Подменять систему и браузер» + Safari сразу ставит macOS', st3.ua === true && st3.br === 'safari' && st3.os === 'mac', JSON.stringify(st3));
      check('окно: показана строка User-Agent выбранного браузера и список ОС заблокирован', /Version\/\d+\.\d Safari\/605\.1\.15/.test(await pop.textContent('#uaStr')) && await pop.isDisabled('#os'), await pop.textContent('#uaStr'));
      await pop.screenshot({ path: shots + 'popup-ua.png' });
      await pop.selectOption('#br', 'edge'); await sleep(600);
      await pop.selectOption('#os', 'linux'); await sleep(600);
      const st4 = await worker.evaluate(() => chrome.storage.local.get(['os', 'br']));
      check('окно: выбор ОС и браузера сохраняется', st4.os === 'linux' && st4.br === 'edge', JSON.stringify(st4));
      await pop.uncheck('#ua'); await sleep(500);
      await pop.selectOption('#country', 'AU'); await sleep(600);
    }
    await pop.screenshot({ path: shots + name + '.png' });
    await pop.close();
  }
  console.log('\nИТОГО: ок', okN, ' плохо', badN);
  await ctx.close(); server.close();
})().catch(e => { console.error('СБОЙ ТЕСТА', e); process.exit(1); });
