// Слой «Шлейф курсора»: комета, искры, свечение, звёзды, пузырьки, сердечки, призраки; круги и вспышка при клике.
#pragma once
#include <atomic>
#include <mutex>
#include <random>
#include <vector>
#include "cursor_settings.hpp"
#include "layer.hpp"

namespace sp {

class TrailLayer : public Layer {
public:
    explicit TrailLayer(int seed = 5) : rng_(seed) {}
    void setSettings(const CursorSettings& s);
    bool wantsFrame() const override;
    void update(const Env& env) override;
    void draw(Gfx& g, const Env& env) override;
    size_t count() const { return ps_.size(); }
    void clear() { ps_.clear(); havePrev_ = false; }

private:
    struct P { float x, y, vx, vy, age, life, size, rot, spin, ph; int kind; };   // kind: 0 точка/комета, 1 искра, 2 свечение, 3 звезда, 4 пузырёк, 5 сердечко, 6 призрак, 7 круг клика
    mutable std::mutex mu_;
    CursorSettings s_;
    std::vector<P> ps_;
    std::mt19937 rng_;
    float prevX_ = 0, prevY_ = 0, acc_ = 0;
    bool havePrev_ = false;
    double time_ = 0;
    float F(float a, float b) { return a + std::uniform_real_distribution<float>(0.f, 1.f)(rng_) * (b - a); }
    Rgb colorAt(const CursorSettings& s, float t, float ph) const;
    void spawnAlong(const CursorSettings& s, float x0, float y0, float x1, float y1);
    void click(const CursorSettings& s, float x, float y);
};

}  // namespace sp
