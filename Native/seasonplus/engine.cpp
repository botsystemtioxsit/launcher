#include "engine.hpp"
#include <cmath>
#include <ctime>
#ifndef _WIN32
#define localtime_s(tmp, tt) localtime_r((tt), (tmp))
#endif

namespace sp {

static const float kPi = 3.14159265f;

static void nowOf(const Engine& e, Date& d, int& h, int& mi, int& s) {
    if (e.hasClock) { d = e.clock; h = e.clockHour; mi = e.clockMin; s = e.clockSec; return; }
    time_t t = time(nullptr); tm lt{}; localtime_s(&lt, &t);
    d = Date{lt.tm_year + 1900, lt.tm_mon + 1, lt.tm_mday}; h = lt.tm_hour; mi = lt.tm_min; s = lt.tm_sec;
}

void Engine::reset() { ps_.clear(); piles_.clear(); segs_.clear(); t_ = 0; rainLeft_ = 0; fwLeft = 0; nyDemo = -1; nyFired_ = false; }

// ---------- окна ----------
void Engine::setSurfaces(const std::vector<Surf>& list) {
    std::unordered_map<long long, bool> seen;
    segs_.clear();
    for (auto& s : list) {
        seen[s.id] = true;
        auto it = piles_.find(s.id);
        if (it == piles_.end()) it = piles_.emplace(s.id, newPile(s)).first;
        else if (std::fabs(it->second->L - s.left) > 2.5 || std::fabs(it->second->R - s.right) > 2.5 || std::fabs(it->second->top - s.top) > 2.5) {
            shed(*it->second);                       // окно двинули или растянули — всё осыпается
            it->second = newPile(s);
        }
        Pile& p = *it->second;
        p.rad = s.radius; p.taskbar = s.taskbar;
        for (auto& v : s.vis) if (v.b - v.a >= 6) segs_.push_back({&p, v.a, v.b});
    }
    for (auto it = piles_.begin(); it != piles_.end();) {
        if (!seen.count(it->first)) { shed(*it->second); it = piles_.erase(it); } else ++it;
    }
}

std::unique_ptr<Pile> Engine::newPile(const Surf& s) {
    auto p = std::make_unique<Pile>();
    p->id = s.id; p->L = s.left; p->R = s.right; p->top = s.top; p->rad = s.radius; p->taskbar = s.taskbar;
    p->seed = hash((unsigned)s.id, 77u);
    p->n = std::max(2, (int)((s.right - s.left) / kStep) + 2);
    p->h.assign(p->n, 0.f); p->g.assign(p->n, 0.f); p->t.assign(p->n, 0.f);
    for (int i = 0; i < p->n; i++) p->t[i] = 5.f + (float)(hash(p->seed, (unsigned)i) % 17);
    return p;
}

// Сугроб осыпается комьями.
void Engine::shed(Pile& p) {
    if (p.total < 2.f || p.kind < 0) return;
    int spawned = 0;
    for (int i = 0; i < p.n && spawned < 80; i += 3) {
        float h = p.h[i];
        if (h < 1.2f) continue;
        if (ps_.size() >= kMax - 20) break;
        float x = (float)(p.L + i * kStep);
        Part c;
        c.k = PK::Clump; c.x = x; c.y = (float)(p.top + dip(p, x) - h); c.vx = F(-35, 35); c.vy = F(0, 40);
        c.s = std::min(h, 10.f) * 0.4f + 1.4f; c.c = p.kind; c.ph = F(0, 6.28f); c.life = 4.f;
        ps_.push_back(c);
        spawned++;
    }
}

// Провал верхнего края у скруглённых углов Windows 11.
double Engine::dip(const Pile& p, double x) {
    if (p.taskbar || p.rad <= 0) return 0;
    double d = std::min(x - p.L, p.R - x);
    if (d >= p.rad) return 0;
    if (d < 0) d = 0;
    double q = p.rad - d;
    return p.rad - std::sqrt(std::max(0.0, p.rad * p.rad - q * q));
}

float Engine::htAt(const Pile& p, double x) {
    double f = (x - p.L) / kStep;
    int i = (int)std::floor(f);
    if (i < 0) return p.h.empty() ? 0.f : p.h[0];
    if (i >= p.n - 1) return p.h[p.n - 1];
    float t = (float)(f - i);
    return p.h[i] + (p.h[i + 1] - p.h[i]) * t;
}

void Engine::add(Pile& p, double x, float amt) {
    int c = (int)((x - p.L) / kStep);
    float mx = maxH(p);
    static const float w[5] = {0.1f, 0.2f, 0.4f, 0.2f, 0.1f};
    for (int k = -2; k <= 2; k++) {
        int i = c + k;
        if (i < 0 || i >= p.n) continue;
        float nv = std::min(mx, p.h[i] + amt * w[k + 2] * 2.2f);
        p.total += nv - p.h[i];
        p.h[i] = nv;
    }
    p.kind = season;
}

// ---------- шаг ----------
void Engine::step(double dt) {
    dt = std::min(std::max(dt, 0.0), 0.05);
    t_ += dt;
    Date d; int hh, mi, ss; nowOf(*this, d, hh, mi, ss);
    season = Cal::seasonIndex(S, d);
    holiday = Cal::holiday(S, d);
    float gust = (float)(std::sin(t_ * 0.37) * std::sin(t_ * 0.113 + 1.3));
    wind_ = S.Wind / 100.f * 90.f + gust * (season == 3 ? 55.f : 32.f) * (0.4f + std::fabs((float)S.Wind) / 200.f);
    spawn(dt);
    updateParts(dt);
    updatePiles(dt);
    updateFireworks(dt);
}

void Engine::emit(PK k, float x, float y) {
    if (ps_.size() >= kMax) return;
    float sz = S.Size / 100.f;
    Part p; p.k = k; p.x = x; p.y = y; p.ph = F(0, 6.28f); p.life = 99.f;
    switch (k) {
        case PK::Snow: p.s = F(1.2f, 3.2f) * sz; p.vy = F(42, 95) * (0.6f + p.s / 4.f); break;
        case PK::Leaf: p.s = F(3.2f, 5.4f) * sz; p.vy = F(48, 90); p.spin = F(-2.2f, 2.2f); p.c = Ri(5); p.rot = F(0, 6.28f); break;
        case PK::Petal: p.s = F(2.4f, 3.8f) * sz; p.vy = F(26, 55); p.spin = F(-1.8f, 1.8f); p.c = Ri(4); p.rot = F(0, 6.28f); break;
        case PK::Fluff: p.s = F(1.6f, 2.6f) * sz; p.vy = F(7, 22); break;
        case PK::Rain: p.s = 1.f; p.vy = F(620, 820); p.vx = wind_ * 2.2f + 40.f; break;
        case PK::Heart: p.s = F(3.5f, 6.5f) * sz; p.vy = -F(38, 70); p.c = Ri(3); break;
        case PK::Confetti: p.s = F(2.6f, 4.f) * sz; p.vy = F(55, 115); p.spin = F(-5, 5); p.rot = F(0, 6.28f); p.c = Ri(7); break;
        case PK::Balloon: p.s = F(10.f, 15.f) * sz; p.vy = -F(30, 55); p.c = Ri(7); break;
        case PK::Twinkle: p.s = F(2.f, 5.f); p.life = F(1.2f, 3.2f); break;
        default: break;
    }
    ps_.push_back(p);
}

void Engine::spawnBat() {
    if (ps_.size() >= kMax) return;
    bool fromLeft = Ri(2) == 0;
    Part p; p.k = PK::Bat; p.x = fromLeft ? OX - 30.f : OX + W + 30.f; p.y = OY + F(0.08f, 0.6f) * H;
    p.vx = (fromLeft ? 1.f : -1.f) * F(80, 150); p.s = F(6.f, 10.f); p.ph = F(0, 6.28f); p.life = 99.f;
    ps_.push_back(p);
}

void Engine::spawn(double dt) {
    if (season < 0 && holiday.empty()) return;
    float area = W * (float)H / 1e6f, k = S.Intensity / 100.f, sp = S.Speed / 100.f;
    auto xr = [&] { return F((float)OX - 120, (float)OX + W + 120); };
    switch (season) {
        case 0: rate(sn_, 16.f * area * k * sp, dt, [&] { emit(PK::Snow, xr(), (float)OY - 10); }); break;
        case 1: rate(sn_, 5.f * area * k * sp, dt, [&] { emit(PK::Petal, xr(), (float)OY - 10); }); break;
        case 2: rate(sn_, 3.2f * area * k * sp, dt, [&] { emit(PK::Fluff, xr(), (float)OY - 10); }); break;
        case 3: rate(sn_, 4.5f * area * k * sp, dt, [&] { emit(PK::Leaf, xr(), (float)OY - 10); }); break;
    }
    // дожди весной и осенью: на 10–20 секунд, примерно раз в минуту
    if (season == 1 || season == 3) {
        if (rainLeft_ <= 0 && std::uniform_real_distribution<double>(0, 1)(rng_) < dt / 70) rainLeft_ = F(10, 20);
        if (rainLeft_ > 0) { rainLeft_ -= (float)dt; rate(rn_, 130.f * area * k, dt, [&] { emit(PK::Rain, xr(), (float)OY - 10); }); }
    } else rainLeft_ = 0;

    auto petal = [&](int c) { emit(PK::Petal, xr(), (float)OY - 10); if (!ps_.empty()) ps_.back().c = c; };
    if (holiday == "halloween") rate(hl_, 0.22f * area * k, dt, [&] { spawnBat(); });
    else if (holiday == "valentine") rate(hl_, 3.f * area * k * sp, dt, [&] { emit(PK::Heart, xr(), (float)OY + H + 10); });
    else if (holiday == "women") rate(hl_, 3.f * area * k * sp, dt, [&] { petal(4 + Ri(3)); });
    else if (holiday == "tet") rate(hl_, 3.f * area * k * sp, dt, [&] { petal(Ri(2) == 0 ? 0 : 7); });
    else if (holiday == "maslenitsa") rate(hl_, 3.5f * area * k * sp, dt, [&] { emit(PK::Confetti, xr(), (float)OY - 10); });
    else if (holiday == "xmas") rate(hl_, 4.f * area * k * sp, dt, [&] { emit(PK::Twinkle, F((float)OX, (float)OX + W), F((float)OY, OY + H * 0.8f)); });
    else if (holiday == "custom") {
        if (S.CustomFx == 0) rate(hl_, 0.35f * area * k, dt, [&] { emit(PK::Balloon, xr(), (float)OY + H + 20); });
        else if (S.CustomFx == 1) rate(hl_, 4.f * area * k * sp, dt, [&] { emit(PK::Confetti, xr(), (float)OY - 10); });
        else if (S.CustomFx == 2) rate(hl_, 3.5f * area * k * sp, dt, [&] { petal(4 + Ri(3)); });
        else if (fwLeft < 1) fwLeft = 3;
    }
}

// ---------- частицы ----------
void Engine::updateParts(double dt) {
    float f = (float)dt, sp = S.Speed / 100.f, ease = std::min(1.f, f * 2.f);
    float minX = OX - 260.f, maxX = OX + W + 260.f, maxY = OY + H + 30.f;
    for (size_t i = 0; i < ps_.size(); i++) {
        Part& p = ps_[i];
        p.age += f;
        float oy = p.y;
        bool dead = false;
        switch (p.k) {
            case PK::Snow:
                p.vx += (wind_ * 0.6f + std::sin(p.age * 0.9f + p.ph) * 16.f - p.vx) * ease;
                p.x += p.vx * sp * f; p.y += p.vy * sp * f;
                dead = tryLand(p, oy); break;
            case PK::Leaf:
                p.vx += (wind_ * 1.1f + std::sin(p.age * 1.6f + p.ph) * 45.f - p.vx) * ease;
                p.x += p.vx * sp * f; p.y += (p.vy + std::sin(p.age * 2.3f + p.ph) * 18.f) * sp * f; p.rot += p.spin * f;
                dead = tryLand(p, oy); break;
            case PK::Petal:
                p.vx += (wind_ * 0.9f + std::sin(p.age * 1.1f + p.ph) * 30.f - p.vx) * ease;
                p.x += p.vx * sp * f; p.y += p.vy * sp * f; p.rot += p.spin * f;
                dead = tryLand(p, oy); break;
            case PK::Fluff:
                p.vx += (wind_ * 0.7f + std::sin(p.age * 0.5f + p.ph) * 12.f - p.vx) * ease;
                p.x += p.vx * sp * f; p.y += (p.vy + std::sin(p.age * 0.8f + p.ph) * 6.f) * sp * f;
                dead = tryLand(p, oy); break;
            case PK::Rain:
                p.vx += (wind_ * 2.2f + 40.f - p.vx) * ease;
                p.x += p.vx * f; p.y += p.vy * f;
                dead = tryLand(p, oy); break;
            case PK::Clump:
                p.vy += 700.f * f; p.x += p.vx * f; p.y += p.vy * f;
                dead = tryLand(p, oy); break;
            case PK::Heart:
                p.vx += (wind_ * 0.4f + std::sin(p.age * 1.3f + p.ph) * 14.f - p.vx) * ease;
                p.x += p.vx * f; p.y += p.vy * sp * f;
                dead = p.y < OY - 30; break;
            case PK::Bat:
                p.x += p.vx * f; p.y += std::sin(p.age * 3.f + p.ph) * 32.f * f;
                dead = p.x < OX - 80 || p.x > OX + W + 80; break;
            case PK::Confetti:
                p.vx += (wind_ * 0.8f + std::sin(p.age * 2.5f + p.ph) * 40.f - p.vx) * ease;
                p.x += p.vx * f; p.y += p.vy * sp * f; p.rot += p.spin * f;
                dead = tryLand(p, oy); break;
            case PK::Spark:
                p.vy += 170.f * f; p.vx *= 1.f - 0.6f * f; p.x += p.vx * f; p.y += p.vy * f;
                p.a = std::min(1.f, std::max(0.f, 1.f - p.age / p.life)); dead = p.age >= p.life; break;
            case PK::Rocket:
                p.y += p.vy * f; p.x += p.vx * f;
                if (p.age >= p.life) { float ex = p.x, ey = p.y; int hue = p.c; dead = true; explode(ex, ey, hue); }
                break;
            case PK::Splash: dead = p.age >= p.life; break;
            case PK::Balloon:
                p.vx += (wind_ * 0.3f + std::sin(p.age * 0.9f + p.ph) * 18.f - p.vx) * ease;
                p.x += p.vx * f; p.y += p.vy * sp * f;
                dead = p.y < OY - 60; break;
            case PK::Twinkle: dead = p.age >= p.life; break;
        }
        // explode() мог добавить частицы и переместить массив — берём ссылку заново
        Part& q = ps_[i];
        if (!dead && q.k != PK::Twinkle && q.k != PK::Splash && (q.x < minX || q.x > maxX || q.y > maxY)) dead = true;
        if (dead) { ps_[i] = ps_.back(); ps_.pop_back(); i--; }
    }
}

// Попал ли на край окна. true — частица кончилась.
bool Engine::tryLand(Part& p, float oy) {
    for (auto& sg : segs_) {
        if (p.x < sg.a || p.x > sg.b) continue;
        Pile& pile = *sg.p;
        float top = (float)(pile.top + dip(pile, p.x) - htAt(pile, p.x));
        if (!(oy <= top && p.y > top)) continue;
        bool grows = S.Piles && season >= 0 && (!pile.taskbar || S.TaskbarPile) && (pile.kind < 0 || pile.kind == season);
        switch (p.k) {
            case PK::Snow: if (grows && season == 0) add(pile, p.x, pileGain * 0.55f); return true;
            case PK::Leaf: if (grows && season == 3) add(pile, p.x, pileGain * 1.1f); return true;
            case PK::Petal: if (grows && season == 1) add(pile, p.x, pileGain * 0.7f); return true;
            case PK::Fluff: if (grows && season == 2 && Ri(3) == 0) add(pile, p.x, pileGain * 0.35f); return true;
            case PK::Clump: if (grows) add(pile, p.x, pileGain * 0.4f); return true;
            case PK::Rain: {
                if (ps_.size() < kMax) { Part s; s.k = PK::Splash; s.x = p.x; s.y = top; s.life = 0.28f; s.s = F(3.f, 5.5f); ps_.push_back(s); }
                return true;
            }
            default: return true;
        }
    }
    return false;
}

// ---------- сугробы ----------
void Engine::updatePiles(double dt) {
    float f = (float)dt;
    relaxAcc_ += dt;
    bool relax = relaxAcc_ >= 0.1; if (relax) relaxAcc_ = 0;
    bool growGrass = (season == 1 || season == 2) && S.Piles;
    // ровный «осадок»: пока идёт снег, листопад или цветение, край окна понемногу обрастает по всей видимой длине
    float rt = season == 0 ? 0.2f : season == 3 ? 0.18f : season == 1 ? 0.1f : season == 2 ? 0.06f : 0.f;
    if (S.Piles && rt > 0) {
        for (auto& sg : segs_) {
            Pile& p = *sg.p;
            if (p.taskbar && !S.TaskbarPile) continue;
            if (p.kind >= 0 && p.kind != season) continue;
            float mx = maxH(p);
            int i0 = std::max(0, (int)((sg.a - p.L) / kStep)), i1 = std::min(p.n - 1, (int)((sg.b - p.L) / kStep));
            bool any = false;
            for (int i = i0; i <= i1; i++) {
                float noise = 0.7f + 0.6f * (float)(hash(p.seed, (unsigned)(i / 3)) % 100) / 100.f;
                float nv = std::min(mx, p.h[i] + rt * (S.Intensity / 100.f) * (S.Speed / 100.f) * noise * f);
                p.total += nv - p.h[i]; p.h[i] = nv; any = true;
            }
            if (any) p.kind = season;
        }
    }
    for (auto& kv : piles_) {
        Pile& p = *kv.second;
        bool melting = !S.Piles || (p.kind >= 0 && p.kind != season);
        if (melting && p.total > 0) {
            float tot = 0;
            for (int i = 0; i < p.n; i++) { float v = p.h[i] - f * 9.f; if (v < 0) v = 0; p.h[i] = v; tot += v; }
            p.total = tot;
            if (tot < 0.5f) { std::fill(p.h.begin(), p.h.end(), 0.f); p.total = 0; p.kind = -1; }
        } else if (relax && p.total > 0) {
            for (int i = 0; i < p.n - 1; i++) {
                float d = p.h[i] - p.h[i + 1];
                if (std::fabs(d) > 1.5f) { float mv = (std::fabs(d) - 1.5f) * 0.35f * (d > 0 ? 1.f : -1.f); p.h[i] -= mv; p.h[i + 1] += mv; }
            }
        }
        // трава: растёт весной и летом, осенью и зимой вянет
        for (int i = 0; i < p.n; i++) {
            float g = p.g[i];
            if (growGrass && !p.taskbar) g = std::min(p.t[i], g + f * (1.4f + std::fmod(p.t[i], 3.f) * 0.3f));
            else if (g > 0) g = std::max(0.f, g - f * 5.f);
            p.g[i] = g;
        }
    }
}

// ---------- салют ----------
void Engine::explode(float x, float y, int hue) {
    int count = 56 + Ri(28);
    bool ring = Ri(10) < 4, two = Ri(3) == 0;
    float h0 = hue / 100.f;
    for (int i = 0; i < count && ps_.size() < kMax; i++) {
        float a = ring ? i * 6.2832f / count : F(0, 6.2832f);
        float sp = ring ? 190.f : F(60, 250);
        int c = (int)((h0 + (two && i % 2 == 0 ? 0.5f : 0.f) + F(-0.03f, 0.03f)) * 100.f);
        Part s; s.k = PK::Spark; s.x = x; s.y = y; s.vx = std::cos(a) * sp; s.vy = std::sin(a) * sp;
        s.s = F(1.4f, 2.6f); s.life = F(1.1f, 1.9f); s.c = c; s.a = 1.f;
        ps_.push_back(s);
    }
}

void Engine::launch() {
    if (ps_.size() >= kMax - 120) return;
    float x = OX + W * F(0.12f, 0.88f), ty = OY + H * F(0.12f, 0.5f), flight = F(0.9f, 1.4f);
    Part r; r.k = PK::Rocket; r.x = x; r.y = (float)(OY + H); r.vx = F(-25, 25); r.vy = -((OY + H) - ty) / flight; r.s = 1.8f; r.life = flight; r.c = Ri(100);
    ps_.push_back(r);
}

// Праздничная ночь: в 00:00 салют; в последние 10 секунд года — большой отсчёт.
void Engine::updateFireworks(double dt) {
    if (fwLeft > 0) {
        fwLeft -= (float)dt; rockAcc_ += dt;
        if (rockAcc_ >= 0.55) { rockAcc_ = 0; launch(); if (Ri(3) == 0) launch(); }
    }
    if (nyDemo >= 0) {
        nyDemo += dt;
        if (nyDemo >= 10 && !nyFired_) { nyFired_ = true; if (S.NyFireworks) fwLeft = 25; }
        if (nyDemo > 40) { nyDemo = -1; nyFired_ = false; }
        return;
    }
    Date d; int hh, mi, ss; nowOf(*this, d, hh, mi, ss);
    bool midnight = holiday == "newyear" && d.m == 1 && d.d == 1 && hh == 0 && mi == 0 && ss < 40;
    if (midnight) { if (!nyFired_) { nyFired_ = true; if (S.NyFireworks) fwLeft = 25; } }
    else if (!(holiday == "newyear" && d.m == 12 && d.d == 31 && hh == 23 && mi == 59 && ss >= 50)) nyFired_ = false;
}

std::string Engine::bigText(bool& greeting) const {
    greeting = false;
    if (!S.Countdown || holiday != "newyear") return "";
    if (nyDemo >= 0) {
        if (nyDemo < 10) return std::to_string(std::max(1, (int)std::ceil(10 - nyDemo)));
        if (nyDemo < 30) { greeting = true; return "\xD0\xA1 \xD0\x9D\xD0\xBE\xD0\xB2\xD1\x8B\xD0\xBC \xD0\xB3\xD0\xBE\xD0\xB4\xD0\xBE\xD0\xBC!"; }   // «С Новым годом!»
        return "";
    }
    Date d; int hh, mi, ss; nowOf(*this, d, hh, mi, ss);
    if (d.m == 12 && d.d == 31 && hh == 23 && mi == 59 && ss >= 50) return std::to_string(60 - ss);
    if (d.m == 1 && d.d == 1 && hh == 0 && mi == 0 && ss < 20) { greeting = true; return "\xD0\xA1 \xD0\x9D\xD0\xBE\xD0\xB2\xD1\x8B\xD0\xBC \xD0\xB3\xD0\xBE\xD0\xB4\xD0\xBE\xD0\xBC!"; }
    return "";
}

}  // namespace sp
