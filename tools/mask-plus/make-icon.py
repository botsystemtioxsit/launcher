#!/usr/bin/env python3
"""Значок Mask Plus (киберпанк-маска) → BrowserPlugins/MaskPlus/icons/{16,32,48,128}.png.
Запуск: python3 tools/mask-plus/make-icon.py"""
import sys
try:
    sys.stdout.reconfigure(encoding='utf-8')   # консоль Windows (cp1252) не печатает кириллицу
except Exception:
    pass

import os
from PIL import Image, ImageDraw, ImageFilter

BG, CYAN, MAG = (6, 5, 13, 255), (0, 240, 255, 255), (255, 42, 157, 255)
out = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'BrowserPlugins', 'MaskPlus', 'icons')
os.makedirs(out, exist_ok=True)

S = 1024
def draw():
    im = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle((24, 24, S - 24, S - 24), 150, fill=BG)
    # свечение маски
    glow = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    g = ImageDraw.Draw(glow)
    mask = [(150, 360), (330, 290), (512, 330), (694, 290), (874, 360), (850, 560), (740, 740), (600, 820), (512, 840), (424, 820), (284, 740), (174, 560)]
    g.polygon(mask, outline=CYAN, width=60)
    glow = glow.filter(ImageFilter.GaussianBlur(28))
    im.alpha_composite(glow)
    d = ImageDraw.Draw(im)
    d.polygon(mask, fill=(10, 36, 48, 255), outline=CYAN, width=34)
    # глаза-прорези
    d.polygon([(250, 470), (440, 500), (420, 590), (290, 580)], fill=BG, outline=MAG, width=22)
    d.polygon([(774, 470), (584, 500), (604, 590), (734, 580)], fill=BG, outline=MAG, width=22)
    # линия-«щель» на лбу и подбородке
    d.line([(400, 400), (624, 400)], fill=CYAN, width=22)
    d.line([(430, 720), (594, 720)], fill=MAG, width=22)
    return im

big = draw()
for n in (16, 32, 48, 128):
    big.resize((n, n), Image.LANCZOS).save(os.path.join(out, '%d.png' % n))
print('значки готовы')
