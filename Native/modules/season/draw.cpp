#include "draw.hpp"
#include <cmath>
#include <algorithm>

namespace sp {
namespace {

Rgb C(float r, float g, float b) { return Rgb{r, g, b}; }
const Rgb kLeaf[5] = {Rgb::hex(0xD6481E), Rgb::hex(0xEC7A20), Rgb::hex(0xF2B228), Rgb::hex(0xA65C2A), Rgb::hex(0xC88C2C)};
const Rgb kPetal[8] = {Rgb::hex(0xFFB7C5), Rgb::hex(0xFFCFDA), Rgb::hex(0xF8A0BA), Rgb::hex(0xFFE8EE), Rgb::hex(0xFFD634), Rgb::hex(0xF05A8C), Rgb::hex(0xFFFFFF), Rgb::hex(0xDC3232)};
const Rgb kFlower[7] = {Rgb::hex(0xFCFCFC), Rgb::hex(0xFF96BE), Rgb::hex(0xFFD634), Rgb::hex(0xDE2A2C), Rgb::hex(0x6082EC), Rgb::hex(0xFF8C28), Rgb::hex(0xB068E2)};
const Rgb kVivid[7] = {Rgb::hex(0xFF3B5C), Rgb::hex(0xFFD23F), Rgb::hex(0x3CFF9A), Rgb::hex(0x00D4FF), Rgb::hex(0xA259FF), Rgb::hex(0xFF8A3D), Rgb::hex(0xFF2E97)};
const std::vector<Rgb> kGarland[6] = {
    {Rgb::hex(0xFF3B3B), Rgb::hex(0xFFD23F), Rgb::hex(0x3CE06A), Rgb::hex(0x35C8FF), Rgb::hex(0xE04DFF), Rgb::hex(0xFF8A3D)},
    {Rgb::hex(0xFFB347), Rgb::hex(0xFFD27F), Rgb::hex(0xFF9A3C), Rgb::hex(0xFFE3A3)},
    {Rgb::hex(0xBFE9FF), Rgb::hex(0xFFFFFF), Rgb::hex(0x9AD0FF), Rgb::hex(0xDDEBFF)},
    {Rgb::hex(0x2E7BFF), Rgb::hex(0x5AA0FF), Rgb::hex(0x1F4FD8), Rgb::hex(0x8EC1FF)},
    {Rgb::hex(0xE3262E), Rgb::hex(0x2BB24C)},
    {Rgb::hex(0xFFD34D), Rgb::hex(0xFFC21A), Rgb::hex(0xFFE48A)},
};

struct Ctx {
    Gfx& g; const Engine& e; float ox, oy; float t;
    float cx(double x) const { return (float)(x - ox); }
    float cy(double y) const { return (float)(y - oy); }
};

// ---------- сугробы, листва, лепестки ----------
void drawFlower(Ctx& c, float x, float y, int kind, float sc, float bloom) {
    Rgb col = kFlower[kind];
    Rgb center = kind == 2 ? C(255, 140, 20) : C(255, 209, 26);
    if (c.e.season == 2 && kind == 2) {                      // подсолнух
        for (int k = 0; k < 10; k++) {
            float a = k * 0.6283f;
            c.g.ellipse(x + std::cos(a) * 4.2f * bloom, y + std::sin(a) * 4.2f * bloom, 2.4f * bloom, 1.2f * bloom, a, C(255, 205, 40), 1.f);
        }
        c.g.disc(x, y, 3.2f * bloom, C(110, 62, 24), 1.f);
        return;
    }
    for (int k = 0; k < 5; k++) {
        float a = k * 1.2566f + 0.3f;
        c.g.disc(x + std::cos(a) * 2.5f * sc * bloom, y + std::sin(a) * 2.5f * sc * bloom, 1.9f * sc * bloom, col, 1.f);
    }
    c.g.disc(x, y, 1.5f * sc * bloom, center, 1.f);
}

void drawGrass(Ctx& c, const PSeg& sg) {
    const Pile& p = *sg.p;
    for (int i = 0; i < p.n; i++) {
        float gr = p.g[i];
        if (gr < 1.2f) continue;
        double bx = p.L + i * Engine::kStep;
        if (bx < sg.a || bx > sg.b) continue;
        float baseY = (float)(p.top + Engine::dip(p, bx)) + 1.f - c.oy;
        unsigned hs = Engine::hash(p.seed, (unsigned)(i * 3 + 1));
        float sway = std::sin(c.t * 1.6f + (float)bx * 0.07f) * gr * 0.2f;
        Rgb green = hs % 3 == 0 ? C(96, 168, 64) : hs % 3 == 1 ? C(70, 140, 52) : C(124, 192, 76);
        float xb = c.cx(bx);
        c.g.line(xb, baseY, xb + sway, baseY - gr, 1.6f, green, 1.f);
        if (hs % 14 == 0 && gr >= p.t[i] * 0.88f) {
            float bloom = std::min(1.f, std::max(0.35f, (gr / p.t[i] - 0.88f) / 0.12f));
            drawFlower(c, xb + sway, baseY - gr, (int)(hs / 14 % 7), c.e.season == 2 ? 1.35f : 1.f, bloom);
        }
    }
}

void drawPile(Ctx& c, const PSeg& sg) {
    const Pile& p = *sg.p;
    int x0 = (int)std::ceil(sg.a), x1 = (int)std::floor(sg.b);
    if (p.total > 0.4f && p.kind >= 0 && x1 > x0) {
        std::vector<float> top, base;
        for (int x = x0; x <= x1; x += Engine::kStep) {
            float h = Engine::htAt(p, x);
            float b = (float)(p.top + Engine::dip(p, x)) + 1.f - c.oy;
            base.push_back(b);
            top.push_back(h < 0.3f ? b : b - h);
        }
        float xs = c.cx(x0);
        switch (p.kind) {
            case 0: {
                c.g.hill(xs, (float)Engine::kStep, top, base, C(255, 255, 255), C(190, 210, 236), 0.97f);
                for (int x = x0, i = 0; x <= x1; x += Engine::kStep, i++)                       // искорки на снегу
                    if (Engine::hash(p.seed, (unsigned)(x * 7 + (int)(c.t * 2))) % 331 == 0 && top[i] < base[i] - 0.5f) c.g.glow(c.cx(x), top[i] - 0.5f, 5.f, C(255, 255, 255), 0.9f);
                break;
            }
            case 3: {
                c.g.hill(xs, (float)Engine::kStep, top, base, C(190, 100, 36), C(90, 46, 18), 1.f);
                for (int x = x0, i = 0; x <= x1; x += Engine::kStep, i++) {                       // листья сверху: цветные блоки по 6 px
                    if (top[i] >= base[i] - 0.5f) continue;
                    int idx = (int)(Engine::hash(p.seed, (unsigned)(x / 6)) % 5);
                    Rgb col = kLeaf[idx].mul(0.88f + (Engine::hash(p.seed, (unsigned)x) % 5) * 0.05f);
                    c.g.rect(c.cx(x), top[i], c.cx(x) + Engine::kStep, std::min(base[i], top[i] + 6.f), col, 1.f);
                }
                break;
            }
            case 1: c.g.hill(xs, (float)Engine::kStep, top, base, C(255, 224, 234), C(238, 168, 196), 0.95f); break;
            case 2: c.g.hill(xs, (float)Engine::kStep, top, base, C(252, 252, 246), C(224, 224, 212), 0.92f); break;
        }
    }
    if (!p.taskbar) drawGrass(c, sg);
}

// ---------- гирлянда ----------
Rgb bulbColor(const Ctx& c, int j, float& lit) {
    const auto& pal = kGarland[std::min(5, std::max(0, c.e.S.GarlandColor))];
    int n = (int)pal.size();
    lit = 1.f;
    switch (c.e.S.GarlandMode) {
        case 0: {
            float f = j * 0.5f + c.t * 1.f; int i = (int)std::floor(f); float fr = f - i;
            return Rgb::lerp(pal[((i % n) + n) % n], pal[(((i + 1) % n) + n) % n], fr);
        }
        case 1: { float wave = std::fmod(std::fmod(j - c.t * 6.f, 6.f) + 6.f, 6.f); lit = wave < 2.f ? 1.f : 0.22f; return pal[j % n]; }
        case 2: lit = 0.35f + 0.65f * (0.5f + 0.5f * std::sin(c.t * (2.f + j % 5) + j * 1.7f)); return pal[j % n];
        case 3: return pal[j % n];
        case 4: lit = (((int)(c.t * 2) + j) % 2 == 0) ? 1.f : 0.15f; return pal[j % n];
        default: { float f = c.t * 0.4f; int i = (int)std::floor(f); return Rgb::lerp(pal[i % n], pal[(i + 1) % n], f - i); }
    }
}

void drawBulb(Ctx& c, float x, float y, Rgb col, float lit) {
    Rgb off = col.mul(0.25f), cc = Rgb::lerp(off, col, lit);
    Gfx& g = c.g;
    switch (c.e.S.GarlandBulb) {
        case 1:
            g.glow(x, y + 4, 9.f, col, 0.5f * lit); g.disc(x, y + 4, 3.4f, cc, 1.f); g.disc(x - 1.f, y + 3.f, 1.f, C(255, 255, 255), 0.55f * lit); break;
        case 2:
            g.glow(x, y + 5, 10.f, col, 0.5f * lit);
            g.line(x - 4.5f, y + 5, x + 4.5f, y + 5, 1.6f, cc, 1.f); g.line(x, y + 0.5f, x, y + 9.5f, 1.6f, cc, 1.f);
            g.line(x - 3.f, y + 2.f, x + 3.f, y + 8.f, 1.1f, cc, 0.8f); g.line(x + 3.f, y + 2.f, x - 3.f, y + 8.f, 1.1f, cc, 0.8f);
            g.disc(x, y + 5, 1.6f, C(255, 255, 255), 0.8f * lit); break;
        case 3:
            g.glow(x, y + 8, 9.f, col, 0.45f * lit); g.line(x, y, x, y + 9.f, 2.2f, cc, 0.95f); g.disc(x, y + 9.f, 1.6f, cc, 1.f); break;
        default:
            g.glow(x, y + 4.5f, 10.f, col, 0.5f * lit); g.ellipse(x, y + 5.f, 2.6f, 3.6f, 0.f, cc, 1.f);
            g.rect(x - 1.4f, y, x + 1.4f, y + 2.2f, C(70, 76, 70), 1.f); g.disc(x - 0.8f, y + 4.f, 0.9f, C(255, 255, 255), 0.5f * lit); break;
    }
}

void drawGarland(Ctx& c) {
    if (!c.e.garlandOn()) return;
    const double span = 150, gap = 22;
    for (auto& sg : c.e.segs()) {
        const Pile& p = *sg.p;
        if (c.e.S.GarlandWhere == 1 && !p.taskbar) continue;
        double y0 = p.top + (p.taskbar ? 3 : 4);
        auto sagAt = [&](double x) {
            int k = (int)std::floor((x - p.L) / span);
            double u = ((x - p.L) - k * span) / span;
            double sag = 8 + Engine::hash(p.seed, (unsigned)(k + 1000)) % 6;
            return y0 + sag * 4 * u * (1 - u);
        };
        for (double x = std::ceil(sg.a); x < sg.b; x += 4) {
            double xe = std::min(x + 4, sg.b);
            c.g.line(c.cx(x), c.cy(sagAt(x)), c.cx(xe), c.cy(sagAt(xe)), 1.3f, C(28, 40, 32), 0.85f);
        }
        int j0 = (int)std::ceil((sg.a - p.L - 11) / gap);
        for (int j = std::max(0, j0);; j++) {
            double bx = p.L + 11 + j * gap;
            if (bx > sg.b) break;
            if (bx < sg.a) continue;
            float lit;
            Rgb col = bulbColor(c, j, lit);
            drawBulb(c, c.cx(bx), c.cy(sagAt(bx)), col, lit);
        }
    }
}

// ---------- украшения на окнах ----------
void pumpkin(Ctx& c, float x, float y) {
    Gfx& g = c.g;
    Rgb o = C(240, 120, 20), d = C(205, 90, 10);
    g.ellipse(x - 7, y - 9, 7.5f, 9.f, 0, d, 1.f); g.ellipse(x + 7, y - 9, 7.5f, 9.f, 0, d, 1.f); g.ellipse(x, y - 9, 8.5f, 10.f, 0, o, 1.f);
    g.rect(x - 1.6f, y - 22, x + 1.6f, y - 17, C(70, 120, 40), 1.f);
    float flick = 0.8f + 0.2f * std::sin(c.t * 7.f + x);
    g.glow(x, y - 9, 16.f, C(255, 170, 40), 0.35f * flick);
    Rgb dark = C(60, 24, 4), lit = C(255, 222, 110);
    g.tri(x - 7, y - 11, x - 2.5f, y - 11, x - 4.7f, y - 16, dark, 1.f); g.tri(x + 7, y - 11, x + 2.5f, y - 11, x + 4.7f, y - 16, dark, 1.f);
    g.tri(x - 6.2f, y - 11.4f, x - 3.3f, y - 11.4f, x - 4.7f, y - 14.6f, lit, flick); g.tri(x + 6.2f, y - 11.4f, x + 3.3f, y - 11.4f, x + 4.7f, y - 14.6f, lit, flick);
    g.line(x - 6, y - 5, x - 3, y - 3.4f, 1.6f, dark, 1.f); g.line(x - 3, y - 3.4f, x, y - 5, 1.6f, dark, 1.f);
    g.line(x, y - 5, x + 3, y - 3.4f, 1.6f, dark, 1.f); g.line(x + 3, y - 3.4f, x + 6, y - 5, 1.6f, dark, 1.f);
}

void tree(Ctx& c, float x, float y) {
    Gfx& g = c.g;
    Rgb gr = C(30, 130, 64), gr2 = C(22, 104, 52);
    g.rect(x - 2, y - 4, x + 2, y, C(96, 60, 30), 1.f);
    g.tri(x - 11, y - 4, x + 11, y - 4, x, y - 18, gr2, 1.f); g.tri(x - 9, y - 13, x + 9, y - 13, x, y - 27, gr, 1.f); g.tri(x - 6.5f, y - 22, x + 6.5f, y - 22, x, y - 34, gr2, 1.f);
    g.disc(x, y - 35, 2.4f, C(255, 214, 70), 1.f);
    g.glow(x, y - 35, 8.f, C(255, 230, 120), 0.5f * (0.7f + 0.3f * std::sin(c.t * 4.f)));
    for (int k = 0; k < 6; k++) g.disc(x + (k % 2 == 0 ? -1 : 1) * (2.f + k), y - 8 - k * 3.6f, 1.5f, kVivid[(k + (int)(c.t * 1.5f)) % 7], 1.f);
}

void gift(Ctx& c, float x, float y, int v) {
    Gfx& g = c.g;
    Rgb col = kVivid[v % 3 == 0 ? 0 : v % 3 == 1 ? 3 : 4], rb = C(255, 235, 150);
    g.rect(x - 8, y - 12, x + 8, y, col, 1.f); g.rect(x - 1.5f, y - 12, x + 1.5f, y, rb, 1.f); g.rect(x - 8, y - 7, x + 8, y - 4.5f, rb, 1.f);
    g.ellipse(x - 3, y - 14, 3.6f, 2.2f, -0.5f, rb, 1.f); g.ellipse(x + 3, y - 14, 3.6f, 2.2f, 0.5f, rb, 1.f);
}

void egg(Ctx& c, float x, float y, int v) {
    Gfx& g = c.g;
    g.ellipse(x, y - 7, 5.f, 7.f, 0, kVivid[((v % 7) + 7) % 7], 1.f);
    g.line(x - 4.4f, y - 7, x + 4.4f, y - 7, 1.8f, C(255, 255, 255), 0.85f); g.line(x - 3.6f, y - 3.6f, x + 3.6f, y - 3.6f, 1.2f, C(255, 255, 255), 0.7f);
    g.disc(x - 1.6f, y - 10.5f, 1.1f, C(255, 255, 255), 0.5f);
}

void tulip(Ctx& c, float x, float y, int v) {
    Gfx& g = c.g;
    Rgb col = v % 2 == 0 ? C(235, 60, 100) : C(255, 150, 190);
    g.line(x, y, x, y - 13, 1.6f, C(70, 150, 60), 1.f); g.ellipse(x - 2.2f, y - 8, 1.2f, 3.2f, 0.5f, C(70, 150, 60), 1.f);
    g.ellipse(x, y - 16, 3.6f, 4.8f, 0, col, 1.f);
    g.tri(x - 3.4f, y - 17, x - 1.7f, y - 22, x, y - 17, col, 1.f); g.tri(x, y - 17, x + 1.7f, y - 22, x + 3.4f, y - 17, col, 1.f);
}

void pancakes(Ctx& c, float x, float y) {
    for (int k = 0; k < 3; k++) c.g.ellipse(x, y - 3 - k * 4.f, 11, 3.6f, 0, k == 2 ? C(232, 178, 92) : C(206, 140, 62), 1.f);
    c.g.rect(x - 2.5f, y - 14.5f, x + 2.5f, y - 12.2f, C(255, 236, 140), 1.f);
}

void lantern(Ctx& c, float x, float y) {
    Gfx& g = c.g;
    float sw = std::sin(c.t * 1.4f + x) * 0.6f;
    g.glow(x, y - 12, 18.f, C(255, 120, 60), 0.35f);
    g.ellipse(x, y - 12, 8, 10, 0, C(218, 38, 38), 1.f);
    g.rect(x - 4.5f, y - 23, x + 4.5f, y - 20.5f, C(235, 190, 60), 1.f); g.rect(x - 4.5f, y - 3.5f, x + 4.5f, y - 1.f, C(235, 190, 60), 1.f);
    g.line(x + sw, y - 1.f, x + sw, y + 5, 1.2f, C(235, 190, 60), 1.f);
}

void drawProp(Ctx& c, float x, float y, int variant, int slot) {
    const std::string& h = c.e.holiday;
    if (h == "halloween") pumpkin(c, x, y);
    else if (h == "newyear" || h == "xmas") { if ((variant + slot) % 2 == 0) tree(c, x, y); else gift(c, x, y, variant); }
    else if (h == "easter") { egg(c, x, y, variant); egg(c, x + 11, y, variant + 1); }
    else if (h == "valentine") { c.g.heart(x, y - 9, 9.f, C(235, 55, 90), 1.f); c.g.heart(x - 4, y - 14, 3.2f, C(255, 255, 255), 0.5f); }
    else if (h == "women") { tulip(c, x, y, variant); tulip(c, x + 8, y, variant + 1); tulip(c, x - 8, y, variant + 2); }
    else if (h == "maslenitsa") pancakes(c, x, y);
    else if (h == "tet") lantern(c, x, y);
}

void drawProps(Ctx& c) {
    if (!c.e.S.Props || c.e.holiday.empty()) return;
    for (auto& sg : c.e.segs()) {
        const Pile& p = *sg.p;
        if (p.taskbar) continue;
        for (int k = 0; k < 2; k++) {
            unsigned h = Engine::hash(p.seed, (unsigned)(500 + k));
            double px = p.L + 40 + (h % 1000) / 1000.0 * std::max(0.0, (p.R - p.L) - 120);
            if (px < sg.a + 12 || px > sg.b - 12) continue;
            float by = (float)(p.top + Engine::dip(p, px)) - std::max(0.f, Engine::htAt(p, px) - 1.5f) + 1.f;
            drawProp(c, c.cx(px), c.cy(by), (int)((h >> 10) % 3), k);
        }
    }
}

// ---------- частицы ----------
void drawParts(Ctx& c) {
    Gfx& g = c.g; int W = c.e.W, H = c.e.H;
    for (const Part& p : c.e.parts()) {
        float x = c.cx(p.x), y = c.cy(p.y);
        if (x < -40 || x > W + 40 || y < -40 || y > H + 40) continue;
        switch (p.k) {
            case PK::Snow:
                if (p.s > 2.4f) g.glow(x, y, p.s * 2.4f, C(255, 255, 255), 0.22f);
                g.disc(x, y, p.s, C(255, 255, 255), 0.92f); break;
            case PK::Leaf: {
                float flip = 0.35f + 0.65f * std::fabs(std::cos(p.ph + p.age * 2.1f));
                Rgb col = kLeaf[p.c % 5];
                g.ellipse(x, y, p.s * 1.7f, p.s * 0.95f * flip, p.rot, col, 1.f);
                g.line(x - std::cos(p.rot) * p.s * 1.5f, y - std::sin(p.rot) * p.s * 1.5f, x + std::cos(p.rot) * p.s * 1.5f, y + std::sin(p.rot) * p.s * 1.5f, 0.9f, col.mul(0.6f), 0.8f);
                break;
            }
            case PK::Petal: {
                float flip = 0.4f + 0.6f * std::fabs(std::cos(p.ph + p.age * 2.f));
                g.ellipse(x, y, p.s * 1.3f, p.s * 0.75f * flip, p.rot, kPetal[p.c % 8], 0.95f); break;
            }
            case PK::Fluff:
                g.glow(x, y, p.s * 3.2f, C(255, 255, 255), 0.5f);
                for (int k = 0; k < 7; k++) { float a = -1.57f + (k - 3) * 0.42f; g.line(x, y, x + std::cos(a) * p.s * 2.4f, y + std::sin(a) * p.s * 2.4f, 0.8f, C(255, 255, 255), 0.75f); }
                g.disc(x, y, p.s * 0.55f, C(120, 110, 90), 0.9f); break;
            case PK::Rain: g.line(x, y, x - p.vx * 0.018f, y - p.vy * 0.018f, 1.2f, C(175, 205, 238), 0.55f); break;
            case PK::Clump: {
                Rgb col = p.c == 3 ? kLeaf[(int)(p.ph * 10) % 5] : p.c == 1 ? C(250, 200, 215) : C(245, 250, 255);
                g.disc(x, y, p.s, col, 0.95f); break;
            }
            case PK::Heart: g.heart(x, y, p.s, Rgb::lerp(C(255, 70, 110), C(255, 150, 190), p.c / 2.f), 0.9f); break;
            case PK::Bat: {
                float flap = std::sin(p.age * 14.f + p.ph), dir = p.vx >= 0 ? 1.f : -1.f, s = p.s;
                Rgb col = C(22, 16, 30);
                g.ellipse(x, y, s * 0.55f, s * 0.9f, 0, col, 1.f); g.disc(x, y - s * 0.8f, s * 0.38f, col, 1.f);
                g.tri(x - s * 0.3f, y - s * 1.0f, x - s * 0.1f, y - s * 0.8f, x - s * 0.4f, y - s * 1.5f, col, 1.f);
                g.tri(x + s * 0.3f, y - s * 1.0f, x + s * 0.1f, y - s * 0.8f, x + s * 0.4f, y - s * 1.5f, col, 1.f);
                for (int side = -1; side <= 1; side += 2) {
                    float wx = x + side * s * 1.4f, wy = y - flap * s * 0.8f;
                    g.tri(x, y - s * 0.5f, wx + side * s * 0.6f, wy - s * 0.4f, x + side * s * 0.3f, y + s * 0.5f, col, 1.f);
                    g.tri(x + side * s * 0.3f, y + s * 0.5f, wx + side * s * 0.6f, wy - s * 0.4f, wx, wy + s * 0.7f, col, 1.f);
                }
                g.disc(x - 1.1f * dir, y - s * 0.85f, 0.9f, C(255, 80, 60), 1.f);
                break;
            }
            case PK::Confetti: {
                float flip = 0.3f + 0.7f * std::fabs(std::cos(p.ph + p.age * 4.f));
                g.ellipse(x, y, p.s * 1.4f, p.s * 0.6f * flip, p.rot, kVivid[p.c % 7], 1.f); break;
            }
            case PK::Spark: {
                Rgb col = Rgb::hsv(p.c / 100.f, 0.7f, 1.f);
                g.line(x, y, x - p.vx * 0.03f, y - p.vy * 0.03f, p.s, col, p.a * 0.6f);
                g.glow(x, y, p.s * 4.f, col, p.a * 0.7f); g.disc(x, y, p.s * 0.8f, C(255, 255, 255), p.a); break;
            }
            case PK::Rocket:
                g.line(x, y, x - p.vx * 0.09f, y - p.vy * 0.09f, 2.f, C(255, 200, 120), 0.8f); g.glow(x, y, 8.f, C(255, 220, 160), 0.8f); break;
            case PK::Splash: { float q = p.age / p.life; g.ring(x, y, p.s * q, 1.1f, C(190, 215, 240), (1.f - q) * 0.6f); break; }
            case PK::Balloon: {
                Rgb col = kVivid[p.c % 7];
                g.line(x, y + p.s * 1.1f, x + std::sin(p.age * 2.f + p.ph) * 3.f, y + p.s * 3.2f, 0.9f, C(220, 220, 230), 0.8f);
                g.ellipse(x, y, p.s * 0.8f, p.s, 0, col, 0.95f); g.disc(x - p.s * 0.28f, y - p.s * 0.35f, p.s * 0.18f, C(255, 255, 255), 0.55f); break;
            }
            case PK::Twinkle: {
                float a = std::sin(p.age / p.life * 3.14159f);
                g.glow(x, y, p.s * 3.f, C(255, 245, 200), a * 0.6f);
                g.line(x - p.s, y, x + p.s, y, 1.1f, C(255, 255, 255), a); g.line(x, y - p.s, x, y + p.s, 1.1f, C(255, 255, 255), a); break;
            }
        }
    }
}

void drawBig(Ctx& c) {
    bool greeting = false;
    std::string text = c.e.bigText(greeting);
    if (text.empty()) return;
    float pulse = 0.82f + 0.18f * std::sin(c.t * 6.f);
    c.g.text(text, greeting ? 96.f : 230.f, greeting ? C(255, 224, 120) : C(255, 255, 255), greeting ? 1.f : pulse, c.e.W / 2.f, c.e.H * 0.34f);
}

}  // namespace

void drawScene(Gfx& g, const Engine& e) {
    Ctx c{g, e, (float)e.OX, (float)e.OY, (float)e.time()};
    for (auto& sg : e.segs()) drawPile(c, sg);
    drawGarland(c);
    drawProps(c);
    drawParts(c);
    drawBig(c);
}

}  // namespace sp
