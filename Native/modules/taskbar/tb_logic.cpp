#include "tb_logic.hpp"
#include <algorithm>
#include <cmath>
#include <cstdlib>
#include <sstream>

namespace tb {

static std::vector<std::string> split(const std::string& s, char c) {
    std::vector<std::string> out; std::string cur;
    for (char ch : s) { if (ch == c) { out.push_back(cur); cur.clear(); } else cur += ch; }
    out.push_back(cur);
    return out;
}
static std::string trim(const std::string& s) {
    size_t a = 0, b = s.size();
    while (a < b && isspace((unsigned char)s[a])) a++;
    while (b > a && isspace((unsigned char)s[b - 1])) b--;
    return s.substr(a, b - a);
}
static bool toInt(const std::string& s, int& v) {
    if (s.empty()) return false;
    char* e = nullptr; long x = std::strtol(s.c_str(), &e, 10);
    if (*e) return false;
    v = (int)x; return true;
}

// Формат (по строке на запись, поля через «|»): pid=… / tick=… ; bar|n|ширина|высота|масштаб×100 ;
//   w|n|л|в|п|н  виджеты   s|n|…  системная область   a|n|…  все значки приложений   i|n|…  один значок
bool GeometryFile::parse(const std::string& text, GeometryFile& f) {
    f = GeometryFile{};
    std::vector<std::pair<int, BarGeometry>> bars;
    auto find = [&](int n) -> BarGeometry* { for (auto& b : bars) if (b.first == n) return &b.second; return nullptr; };
    for (auto& raw : split(text, '\n')) {
        std::string line = trim(raw);
        if (line.empty()) continue;
        size_t eq = line.find('=');
        if (eq != std::string::npos && eq > 0 && line.find('|') == std::string::npos) {
            std::string k = line.substr(0, eq), v = line.substr(eq + 1);
            if (k == "pid") f.pid = std::atoll(v.c_str()); else if (k == "tick") f.tick = std::atoll(v.c_str());
            continue;
        }
        auto p = split(line, '|');
        int n = 0;
        if (p.size() < 5 || !toInt(p[1], n) || n < 0 || n > 16) continue;
        int v[4] = {};
        bool ok = true;
        for (int i = 0; i < 4 && 2 + i < (int)p.size(); i++) ok = ok && toInt(p[2 + i], v[i]);
        if (!ok) continue;
        if (p[0] == "bar") {
            bars.erase(std::remove_if(bars.begin(), bars.end(), [&](auto& b) { return b.first == n; }), bars.end());
            BarGeometry g; g.rootW = v[0]; g.rootH = v[1]; g.scale100 = std::max(50, v[2]);
            bars.push_back({n, g});
            continue;
        }
        BarGeometry* bar = find(n);
        if (!bar || p.size() < 6) continue;
        Box b{v[0], v[1], v[2], v[3]};
        if (b.w() < 1 || b.h() < 1) continue;
        if (p[0] == "w") bar->widgets = b; else if (p[0] == "s") bar->system = b; else if (p[0] == "a") bar->apps = b; else if (p[0] == "i") bar->icons.push_back(b);
    }
    std::sort(bars.begin(), bars.end(), [](auto& a, auto& b) { return a.first < b.first; });
    for (auto& b : bars) f.bars.push_back(b.second);
    return true;
}

void Appearance::normalize() {
    auto cl = [](int v, int a, int b) { return std::min(b, std::max(a, v)); };
    if (Shape != "none" && Shape != "pill" && Shape != "tab" && Shape != "islands") Shape = "none";
    if (IslandStyle != "float" && IslandStyle != "tab") IslandStyle = "float";
    if (IslandMode != "each" && IslandMode != "group") IslandMode = "each";
    IslandGap = cl(IslandGap, 0, 40); Radius = cl(Radius, 0, 30); SideMargin = cl(SideMargin, 0, 600); BottomGap = cl(BottomGap, 0, 20); IslandPadding = cl(IslandPadding, 0, 40);
}

static std::vector<Box> iconsSorted(const BarGeometry& g) {
    std::vector<Box> in = g.icons, list;
    std::sort(in.begin(), in.end(), [](const Box& a, const Box& b) { return a.L != b.L ? a.L < b.L : a.R < b.R; });
    for (auto& b : in) {
        if (!list.empty()) { const Box& prev = list.back(); if (b.L >= prev.L - 2 && b.R <= prev.R + 2) continue; }
        list.push_back(b);
    }
    return list;
}

std::vector<RoundRect> buildShapes(const Appearance& s, int barW, int barH, double scale, const BarGeometry& g) {
    std::vector<RoundRect> rects;
    if (s.Shape == "none" || barW < 10 || barH < 10) return rects;
    double pad = s.IslandPadding * scale, radius = s.Radius * scale, inset = 3 * scale;
    bool attached = s.Shape == "tab" || (s.Shape == "islands" && s.IslandStyle == "tab");

    // Вертикаль: у «вкладки» плашка идёт от верхнего края и уходит за нижний; у плавающих форм остаётся зазор снизу,
    // но не больше, чем позволяют сами значки (они сидят на своих местах и должны оказаться на плашке).
    double contentTop = 1e18, contentBottom = 0;
    std::optional<Box> widgets = s.WidgetsIsland ? g.widgets : std::nullopt;
    const std::optional<Box>* parts[] = {&g.apps, &widgets, &g.system};
    for (auto* b : parts)
        if (b->has_value()) { contentTop = std::min(contentTop, (double)(*b)->T); contentBottom = std::max(contentBottom, (double)(*b)->B); }
    if (contentTop == 1e18) return rects;
    double top, bottom;
    if (attached) { top = 0; bottom = barH + radius; }
    else {
        top = std::min(inset, contentTop);
        bottom = std::min((double)barH, std::max(barH - s.BottomGap * scale, contentBottom));
        if (bottom - top < 8) { top = 0; bottom = barH; }
    }
    double visibleH = (attached ? barH : bottom) - top;

    auto add = [&](double l, double r) {
        l = std::max(0.0, l); r = std::min((double)barW, r);
        if (r - l < 4) return;
        // скругление не больше ~40% меньшей стороны видимой фигуры: узкий остров остаётся скруглённым квадратом, а не кругом
        double rr = std::min(radius, 0.4 * std::min(r - l, visibleH));
        rects.push_back({l, top, r - l, bottom - top, std::max(0.0, rr)});
    };

    std::vector<std::pair<double, double>> edges;   // виджеты и системная область — отдельными островами (или одной плашкой)
    if (widgets) edges.push_back({widgets->L - pad, widgets->R + pad});
    if (g.system) edges.push_back({g.system->L - pad, g.system->R + pad});

    if (s.Shape == "pill" || s.Shape == "tab") {
        if (!g.apps) { for (auto& e : edges) add(e.first, e.second); return rects; }
        if (s.KeepEdges) { add(g.apps->L - pad, g.apps->R + pad); for (auto& e : edges) add(e.first, e.second); }
        else {
            double l = g.apps->L - pad, r = g.apps->R + pad;
            for (auto& e : edges) { l = std::min(l, e.first); r = std::max(r, e.second); }
            add(l, r);
        }
    } else if (s.Shape == "islands") {
        for (auto& e : edges) add(e.first, e.second);
        auto icons = iconsSorted(g);
        if (s.IslandMode == "group") {
            double join = 16 * scale; bool has = false; double cl = 0, cr = 0;
            for (auto& ic : icons) {
                if (has && ic.L - cr <= join) cr = std::max(cr, (double)ic.R);
                else { if (has) add(cl - pad, cr + pad); cl = ic.L; cr = ic.R; has = true; }
            }
            if (has) add(cl - pad, cr + pad);
        } else {
            double half = s.IslandGap * scale / 2;
            for (auto& ic : icons) add(ic.L + half, ic.R - half);
        }
    }
    return rects;
}

}  // namespace tb
