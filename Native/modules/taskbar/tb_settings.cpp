#include "tb_settings.hpp"
#include "json.hpp"
#include "win.hpp"

namespace tb {

using gs::Json;

std::wstring dataDir() { return gs::appDataDir(); }
static std::wstring file() { return gs::appDataDir() + L"\\taskbar-plus-settings.json"; }

Appearance loadAppearance() {
    Appearance a; std::string t; Json j;
    if (gs::readFile(file(), t) && gs::jsonParse(t, j) && j.t == Json::Obj) {
        a.Shape = j.str("Shape", a.Shape); a.Radius = j.num("Radius", a.Radius); a.SideMargin = j.num("SideMargin", a.SideMargin); a.BottomGap = j.num("BottomGap", a.BottomGap);
        a.IslandStyle = j.str("IslandStyle", a.IslandStyle); a.IslandPadding = j.num("IslandPadding", a.IslandPadding); a.IslandMode = j.str("IslandMode", a.IslandMode);
        a.IslandGap = j.num("IslandGap", a.IslandGap); a.WidgetsIsland = j.boolean("WidgetsIsland", a.WidgetsIsland); a.KeepEdges = j.boolean("KeepEdges", a.KeepEdges);
        a.UseXaml = j.boolean("UseXaml", a.UseXaml); a.ShowStroke = j.boolean("ShowStroke", a.ShowStroke); a.XamlConsentShown = j.boolean("XamlConsentShown", a.XamlConsentShown);
        a.AutoReport = j.boolean("AutoReport", a.AutoReport);
    }
    a.normalize();
    return a;
}

static Json toJson(const Appearance& a, Json base) {
    if (base.t != Json::Obj) base = Json::Object();
    base.set("Shape", Json::String(a.Shape)); base.set("Radius", Json::Number(a.Radius)); base.set("SideMargin", Json::Number(a.SideMargin)); base.set("BottomGap", Json::Number(a.BottomGap));
    base.set("IslandStyle", Json::String(a.IslandStyle)); base.set("IslandPadding", Json::Number(a.IslandPadding)); base.set("IslandMode", Json::String(a.IslandMode));
    base.set("IslandGap", Json::Number(a.IslandGap)); base.set("WidgetsIsland", Json::Boolean(a.WidgetsIsland)); base.set("KeepEdges", Json::Boolean(a.KeepEdges));
    base.set("UseXaml", Json::Boolean(a.UseXaml)); base.set("ShowStroke", Json::Boolean(a.ShowStroke)); base.set("XamlConsentShown", Json::Boolean(a.XamlConsentShown));
    base.set("AutoReport", Json::Boolean(a.AutoReport));
    return base;
}

void saveAppearance(const Appearance& a) {
    std::string t; Json old;
    if (!(gs::readFile(file(), t) && gs::jsonParse(t, old))) old = Json::Object();
    std::string out; gs::jsonDump(toJson(a, old), out);
    gs::makeDirs(gs::appDataDir());
    gs::writeFileAtomic(file(), out);
}

std::string appearanceJson(const Appearance& a) { std::string out; gs::jsonDump(toJson(a, Json::Object()), out); return out; }

}  // namespace tb
