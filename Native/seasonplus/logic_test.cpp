// Проверки логики Season Plus без экрана: календарь, сугробы, закрытые куски окон, трава, праздники, салют, отсчёт. Запускается и на Windows, и под Wine.
#include <cstdio>
#include <string>
#include <vector>
#include "engine.hpp"

using namespace sp;

static int fails = 0;
static std::string report;
static void check(const std::string& name, bool ok, const std::string& extra = "") {
    report += (ok ? "PASS " : "FAIL ") + name + (extra.empty() ? "" : " — " + extra) + "\n";
    std::fprintf(stderr, "%s %s\n", ok ? "PASS" : "FAIL", name.c_str()); std::fflush(stderr);
    if (!ok) fails++;
}
static std::string f1(double v) { char b[32]; std::snprintf(b, sizeof b, "%.1f", v); return b; }

static std::vector<Surf> surfs() {
    std::vector<Surf> v(3);
    v[0].id = 1; v[0].left = 60; v[0].right = 420; v[0].top = 90; v[0].radius = 8; v[0].vis = {{60, 420}};
    v[1].id = 2; v[1].left = 330; v[1].right = 800; v[1].top = 160; v[1].radius = 8; v[1].vis = {{330, 800}};
    v[2].id = 3; v[2].left = 0; v[2].right = 900; v[2].top = 380; v[2].radius = 0; v[2].taskbar = true; v[2].vis = {{0, 900}};
    return v;
}
static Engine make(const char* season, const char* holiday, int intensity = 100, int seed = 3) {
    Engine e(seed); e.W = 900; e.H = 420;
    e.S.Season = season; e.S.Holiday = holiday; e.S.Intensity = intensity; e.S.Wind = 10; e.S.normalize();
    return e;
}
static const Pile* pile(const Engine& e, long long id) { for (auto* p : e.piles()) if (p->id == id) return p; return nullptr; }
static double mid(const Engine& e, long long id) { auto* p = pile(e, id); return p ? Engine::htAt(*p, (p->L + p->R) / 2) : -1; }

