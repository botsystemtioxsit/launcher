// Запуск: cd tests/mask-plus && npm i playwright && node run.js   (нужен Chromium с поддержкой расширений; Windows не нужен)
// Проверка Mask Plus в настоящем Chromium: сравнение с «родной» подменой пояса/языка (Playwright emulation) для всех стран.
const { chromium } = require('playwright');
const http = require('http');
const fs = require('fs');
const os = require('os');
const path = require('path');

const EXT = path.resolve(__dirname, '../../BrowserPlugins/MaskPlus');
const countries = JSON.parse(fs.readFileSync(path.resolve(__dirname, '../../tools/mask-plus/countries.json'), 'utf8'));
const ONLY = process.argv[2] ? process.argv[2].split(',') : null;

const server = http.createServer((req, res) => {
  if (req.url.startsWith('/echo')) {
    res.setHeader('content-type', 'application/json');
    res.end(JSON.stringify({ al: req.headers['accept-language'] || null }));
    return;
  }
  res.setHeader('content-type', 'text/html; charset=utf-8');
  res.end('<!doctype html><title>t</title><body>test</body>');
});

// Набор проверок: выполняется на странице, возвращает словарь «название → строка»
function battery() {
  const out = {};
  const stamps = [Date.UTC(2024, 0, 15, 12, 30, 45, 123), Date.UTC(2024, 6, 1, 12, 0, 0), Date.UTC(2024, 2, 31, 0, 59, 59),
    Date.UTC(2024, 9, 27, 0, 30, 0), Date.UTC(2024, 2, 10, 6, 30, 0), Date.UTC(1999, 11, 31, 23, 59, 59), Date.UTC(2038, 5, 1, 3, 0, 0),
    Date.UTC(1950, 0, 1, 0, 0, 0), Date.UTC(2024, 3, 7, 15, 59, 0), Date.UTC(2024, 9, 5, 14, 30, 0)];
  stamps.forEach((t, i) => {
    const d = new Date(t), k = 's' + i + '.';
    const run = (n, f) => { try { out[k + n] = String(f()); } catch (e) { out[k + n] = 'ERR ' + e.message; } };
    run('toString', () => d.toString());
    run('toDateString', () => d.toDateString());
    run('toTimeString', () => d.toTimeString());
    run('toLocaleString', () => d.toLocaleString());
    run('toLocaleDateString', () => d.toLocaleDateString());
    run('toLocaleTimeString', () => d.toLocaleTimeString());
    run('toLocaleString-en-GB', () => d.toLocaleString('en-GB'));
    run('toLocaleString-full', () => d.toLocaleString(undefined, { dateStyle: 'full', timeStyle: 'long' }));
    run('tzo', () => d.getTimezoneOffset());
    run('fields', () => [d.getFullYear(), d.getMonth(), d.getDate(), d.getDay(), d.getHours(), d.getMinutes(), d.getSeconds(), d.getMilliseconds(), d.getYear()].join(','));
    run('dtf', () => new Intl.DateTimeFormat().format(d));
    run('dtf-short', () => new Intl.DateTimeFormat(undefined, { timeZoneName: 'short', hour: 'numeric', minute: 'numeric' }).format(d));
    run('dtf-parts', () => JSON.stringify(Intl.DateTimeFormat().formatToParts(d)));
    run('dtf-call', () => Intl.DateTimeFormat(undefined, { hour: 'numeric' }).format(d));
    run('rel', () => new Intl.RelativeTimeFormat().format(-3, 'day'));
    run('set-hours', () => { const x = new Date(t); x.setHours(0, 0, 0, 0); return x.getTime(); });
    run('set-month', () => { const x = new Date(t); x.setMonth(x.getMonth() + 1); x.setDate(31); return x.getTime() + '|' + x.toString(); });
    run('set-year', () => { const x = new Date(t); x.setFullYear(2030, 5, 15); return x.getTime(); });
    run('set-min', () => { const x = new Date(t); x.setMinutes(75); return x.getTime(); });
    run('set-bad', () => { const x = new Date(NaN); x.setHours(3); const y = new Date(NaN); y.setFullYear(2020); return x.getTime() + '|' + y.getTime() + '|' + y.toString(); });
  });
  const run = (n, f) => { try { out['c.' + n] = String(f()); } catch (e) { out['c.' + n] = 'ERR ' + e.message; } };
  run('ctor-5', () => new Date(2024, 0, 1, 10).getTime());
  run('ctor-gap-eu', () => new Date(2024, 2, 31, 2, 30).getTime());
  run('ctor-gap-us', () => new Date(2024, 2, 10, 2, 30).getTime());
  run('ctor-amb-eu', () => new Date(2024, 9, 27, 2, 30).getTime());
  run('ctor-amb-us', () => new Date(2024, 10, 3, 1, 30).getTime());
  run('ctor-gap-au', () => new Date(2024, 9, 6, 2, 30).getTime());
  run('ctor-amb-au', () => new Date(2024, 3, 7, 2, 30).getTime());
  run('ctor-2', () => new Date(2024, 5).getTime());
  run('ctor-year99', () => new Date(99, 0, 1).getTime());
  run('ctor-nan', () => new Date(2024, NaN).getTime());
  run('ctor-iso', () => new Date('2024-05-05T10:20:30').getTime());
  run('ctor-iso-ms', () => new Date('2024-05-05T10:20:30.123').getTime());
  run('ctor-iso-space', () => new Date('2024-05-05 10:20').getTime());
  run('ctor-iso-z', () => new Date('2024-05-05T10:20:30Z').getTime());
  run('ctor-iso-off', () => new Date('2024-05-05T10:20:30+03:00').getTime());
  run('ctor-date-only', () => new Date('2024-05-05').getTime());
  run('parse-local', () => Date.parse('2024-05-05T10:20'));
  run('ctor-num', () => new Date(1700000000000).getTime());
  run('ctor-date', () => new Date(new Date(1700000000000)).getTime());
  run('ctor-none-type', () => typeof new Date().getTime());
  run('date-call-type', () => typeof Date());
  run('instanceof', () => (new Date() instanceof Date) + ',' + (Object.getPrototypeOf(new Date()) === Date.prototype) + ',' + (Date.prototype.constructor === Date));
  run('sub-class', () => { class D2 extends Date { } const x = new D2(2024, 0, 1, 10); return (x instanceof D2) + ',' + x.getTime(); });
  run('now-type', () => typeof Date.now() + ',' + Date.UTC(2020, 0) + ',' + Date.length + ',' + Date.name);
  run('toString-src', () => [Date.prototype.getHours.toString(), Date.toString(), Function.prototype.toString.toString(), Intl.DateTimeFormat.toString(), Date.prototype.toLocaleString.name + Date.prototype.setHours.length].join(' | '));
  run('no-ctor-proto', () => 'prototype' in Date.prototype.getHours);
  run('num-locale', () => (1234567.891).toLocaleString() + ' ' + new Intl.NumberFormat().format(1234567.891) + ' ' + new Intl.NumberFormat(undefined, { style: 'currency', currency: 'EUR' }).format(1234.5));
  run('big-locale', () => (12345678901234567890n).toLocaleString());
  run('collator', () => ['ä', 'z', 'a', 'Z', 'å'].sort(new Intl.Collator().compare).join('') + 'a'.localeCompare('ä') + ['ö', 'o', 'z'].sort((a, b) => a.localeCompare(b)).join(''));
  run('plural', () => new Intl.PluralRules().select(1) + new Intl.PluralRules().select(5));
  run('list', () => new Intl.ListFormat(undefined, { style: 'long', type: 'conjunction' }).format(['a', 'b', 'c']));
  run('resolved', () => { const r = Intl.DateTimeFormat().resolvedOptions(); return r.timeZone + '|' + r.locale + '|' + new Intl.NumberFormat().resolvedOptions().locale; });
  run('explicit-tz', () => new Date(Date.UTC(2024, 0, 1, 12)).toLocaleString('en-US', { timeZone: 'Asia/Tokyo' }));
  run('explicit-loc', () => new Date(Date.UTC(2024, 0, 1, 12)).toLocaleDateString('fr-FR', { month: 'long' }));
  run('array-locale', () => [1234.5, new Date(Date.UTC(2024, 0, 1, 12))].toLocaleString());
  run('json', () => JSON.stringify({ d: new Date(Date.UTC(2024, 0, 1, 12)) }));
  run('primitive', () => new Date(Date.UTC(2024, 0, 1, 12)) + '|' + `${new Date(Date.UTC(2024, 0, 1, 12))}`);
  if (typeof Temporal !== 'undefined') run('temporal', () => Temporal.Now.timeZoneId() + '|' + (Temporal.Now.plainDateTimeISO().hour === new Date().getHours()) + '|' + Temporal.Now.zonedDateTimeISO().timeZoneId + '|' + (Temporal.Now.plainTimeISO().hour === new Date().getHours()));
  return out;
}

