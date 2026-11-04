#!/usr/bin/env python3
"""Mask Plus: собирает расширение в один zip для релиза лаунчера.

  python3 tools/mask-plus/pack.py <папка-вывода>
  → <папка-вывода>/MaskPlus-extension.zip и MaskPlus-extension.zip.sha256

Архив детерминированный (одинаковые файлы → одинаковый zip и одинаковая сумма): даты в архиве фиксированы,
порядок файлов отсортирован, переводы строк в текстовых файлах приведены к LF. Благодаря этому лаунчер не видит
«обновление» там, где ничего не менялось (он сравнивает контрольную сумму).
"""
import sys
try:
    sys.stdout.reconfigure(encoding='utf-8')   # консоль Windows (cp1252) не печатает кириллицу
except Exception:
    pass

import hashlib, json, os, sys, zipfile

root = os.path.dirname(os.path.abspath(__file__))
src = os.path.join(root, '..', '..', 'BrowserPlugins', 'MaskPlus')
out = sys.argv[1] if len(sys.argv) > 1 else '.'
os.makedirs(out, exist_ok=True)

manifest = json.load(open(os.path.join(src, 'manifest.json'), encoding='utf-8'))
assert manifest.get('manifest_version') == 3 and manifest.get('version'), 'manifest.json: нет версии'

TEXT = ('.js', '.json', '.html', '.css', '.txt', '.md', '.svg')
files = []
for base, dirs, names in os.walk(src):
    dirs[:] = sorted(d for d in dirs if not d.startswith('.'))
    for n in sorted(names):
        if n.startswith('.'):
            continue
        full = os.path.join(base, n)
        files.append((os.path.relpath(full, src).replace(os.sep, '/'), full))
files.sort()

# нужные файлы должны быть на месте
need = ['manifest.json', 'background.js', 'engine.js', 'popup.html', 'popup.js', 'popup.css', 'countries.js', 'icons/128.png', 'c/DE.js', 'f/111.js', 'ua.js', 'u/win11-chrome.js', 'u/mac-safari.js']
names = {f[0] for f in files}
missing = [n for n in need if n not in names]
assert not missing, 'в расширении не хватает файлов: ' + ', '.join(missing) + ' (запустите tools/mask-plus/generate.py)'

zpath = os.path.join(out, 'MaskPlus-extension.zip')
with zipfile.ZipFile(zpath, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for rel, full in files:
        data = open(full, 'rb').read()
        if rel.endswith(TEXT):
            data = data.replace(b'\r\n', b'\n')
        info = zipfile.ZipInfo(rel, date_time=(2024, 1, 1, 0, 0, 0))
        info.compress_type = zipfile.ZIP_DEFLATED
        info.external_attr = 0o644 << 16
        z.writestr(info, data, compresslevel=9)

h = hashlib.sha256(open(zpath, 'rb').read()).hexdigest()
with open(zpath + '.sha256', 'w', encoding='utf-8', newline='') as f:
    f.write(h + '  MaskPlus-extension.zip')
print('MaskPlus-extension.zip', manifest['version'], len(files), 'файлов', os.path.getsize(zpath), 'байт', h)