int main() {
    Settings s;
    check("сезон по календарю: январь — зима", Cal::seasonIndex(s, {2027, 1, 15}) == 0);
    check("сезон по календарю: апрель — весна", Cal::seasonIndex(s, {2027, 4, 15}) == 1);
    check("сезон по календарю: июль — лето", Cal::seasonIndex(s, {2027, 7, 15}) == 2);
    check("сезон по календарю: октябрь — осень", Cal::seasonIndex(s, {2027, 10, 15}) == 3);
    { Settings o; o.Season = "off"; check("сезон «выключено» → −1", Cal::seasonIndex(o, {2027, 1, 1}) == -1); }
    check("праздник: 31 декабря — Новый год", Cal::holiday(s, {2026, 12, 31}) == "newyear");
    check("праздник: 31 октября — Хэллоуин", Cal::holiday(s, {2026, 10, 31}) == "halloween");
    check("праздник: 14 февраля", Cal::holiday(s, {2027, 2, 14}) == "valentine");
    check("праздник: 8 марта", Cal::holiday(s, {2027, 3, 8}) == "women");
    check("праздник: середина лета — нет", Cal::holiday(s, {2027, 7, 15}).empty());
    { Date e = Cal::easter(2027); check("Пасха 2027 — 2 мая", e.y == 2027 && e.m == 5 && e.d == 2); }
    { Date e = Cal::easter(2026); check("Пасха 2026 — 12 апреля", e.y == 2026 && e.m == 4 && e.d == 12); }
    check("Масленица 2027 — за 7 недель до Пасхи", Cal::holiday(s, Cal::addDays(Cal::easter(2027), -52)) == "maslenitsa");
    { Settings h; h.Holiday = "halloween"; check("праздник вручную важнее даты", Cal::holiday(h, {2027, 7, 1}) == "halloween"); }
    { Settings c; c.CustomDay = 5; c.CustomMonth = 6; check("свой праздник по дате", Cal::holiday(c, {2027, 6, 5}) == "custom"); }

    std::vector<Seg> cut{{100, 200}};
    auto parts = subtract(0, 300, cut);
    check("закрытый кусок вырезается из середины", parts.size() == 2 && parts[0].a == 0 && parts[0].b == 100 && parts[1].a == 200 && parts[1].b == 300);
    check("закрыто целиком — ничего не остаётся", subtract(120, 180, cut).empty());
    check("не пересекается — без изменений", subtract(250, 300, cut).size() == 1);
    check("два окна подряд", subtract(0, 300, {{50, 100}, {90, 150}}).size() == 2);

    Pile pl; pl.L = 100; pl.R = 500; pl.rad = 8;
    check("край на самом углу провален на радиус", std::fabs(Engine::dip(pl, 100) - 8) < 0.01);
    check("за пределами угла провала нет", Engine::dip(pl, 300) == 0);
    check("провал плавно уменьшается", Engine::dip(pl, 103) > Engine::dip(pl, 106) && Engine::dip(pl, 106) > 0);
    { Pile t; t.L = 0; t.R = 500; t.taskbar = true; check("у панели задач провала нет", Engine::dip(t, 1) == 0); }

    // сугроб растёт и упирается в предел
    {
        Engine e = make("winter", "off");
        e.setSurfaces(surfs());
        double half = -1; double maxv = 0.5 * 26 + 4;
        for (double tt = 0; tt < 420; tt += 0.05) {
            e.step(0.05);
            if (((int)(tt * 20)) % 40 == 0) e.setSurfaces(surfs());
            if (half < 0 && mid(e, 1) >= 0.5 * maxv) half = tt;
        }
        double m1 = mid(e, 1);
        check("зимой на окне вырастает сугроб", m1 > 3.5, "высота " + f1(m1) + " px, потолок " + f1(maxv));
        check("сугроб не выше потолка", m1 <= maxv + 0.01);
        check("сугроб растёт не слишком быстро и не слишком медленно", half > 8 && half < 300, "половина потолка за " + f1(half) + " с");
        auto* p1 = pile(e, 1);
        auto surfY = [&](double x) { return p1->top + Engine::dip(*p1, x) - Engine::htAt(*p1, x); };
        check("у скруглённого угла поверхность ниже, чем в середине", surfY(p1->L + 1) > surfY((p1->L + p1->R) / 2) - 0.01);
        check("на панели задач тоже ложится", mid(e, 3) > 2);
        check("частиц не больше предела", e.parts().size() <= 3200, "частиц " + std::to_string(e.parts().size()));

        size_t before = e.parts().size();
        auto moved = surfs(); moved[0].left += 40; moved[0].right += 40; moved[0].vis = {{moved[0].left, moved[0].right}};
        e.setSurfaces(moved);
        check("после сдвига окна сугроб пуст", mid(e, 1) < 0.01);
        check("и осыпается комьями", (int)e.parts().size() > (int)before - 50, "частиц было " + std::to_string(before) + ", стало " + std::to_string(e.parts().size()));
        e.setSurfaces({});
        check("закрытое окно забыто", e.piles().empty());
    }
    { // смена времени года тает
        Engine e = make("winter", "off"); e.setSurfaces(surfs()); e.warm(200);
        double wH = mid(e, 1);
        e.S.Season = "summer"; e.S.normalize();
        for (double tt = 0; tt < 14; tt += 0.05) e.step(0.05);
        check("зимой был сугроб, при смене на лето он растаял", wH > 1 && mid(e, 1) < 2, f1(wH) + " → " + f1(mid(e, 1)));
    }
    { // трава весной
        Engine e = make("spring", "off"); e.setSurfaces(surfs()); e.warm(40);
        float g = 0; for (float v : pile(e, 1)->g) g = std::max(g, v);
        float g3 = 0; for (float v : pile(e, 3)->g) g3 = std::max(g3, v);
        check("весной на окне растёт трава", g > 3, f1(g));
        check("на панели задач трава не растёт", g3 < 0.01);
    }
    { Engine e = make("autumn", "off"); e.setSurfaces(surfs()); e.warm(300); check("осенью на окне копятся листья", mid(e, 1) > 2, f1(mid(e, 1))); }
    { Engine e = make("autumn", "halloween"); e.setSurfaces(surfs()); e.warm(80); check("в Хэллоуин праздник распознан", e.holiday == "halloween"); }
    {
        Engine e = make("winter", "off"); e.startFireworks(10); e.warm(6);
        int n = e.countOf(PK::Spark) + e.countOf(PK::Rocket);
        check("салют: появляются ракеты и искры", n > 10, "искр и ракет " + std::to_string(n));
    }
    {
        Engine e = make("winter", "newyear"); e.step(0.001); e.nyDemo = 7; bool g = false;
        check("отсчёт перед Новым годом показывает цифру", e.bigText(g) == "3");
        e.nyDemo = 10.5;
        check("после полуночи — поздравление", !e.bigText(g).empty() && g);
        e.S.Countdown = false;
        check("отсчёт можно выключить", e.bigText(g).empty());
    }
    { // настройки: запись и чтение
        Settings a; a.Season = "autumn"; a.Intensity = 77; a.CustomText = "С днём рождения \"друг\"\n"; a.Gpu = 1;
        std::string out; gs::jsonDump(a.toJson(), out);
        gs::Json j; bool ok = gs::jsonParse(out, j);
        Settings b = Settings::fromJson(j);
        check("настройки: записались и прочитались без потерь", ok && b.Season == "autumn" && b.Intensity == 77 && b.Gpu == 1 && b.CustomText.find("друг") != std::string::npos);
        gs::Json bad; check("настройки: мусор вместо JSON отвергается", !gs::jsonParse("{\"Season\": ", bad));
        // файл старой версии на C# (PascalCase, без нового поля)
        gs::Json old; gs::jsonParse("{\"Season\":\"spring\",\"Holiday\":\"off\",\"Intensity\":300,\"Wind\":-5,\"Active\":true}", old);
        Settings c = Settings::fromJson(old);
        check("настройки C#-версии читаются, значения зажимаются", c.Season == "spring" && c.Intensity == 100 && c.Wind == -5 && c.Active && c.Gpu == 0);
    }
    report += fails == 0 ? "ВСЁ В ПОРЯДКЕ\n" : "ЕСТЬ ОШИБКИ: " + std::to_string(fails) + "\n";
    std::fputs(report.c_str(), stdout);
    return fails == 0 ? 0 : 1;
}
