// Mask Plus — движок подмены. Запускается В САМОЙ СТРАНИЦЕ (world MAIN) до её собственных скриптов.
// Настройки приходят от файлов c/<СТРАНА>.js и f/<флаги>.js через временное свойство __gsmask (движок сразу его удаляет).
// Что подменяется: часовой пояс (Date, Intl, Temporal), язык (navigator.language(s), язык по умолчанию у Intl и toLocale*),
// местоположение (navigator.geolocation), система и браузер (navigator.userAgent/platform/vendor/userAgentData; те же значения расширение ставит в
// заголовки запросов). IP-адрес этим не меняется — это делает только шлюз.
(function () {
  'use strict';
  var holder = Object.getOwnPropertyDescriptor(globalThis, '__gsmask');
  if (!holder) return;
  delete globalThis.__gsmask;
  var CFG = holder.value, F = CFG.f || {};
  var TZ = CFG.tz, LOC = CFG.locale;

  // «Паспорт» системы и браузера (если включена подмена): строится из настоящего User-Agent и настоящих «брендов» этого браузера
  var UAI = null;
  if (CFG.ua && typeof CFG.uaBuild === 'function') {
    try {
      var realBrands = null;
      try { var rb = navigator.userAgentData && navigator.userAgentData.brands; if (rb) realBrands = Array.prototype.map.call(rb, function (x) { return { brand: x.brand, version: x.version }; }); } catch (e) { /* небезопасная страница — brands нет */ }
      UAI = CFG.uaBuild(CFG.ua.os, CFG.ua.br, { ua: navigator.userAgent, brands: realBrands });
    } catch (e) { UAI = null; }
  }

  var nativeSrc = new WeakMap();   // подделка → строка «function имя() { [native code] }»
  var done = new WeakSet();        // окна, где подмена уже стоит

  function safe(fn) { try { fn(); } catch (e) { /* одна неудавшаяся подмена не должна ломать страницу */ } }

  function disguise(fn, name, len) {
    Object.defineProperty(fn, 'name', { value: name, configurable: true });
    Object.defineProperty(fn, 'length', { value: len, configurable: true });
    nativeSrc.set(fn, 'function ' + name + '() { [native code] }');
    return fn;
  }
  function def(o, k, v) { Object.defineProperty(o, k, { value: v, writable: true, configurable: true, enumerable: false }); }
  function p2(n) { return n < 10 ? '0' + n : '' + n; }
  function pad(n, w) { var s = '' + n; while (s.length < w) s = '0' + s; return s; }

  function install(W) {
    if (done.has(W)) return;
    done.add(W);

    // --- Function.prototype.toString: подделки выглядят как родные функции ---
    safe(function () {
      var FP = W.Function.prototype, orig = FP.toString;
      var fake = {
        toString() {
          if (nativeSrc.has(this)) return nativeSrc.get(this);
          return orig.call(this);
        }
      }.toString;
      disguise(fake, 'toString', 0);
      def(FP, 'toString', fake);
    });

    var OD = W.Date, P = OD.prototype, DTF = W.Intl.DateTimeFormat;

    // --- Часовой пояс ---
    if (F.tz) safe(function () {
      var gTime = P.getTime, uY = P.getUTCFullYear, uMo = P.getUTCMonth, uD = P.getUTCDate, uDay = P.getUTCDay,
        uH = P.getUTCHours, uMi = P.getUTCMinutes, uS = P.getUTCSeconds, uMs = P.getUTCMilliseconds, sTime = P.setTime;
      var wallFmt = new DTF('en-US', {
        timeZone: TZ, hourCycle: 'h23', era: 'short', year: 'numeric', month: 'numeric', day: 'numeric',
        hour: 'numeric', minute: 'numeric', second: 'numeric'
      });
      var nameFmt = new DTF(F.lang ? LOC : undefined, { timeZone: TZ, timeZoneName: 'long' });
      var cache = new Map();

      // смещение пояса (мс) в момент ts (UTC)
      function offMs(ts) {
        if (ts !== ts) return 0;
        var key = Math.floor(ts / 900000), v = cache.get(key);
        if (v !== undefined) return v;
        v = 0;
        try {
          var p = {}, parts = wallFmt.formatToParts(new OD(ts));
          for (var i = 0; i < parts.length; i++) p[parts[i].type] = parts[i].value;
          var y = +p.year;
          if (p.era && /^B/i.test(p.era)) y = 1 - y;
          var w = new OD(0);
          w.setUTCFullYear(y, p.month - 1, +p.day);
          w.setUTCHours(+p.hour, +p.minute, +p.second, 0);
          v = gTime.call(w) - Math.floor(ts / 1000) * 1000;
        } catch (e) { v = 0; }
        if (cache.size > 4000) cache.clear();
        cache.set(key, v);
        return v;
      }
      function wallDate(t) { return new OD(t + offMs(t)); }              // поля через getUTC… = местное время выбранного пояса
      // местное время → момент UTC. Как в стандарте: при «двойном» часе (осенью) берётся первый, при «пропавшем» (весной) — смещение до перехода
      function wallToUtc(w) {
        if (w !== w) return NaN;
        var a = offMs(w - 86400000), b = offMs(w + 86400000);
        var ua = w - a;
        if (a === b) return ua;
        var ub = w - b, okA = offMs(ua) === a, okB = offMs(ub) === b;
        if (okA && okB) return Math.min(ua, ub);
        return okB ? ub : ua;
      }

      function gmt(t) {
        var m = Math.round(offMs(t) / 60000), a = Math.abs(m);
        return 'GMT' + (m < 0 ? '-' : '+') + p2(Math.floor(a / 60)) + p2(a % 60);
      }
      function nameAt(t) {
        var parts = nameFmt.formatToParts(new OD(t));
        for (var i = 0; i < parts.length; i++) if (parts[i].type === 'timeZoneName') return parts[i].value;
        return TZ;
      }
      // «летнее» ли время в момент t: смещение больше, чем самое маленькое за тот же год
      function isDst(t) {
        var y = uY.call(wallDate(t));
        return offMs(t) > Math.min(offMs(OD.UTC(y, 0, 1)), offMs(OD.UTC(y, 6, 1)));
      }
      // Родной Date.toString берёт название пояса не на дату, а «сегодняшнее» (летнее/зимнее по признаку) — делаем так же
      function zoneName(t) {
        try {
          var dst = isDst(t), now = OD.now(), refs = [now, now + 182 * 86400000];
          for (var i = 0; i < refs.length; i++) if (isDst(refs[i]) === dst) return nameAt(refs[i]);
          return nameAt(t);
        } catch (e) { return TZ; }
      }
      var DOW = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
      var MON = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
      function dateStr(w) {
        var y = uY.call(w);
        return DOW[uDay.call(w)] + ' ' + MON[uMo.call(w)] + ' ' + p2(uD.call(w)) + ' ' + (y < 0 ? '-' + pad(-y, 6) : pad(y, 4));
      }
      function timeStr(w, t) {
        return p2(uH.call(w)) + ':' + p2(uMi.call(w)) + ':' + p2(uS.call(w)) + ' ' + gmt(t) + ' (' + zoneName(t) + ')';
      }

      var M = {}, LEN = {};
      // местные «получатели»
      var G = { getFullYear: uY, getMonth: uMo, getDate: uD, getDay: uDay, getHours: uH, getMinutes: uMi, getSeconds: uS, getMilliseconds: uMs };
      Object.keys(G).forEach(function (k) {
        var u = G[k];
        M[k] = { [k]() { var t = gTime.call(this); return t !== t ? NaN : u.call(wallDate(t)); } }[k];
        LEN[k] = 0;
      });
      M.getYear = { getYear() { var t = gTime.call(this); return t !== t ? NaN : uY.call(wallDate(t)) - 1900; } }.getYear;
      LEN.getYear = 0;
      M.getTimezoneOffset = {
        getTimezoneOffset() {
          var t = gTime.call(this);
          if (t !== t) return NaN;
          var v = -offMs(t) / 60000;
          return v === 0 ? 0 : v;
        }
      }.getTimezoneOffset;
      LEN.getTimezoneOffset = 0;
      // местные «установщики»
      var S = { setFullYear: ['setUTCFullYear', 3], setMonth: ['setUTCMonth', 2], setDate: ['setUTCDate', 1], setHours: ['setUTCHours', 4],
        setMinutes: ['setUTCMinutes', 3], setSeconds: ['setUTCSeconds', 2], setMilliseconds: ['setUTCMilliseconds', 1] };
      Object.keys(S).forEach(function (k) {
        var us = P[S[k][0]];
        M[k] = {
          [k](...args) {
            var t = gTime.call(this);
            if (t !== t && k !== 'setFullYear') return NaN;
            var w = t !== t ? new OD(0) : wallDate(t);
            us.apply(w, args);
            return sTime.call(this, wallToUtc(gTime.call(w)));
          }
        }[k];
        LEN[k] = S[k][1];
      });
      // строки
      M.toString = { toString() { var t = gTime.call(this); if (t !== t) return 'Invalid Date'; var w = wallDate(t); return dateStr(w) + ' ' + timeStr(w, t); } }.toString;
      M.toDateString = { toDateString() { var t = gTime.call(this); return t !== t ? 'Invalid Date' : dateStr(wallDate(t)); } }.toDateString;
      M.toTimeString = { toTimeString() { var t = gTime.call(this); return t !== t ? 'Invalid Date' : timeStr(wallDate(t), t); } }.toTimeString;
      LEN.toString = LEN.toDateString = LEN.toTimeString = 0;
      ['toLocaleString', 'toLocaleDateString', 'toLocaleTimeString'].forEach(function (k) {
        var orig = P[k];
        M[k] = {
          [k](l, o) {
            if (F.lang && l === undefined) l = LOC;
            if (o === undefined) o = { timeZone: TZ };
            else if (o !== null && typeof o === 'object' && o.timeZone === undefined) { var c = Object.create(o); c.timeZone = TZ; o = c; }
            return orig.call(this, l, o);
          }
        }[k];
        LEN[k] = 0;
      });
      Object.keys(M).forEach(function (k) { def(P, k, disguise(M[k], k, LEN[k])); });

      // конструктор Date: «new Date(2024, 0, 1)» и «new Date('2024-01-01T10:00')» — это местное время выбранного пояса
      var ISO = /^([+-]\d{6}|\d{4})-(\d\d)-(\d\d)[T ](\d\d):(\d\d)(?::(\d\d)(?:[.,](\d{1,9}))?)?$/;
      function localIso(s) {
        var m = ISO.exec(s);
        if (!m) return null;
        var mo = +m[2], d = +m[3], h = +m[4], mi = +m[5], sec = +(m[6] || 0);
        if (mo < 1 || mo > 12 || d < 1 || d > 31 || h > 23 || mi > 59 || sec > 59) return null;   // странное — пусть решает сам браузер
        return wallToUtc(OD.UTC(+m[1], mo - 1, d, h, mi, sec, +((m[7] || '0') + '00').slice(0, 3)));
      }
      var parseFake = disguise({ parse(s) { var str = String(s), u = localIso(str); return u === null ? OD.parse(str) : u; } }.parse, 'parse', 1);
      var DP = new Proxy(OD, {
        construct(t, args, nt) {
          var real = nt === DP ? t : nt;
          if (args.length >= 2) return Reflect.construct(t, [wallToUtc(OD.UTC.apply(OD, args))], real);
          if (args.length === 1 && typeof args[0] === 'string') {
            var u = localIso(args[0]);
            if (u !== null) return Reflect.construct(t, [u], real);
          }
          return Reflect.construct(t, args, real);
        },
        apply() { return P.toString.call(new OD(OD.now())); },
        get(t, k) { return k === 'parse' ? parseFake : Reflect.get(t, k, t); }
      });
      nativeSrc.set(DP, 'function Date() { [native code] }');
      def(P, 'constructor', DP);
      def(W, 'Date', DP);

      // Temporal (есть в новых Chrome)
      if (W.Temporal && W.Temporal.Now) {
        var N = W.Temporal.Now;
        var TZID = new DTF('en-US', { timeZone: TZ }).resolvedOptions().timeZone;   // так Chrome называет пояс (например, Asia/Calcutta)
        if (typeof N.timeZoneId === 'function') def(N, 'timeZoneId', disguise({ timeZoneId() { return TZID; } }.timeZoneId, 'timeZoneId', 0));
        ['zonedDateTimeISO', 'plainDateTimeISO', 'plainDateISO', 'plainTimeISO'].forEach(function (k) {
          var o = N[k];
          if (typeof o !== 'function') return;
          def(N, k, disguise({ [k](tz) { return o.call(this, tz === undefined ? TZID : tz); } }[k], k, 0));
        });
      }
    });

    // --- Язык ---
    if (F.lang) {
      safe(function () {
        var langs = Object.freeze(CFG.langs.slice());
        function getter(proto, name, value) {
          var g = Object.getOwnPropertyDescriptor({ get [name]() { return value; } }, name).get;
          nativeSrc.set(g, 'function get ' + name + '() { [native code] }');
          Object.defineProperty(proto, name, { get: g, set: undefined, configurable: true, enumerable: true });
        }
        getter(W.Navigator.prototype, 'language', langs[0]);
        getter(W.Navigator.prototype, 'languages', langs);
      });
    }

    // --- Система и браузер ---
    if (UAI) safe(function () {
      var N = W.Navigator.prototype;
      function g(proto, name, value) {
        var fn = Object.getOwnPropertyDescriptor({ get [name]() { return value; } }, name).get;
        nativeSrc.set(fn, 'function get ' + name + '() { [native code] }');
        Object.defineProperty(proto, name, { get: fn, set: undefined, configurable: true, enumerable: true });
      }
      g(N, 'userAgent', UAI.ua);
      g(N, 'appVersion', UAI.appVersion);
      g(N, 'platform', UAI.platform);
      g(N, 'vendor', UAI.vendor);
      g(N, 'productSub', UAI.productSub);
      if (UAI.oscpu) { g(N, 'oscpu', UAI.oscpu); g(N, 'buildID', UAI.buildID); }
      var UADc = W.NavigatorUAData;
      if (!UAI.hasUAD) {
        // Firefox и Safari не умеют userAgentData и не имеют window.chrome — убираем следы Chromium, которые убираются
        safe(function () { delete N.userAgentData; });
        safe(function () { delete W.NavigatorUAData; });
        safe(function () { delete W.chrome; });
      } else if (UADc) {
        var P = UADc.prototype, U = UAI.uad, realGhev = P.getHighEntropyValues;
        // как у родного: каждый раз новый замороженный массив (сами записи не заморожены)
        var fresh = Object.getOwnPropertyDescriptor({ get brands() { return Object.freeze(U.brands.map(function (b) { return { brand: b.brand, version: b.version }; })); } }, 'brands').get;
        nativeSrc.set(fresh, 'function get brands() { [native code] }');
        Object.defineProperty(P, 'brands', { get: fresh, set: undefined, configurable: true, enumerable: true });
        g(P, 'mobile', false);
        g(P, 'platform', U.platform);
        def(P, 'getHighEntropyValues', disguise({
          getHighEntropyValues(hints) {
            if (arguments.length < 1) return W.Promise.reject(new W.TypeError("Failed to execute 'getHighEntropyValues' on 'NavigatorUAData': 1 argument required, but only 0 present."));
            var want = Array.prototype.slice.call(hints || []);
            return realGhev.call(this, ['fullVersionList', 'uaFullVersion']).then(function (real) {
              var out = { brands: U.brands.map(function (b) { return { brand: b.brand, version: b.version }; }), mobile: false, platform: U.platform };
              want.forEach(function (h) {
                if (h === 'architecture') out.architecture = U.arch;
                else if (h === 'bitness') out.bitness = U.bitness;
                else if (h === 'model') out.model = '';
                else if (h === 'platformVersion') out.platformVersion = U.platformVersion;
                else if (h === 'uaFullVersion') out.uaFullVersion = real.uaFullVersion;
                else if (h === 'fullVersionList') out.fullVersionList = UAI.swapFull(real.fullVersionList);
                else if (h === 'wow64') out.wow64 = false;
                else if (h === 'formFactors') out.formFactors = U.formFactors.slice();
              });
              return out;
            });
          }
        }.getHighEntropyValues, 'getHighEntropyValues', 1));
        def(P, 'toJSON', disguise({ toJSON() { return { brands: U.brands.map(function (b) { return { brand: b.brand, version: b.version }; }), mobile: false, platform: U.platform }; } }.toJSON, 'toJSON', 0));
      }
    });

    // --- Intl: язык по умолчанию (если подменяем язык) и часовой пояс по умолчанию у DateTimeFormat (если подменяем время) ---
    if (F.lang || F.tz) safe(function () {
        var I = W.Intl;
        function fix(args, withTz) {
          var a = Array.prototype.slice.call(args);
          if (F.lang && a[0] === undefined) a[0] = LOC;
          if (withTz && F.tz) {
            var o = a[1];
            if (o === undefined) a[1] = { timeZone: TZ };
            else if (o !== null && typeof o === 'object' && o.timeZone === undefined) { var c = Object.create(o); c.timeZone = TZ; a[1] = c; }
          }
          return a;
        }
        (F.lang ? ['DateTimeFormat', 'NumberFormat', 'Collator', 'PluralRules', 'RelativeTimeFormat', 'ListFormat', 'DisplayNames', 'Segmenter'] : ['DateTimeFormat']).forEach(function (name) {
          var T = I[name];
          if (typeof T !== 'function') return;
          var withTz = name === 'DateTimeFormat';
          var px = new Proxy(T, {
            construct(t, args, nt) { return Reflect.construct(t, fix(args, withTz), nt === px ? t : nt); },
            apply(t, self, args) { return Reflect.apply(t, self, fix(args, withTz)); }
          });
          nativeSrc.set(px, 'function ' + name + '() { [native code] }');
          def(T.prototype, 'constructor', px);
          def(I, name, px);
        });
        if (!F.lang) return;
        [[W.Number.prototype, 'toLocaleString', 0], [W.String.prototype, 'localeCompare', 1]].forEach(function (x) {
          var o = x[0][x[1]];
          def(x[0], x[1], disguise({
            [x[1]](a, l, opt) {
              if (x[1] === 'localeCompare') return o.call(this, a, l === undefined ? LOC : l, opt);
              return o.call(this, a === undefined ? LOC : a, l);
            }
          }[x[1]], x[1], x[2]));
        });
        if (W.BigInt) {
          var bo = W.BigInt.prototype.toLocaleString;
          def(W.BigInt.prototype, 'toLocaleString', disguise({ toLocaleString(l, o) { return bo.call(this, l === undefined ? LOC : l, o); } }.toLocaleString, 'toLocaleString', 0));
        }
      });

    // --- Местоположение ---
    if (F.geo) safe(function () {
      if (!W.Geolocation || !W.GeolocationPosition || !W.GeolocationCoordinates) return;
      var G = W.Geolocation.prototype, st = W.setTimeout, si = W.setInterval, ci = W.clearInterval, now = OD.now;
      var lat = CFG.lat + (Math.random() - 0.5) * 0.03, lon = CFG.lon + (Math.random() - 0.5) * 0.04;
      var acc = 25 + Math.floor(Math.random() * 90);
      function pos() {
        var c = Object.create(W.GeolocationCoordinates.prototype);
        var vals = { latitude: lat, longitude: lon, accuracy: acc, altitude: null, altitudeAccuracy: null, heading: null, speed: null };
        Object.keys(vals).forEach(function (k) { Object.defineProperty(c, k, { value: vals[k], enumerable: true }); });
        Object.defineProperty(c, 'toJSON', { value: function () { return vals; }, enumerable: false });
        var p = Object.create(W.GeolocationPosition.prototype);
        Object.defineProperty(p, 'coords', { value: c, enumerable: true });
        Object.defineProperty(p, 'timestamp', { value: now(), enumerable: true });
        Object.defineProperty(p, 'toJSON', { value: function () { return { coords: vals, timestamp: this.timestamp }; }, enumerable: false });
        return p;
      }
      function need(name, args) {
        if (args.length < 1) throw new TypeError("Failed to execute '" + name + "' on 'Geolocation': 1 argument required, but only 0 present.");
        if (typeof args[0] !== 'function') throw new TypeError("Failed to execute '" + name + "' on 'Geolocation': The callback provided as parameter 1 is not a function.");
      }
      var watches = new Map(), nextId = 1;
      def(G, 'getCurrentPosition', disguise({
        getCurrentPosition(ok) { need('getCurrentPosition', arguments); st.call(W, function () { ok(pos()); }, 80 + Math.random() * 250); }
      }.getCurrentPosition, 'getCurrentPosition', 1));
      def(G, 'watchPosition', disguise({
        watchPosition(ok) {
          need('watchPosition', arguments);
          var id = nextId++;
          st.call(W, function () { if (watches.has(id)) ok(pos()); }, 80 + Math.random() * 250);
          watches.set(id, si.call(W, function () { ok(pos()); }, 8000));
          return id;
        }
      }.watchPosition, 'watchPosition', 1));
      def(G, 'clearWatch', disguise({
        clearWatch(id) { if (watches.has(id)) { ci.call(W, watches.get(id)); watches.delete(id); } }
      }.clearWatch, 'clearWatch', 1));
    });
  }

  install(globalThis);

  // Страницы любят брать «чистые» функции из только что созданного iframe: на лету ставим подмену и туда.
  safe(function () {
    function hook(Ctor, prop, getWin) {
      if (typeof Ctor !== 'function') return;
      var d = Object.getOwnPropertyDescriptor(Ctor.prototype, prop);
      if (!d || !d.get) return;
      var g = d.get;
      var fake = Object.getOwnPropertyDescriptor({
        get [prop]() {
          var v = g.call(this);
          try { var w = getWin(v); if (w) install(w); } catch (e) { /* чужой источник — там своя копия движка */ }
          return v;
        }
      }, prop).get;
      nativeSrc.set(fake, 'function get ' + prop + '() { [native code] }');
      Object.defineProperty(Ctor.prototype, prop, { get: fake, set: undefined, configurable: true, enumerable: d.enumerable });
    }
    hook(HTMLIFrameElement, 'contentWindow', function (w) { return w; });
    hook(HTMLIFrameElement, 'contentDocument', function (doc) { return doc && doc.defaultView; });
  });
})();
