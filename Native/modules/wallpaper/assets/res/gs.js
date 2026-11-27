// Ghost Shell SDK для страниц обоев (подставляется в каждую страницу самим Wallpaper Plus; в обои его подключать не нужно).
// Страница получает данные компьютера только если в манифесте обоев перечислено, что ей нужно ("Telemetry": ["sys","audio","media","input","journal","weather"]).
//   GS.on('sys', d => ...)       d: {cpu, ram, ramUsedMB, ramTotalMB, netDown, netUp, uptime}          раз в секунду
//   GS.on('audio', d => ...)     d: {level, bands[32], vol, muted, active}                              ~25 раз в секунду
//   GS.on('media', d => ...)     d: {has, title, artist, album, app, playing, pos, dur}                 раз в секунду
//   GS.on('input', d => ...)     d: {x, y, w, h, ev: 'MOVE'|'DOWN'|'UP'|'IDLE', btn}                    ~20 раз в секунду
//   GS.on('journal', d => ...)   d: {items: [{id, t, lv: 'INFO'|'WARN'|'ERROR', src, msg}]}             новые события
//   GS.on('weather', d => ...)   d: {ok, city, temp, code, prec, wind, sunrise, sunset, err}            раз в 10 минут
//   GS.data.<вид>                последнее полученное значение;  GS.send({...}) — сообщение окну плагина (только в редакторе).
// Без Wallpaper Plus (обычный браузер) или с ?mock=1 приходят выдуманные данные — так обои проверяют и рисуют без плагина.
(function () {
  if (window.GS) return;
  var subs = [], last = {};
  var GS = window.GS = {
    data: last,
    on: function (kind, cb) { subs.push({ k: kind, cb: cb }); if (kind !== '*' && last[kind] !== undefined) { try { cb(last[kind], kind); } catch (e) { } } },
    send: function (obj) { send(obj); },
    isHost: false
  };
  var isTop = window.top === window;
  var host = !!(window.chrome && window.chrome.webview);
  var q = new URLSearchParams(location.search);
  var wantMock = q.get('mock') === '1';

  var hist = [];   // последние события журнала: новому дочернему окну (слою) отдаются сразу
  function deliver(kind, data) {
    last[kind] = data;
    if (kind === 'journal' && data && data.items) { hist = hist.concat(data.items).slice(-40); }
    for (var i = 0; i < subs.length; i++) {
      var s = subs[i];
      if (s.k === kind || s.k === '*') { try { s.cb(data, kind); } catch (e) { } }
    }
  }
  function relay(msg) {
    for (var i = 0; i < window.frames.length; i++) { try { window.frames[i].postMessage(msg, '*'); } catch (e) { } }
  }
  function onMsg(msg) {
    if (!msg || msg.gs !== 1 || typeof msg.k !== 'string') return;
    GS.isHost = true;
    deliver(msg.k, msg.d);
    relay(msg);
  }
  function send(obj) {
    try {
      if (host && isTop) window.chrome.webview.postMessage(obj);
      else if (!isTop) window.top.postMessage({ gsUp: 1, d: obj }, '*');
    } catch (e) { }
  }
  if (host && isTop) window.chrome.webview.addEventListener('message', function (e) { onMsg(e.data); });
  window.addEventListener('message', function (e) {
    var m = e.data;
    if (!m) return;
    if (m.gsHello === 1 && isTop && e.source) {
      // слой загрузился позже, чем пришли данные: отдаём ему всё, что уже известно
      for (var k in last) { try { e.source.postMessage({ gs: 1, k: k, d: k === 'journal' ? { items: hist } : last[k] }, '*'); } catch (_) { } }
      return;
    }
    if (m.gsUp === 1) { if (isTop) send(m.d); else try { window.top.postMessage(m, '*'); } catch (_) { } return; }
    if (!isTop && e.source === window.parent) onMsg(m);
  });

  if (!isTop) { try { window.parent.postMessage({ gsHello: 1 }, '*'); } catch (e) { } }

  // ---------- выдуманные данные ----------
  if (isTop && (!host || wantMock)) startMock();
  function startMock() {
    GS.isHost = false;
    var t0 = Date.now(), pos = 1693, bands = new Array(32).fill(0), mx = 0, my = 0, ev = 'IDLE', evT = 0, jid = 0;
    var mon = { w: screen.width || 1920, h: screen.height || 1080 };
    addEventListener('mousemove', function (e) { mx = e.clientX / innerWidth * mon.w; my = e.clientY / innerHeight * mon.h; ev = 'MOVE'; evT = Date.now(); });
    addEventListener('mousedown', function () { ev = 'DOWN'; evT = Date.now(); });
    addEventListener('mouseup', function () { ev = 'UP'; evT = Date.now(); });
    function emit(k, d) { deliver(k, d); relay({ gs: 1, k: k, d: d }); }
    var now = new Date(), day = new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime();
    emit('weather', { ok: true, city: 'Moscow', temp: 19, code: 2, prec: 0, wind: 3.2, sunrise: day + (8 * 60 + 48) * 60000, sunset: day + (22 * 60 + 3) * 60000, err: '' });
    var msgs = [['INFO', 'FOCUS', 'chrome'], ['INFO', 'START', 'notepad'], ['INFO', 'EXIT', 'notepad'], ['INFO', 'NET', 'online'], ['WARN', 'CPU', 'load 92%'], ['INFO', 'PLAY', 'DeadP47 — Cyberpunk'], ['WARN', 'MEM', 'usage 88%'], ['ERROR', 'NET', 'offline'], ['INFO', 'VOL', '45%'], ['INFO', 'FOCUS', 'explorer']];
    function journal(n) {
      var items = [];
      for (var i = 0; i < n; i++) { var m = msgs[Math.floor(Math.random() * msgs.length)]; items.push({ id: ++jid, t: Date.now() - (n - 1 - i) * 4000, lv: m[0], src: m[1], msg: m[2] }); }
      emit('journal', { items: items });
    }
    journal(8);
    setInterval(function () { journal(1); }, 2600);
    setInterval(function () {
      var t = (Date.now() - t0) / 1000;
      emit('sys', { cpu: 28 + 18 * Math.sin(t / 3) + Math.random() * 6, ram: 56 + 4 * Math.sin(t / 9), ramUsedMB: 9100, ramTotalMB: 16000, netDown: 120 + Math.random() * 400, netUp: 30 + Math.random() * 90, uptime: 86400 + t });
      pos = (pos + 1) % 2400;
      emit('media', { has: true, title: 'ВОЙНА ЗА ОЗВУЧКУ КИБЕРПАНКА', artist: 'DeadP47', album: '', app: 'Chrome', playing: true, pos: pos, dur: 2400 });
    }, 1000);
    setInterval(function () {
      var t = (Date.now() - t0) / 1000, lvl = 0;
      for (var i = 0; i < 32; i++) {
        var target = Math.max(0, (0.55 + 0.45 * Math.sin(t * 3.1 + i * 0.7)) * Math.random() * (1 - i / 48));
        bands[i] += (target - bands[i]) * 0.5; lvl += bands[i];
      }
      emit('audio', { level: Math.min(1, lvl / 14), bands: bands.slice(), vol: 0.23, muted: false, active: true });
      if (Date.now() - evT > 700) ev = 'IDLE';
      emit('input', { x: Math.round(mx), y: Math.round(my), w: mon.w, h: mon.h, ev: ev, btn: '' });
    }, 40);
  }
})();
