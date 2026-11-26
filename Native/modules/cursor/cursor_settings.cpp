#include "cursor_settings.hpp"
#include <algorithm>
#include <cstdio>
#include "win.hpp"

namespace sp {

static int clampi(int v, int a, int b) { return v < a ? a : v > b ? b : v; }
static bool in(const std::string& v, std::initializer_list<const char*> l) { for (auto* x : l) if (v == x) return true; return false; }

bool CursorSettings::isHex(const std::string& s) {
    if (s.size() != 7 || s[0] != '#') return false;
    for (size_t i = 1; i < 7; i++) if (!isxdigit((unsigned char)s[i])) return false;
    return true;
}

Rgb parseHex(const std::string& s) {
    if (!CursorSettings::isHex(s)) return {255, 42, 157};
    unsigned v = (unsigned)std::stoul(s.substr(1), nullptr, 16);
    return Rgb::hex((int)v);
}

std::string toHex(Rgb c) {
    char b[16];
    auto cl = [](float v) { return v < 0 ? 0 : v > 255 ? 255 : (int)(v + 0.5f); };
    std::snprintf(b, sizeof b, "#%02X%02X%02X", cl(c.r), cl(c.g), cl(c.b));
    return b;
}

void CursorSettings::normalize() {
    bool mine = Shape.rfind("mine:", 0) == 0 && Shape.size() > 5 && Shape.size() < 40;
    if (!(in(Shape, {"windows", "classic", "round", "neon", "dot", "ring", "cross", "ghost", "star"}) || mine)) Shape = "classic";
    if (!isHex(Fill)) Fill = "#FF2A9D";
    std::transform(Fill.begin(), Fill.end(), Fill.begin(), ::toupper);
    if (Outline != "auto" && !isHex(Outline)) Outline = "auto";
    Size = clampi(Size, 100, 300);
    if (!in(Trail, {"none", "comet", "sparks", "glow", "stars", "bubbles", "hearts", "ghost"})) Trail = "none";
    if (!in(TrailColorMode, {"cursor", "rainbow", "two", "custom"})) TrailColorMode = "cursor";
    if (!isHex(TrailColor)) TrailColor = "#00F0FF";
    if (!isHex(TrailColor2)) TrailColor2 = "#FF2A9D";
    TrailLength = clampi(TrailLength, 0, 100); TrailSize = clampi(TrailSize, 0, 100);
    if (!in(Click, {"none", "ring", "burst"})) Click = "none";
}

CursorSettings CursorSettings::fromJson(const gs::Json& j) {
    CursorSettings s;
    s.Shape = j.str("Shape", s.Shape); s.Fill = j.str("Fill", s.Fill); s.Outline = j.str("Outline", s.Outline);
    s.Size = j.num("Size", s.Size); s.All = j.boolean("All", s.All);
    s.Trail = j.str("Trail", s.Trail); s.TrailColorMode = j.str("TrailColorMode", s.TrailColorMode);
    s.TrailColor = j.str("TrailColor", s.TrailColor); s.TrailColor2 = j.str("TrailColor2", s.TrailColor2);
    s.TrailLength = j.num("TrailLength", s.TrailLength); s.TrailSize = j.num("TrailSize", s.TrailSize);
    s.Click = j.str("Click", s.Click);
    s.Applied = j.boolean("Applied", s.Applied); s.OverlayOn = j.boolean("OverlayOn", s.OverlayOn);
    s.normalize();
    return s;
}

gs::Json CursorSettings::toJson() const {
    using gs::Json;
    Json j = Json::Object();
    j.set("Shape", Json::String(Shape)); j.set("Fill", Json::String(Fill)); j.set("Outline", Json::String(Outline));
    j.set("Size", Json::Number(Size)); j.set("All", Json::Boolean(All));
    j.set("Trail", Json::String(Trail)); j.set("TrailColorMode", Json::String(TrailColorMode));
    j.set("TrailColor", Json::String(TrailColor)); j.set("TrailColor2", Json::String(TrailColor2));
    j.set("TrailLength", Json::Number(TrailLength)); j.set("TrailSize", Json::Number(TrailSize));
    j.set("Click", Json::String(Click));
    j.set("Applied", Json::Boolean(Applied)); j.set("OverlayOn", Json::Boolean(OverlayOn));
    return j;
}

std::wstring CursorSettings::dir() { return gs::appDataDir() + L"\\cursor-plus"; }

CursorSettings CursorSettings::load() {
    std::string t; gs::Json j;
    if (gs::readFile(dir() + L"\\settings.json", t) && gs::jsonParse(t, j) && j.t == gs::Json::Obj) return fromJson(j);
    return CursorSettings();
}

void CursorSettings::save() const {
    gs::makeDirs(dir());
    std::string out; gs::jsonDump(toJson(), out);
    gs::writeFileAtomic(dir() + L"\\settings.json", out);
}

Rgb CursorSettings::fillRgb() const { return parseHex(Fill); }
Rgb CursorSettings::lineRgb() const {
    if (Outline != "auto") return parseHex(Outline);
    return Rgb::lerp(parseHex(Fill), {8, 6, 16}, 0.88f);
}
Rgb CursorSettings::trailRgb() const { return parseHex(TrailColor); }
Rgb CursorSettings::trailRgb2() const { return parseHex(TrailColor2); }

}  // namespace sp
