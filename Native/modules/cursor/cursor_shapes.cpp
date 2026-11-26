#include "cursor_shapes.hpp"
#include <cmath>

namespace sp {

const std::vector<ShapeInfo> kShapes = {
    {"windows", "Как в Windows"}, {"classic", "Классика"}, {"round", "Мягкая"}, {"neon", "Неон"},
    {"dot", "Точка"}, {"ring", "Кольцо"}, {"cross", "Прицел"}, {"ghost", "Призрак"}, {"star", "Звезда"},
};

bool isProcedural(const std::string& s) { return s == "classic" || s == "round" || s == "neon" || s == "dot" || s == "ring" || s == "cross" || s == "ghost" || s == "star"; }
bool isOwnRole(const std::string& s) { return s == "ibeam" || s == "crosshair" || s == "no"; }

void shapeHot(const std::string& s, float& hx, float& hy) {
    if (s == "classic" || s == "round" || s == "neon") { hx = 2.f / 32; hy = 1.5f / 32; }
    else if (s == "ghost") { hx = 7.f / 32; hy = 4.f / 32; }
    else { hx = 0.5f; hy = 0.5f; }
}

using Pt = Gfx::Pt;
static const std::vector<Pt> kArrow = {{2, 1.5f}, {2, 20.5f}, {6.6f, 16.6f}, {10, 24.5f}, {13, 23.2f}, {9.6f, 15.5f}, {15.6f, 15.5f}};
const std::vector<Pt>& arrowPoints() { return kArrow; }

static std::vector<Pt> ghostBody() {
    std::vector<Pt> p = {{5, 28}, {5, 14}};
    for (int i = 1; i < 24; i++) { float a = 3.14159265f + i * 3.14159265f / 24; p.push_back({16 + 11 * std::cos(a), 14 + 11 * std::sin(a)}); }
    p.push_back({27, 14}); p.push_back({27, 28});
    p.push_back({23.33f, 24.6f}); p.push_back({19.67f, 28}); p.push_back({16, 24.6f}); p.push_back({12.33f, 28}); p.push_back({8.67f, 24.6f});
    return p;
}

static std::vector<Pt> starPoints() {
    std::vector<Pt> p;
    for (int i = 0; i < 10; i++) {
        float r = i % 2 == 0 ? 12.8f : 5.4f, a = -3.14159265f / 2 + i * 3.14159265f / 5;
        p.push_back({16 + r * std::cos(a), 16.8f + r * std::sin(a)});
    }
    return p;
}

void drawShape(Gfx& g, const std::string& shape, Rgb fill, Rgb line) {
    Rgb white{255, 255, 255};
    if (shape == "classic") { g.polyStroke(kArrow, true, 2.8f, line, 1.f, false); g.polyFill(kArrow, fill, 1.f); }
    else if (shape == "round") { g.polyStroke(kArrow, true, 3.4f, line, 1.f, true); g.polyFill(kArrow, fill, 1.f); }
    else if (shape == "neon") {
        g.polyFill(kArrow, fill, 0.18f);
        const float w[4] = {8.f, 5.6f, 3.6f, 2.2f}, a[4] = {0.1f, 0.18f, 0.35f, 1.f};
        for (int i = 0; i < 4; i++) g.polyStroke(kArrow, true, w[i], fill, a[i], true);
        g.polyStroke(kArrow, true, 1.f, Rgb::lerp(fill, white, 0.6f), 1.f, true);
    }
    else if (shape == "dot") {
        g.ring(16, 16, 6.6f, 3.2f, line, 1.f); g.disc(16, 16, 6.6f, fill, 1.f); g.disc(14.2f, 14.2f, 2.2f, Rgb::lerp(fill, white, 0.55f), 0.6f);
    }
    else if (shape == "ring") {
        g.ring(16, 16, 9.2f, 6.4f, line, 1.f); g.ring(16, 16, 9.2f, 3.f, fill, 1.f);
        g.disc(16, 16, 2.2f, fill, 1.f); g.ring(16, 16, 2.2f, 1.6f, line, 1.f);
    }
    else if (shape == "cross") {
        for (int k = 0; k < 2; k++) {
            float w = k == 0 ? 5.4f : 2.6f; Rgb c = k == 0 ? line : fill;
            g.line(3, 16, 12, 16, w, c, 1.f); g.line(20, 16, 29, 16, w, c, 1.f); g.line(16, 3, 16, 12, w, c, 1.f); g.line(16, 20, 16, 29, w, c, 1.f);
        }
        g.disc(16, 16, 1.7f, fill, 1.f); g.ring(16, 16, 1.7f, 1.4f, line, 1.f);
    }
    else if (shape == "ghost") {
        auto b = ghostBody();
        g.polyStroke(b, true, 2.8f, line, 1.f, true); g.polyFill(b, fill, 1.f);
        g.ellipse(12, 14, 1.9f, 2.8f, 0, line, 1.f); g.ellipse(20, 14, 1.9f, 2.8f, 0, line, 1.f);
    }
    else if (shape == "star") { auto s = starPoints(); g.polyStroke(s, true, 2.8f, line, 1.f, true); g.polyFill(s, fill, 1.f); }
    // указатели, которые Windows рисует «наоборот» (текст, точный выбор) или красным (недоступно): рисуем сами в цвете курсора
    else if (shape == "ibeam") {
        for (int k = 0; k < 2; k++) {
            float w = k == 0 ? 5.f : 2.2f; Rgb c = k == 0 ? line : fill;
            g.line(16, 5.5f, 16, 26.5f, w, c, 1.f); g.line(11, 5.5f, 21, 5.5f, w, c, 1.f); g.line(11, 26.5f, 21, 26.5f, w, c, 1.f);
        }
    }
    else if (shape == "crosshair") {
        for (int k = 0; k < 2; k++) {
            float w = k == 0 ? 5.f : 2.2f; Rgb c = k == 0 ? line : fill;
            g.line(2.5f, 16, 13, 16, w, c, 1.f); g.line(19, 16, 29.5f, 16, w, c, 1.f); g.line(16, 2.5f, 16, 13, w, c, 1.f); g.line(16, 19, 16, 29.5f, w, c, 1.f);
        }
    }
    else if (shape == "no") {
        g.ring(16, 16, 10, 6.2f, line, 1.f); g.ring(16, 16, 10, 3.f, fill, 1.f);
        g.line(9.4f, 9.4f, 22.6f, 22.6f, 5.8f, line, 1.f); g.line(9.4f, 9.4f, 22.6f, 22.6f, 2.8f, fill, 1.f);
    }
}

}  // namespace sp
