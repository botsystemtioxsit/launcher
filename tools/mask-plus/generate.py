#!/usr/bin/env python3
"""Mask Plus: собирает из countries.json файлы расширения.

  BrowserPlugins/MaskPlus/c/<CC>.js   — страна (часовой пояс, язык, место), по файлу на страну
  BrowserPlugins/MaskPlus/f/<tlg>.js  — какие подмены включены (t = время, l = язык, g = место): 8 файлов
  BrowserPlugins/MaskPlus/countries.js — список стран для окна расширения

Расширение подключает на страницу три файла подряд: страна → флаги → engine.js. Они выполняются в самой
странице до её скриптов, поэтому настройки нельзя прочитать из хранилища — они «запечены» в файлы.
Запуск: python3 tools/mask-plus/generate.py
"""
import sys
try:
    sys.stdout.reconfigure(encoding='utf-8')   # консоль Windows (cp1252) не печатает кириллицу
except Exception:
    pass

import json, os, re, subprocess, itertools

root = os.path.dirname(os.path.abspath(__file__))
out = os.path.join(root, '..', '..', 'BrowserPlugins', 'MaskPlus')
countries = json.load(open(os.path.join(root, 'countries.json'), encoding='utf-8'))

seen = set()
for c in countries:
    assert re.fullmatch(r'[A-Z]{2}', c['cc']), c
    assert c['cc'] not in seen, 'дубль ' + c['cc']
    seen.add(c['cc'])
    assert re.fullmatch(r'[a-z]{2,3}-[A-Z]{2}', c['locale']), c
    assert c['langs'][0] == c['locale'], c
    assert -90 <= c['lat'] <= 90 and -180 <= c['lon'] <= 180, c

# Часовые пояса и языки проверяет сам Intl (node есть на раннере GitHub; без node проверка пропускается)
try:
    code = ("for (const c of %s) { new Intl.DateTimeFormat(c.locale, {timeZone: c.tz}); "
            "for (const l of c.langs) Intl.getCanonicalLocales(l); }" % json.dumps(countries))
    subprocess.run(['node', '-e', code], check=True, capture_output=True)
except FileNotFoundError:
    print('node не найден — часовые пояса не проверены')
except subprocess.CalledProcessError as e:
    raise SystemExit('Неверный пояс или язык в countries.json: ' + e.stderr.decode('utf-8', 'replace')[:400])

def js(o):
    return json.dumps(o, ensure_ascii=False, separators=(',', ':'))

for d in ('c', 'f'):
    p = os.path.join(out, d)
    os.makedirs(p, exist_ok=True)
    for f in os.listdir(p):
        if f.endswith('.js'):
            os.remove(os.path.join(p, f))

for c in countries:
    cfg = {k: c[k] for k in ('cc', 'tz', 'locale', 'langs', 'lat', 'lon')}
    with open(os.path.join(out, 'c', c['cc'] + '.js'), 'w', encoding='utf-8', newline='\n') as f:
        f.write("Object.defineProperty(globalThis,'__gsmask',{value:" + js(cfg) + ",configurable:true});\n")

for t, l, g in itertools.product((0, 1), repeat=3):
    name = '%d%d%d' % (t, l, g)
    with open(os.path.join(out, 'f', name + '.js'), 'w', encoding='utf-8', newline='\n') as f:
        f.write("globalThis.__gsmask.f={tz:%d,lang:%d,geo:%d};\n" % (t, l, g))

ui = [{k: c[k] for k in ('cc', 'name', 'city', 'tz', 'locale', 'langs')} for c in countries]
with open(os.path.join(out, 'countries.js'), 'w', encoding='utf-8', newline='\n') as f:
    f.write('// Создано tools/mask-plus/generate.py из countries.json — руками не править.\n')
    f.write('const MASK_COUNTRIES = ' + json.dumps(ui, ensure_ascii=False, indent=1) + ';\n')

print('стран:', len(countries), '· файлов c/:', len(countries), '· f/: 8')
