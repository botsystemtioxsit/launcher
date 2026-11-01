// «Терминал» — набор панелей-виджетов поверх обоев. Раскладка лежит в параметре ?layout=<json>; в редакторе (?edit=1) панели двигаются мышью.
// Данные компьютера приходят от Wallpaper Plus через GS (см. gs.js). Новый виджет = один вызов reg('тип', {...}) ниже.
(() => {
'use strict';
const q = new URLSearchParams(location.search);
const P = (k, d) => { const v = q.get(k); return v === null || v === '' ? d : v; };
const EDIT = P('edit', '0') === '1';
const COMPOSE = P('compose', '0') === '1';
const RU = P('lang', 'ru') !== 'en';
const GS = window.GS || { on() {}, data: {}, send() {} };

// ---------- оформление ----------
const hex = h => { h = (h || '').replace('#', ''); return [parseInt(h.slice(0, 2), 16) || 0, parseInt(h.slice(2, 4), 16) || 0, parseInt(h.slice(4, 6), 16) || 0]; };
const root = document.documentElement.style;
const acc = P('acc', '#C9A7FF'), bgc = P('bgc', '#07030F');
root.setProperty('--acc', acc); root.setProperty('--accrgb', hex(acc).join(','));
root.setProperty('--bg', bgc); root.setProperty('--bgrgb', hex(bgc).join(','));
root.setProperty('--g', P('glitch', '1')); root.setProperty('--scan', P('scan', '0.5'));
root.setProperty('--fill', COMPOSE ? '0' : P('fill', '1'));   // поверх других обоев тёмной подложки нет: под терминалом видно то, что выбрано
const textScale = parseFloat(P('size', '1')) || 1;
const stage = document.getElementById('stage'), layer = document.getElementById('layer');
function rescale() {
  const r = stage.getBoundingClientRect();
  root.setProperty('--s', (Math.min(r.width / 1920, r.height / 1080) * textScale).toFixed(4));
}
addEventListener('resize', rescale);
if (EDIT) { document.body.classList.add('edit'); root.setProperty('--panelw', '300px'); }
rescale();

// ---------- вспомогательное ----------
const el = (tag, cls, txt) => { const e = document.createElement(tag); if (cls) e.className = cls; if (txt != null) e.textContent = txt; return e; };
const pad = (n, w = 2) => String(Math.max(0, Math.floor(n))).padStart(w, '0');
const DAYS_EN = ['SUN.', 'MON.', 'TUE.', 'WED.', 'THU.', 'FRI.', 'SAT.'], DAYS_RU = ['ВС.', 'ПН.', 'ВТ.', 'СР.', 'ЧТ.', 'ПТ.', 'СБ.'];
const SHORT = { Sun: 0, Mon: 1, Tue: 2, Wed: 3, Thu: 4, Fri: 5, Sat: 6 };
const fmtCache = {};
function zoned(tz) {
  let f = fmtCache[tz || ''];
  if (!f) {
    const o = { hourCycle: 'h23', hour: '2-digit', minute: '2-digit', second: '2-digit', day: '2-digit', month: '2-digit', year: '2-digit', weekday: 'short' };
    try { f = new Intl.DateTimeFormat('en-GB', tz ? { ...o, timeZone: tz } : o); } catch (e) { f = new Intl.DateTimeFormat('en-GB', o); }
    fmtCache[tz || ''] = f;
  }
  const r = {}; for (const p of f.formatToParts(new Date())) r[p.type] = p.value;
  r.dow = SHORT[r.weekday] ?? 0;
  return r;
}
function bar(pct, n) {
  const k = Math.max(0, Math.min(n, Math.round(pct / 100 * n)));
  const s = el('span', 'bar'), a = el('span', 'f', '0'.repeat(k)), b = el('span', 'e', '·'.repeat(n - k));
  s.append(a, b); return s;
}
function setBar(s, pct, n) {
  const k = Math.max(0, Math.min(n, Math.round(pct / 100 * n)));
  s.firstChild.textContent = '0'.repeat(k); s.lastChild.textContent = '·'.repeat(n - k);
}
const hm = ms => { if (!ms) return '--:--'; const d = new Date(ms); return d.getHours() + ':' + pad(d.getMinutes()); };
const mmss = s => { s = Math.max(0, Math.floor(s || 0)); const h = Math.floor(s / 3600); return (h ? h + ':' + pad(Math.floor(s % 3600 / 60)) : pad(Math.floor(s / 60))) + ':' + pad(s % 60); };
const WCOND = {
  en: { 0: 'Clear', 1: 'Mostly Clear', 2: 'Partly Cloudy', 3: 'Overcast', 45: 'Fog', 48: 'Fog', 51: 'Drizzle', 53: 'Drizzle', 55: 'Drizzle', 56: 'Freezing Drizzle', 57: 'Freezing Drizzle', 61: 'Rain', 63: 'Rain', 65: 'Heavy Rain', 66: 'Freezing Rain', 67: 'Freezing Rain', 71: 'Snow', 73: 'Snow', 75: 'Heavy Snow', 77: 'Snow Grains', 80: 'Showers', 81: 'Showers', 82: 'Heavy Showers', 85: 'Snow Showers', 86: 'Snow Showers', 95: 'Thunderstorm', 96: 'Thunderstorm', 99: 'Thunderstorm' },
  ru: { 0: 'Ясно', 1: 'Малооблачно', 2: 'Переменная облачность', 3: 'Пасмурно', 45: 'Туман', 48: 'Туман', 51: 'Морось', 53: 'Морось', 55: 'Морось', 56: 'Ледяная морось', 57: 'Ледяная морось', 61: 'Дождь', 63: 'Дождь', 65: 'Сильный дождь', 66: 'Ледяной дождь', 67: 'Ледяной дождь', 71: 'Снег', 73: 'Снег', 75: 'Сильный снег', 77: 'Снежная крупа', 80: 'Ливень', 81: 'Ливень', 82: 'Сильный ливень', 85: 'Снегопад', 86: 'Снегопад', 95: 'Гроза', 96: 'Гроза', 99: 'Гроза' }
};
const cond = c => (WCOND[RU ? 'ru' : 'en'][c]) || (RU ? 'Нет данных' : 'No data');
const T = (ru, en) => RU ? ru : en;

// ---------- виджеты ----------
const COMMON = [
  { k: 'align', t: 'choice', l: 'Выравнивание', o: ['left', 'right'], ol: ['по левому краю', 'по правому краю'] },
  { k: 'tilt', t: 'num', l: 'Наклон панели', min: -6, max: 6, step: 0.5 },
  { k: 'scale', t: 'num', l: 'Размер текста панели', min: 0.5, max: 2.5, step: 0.05 },
  { k: 'panel', t: 'bool', l: 'Полоса-подложка' },
];
const REG = {};
const reg = (type, def) => { REG[type] = def; };
const num = (v, d) => { const x = parseFloat(v); return isNaN(x) ? d : x; };

reg('title', {
  name: 'Заголовок', w: 0.3, defs: { text: 'SYSTEM OVERVIEW', sub: 'WALLPAPER LOADING', sub2: 'TERMINAL WALLPAPER 02' },
  schema: [{ k: 'text', t: 'text', l: 'Заголовок' }, { k: 'sub', t: 'text', l: 'Строка под заголовком' }, { k: 'sub2', t: 'text', l: 'Правая подпись' }],
  mount(box, o) {
    const r = el('div', 'row'); r.append(el('span', '', o.sub), el('span', 'chev', '>>>>'), el('span', 'dim', o.sub2));
    box.append(el('div', 'big', o.text), r); return {};
  }
});

reg('clock', {
  name: 'Часы', w: 0.27, defs: { label: 'TIME', tz: '', sec: true, date: true },
  schema: [{ k: 'label', t: 'text', l: 'Подпись' }, { k: 'tz', t: 'text', l: 'Часовой пояс (например Europe/London; пусто — ваш)' }, { k: 'sec', t: 'bool', l: 'Секунды' }, { k: 'date', t: 'bool', l: 'Дата и день недели' }],
  mount(box, o) {
    const r1 = el('div', 'row'), r2 = el('div', 'row');
    const l1 = el('span', 'lab', o.label), g = [el('span', 'grp'), el('span', 'grp'), el('span', 'grp')];
    r1.append(l1, ...(o.sec ? g : g.slice(0, 2)));
    const l2 = el('span', 'lab'), d = [el('span', 'grp'), el('span', 'grp'), el('span', 'grp')];
    r2.append(l2, ...d);
    box.append(r1); if (o.date) box.append(r2);
    return { tick() { const z = zoned(o.tz); g[0].textContent = z.hour; g[1].textContent = z.minute; g[2].textContent = z.second; l2.textContent = (RU ? DAYS_RU : DAYS_EN)[z.dow]; d[0].textContent = z.day; d[1].textContent = z.month; d[2].textContent = z.year; } };
  }
});

reg('daybar', {
  name: 'Прогресс дня и рассвет', w: 0.27, defs: {},
  schema: [],
  mount(box) {
    const r1 = el('div', 'row'), pc = el('span', 'lab', '000%'), b = bar(0, 16), r2 = el('div', 'row'), dw = el('span', 'lab'), up = el('span', ''), dn = el('span', 'fl');
    r1.append(pc, b); r2.append(dw, up, dn); box.append(r1, r2);
    return {
      tick() { const n = new Date(), p = (n.getHours() * 3600 + n.getMinutes() * 60 + n.getSeconds()) / 864; pc.textContent = pad(p, 3) + '%'; setBar(b, p, 16); dw.textContent = (RU ? DAYS_RU : DAYS_EN)[n.getDay()]; },
      on(k, d) { if (k === 'weather') { up.textContent = '↓ ' + hm(d.ok ? d.sunrise : 0); dn.textContent = hm(d.ok ? d.sunset : 0) + ' ↑'; } }
    };
  }
});

reg('weather', {
  name: 'Погода', w: 0.2, defs: { units: 'c' },
  schema: [{ k: 'units', t: 'choice', l: 'Температура', o: ['c', 'f'], ol: ['°C', '°F'] }],
  mount(box, o) {
    const rows = ['COND', 'TEMP', 'PREC', 'WIND'].map(n => { const r = el('div', 'row'), v = el('span', ''), l = el('span', 'lab', n); r.append(l, v); r.style.marginBottom = '.7em'; box.append(r); return v; });
    rows[0].textContent = T('нет данных', 'no data');
    return {
      on(k, d) {
        if (k !== 'weather') return;
        if (!d.ok) { rows[0].textContent = d.err ? d.err : T('укажите город в настройках', 'set a city in settings'); rows[1].textContent = rows[2].textContent = rows[3].textContent = '---'; return; }
        rows[0].textContent = cond(d.code);
        rows[1].textContent = o.units === 'f' ? pad(d.temp * 9 / 5 + 32, 3) + '°F' : pad(Math.abs(d.temp), 3) + '°' + (d.temp < 0 ? '−' : '');
        rows[2].textContent = pad(d.prec, 3) + '%';
        rows[3].textContent = d.wind.toFixed(1) + ' m/s';
      }
    };
  }
});

reg('display', {
  name: 'Экран и курсор', w: 0.2, defs: {},
  schema: [],
  mount(box) {
    const mk = n => { const r = el('div', 'row'); r.style.marginBottom = '.7em'; const l = el('span', 'lab', n); r.append(l); box.append(r); return r; };
    const res = mk('RES'), pos = mk('POS'), evt = mk('EVT');
    const rv = [el('span', '', '----'), el('span', '', '----')], pv = [el('span', '', '0000'), el('span', '', '0000')];
    res.append(...rv); pos.append(...pv);
    const ev = ['UP', 'DOWN', 'MOVE'].map(n => { const s = el('span', '', n); s.style.opacity = '.35'; evt.append(s); return s; });
    return {
      on(k, d) {
        if (k !== 'input') return;
        rv[0].textContent = pad(d.w, 4); rv[1].textContent = pad(d.h, 4); pv[0].textContent = pad(d.x, 4); pv[1].textContent = pad(d.y, 4);
        ev.forEach((s, i) => { s.style.opacity = (['UP', 'DOWN', 'MOVE'][i] === d.ev) ? '1' : '.35'; });
      }
    };
  }
});

reg('media', {
  name: 'Что играет', w: 0.27, defs: {},
  schema: [],
  mount(box) {
    const r1 = el('div', 'row'), t = el('span', 'big', T('Ничего не играет', 'Nothing playing')), tm = el('span', 'fl dim', '--:--');
    const a = el('div', 'dim', ' '); const r3 = el('div', 'row'), pc = el('span', 'lab', '000%'), b = bar(0, 18);
    r1.append(t, tm); r3.append(pc, b); box.append(r1, a, r3);
    return {
      on(k, d) {
        if (k !== 'media') return;
        if (!d.has) { t.textContent = T('Ничего не играет', 'Nothing playing'); a.textContent = ' '; tm.textContent = '--:--'; pc.textContent = '000%'; setBar(b, 0, 18); return; }
        t.textContent = (d.title || '').slice(0, 40); a.textContent = d.artist || d.app || ' ';
        tm.textContent = d.dur > 0 ? mmss(d.dur) : '--:--';
        const p = d.dur > 0 ? d.pos / d.dur * 100 : 0; pc.textContent = pad(p, 3) + '%'; setBar(b, p, 18);
      }
    };
  }
});

reg('spectrum', {
  name: 'Звук (полосы)', w: 0.27, defs: { height: 4, bars: 32 },
  schema: [{ k: 'height', t: 'num', l: 'Высота', min: 1.5, max: 14, step: 0.5 }, { k: 'bars', t: 'num', l: 'Количество полос', min: 8, max: 32, step: 1 }],
  mount(box, o) {
    const cv = el('canvas'); box.append(cv);
    let bands = null;
    const draw = () => {
      const w = box.clientWidth || 300, h = parseFloat(getComputedStyle(box).fontSize) * num(o.height, 4);
      if (cv.width !== Math.round(w) || cv.height !== Math.round(h)) { cv.width = Math.round(w); cv.height = Math.round(h); }
      const g = cv.getContext('2d'); g.clearRect(0, 0, cv.width, cv.height);
      const n = Math.round(num(o.bars, 32)), gap = Math.max(2, cv.width / n * 0.35), bw = (cv.width - gap * (n - 1)) / n;
      g.fillStyle = acc; g.shadowColor = acc; g.shadowBlur = 8;
      for (let i = 0; i < n; i++) {
        const v = bands ? bands[Math.floor(i * bands.length / n)] || 0 : 0;
        const bh = Math.max(2, v * cv.height); g.globalAlpha = .35 + .65 * v;
        g.fillRect(i * (bw + gap), cv.height - bh, bw, bh);
      }
    };
    draw();
    return { on(k, d) { if (k === 'audio') { bands = d.bands; draw(); } } };
  }
});

reg('vu', {
  name: 'Громкость (шкала)', w: 0.27, defs: { label: 'VOL' },
  schema: [{ k: 'label', t: 'text', l: 'Подпись' }],
  mount(box, o) {
    const r = el('div', 'row'), l = el('span', 'lab', o.label), pc = el('span', 'lab', '000%'), b = bar(0, 22);
    r.append(pc, b, l); box.append(r);
    return { on(k, d) { if (k !== 'audio') return; const p = d.muted ? 0 : d.level * 100; pc.textContent = pad(p, 3) + '%'; setBar(b, p, 22); } };
  }
});

reg('log', {
  name: 'Журнал событий', w: 0.32, defs: { lines: 9 },
  schema: [{ k: 'lines', t: 'num', l: 'Сколько строк', min: 3, max: 20, step: 1 }],
  mount(box, o) {
    const items = [], n = Math.round(num(o.lines, 9));
    const render = () => {
      box.textContent = '';
      if (!items.length) { box.append(el('div', 'dim', T('событий пока нет', 'no events yet'))); return; }
      for (const it of items.slice(-n)) {
        const r = el('div', 'logl'), d = new Date(it.t);
        r.append(el('span', 'lv-' + it.lv, it.src || it.lv), el('span', '', it.msg), el('span', 'tm', pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds())));
        box.append(r);
      }
    };
    render();
    return { on(k, d) { if (k !== 'journal') return; for (const it of d.items || []) items.push(it); if (items.length > 60) items.splice(0, items.length - 60); render(); } };
  }
});

reg('sys', {
  name: 'Процессор и память', w: 0.27, defs: {},
  schema: [],
  mount(box) {
    const mk = (n, len) => { const r = el('div', 'row'), l = el('span', 'lab', n), v = el('span', 'lab', '000%'), b = bar(0, len); r.append(l, v, b); box.append(r); return [v, b, len]; };
    const cpu = mk('CPU', 16), ram = mk('RAM', 16); const nr = el('div', 'row'), nl = el('span', 'lab', 'NET'), nv = el('span', '', '↓ 0 ↑ 0 KB/s'); nr.append(nl, nv); box.append(nr);
    return {
      on(k, d) {
        if (k !== 'sys') return;
        cpu[0].textContent = pad(d.cpu, 3) + '%'; setBar(cpu[1], d.cpu, cpu[2]);
        ram[0].textContent = pad(d.ram, 3) + '%'; setBar(ram[1], d.ram, ram[2]);
        nv.textContent = '↓ ' + Math.round(d.netDown) + '  ↑ ' + Math.round(d.netUp) + ' KB/s';
      }
    };
  }
});

reg('text', {
  name: 'Свой текст', w: 0.25, defs: { text: 'CPU {cpu}%  RAM {ram}%\n{title}' },
  schema: [{ k: 'text', t: 'area', l: 'Текст. Вставки: {cpu} {ram} {time} {date} {title} {artist} {temp} {city} {cond} {vol} {track}' }],
  mount(box, o) {
    const out = el('div'); out.style.whiteSpace = 'pre'; box.append(out);
    const D = GS.data;
    const val = {
      cpu: () => D.sys ? Math.round(D.sys.cpu) : '--', ram: () => D.sys ? Math.round(D.sys.ram) : '--',
      time: () => { const z = zoned(''); return z.hour + ':' + z.minute + ':' + z.second; }, date: () => { const z = zoned(''); return z.day + '.' + z.month + '.' + z.year; },
      title: () => D.media && D.media.has ? D.media.title : '', artist: () => D.media && D.media.has ? D.media.artist : '',
      temp: () => D.weather && D.weather.ok ? Math.round(D.weather.temp) + '°' : '--', city: () => D.weather && D.weather.ok ? D.weather.city : '',
      cond: () => D.weather && D.weather.ok ? cond(D.weather.code) : '', vol: () => D.audio ? Math.round(D.audio.vol * 100) : '--',
      track: () => D.media && D.media.has && D.media.dur > 0 ? Math.round(D.media.pos / D.media.dur * 100) : '--',
    };
    return { tick() { out.textContent = String(o.text || '').replace(/\{(\w+)\}/g, (m, k) => val[k] ? val[k]() : m); } };
  }
});

reg('bar', {
  name: 'Шкала', w: 0.25, defs: { label: 'CPU', src: 'cpu', len: 20 },
  schema: [{ k: 'label', t: 'text', l: 'Подпись' }, { k: 'src', t: 'choice', l: 'Что показывать', o: ['cpu', 'ram', 'vol', 'level', 'track', 'day'], ol: ['процессор', 'память', 'громкость системы', 'уровень звука', 'прогресс трека', 'прогресс дня'] }, { k: 'len', t: 'num', l: 'Длина', min: 8, max: 40, step: 1 }],
  mount(box, o) {
    const len = Math.round(num(o.len, 20)), r = el('div', 'row'), l = el('span', 'lab', o.label), v = el('span', 'lab', '000%'), b = bar(0, len);
    r.append(l, v, b); box.append(r);
    const D = GS.data;
    const get = () => ({
      cpu: () => D.sys ? D.sys.cpu : 0, ram: () => D.sys ? D.sys.ram : 0, vol: () => D.audio ? D.audio.vol * 100 : 0, level: () => D.audio ? D.audio.level * 100 : 0,
      track: () => D.media && D.media.has && D.media.dur > 0 ? D.media.pos / D.media.dur * 100 : 0,
      day: () => { const n = new Date(); return (n.getHours() * 3600 + n.getMinutes() * 60 + n.getSeconds()) / 864; }
    }[o.src] || (() => 0))();
    const upd = () => { const p = get(); v.textContent = pad(p, 3) + '%'; setBar(b, p, len); };
    return { tick: upd, on() { upd(); } };
  }
});

// ---------- раскладка ----------
const DEFAULT = { v: 1, widgets: [
  { id: 'a1', type: 'title', x: 0.05, y: 0.085, w: 0.3, opts: { tilt: -1 } },
  { id: 'a2', type: 'clock', x: 0.05, y: 0.185, w: 0.27, opts: { label: 'TIME', panel: true, tilt: 1 } },
  { id: 'a3', type: 'daybar', x: 0.05, y: 0.265, w: 0.27, opts: {} },
  { id: 'a4', type: 'media', x: 0.05, y: 0.8, w: 0.27, opts: { tilt: -1, panel: true } },
  { id: 'a5', type: 'spectrum', x: 0.05, y: 0.885, w: 0.27, opts: {} },
  { id: 'b1', type: 'clock', x: 0.95, y: 0.185, w: 0.27, opts: { label: 'LON', tz: 'Europe/London', align: 'right', panel: true, tilt: -1 } },
  { id: 'b2', type: 'clock', x: 0.95, y: 0.255, w: 0.27, opts: { label: 'NYC', tz: 'America/New_York', align: 'right', panel: true, tilt: -1 } },
  { id: 'b3', type: 'weather', x: 0.95, y: 0.35, w: 0.2, opts: { align: 'right' } },
  { id: 'b4', type: 'display', x: 0.95, y: 0.58, w: 0.2, opts: { align: 'right' } },
  { id: 'b5', type: 'log', x: 0.95, y: 0.72, w: 0.32, opts: { align: 'right', tilt: 1 } },
] };
let layout;
try { layout = JSON.parse(P('layout', '')); if (!layout || !Array.isArray(layout.widgets)) throw 0; } catch (e) { layout = JSON.parse(JSON.stringify(DEFAULT)); }
const inst = new Map();   // id -> {box, wd, api}
let sel = null, saveT = 0;

function mountWidget(wd) {
  const def = REG[wd.type]; if (!def) return;
  const o = { ...def.defs, ...(wd.opts || {}) };
  const box = el('div', 'w');
  if (o.panel) box.classList.add('panel');
  const right = o.align === 'right';
  if (right) box.classList.add('r');
  box.style.width = (wd.w * 100) + '%';
  box.style.top = (wd.y * 100) + '%';
  if (right) box.style.right = ((1 - wd.x) * 100) + '%'; else box.style.left = (wd.x * 100) + '%';
  const sc = num(o.scale, 1); if (sc !== 1) box.style.fontSize = sc + 'em';
  const tilt = num(o.tilt, 0); if (tilt) box.style.transform = 'skewY(' + tilt + 'deg)';
  const api = def.mount(box, o) || {};
  if (EDIT) {
    const rz = el('div', 'rz'); box.append(rz);
    box.addEventListener('pointerdown', ev => startDrag(ev, wd, box, ev.target === rz));
  }
  layer.append(box);
  inst.set(wd.id, { box, wd, api });
  for (const k of Object.keys(GS.data)) if (api.on) { try { api.on(k, GS.data[k]); } catch (e) { } }
  if (api.tick) try { api.tick(); } catch (e) { }
}
function rebuild() {
  layer.textContent = ''; inst.clear();
  for (const wd of layout.widgets) mountWidget(wd);
  if (EDIT) markSel();
}
rebuild();
GS.on('*', (d, k) => { for (const i of inst.values()) if (i.api.on) { try { i.api.on(k, d); } catch (e) { } } });
setInterval(() => { for (const i of inst.values()) if (i.api.tick) { try { i.api.tick(); } catch (e) { } } }, 250);
// редкие «сбои» изображения для настроения
if (!EDIT) (function glitchLoop() {
  setTimeout(() => {
    const all = [...inst.values()];
    if (all.length && parseFloat(P('glitch', '1')) > 0) { const b = all[Math.floor(Math.random() * all.length)].box; b.classList.add('glt'); setTimeout(() => b.classList.remove('glt'), 180); }
    glitchLoop();
  }, 3500 + Math.random() * 6000);
})();

// ---------- редактор ----------
function emit() { clearTimeout(saveT); saveT = setTimeout(() => GS.send({ t: 'layout', v: JSON.stringify(layout) }), 200); }
function markSel() { for (const [id, i] of inst) i.box.classList.toggle('sel', id === sel); }
function startDrag(ev, wd, box, resize) {
  ev.preventDefault(); select(wd.id);
  const r = stage.getBoundingClientRect(), right = (wd.opts || {}).align === 'right';
  const s = { x: ev.clientX, y: ev.clientY, wx: wd.x, wy: wd.y, ww: wd.w };
  box.setPointerCapture(ev.pointerId);
  const snap = v => Math.round(v * 200) / 200;
  const move = e => {
    const dx = (e.clientX - s.x) / r.width, dy = (e.clientY - s.y) / r.height;
    if (resize) wd.w = Math.max(0.05, Math.min(1, snap(s.ww + (right ? -dx : dx))));
    else { wd.x = Math.max(0, Math.min(1, snap(s.wx + dx))); wd.y = Math.max(0, Math.min(0.98, snap(s.wy + dy))); }
    box.style.width = (wd.w * 100) + '%'; box.style.top = (wd.y * 100) + '%';
    if (right) box.style.right = ((1 - wd.x) * 100) + '%'; else box.style.left = (wd.x * 100) + '%';
  };
  const up = () => { box.removeEventListener('pointermove', move); box.removeEventListener('pointerup', up); emit(); };
  box.addEventListener('pointermove', move); box.addEventListener('pointerup', up);
}
function select(id) { sel = id; markSel(); drawPanel(); }
function addWidget(type) {
  const d = REG[type]; const id = 'w' + Date.now().toString(36);
  layout.widgets.push({ id, type, x: right0(), y: 0.4, w: d.w, opts: {} });
  rebuild(); select(id); emit();
}
const right0 = () => 0.4;
function drawPanel() {
  const p = document.getElementById('panel'); p.hidden = false; p.textContent = '';
  const h = (t) => { const e = el('h4', '', t); p.append(e); };
  const btn = (t, f, cls) => { const b = el('button', cls || '', t); b.onclick = f; return b; };
  h('Добавить панель');
  const add = el('div'); for (const [t, d] of Object.entries(REG)) add.append(btn('+ ' + d.name, () => addWidget(t))); p.append(add);
  const wd = layout.widgets.find(x => x.id === sel);
  h('Выбранная панель');
  if (!wd) { p.append(el('div', 'hint', 'Нажмите на панель в предпросмотре. Перетаскивайте её мышью, угловой квадратик меняет ширину.')); }
  else {
    const def = REG[wd.type]; p.append(el('div', '', def.name));
    const o = wd.opts = wd.opts || {};
    const form = (s) => {
      const cur = o[s.k] !== undefined ? o[s.k] : (def.defs[s.k] !== undefined ? def.defs[s.k] : (s.k === 'align' ? 'left' : s.k === 'scale' ? 1 : s.k === 'tilt' ? 0 : s.k === 'panel' ? false : ''));
      p.append(el('label', '', s.l));
      let inp;
      const set = v => { o[s.k] = v; rebuild(); emit(); };
      if (s.t === 'text') { inp = el('input'); inp.type = 'text'; inp.value = cur; inp.onchange = () => set(inp.value); }
      else if (s.t === 'area') { inp = el('textarea'); inp.value = cur; inp.onchange = () => set(inp.value); }
      else if (s.t === 'bool') { inp = el('input'); inp.type = 'checkbox'; inp.checked = !!cur; inp.onchange = () => set(inp.checked); }
      else if (s.t === 'num') { inp = el('input'); inp.type = 'range'; inp.min = s.min; inp.max = s.max; inp.step = s.step; inp.value = cur; inp.onchange = () => set(parseFloat(inp.value)); }
      else if (s.t === 'choice') { inp = el('select'); s.o.forEach((v, i) => { const op = el('option', '', (s.ol || s.o)[i]); op.value = v; inp.append(op); }); inp.value = cur; inp.onchange = () => set(inp.value); }
      p.append(inp);
    };
    for (const s of def.schema) form(s);
    for (const s of COMMON) form(s);
    const row = el('div'); row.style.marginTop = '10px';
    row.append(btn('Дублировать', () => { const c = JSON.parse(JSON.stringify(wd)); c.id = 'w' + Date.now().toString(36); c.y = Math.min(0.95, c.y + 0.05); layout.widgets.push(c); rebuild(); select(c.id); emit(); }),
      btn('Удалить', () => { layout.widgets = layout.widgets.filter(x => x !== wd); sel = null; rebuild(); drawPanel(); emit(); }, 'danger'));
    p.append(row);
  }
  h('Макет');
  p.append(btn('Сбросить к исходному', () => { layout = JSON.parse(JSON.stringify(DEFAULT)); sel = null; rebuild(); drawPanel(); emit(); }, 'danger'));
  p.append(el('div', 'hint', 'Макет сохраняется вместе с обоями кнопкой «Сохранить» в окне Wallpaper Plus. Данные в предпросмотре — настоящие (звук, музыка, погода, курсор).'));
}
if (EDIT) { drawPanel(); addEventListener('keydown', e => { if (e.key === 'Delete' && sel && document.activeElement === document.body) { layout.widgets = layout.widgets.filter(x => x.id !== sel); sel = null; rebuild(); drawPanel(); emit(); } }); }
})();
