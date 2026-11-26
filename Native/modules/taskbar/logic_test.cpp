// Проверка расчёта плашек и разбора измерений без Windows и Проводника (те же проверки, что были у прежнего плагина Taskbar Plus).
#include <cstdio>
#include <functional>
#include <string>
#include "tb_logic.hpp"

namespace tb {
int runLogicChecks(std::string& log);
}

#ifdef TB_LOGIC_MAIN
int main() { std::string log; int bad = tb::runLogicChecks(log); std::fputs(log.c_str(), stdout); return bad == 0 ? 0 : 1; }
#endif

namespace tb {

int runLogicChecks(std::string& log) {
    int bad = 0;
    auto check = [&](const std::string& name, bool ok, const std::string& extra = "") { log += (ok ? "PASS " : "FAIL ") + name + (extra.empty() ? "" : " — " + extra) + "\n"; if (!ok) bad++; };
    std::string text = "v=1\npid=4242\ntick=1000\nbars=2\nbar|0|1920|48|100\nw|0|12|4|390|44\ns|0|1520|4|1913|44\na|0|1127|4|1507|44\ni|0|1127|4|1171|44\ni|0|1175|4|1219|44\n"
                       "bar|1|1280|48|125\na|1|100|4|300|44\nмусор|строка\ni|7|1|2|3|4\n";
    GeometryFile f; bool ok = GeometryFile::parse(text, f);
    check("файл измерений читается", ok && f.bars.size() == 2 && f.pid == 4242 && f.tick == 1000);
    check("группы разобраны", f.bars.size() == 2 && f.bars[0].widgets && *f.bars[0].widgets == Box{12, 4, 390, 44} && f.bars[0].system && *f.bars[0].system == Box{1520, 4, 1913, 44} && f.bars[0].apps && *f.bars[0].apps == Box{1127, 4, 1507, 44} && f.bars[0].icons.size() == 2);
    check("масштаб и вторая панель", f.bars.size() == 2 && f.bars[1].scale100 == 125 && f.bars[1].apps && *f.bars[1].apps == Box{100, 4, 300, 44} && !f.bars[1].widgets);
    GeometryFile g0; GeometryFile::parse("ааа\n|||\nbar|x|y", g0);
    check("мусор не ломает разбор", g0.bars.empty());

    BarGeometry g1; g1.rootW = 1920; g1.rootH = 48; g1.widgets = Box{12, 4, 390, 44}; g1.system = Box{1520, 4, 1913, 44}; g1.apps = Box{1127, 4, 1507, 44};
    for (int x = 1127; x + 44 <= 1507; x += 48) g1.icons.push_back(Box{x, 4, x + 44, 44});
    auto covers = [](const std::vector<RoundRect>& rs, const Box& b) { for (auto& r : rs) if (r.x <= b.L + 0.5 && r.x + r.w >= b.R - 0.5) return true; return false; };
    auto allIcons = [&](const std::vector<RoundRect>& rs) { for (auto& i : g1.icons) if (!covers(rs, i)) return false; return true; };

    Appearance pill; pill.Shape = "pill"; pill.Radius = 18; pill.IslandPadding = 10; pill.BottomGap = 6; pill.KeepEdges = true; pill.WidgetsIsland = true;
    auto r1 = buildShapes(pill, 1920, 48, 1.0, g1);
    check("плавающая: накрывает ВСЕ значки", allIcons(r1), "плашек " + std::to_string(r1.size()));
    check("плавающая: накрывает трей и виджеты отдельно", covers(r1, *g1.system) && covers(r1, *g1.widgets));
    bool within = true, tall = true, widthOk = false;
    for (auto& r : r1) { if (!(r.x >= 0 && r.x + r.w <= 1920 && r.y >= 0 && r.y + r.h <= 48.01)) within = false; if (!(r.y <= 4.01 && r.y + r.h >= 43.99)) tall = false; if (r.w >= g1.apps->w()) widthOk = true; }
    check("плавающая: не шире окна и не выше", within);
    check("плавающая: накрывает значки по высоте", tall);
    check("плавающая: плашка не уже значков", widthOk);

    pill.KeepEdges = false;
    auto r2 = buildShapes(pill, 1920, 48, 1.0, g1);
    check("одна общая плашка: от виджетов до часов", r2.size() == 1 && r2[0].x <= 12 && r2[0].x + r2[0].w >= 1913, "плашек " + std::to_string(r2.size()));
    pill.KeepEdges = true;

    Appearance tab; tab.Shape = "tab"; tab.Radius = 16; tab.IslandPadding = 10; tab.WidgetsIsland = true;
    auto r3 = buildShapes(tab, 1920, 48, 1.0, g1);
    bool tabOk = !r3.empty(); for (auto& r : r3) if (!(r.y == 0 && r.y + r.h > 48)) tabOk = false;
    check("вкладка: от верхнего края и уходит за нижний", tabOk);
    check("вкладка: накрывает все значки", allIcons(r3));

    Appearance isl; isl.Shape = "islands"; isl.IslandMode = "each"; isl.IslandStyle = "float"; isl.Radius = 14; isl.IslandGap = 8; isl.BottomGap = 6; isl.WidgetsIsland = true;
    auto r4 = buildShapes(isl, 1920, 48, 1.0, g1);
    check("острова: по одному на значок + виджеты + трей", r4.size() == g1.icons.size() + 2, "плашек " + std::to_string(r4.size()));
    bool each = true;
    for (auto& i : g1.icons) { bool f2 = false; for (auto& r : r4) if (r.x <= i.L + 4 && r.x + r.w >= i.R - 4) f2 = true; if (!f2) each = false; }
    check("острова: каждый значок на своём острове", each);
    auto sorted = r4; std::sort(sorted.begin(), sorted.end(), [](auto& a, auto& b) { return a.x < b.x; });
    bool noOverlap = true; for (size_t i = 1; i < sorted.size(); i++) if (sorted[i].x < sorted[i - 1].x + sorted[i - 1].w - 0.01) noOverlap = false;
    check("острова не налезают друг на друга", noOverlap);

    isl.IslandMode = "group"; isl.IslandPadding = 8;
    auto r5 = buildShapes(isl, 1920, 48, 1.0, g1);
    check("острова-группы: значки одной группой", r5.size() == 3 && covers(r5, *g1.apps), "плашек " + std::to_string(r5.size()));

    Appearance nw; nw.Shape = "pill"; nw.Radius = 18; nw.IslandPadding = 10; nw.KeepEdges = true;
    auto rw = buildShapes(nw, 1920, 48, 1.0, g1);
    bool noLeft = true; for (auto& r : rw) if (r.x < 100) noLeft = false;
    check("виджеты по умолчанию без острова", rw.size() == 2 && noLeft, "плашек " + std::to_string(rw.size()));

    BarGeometry narrow; narrow.rootW = 400; narrow.rootH = 48; narrow.apps = Box{100, 4, 120, 44}; narrow.icons.push_back(Box{100, 4, 120, 44});
    Appearance big; big.Shape = "pill"; big.Radius = 30; big.IslandPadding = 2;
    auto r6 = buildShapes(big, 400, 48, 1.0, narrow);
    check("скругление ограничено 40% меньшей стороны", r6.size() == 1 && r6[0].r <= 0.4 * std::min(r6[0].w, r6[0].h) + 0.01);

    BarGeometry g7; g7.rootW = 2880; g7.rootH = 72; g7.scale100 = 150; g7.apps = Box{1500, 6, 2000, 66}; g7.icons.push_back(Box{1500, 6, 1566, 66});
    Appearance a7; a7.Shape = "pill"; a7.Radius = 10; a7.IslandPadding = 10;
    auto r7 = buildShapes(a7, 2880, 72, 1.5, g7);
    check("масштаб 150%: отступ 10 → 15 px", r7.size() == 1 && std::abs(r7[0].x - (1500 - 15)) < 0.01 && std::abs(r7[0].x + r7[0].w - (2000 + 15)) < 0.01);

    check("форма «обычная» — плашек нет", buildShapes(Appearance{}, 1920, 48, 1.0, g1).empty());
    BarGeometry empty; empty.rootW = 1920; empty.rootH = 48;
    check("нет измерений — плашек нет", buildShapes(pill, 1920, 48, 1.0, empty).empty());

    Appearance gap; gap.Shape = "pill"; gap.Radius = 18; gap.IslandPadding = 10; gap.BottomGap = 20;
    auto r8 = buildShapes(gap, 1920, 48, 1.0, g1);
    bool gapOk = !r8.empty(); for (auto& r : r8) if (r.y + r.h < 43.99) gapOk = false;
    check("большой зазор снизу не отрывает плашку от значков", gapOk);
    return bad;
}

}  // namespace tb
