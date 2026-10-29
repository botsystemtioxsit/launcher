#!/usr/bin/env python3
# Рисует значки программ Ghost Shell (лаунчер, Терминал, плагины) в киберпанк-стиле владельца и собирает .ico (16–256).
# Запуск: python3 tools/make-icons.py  (нужен Pillow). Готовые .ico лежат рядом с проектами; этот файл — чтобы их можно было перерисовать.
import math, os
from PIL import Image, ImageDraw, ImageFilter, ImageChops

S = 1024
BG1, BG2 = (6, 5, 13), (24, 18, 52)
LINE = (45, 32, 96)
CYAN, MAG, GREEN, YEL = (0, 240, 255), (255, 42, 157), (44, 255, 154), (255, 214, 64)

def base():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    grad = Image.new("RGBA", (S, S))
    gd = ImageDraw.Draw(grad)
    for y in range(S):
        t = y / (S - 1)
        gd.line([(0, y), (S, y)], fill=tuple(int(BG1[i] + (BG2[i] - BG1[i]) * t) for i in range(3)) + (255,))
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([24, 24, S - 24, S - 24], radius=170, fill=255)
    img.paste(grad, (0, 0), mask)
    ImageDraw.Draw(img).rounded_rectangle([24, 24, S - 24, S - 24], radius=170, outline=LINE + (255,), width=12)
    return img, mask

def glow(img, layer, color, radius=26, strength=1.0):
    a = layer.split()[3]
    g = Image.new("RGBA", (S, S), color + (0,))
    g.putalpha(a.filter(ImageFilter.GaussianBlur(radius)).point(lambda v: min(255, int(v * strength))))
    img.alpha_composite(g)
    img.alpha_composite(layer)

def layer(): return Image.new("RGBA", (S, S), (0, 0, 0, 0))

def ghost():
    img, _ = base()
    l = layer(); d = ImageDraw.Draw(l)
    # тело: купол сверху, прямые бока, зубцы снизу
    cx, top, w, bottom = S // 2, 250, 460, 790
    d.pieslice([cx - w // 2, top, cx + w // 2, top + w], 180, 360, fill=CYAN + (255,))
    d.rectangle([cx - w // 2, top + w // 2, cx + w // 2, bottom - 70], fill=CYAN + (255,))
    n = 4; step = w / n
    pts = [(cx - w // 2, bottom - 70)]
    for i in range(n):
        x0 = cx - w // 2 + i * step
        pts += [(x0 + step / 2, bottom), (x0 + step, bottom - 70)]
    d.polygon(pts + [(cx + w // 2, bottom - 80), (cx - w // 2, bottom - 80)], fill=CYAN + (255,))
    glow(img, l, CYAN, 30, 0.9)
    e = ImageDraw.Draw(img)
    for dx in (-95, 95):
        e.rounded_rectangle([cx + dx - 42, 440, cx + dx + 42, 560], radius=36, fill=(6, 5, 13, 255))
        e.ellipse([cx + dx - 14, 470, cx + dx + 14, 498], fill=MAG + (255,))
    e.rounded_rectangle([cx - 70, 620, cx + 70, 650], radius=14, fill=MAG + (255,))
    return img

def terminal():
    img, _ = base()
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([150, 230, S - 150, S - 230], radius=60, outline=CYAN + (255,), width=18)
    d.line([(150, 340), (S - 150, 340)], fill=CYAN + (255,), width=14)
    for i, c in enumerate((MAG, YEL, GREEN)):
        d.ellipse([205 + i * 62, 262, 245 + i * 62, 302], fill=c + (255,))
    l = layer(); ld = ImageDraw.Draw(l)
    ld.line([(270, 470), (400, 560), (270, 650)], fill=MAG + (255,), width=46, joint="curve")
    ld.rounded_rectangle([450, 620, 700, 666], radius=14, fill=CYAN + (255,))
    glow(img, l, MAG, 22, 0.8)
    return img

def taskbar():
    img, _ = base()
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([120, 640, S - 120, 790], radius=70, fill=(21, 17, 43, 255), outline=LINE + (255,), width=10)
    l = layer(); ld = ImageDraw.Draw(l)
    cols = (CYAN, MAG, CYAN, GREEN)
    for i, c in enumerate(cols):
        x = 175 + i * 170
        ld.rounded_rectangle([x, 668, x + 120, 762], radius=36, fill=c + (255,))
    glow(img, l, CYAN, 16, 0.7)
    l2 = layer(); d2 = ImageDraw.Draw(l2)
    d2.rounded_rectangle([425, 270, 599, 300 + 140], radius=0, fill=(0, 0, 0, 0))
    d2.rounded_rectangle([470, 230, 554, 470], radius=20, fill=MAG + (255,))
    d2.rounded_rectangle([392, 308, 632, 392], radius=20, fill=MAG + (255,))
    glow(img, l2, MAG, 24, 0.9)
    return img

def color():
    img, _ = base()
    l = layer()
    for (cx, cy), c in zip(((420, 430), (604, 430), (512, 590)), (CYAN, MAG, YEL)):
        c1 = layer(); ImageDraw.Draw(c1).ellipse([cx - 210, cy - 210, cx + 210, cy + 210], fill=c + (170,))
        l = ImageChops.add(l, c1)
    glow(img, l, CYAN, 26, 0.6)
    return img

def icons():
    img, _ = base()
    l = layer(); d = ImageDraw.Draw(l)
    cells = [(250, 250, CYAN), (540, 250, MAG), (250, 540, GREEN)]
    for x, y, c in cells:
        d.rounded_rectangle([x, y, x + 235, y + 235], radius=52, fill=c + (255,))
    glow(img, l, CYAN, 20, 0.7)
    l2 = layer(); d2 = ImageDraw.Draw(l2)
    d2.rounded_rectangle([540, 540, 775, 775], radius=52, outline=YEL + (255,), width=18)
    d2.rounded_rectangle([632, 592, 683, 723], radius=10, fill=YEL + (255,))
    d2.rounded_rectangle([576, 648, 739, 699], radius=10, fill=YEL + (255,))
    glow(img, l2, YEL, 16, 0.7)
    return img

JOBS = {
    "Launcher/app.ico": ghost,
    "Terminal/app.ico": terminal,
    "Plugins/TaskbarPlus/app.ico": taskbar,
    "Plugins/ColorPlus/app.ico": color,
    "Plugins/IconPlus/app.ico": icons,
}

if __name__ == "__main__":
    root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
    os.makedirs("/tmp/icon-preview", exist_ok=True)
    for rel, fn in JOBS.items():
        im = fn()
        im.resize((256, 256), Image.LANCZOS).save("/tmp/icon-preview/" + os.path.basename(os.path.dirname(rel) or rel) + "-" + os.path.basename(rel).replace(".ico", ".png"))
        out = os.path.join(root, rel)
        im.resize((256, 256), Image.LANCZOS).save(out, format="ICO", sizes=[(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (24, 24), (16, 16)])
        print("записан", rel)
