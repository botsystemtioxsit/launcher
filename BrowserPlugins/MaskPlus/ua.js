// Mask Plus — «паспорт» системы и браузера: из выбора (ОС + браузер) собирает строку User-Agent, значения navigator.* и Client Hints.
// Один и тот же код нужен двум местам, поэтому он общий: движок в странице (даёт страницам navigator.userAgent, platform, userAgentData)
// и фон расширения (ставит те же значения в заголовки запросов). Версия Chrome всегда настоящая — берётся из браузера: у подмены не должно быть
// «чужой» версии, которую выдаёт расхождение с возможностями движка. Версия Firefox — оценка: у Firefox версия всегда на 3 выше версии Chrome
// (оба выходят раз в 4 недели; проверено по датам выпусков 2025 года). Версию Safari (SAFARI) раз в полгода нужно освежать.
(function (root) {
  'use strict';
  var SAFARI = '26.0';
  var OS = {
    win11: { name: 'Windows', ua: 'Windows NT 10.0; Win64; x64', fx: 'Windows NT 10.0; Win64; x64', platform: 'Win32', pv: '19.0.0', arch: 'x86', app: 'Windows', oscpu: 'Windows NT 10.0; Win64; x64' },
    win10: { name: 'Windows', ua: 'Windows NT 10.0; Win64; x64', fx: 'Windows NT 10.0; Win64; x64', platform: 'Win32', pv: '10.0.0', arch: 'x86', app: 'Windows', oscpu: 'Windows NT 10.0; Win64; x64' },
    mac: { name: 'macOS', ua: 'Macintosh; Intel Mac OS X 10_15_7', fx: 'Macintosh; Intel Mac OS X 10.15', platform: 'MacIntel', pv: '15.5.0', arch: 'arm', app: 'Macintosh', oscpu: 'Intel Mac OS X 10.15' },
    linux: { name: 'Linux', ua: 'X11; Linux x86_64', fx: 'X11; Linux x86_64', platform: 'Linux x86_64', pv: '6.8.0', arch: 'x86', app: 'X11', oscpu: 'Linux x86_64' }
  };
  var CHROMIUM_FAMILY = { chrome: 'Google Chrome', edge: 'Microsoft Edge' };
  var HIGH_ENTROPY = ['sec-ch-ua-platform-version', 'sec-ch-ua-arch', 'sec-ch-ua-bitness', 'sec-ch-ua-model', 'sec-ch-ua-full-version-list', 'sec-ch-ua-full-version', 'sec-ch-ua-wow64', 'sec-ch-ua-form-factors'];
  var LOW_ENTROPY = ['sec-ch-ua', 'sec-ch-ua-mobile', 'sec-ch-ua-platform'];

  var GREASE = /Not.?A.?Brand/i;
  // Список «брендов» настоящего браузера с подменённым названием: «заглушка» и Chromium остаются как есть, вместо Chrome/Edge/Brave — выбранный бренд
  function swap(list, name) {
    var out = [], placed = false, chromiumV = null;
    for (var i = 0; i < list.length; i++) {
      var b = list[i];
      if (/^Chromium$/i.test(b.brand)) { chromiumV = b.version; out.push({ brand: b.brand, version: b.version }); }
      else if (GREASE.test(b.brand)) out.push({ brand: b.brand, version: b.version });
      else if (!placed) { out.push({ brand: name, version: b.version }); placed = true; }
    }
    if (!placed && chromiumV !== null) out.push({ brand: name, version: chromiumV });
    return out;
  }
  function header(list) { return list.map(function (b) { return '"' + b.brand + '";v="' + b.version + '"'; }).join(', '); }

  // os: win11 | win10 | mac | linux; br: chrome | edge | firefox | safari (Safari — только на macOS); real: { ua, brands }
  function build(os, br, real) {
    if (!OS[os]) os = 'win10';
    if (br !== 'chrome' && br !== 'edge' && br !== 'firefox' && br !== 'safari') br = 'chrome';
    if (br === 'safari') os = 'mac';
    var O = OS[os];
    var m = /Chrome\/(\d+)/.exec(real && real.ua || '');
    var major = m ? parseInt(m[1], 10) : 140;
    var r = { os: os, br: br, platform: O.platform, hasUAD: false, oscpu: null, buildID: null, uad: null, chUa: null, swapFull: null, strip: [] };
    if (br === 'firefox') {
      var ff = (major + 3) + '.0';
      r.ua = 'Mozilla/5.0 (' + O.fx + '; rv:' + ff + ') Gecko/20100101 Firefox/' + ff;
      r.appVersion = '5.0 (' + O.app + ')';
      r.vendor = ''; r.productSub = '20100101'; r.oscpu = O.oscpu; r.buildID = '20181001000000';
      r.strip = LOW_ENTROPY.concat(HIGH_ENTROPY);
    } else if (br === 'safari') {
      r.ua = 'Mozilla/5.0 (' + O.ua + ') AppleWebKit/605.1.15 (KHTML, like Gecko) Version/' + SAFARI + ' Safari/605.1.15';
      r.appVersion = r.ua.slice(8);
      r.vendor = 'Apple Computer, Inc.'; r.productSub = '20030107';
      r.strip = LOW_ENTROPY.concat(HIGH_ENTROPY);
    } else {
      r.ua = 'Mozilla/5.0 (' + O.ua + ') AppleWebKit/537.36 (KHTML, like Gecko) Chrome/' + major + '.0.0.0 Safari/537.36' + (br === 'edge' ? ' Edg/' + major + '.0.0.0' : '');
      r.appVersion = r.ua.slice(8);
      r.vendor = 'Google Inc.'; r.productSub = '20030107';
      var name = CHROMIUM_FAMILY[br];
      var base = real && real.brands && real.brands.length ? real.brands : [{ brand: 'Not.A/Brand', version: '8' }, { brand: 'Chromium', version: String(major) }, { brand: 'Google Chrome', version: String(major) }];
      var brands = swap(base, name);
      r.hasUAD = true;
      r.uad = { brands: brands, mobile: false, platform: O.name, platformVersion: O.pv, arch: O.arch, bitness: '64', model: '', wow64: false, formFactors: ['Desktop'], brandName: name };
      r.chUa = header(brands);
      r.chPlatform = '"' + O.name + '"';
      r.strip = HIGH_ENTROPY;
      r.swapFull = function (list) { return swap(list || [], name); };
    }
    return r;
  }

  // В странице (world MAIN) ua.js выполняется после файла с настройками — кладёт сборщик рядом с ними; в фоне — отдельный объект
  if (root.__gsmask && typeof root.__gsmask === 'object') root.__gsmask.uaBuild = build;
  else root.MaskUA = { build: build, LOW_ENTROPY: LOW_ENTROPY, HIGH_ENTROPY: HIGH_ENTROPY };
})(globalThis);
