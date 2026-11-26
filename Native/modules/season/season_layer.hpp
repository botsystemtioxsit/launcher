// Слой «Сезоны»: снег, листья, лепестки, трава на окнах, праздники. Рисует движок Season (engine.hpp) в общем окне поверх экрана.
#pragma once
#include <atomic>
#include <mutex>
#include "draw.hpp"
#include "engine.hpp"
#include "layer.hpp"

namespace sp {

class SeasonLayer : public Layer {
public:
    void setSettings(const Settings& s) { std::lock_guard<std::mutex> l(mu_); s_ = s; }
    void fireworks() { fw_ = true; }
    bool wantsFrame() const override { std::lock_guard<std::mutex> l(mu_); return s_.Active; }
    bool needsSurfaces() const override { return true; }
    void onResize(int vx, int vy, int w, int h) override { eng_.OX = vx; eng_.OY = vy; eng_.W = w; eng_.H = h; }
    void update(const Env& env) override {
        { std::lock_guard<std::mutex> l(mu_); eng_.S = s_; }
        if (fw_.exchange(false)) eng_.startFireworks();
        if (env.surfacesChanged && env.surfaces) { eng_.setSurfaces(*env.surfaces); if (!warmed_) { eng_.warm(8); warmed_ = true; } }
        eng_.step(env.dt);
        particles = (int)eng_.parts().size();
    }
    void draw(Gfx& g, const Env&) override { drawScene(g, eng_); }
    std::atomic<int> particles{0};

private:
    mutable std::mutex mu_;
    Settings s_;
    Engine eng_{(int)GetTickCount()};
    std::atomic<bool> fw_{false};
    bool warmed_ = false;
};

}  // namespace sp
