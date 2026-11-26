// Панель задач: измерения значков (от движка в Проводнике), настройки внешнего вида и расчёт плашек.
// Чистая логика без Windows: проверяется отдельно (logic_test.cpp) и в самопроверке модуля.
#pragma once
#include <optional>
#include <string>
#include <vector>

namespace tb {

struct Box {
    int L = 0, T = 0, R = 0, B = 0;
    int w() const { return R - L; }
    int h() const { return B - T; }
    bool operator==(const Box& o) const { return L == o.L && T == o.T && R == o.R && B == o.B; }
};

struct BarGeometry {
    int rootW = 0, rootH = 0, scale100 = 100;
    std::optional<Box> widgets, system, apps;
    std::vector<Box> icons;
    double scale() const { return scale100 / 100.0; }
};

struct GeometryFile {
    long long pid = 0, tick = 0;
    std::vector<BarGeometry> bars;
    static bool parse(const std::string& text, GeometryFile& out);
};

// Скруглённый прямоугольник в пикселях окна панели (от его левого верхнего угла).
struct RoundRect {
    double x = 0, y = 0, w = 0, h = 0, r = 0;
    bool operator==(const RoundRect& o) const { return x == o.x && y == o.y && w == o.w && h == o.h && r == o.r; }
};

// Внешний вид: файл %APPDATA%\BattleLauncher\taskbar-plus-settings.json (тот же, что у прежнего плагина).
struct Appearance {
    std::string Shape = "none";            // none | pill | tab | islands
    int Radius = 14, SideMargin = 0, BottomGap = 6;
    std::string IslandStyle = "float";     // float | tab
    int IslandPadding = 8;
    std::string IslandMode = "each";       // each | group
    int IslandGap = 8;
    bool WidgetsIsland = false, KeepEdges = true;
    bool UseXaml = false;
    bool ShowStroke = false, XamlConsentShown = false, AutoReport = true;
    bool active() const { return Shape != "none" || UseXaml; }
    void normalize();
};

// Единственное место, где решается, что где рисовать: по нему рисуется и окно под панелью, и предпросмотр.
std::vector<RoundRect> buildShapes(const Appearance& s, int barW, int barH, double scale, const BarGeometry& g);

}  // namespace tb
