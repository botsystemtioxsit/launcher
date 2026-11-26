// Движок Season Plus: что падает, что лежит на окнах, салют, отсчёт. Про Windows и рисование не знает — только цифры.
#pragma once
#include <unordered_map>
#include <memory>
#include <random>
#include <string>
#include <vector>
#include "model.hpp"

namespace sp {

enum class PK : unsigned char { Snow, Leaf, Petal, Fluff, Rain, Clump, Heart, Bat, Confetti, Spark, Rocket, Splash, Balloon, Twinkle };

struct Part {
    float x = 0, y = 0, vx = 0, vy = 0, s = 0, rot = 0, spin = 0, ph = 0, age = 0, life = 99, a = 0;
    PK k = PK::Snow;
    int c = 0;
};

// Накопленное на верхнем крае окна: сугроб/листва/лепестки (h) и трава (g, её целевая высота t).
struct Pile {
    long long id = 0;
    double L = 0, R = 0, top = 0, rad = 0;
    bool taskbar = false;
    int n = 0;
    std::vector<float> h, g, t;
    int kind = -1;               // из какого времени года то, что лежит (−1 — пусто)
    unsigned seed = 0;
    float total = 0;
};

struct PSeg { Pile* p; double a, b; };

class Engine {
public:
    static constexpr int kStep = 2;                 // ширина колонки сугроба, px
    Settings S;
    int W = 1920, H = 1080, OX = 0, OY = 0;         // размер и начало экрана (виртуальный рабочий стол)
    float pileGain = 1.2f;
    int season = 0;
    std::string holiday;
    bool hasClock = false; Date clock{2026, 1, 1}; int clockHour = 12, clockMin = 0, clockSec = 0;   // для проверки: подменить «сегодня»
    float fwLeft = 0;                               // сколько секунд ещё запускать салют
    double nyDemo = -1;                             // «проверить полночь» (−1 — не идёт)

    explicit Engine(int seed = 1) : rng_(seed) { ps_.reserve(kMax + 300); }
    void reset();
    void setSurfaces(const std::vector<Surf>& list);
    void step(double dt);
    void warm(double seconds, double stepSec = 0.05) { for (double t = 0; t < seconds; t += stepSec) step(stepSec); }
    void startFireworks(float seconds = 14) { if (seconds > fwLeft) fwLeft = seconds; }
    void forceRain(float seconds) { rainLeft_ = seconds; }
    std::string bigText(bool& greeting) const;      // что показать посреди экрана: "" или отсчёт/поздравление

    // для рисования и проверок
    double time() const { return t_; }
    const std::vector<Part>& parts() const { return ps_; }
    const std::vector<PSeg>& segs() const { return segs_; }
    std::vector<const Pile*> piles() const { std::vector<const Pile*> v; for (auto& kv : piles_) v.push_back(kv.second.get()); return v; }
    int countOf(PK k) const { int c = 0; for (auto& p : ps_) if (p.k == k) c++; return c; }
    float wind() const { return wind_; }
    bool garlandOn() const { return S.Garland != 0 && !(S.Garland == 1 && holiday.empty()); }
    float maxH(const Pile& p) const { return (S.PileHeight / 100.f * 26.f + 4.f) * (p.taskbar ? 0.8f : 1.f); }

    static double dip(const Pile& p, double x);     // насколько край у скруглённого угла ниже
    static float htAt(const Pile& p, double x);
    static unsigned hash(unsigned a, unsigned b) {
        unsigned h = a * 374761393u + b * 668265263u;
        h = (h ^ (h >> 13)) * 1274126177u;
        return h ^ (h >> 16);
    }

private:
    static constexpr size_t kMax = 3200;
    std::mt19937 rng_;
    std::vector<Part> ps_;
    std::unordered_map<long long, std::unique_ptr<Pile>> piles_;
    std::vector<PSeg> segs_;
    double t_ = 0, relaxAcc_ = 0, rainLeft_ = 0, rockAcc_ = 0, sn_ = 0, rn_ = 0, hl_ = 0;
    float wind_ = 0;
    bool nyFired_ = false;

    float F(float a, float b) { return a + std::uniform_real_distribution<float>(0.f, 1.f)(rng_) * (b - a); }
    int Ri(int n) { return (int)(rng_() % (unsigned)n); }
    std::unique_ptr<Pile> newPile(const Surf& s);
    void shed(Pile& p);
    void add(Pile& p, double x, float amt);
    void emit(PK k, float x, float y);
    void spawn(double dt);
    void spawnBat();
    void updateParts(double dt);
    bool tryLand(Part& p, float oy);
    void updatePiles(double dt);
    void explode(float x, float y, int hue);
    void launch();
    void updateFireworks(double dt);
    template <class Fn> void rate(double& acc, float perSec, double dt, Fn make) {
        acc += perSec * dt;
        int c = 0;
        while (acc >= 1 && c++ < 40) { make(); acc -= 1; }
        if (acc > 50) acc = 0;
    }
};

}  // namespace sp