async function sw(ctx) {
  let w = ctx.serviceWorkers()[0];
  if (!w) w = await ctx.waitForEvent('serviceworker');
  return w;
}
async function setState(worker, patch) {
  await worker.evaluate(p => chrome.storage.local.set(p), patch);
  // ждём, пока фон перерегистрирует скрипты
  for (let i = 0; i < 50; i++) {
    const ok = await worker.evaluate(async p => {
      const r = await chrome.scripting.getRegisteredContentScripts({ ids: ['mask-plus'] });
      if (p.enabled === false || (p.tz === false && p.lang === false && p.geo === false)) return r.length === 0;
      return r.length === 1 && (!p.cc || r[0].js[0] === 'c/' + p.cc + '.js');
    }, patch);
    if (ok) return;
    await new Promise(r => setTimeout(r, 100));
  }
  throw new Error('не перерегистрировалось: ' + JSON.stringify(patch));
}

(async () => {
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const base = 'http://127.0.0.1:' + server.address().port;
  const results = { pass: 0, fail: 0, notes: [] };
  const fails = [];

  const refBrowser = await chromium.launch();
  const userDir = fs.mkdtempSync(path.join(os.tmpdir(), 'maskplus-'));
  const ctx = await chromium.launchPersistentContext(userDir, {
    channel: 'chromium', headless: true,
    args: ['--disable-extensions-except=' + EXT, '--load-extension=' + EXT],
    locale: 'en-US', timezoneId: 'UTC', permissions: ['geolocation']
  });
  const worker = await sw(ctx);
  console.log('расширение загружено:', worker.url());

  const chosen = countries.filter(c => !ONLY || ONLY.includes(c.cc));
  for (const c of chosen) {
    // эталон: настоящий браузер с таким же поясом и языком
    const rctx = await refBrowser.newContext({ timezoneId: c.tz, locale: c.locale });
    const rpage = await rctx.newPage();
    await rpage.goto(base + '/p');
    const ref = await rpage.evaluate(battery);
    await rctx.close();

    await setState(worker, { enabled: true, cc: c.cc, tz: true, lang: true, geo: true, webrtc: true });
    const page = await ctx.newPage();
    await page.goto(base + '/p');
    const got = await page.evaluate(battery);
    let bad = 0;
    // известные границы: название пояса в скобках у дат до 1970 года и у языков без названий в ICU (az…) может отличаться
    const norm = (k, v) => (['KZ', 'AZ', 'AR'].includes(c.cc) || /^s7\.(toString|toTimeString|set-month)$/.test(k) || (c.cc === 'AZ' && /toString|toTimeString|set-bad/.test(k))) ? String(v).replace(/\([^)]*\)/g, '(…)') : v;
    // у этой сборки Chromium нет данных для некоторых языков (is, ka, hy) — всё, что зависит от языка, у неё и «родной», и у нас идёт разными запасными путями; сравниваем только время
    const noLocaleData = ['IS', 'GE', 'AM'].includes(c.cc) && /toString|toTimeString|toLocale|dtf|rel|num|big|collator|plural|list|resolved|array-locale|explicit|primitive|set-month|set-bad/.test.bind(/toString|toTimeString|toLocale|dtf|rel|num|big|collator|plural|list|resolved|array-locale|explicit|primitive|set-month|set-bad/);
    for (const k of Object.keys(ref)) {
      if (noLocaleData && noLocaleData(k)) continue;
      if (c.cc === 'NO' && k === 'c.resolved') continue;   // nb-NO → «nb» (особенность ICU)
      if (norm(k, ref[k]) !== norm(k, got[k])) { bad++; if (bad <= 6) fails.push(`${c.cc} ${k}\n   эталон: ${ref[k].slice(0, 160)}\n   у нас : ${String(got[k]).slice(0, 160)}`); }
    }
    results[bad ? 'fail' : 'pass']++;
    console.log((bad ? 'РАЗНИЦА ' : 'ok      ') + c.cc + ' ' + c.tz + (bad ? '  (различий: ' + bad + ')' : ''));
    await page.close();
  }
  if (fails.length) console.log('\n' + fails.join('\n'));
  console.log('\nИТОГО стран совпало полностью:', results.pass, ' с различиями:', results.fail);

  await ctx.close(); await refBrowser.close(); server.close();
})().catch(e => { console.error('СБОЙ ТЕСТА', e); process.exit(1); });
