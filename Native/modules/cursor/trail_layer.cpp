#include "trail_layer.hpp"
#include <algorithm>
#include <cmath>
#include "cursor_shapes.hpp"

namespace sp {

void TrailLayer::setSettings(const CursorSettings& s) { std::lock_guard<std::mutex> l(mu_); s_ = s; }
bool TrailLayer::wantsFrame() const { std::lock_guard<std::mutex> l(mu_); return s_.OverlayOn && s_.wantsOverlay(); }

Rgb TrailLayer::colorAt(const CursorSettings& s, float t, float ph) const {
    t = std::clamp(t, 0.f, 1.f);
    if (s.TrailColorMode == "rainbow") return Rgb::hsv((float)time_ * 0.25f + ph + t * 0.35f, 0.75f, 1.f);
    if (s.TrailColorMode == "two") return Rgb::lerp(s.trailRgb(), s.trailRgb2(), t);
    if (s.TrailColorMode == "custom") return s.trailRgb();
    return s.fillRgb();
}

void TrailLayer::spawnAlong(const CursorSettings& s, float x0, float y0, float x1, float y1) {
    float dx = x1 - x0, dy = y1 - y0, d = std::sqrt(dx * dx + dy * dy);
    if (d < 0.5f) return;
    float len = 0.15f + s.TrailLength / 100.f * 1.5f;          // сколько секунд живёт хвост
    float r0 = 2.5f + s.TrailSize / 100.f * 20.f;               // размер
    float spacing = s.Trail == "comet" ? std::max(1.5f, r0 * 0.35f) : s.Trail == "glow" ? std::max(3.f, r0 * 0.6f) : s.Trail == "sparks" ? 7.f : s.Trail == "stars" ? 16.f : s.Trail == "bubbles" ? 20.f : s.Trail == "hearts" ? 22.f : 24.f;
    acc_ += d;
    while (acc_ >= spacing && ps_.size() < 1400) {
        acc_ -= spacing;
        float t = 1.f - acc_ / d; t = std::clamp(t, 0.f, 1.f);
        P p{x0 + dx * t, y0 + dy * t, 0, 0, 0, len, r0, F(0, 6.28f), F(-3, 3), F(0, 1), 0};
        if (s.Trail == "comet") { p.kind = 0; p.size = r0; }
        else if (s.Trail == "sparks") { p.kind = 1; p.vx = F(-70, 70); p.vy = F(-110, 20); p.size = F(1.2f, 1.6f + r0 * 0.12f); p.life = F(0.3f, 0.5f + len * 0.6f); }
        else if (s.Trail == "glow") { p.kind = 2; p.size = r0 * 1.6f; p.life = len * 0.9f; }
        else if (s.Trail == "stars") { p.kind = 3; p.size = r0 * 0.8f; p.vx = F(-25, 25); p.vy = F(-25, 25); p.life = F(0.3f, 0.3f + len); }
        else if (s.Trail == "bubbles") { p.kind = 4; p.size = r0 * F(0.5f, 1.f); p.vx = F(-12, 12); p.vy = F(-60, -25); p.life = 0.4f + len; }
        else if (s.Trail == "hearts") { p.kind = 5; p.size = r0 * 0.55f; p.vx = F(-15, 15); p.vy = F(-55, -20); p.life = 0.4f + len; }
        else if (s.Trail == "ghost") { p.kind = 6; p.size = 0.6f + s.TrailSize / 100.f * 0.5f; p.life = 0.2f + len * 0.6f; }
        else continue;
        ps_.push_back(p);
    }
}

void TrailLayer::click(const CursorSettings& s, float x, float y) {
    float r0 = 24.f + s.TrailSize / 100.f * 36.f;
    if (s.Click == "ring") {
        for (int i = 0; i < 2; i++) ps_.push_back({x, y, 0, 0, -0.12f * i, 0.55f, r0 * (1.f - 0.25f * i), 0, 0, 0.1f * i, 7});
    } else if (s.Click == "burst") {
        for (int i = 0; i < 16 && ps_.size() < 1400; i++) {
            float a = i * 6.2832f / 16 + F(-0.15f, 0.15f), sp = F(90, 220);
            ps_.push_back({x, y, std::cos(a) * sp, std::sin(a) * sp, 0, F(0.35f, 0.7f), F(1.5f, 3.f), 0, 0, F(0, 1), 1});
        }
    }
}

void TrailLayer::update(const Env& env) {
    CursorSettings s; { std::lock_guard<std::mutex> l(mu_); s = s_; }
    time_ = env.time;
    float x = (float)(env.cursor.x - env.vx), y = (float)(env.cursor.y - env.vy);
    float dt = (float)env.dt;
    if (s.Trail != "none") { if (havePrev_) spawnAlong(s, prevX_, prevY_, x, y); }
    if (env.lclick || env.rclick) click(s, x, y);
    prevX_ = x; prevY_ = y; havePrev_ = true;
    for (size_t i = 0; i < ps_.size(); i++) {
        P& p = ps_[i];
        p.age += dt;
        if (p.kind == 1) p.vy += 260.f * dt;
        if (p.kind == 4) p.vx += std::sin(p.age * 6.f + p.ph * 6.f) * 40.f * dt;
        p.x += p.vx * dt; p.y += p.vy * dt; p.rot += p.spin * dt;
        if (p.age >= p.life) { ps_[i] = ps_.back(); ps_.pop_back(); i--; }
    }
}

void TrailLayer::draw(Gfx& g, const Env&) {
    CursorSettings s; { std::lock_guard<std::mutex> l(mu_); s = s_; }
    for (const P& p : ps_) {
        if (p.age < 0) continue;
        float t = p.age / p.life, a = std::pow(1.f - t, 1.2f);
        Rgb c = colorAt(s, t, p.ph);
        switch (p.kind) {
            case 0: g.glow(p.x, p.y, p.size * 2.2f * (1.f - t * 0.5f), c, 0.45f * a); g.disc(p.x, p.y, std::max(0.6f, p.size * (1.f - t)), c, a); break;
            case 1: g.glow(p.x, p.y, p.size * 4.f, c, 0.6f * a); g.disc(p.x, p.y, p.size, Rgb::lerp(c, {255, 255, 255}, 0.5f), a); break;
            case 2: g.glow(p.x, p.y, p.size * (1.2f - t * 0.4f), c, 0.55f * a); break;
            case 3: {
                float r = p.size * (1.f - t * 0.4f);
                float ca = std::cos(p.rot), sa = std::sin(p.rot);
                g.glow(p.x, p.y, r * 2.2f, c, 0.35f * a);
                g.line(p.x - ca * r, p.y - sa * r, p.x + ca * r, p.y + sa * r, 1.8f, c, a);
                g.line(p.x + sa * r, p.y - ca * r, p.x - sa * r, p.y + ca * r, 1.8f, c, a);
                g.disc(p.x, p.y, r * 0.22f, {255, 255, 255}, a);
                break;
            }
            case 4: g.ring(p.x, p.y, p.size * (0.8f + t * 0.4f), 1.4f, c, 0.8f * a); g.disc(p.x - p.size * 0.3f, p.y - p.size * 0.3f, p.size * 0.15f, {255, 255, 255}, 0.7f * a); break;
            case 5: g.heart(p.x, p.y, p.size * (1.f - t * 0.3f), c, 0.9f * a); break;
            case 6: {
                D2D1_MATRIX_3X2_F m = D2D1::Matrix3x2F::Scale(p.size, p.size) * D2D1::Matrix3x2F::Translation(p.x - 2.f * p.size, p.y - 1.5f * p.size);
                g.setTransform(m);
                auto& pts = arrowPoints();
                g.polyFill(pts, c, 0.5f * a);
                g.setTransform(D2D1::Matrix3x2F::Identity());
                break;
            }
            case 7: g.ring(p.x, p.y, p.size * std::sqrt(t), 2.5f * (1.f - t) + 0.6f, colorAt(s, 0.f, p.ph), 0.85f * (1.f - t)); break;
        }
    }
}

}  // namespace sp
