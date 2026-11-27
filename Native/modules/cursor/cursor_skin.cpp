// Скин: часть «курсор» (ключи camelCase, значения из белого списка; свой нарисованный курсор в скин не входит)
#include "cursor_module.hpp"

namespace sp {

bool CursorModule::skinCapture(gs::Json& o) {
    if (!saved_.Applied) return false;
    o = gs::Json::Object();
    using gs::Json;
    o.set("shape", Json::String(saved_.Shape.rfind("mine:", 0) == 0 ? "classic" : saved_.Shape)); o.set("fill", Json::String(saved_.Fill)); o.set("outline", Json::String(saved_.Outline));
    o.set("size", Json::Number(saved_.Size)); o.set("all", Json::Boolean(saved_.All)); o.set("trail", Json::String(saved_.Trail)); o.set("trailColorMode", Json::String(saved_.TrailColorMode));
    o.set("trailColor", Json::String(saved_.TrailColor)); o.set("trailColor2", Json::String(saved_.TrailColor2)); o.set("trailLength", Json::Number(saved_.TrailLength)); o.set("trailSize", Json::Number(saved_.TrailSize));
    o.set("click", Json::String(saved_.Click));
    return true;
}

std::string CursorModule::skinApply(const gs::Json& p) {
    CursorSettings s;
    s.Shape = p.str("shape", s.Shape); if (s.Shape.rfind("mine:", 0) == 0) s.Shape = "classic";
    s.Fill = p.str("fill", s.Fill); s.Outline = p.str("outline", s.Outline); s.Size = p.num("size", s.Size); s.All = p.boolean("all", s.All);
    s.Trail = p.str("trail", "none"); s.TrailColorMode = p.str("trailColorMode", s.TrailColorMode); s.TrailColor = p.str("trailColor", s.TrailColor); s.TrailColor2 = p.str("trailColor2", s.TrailColor2);
    s.TrailLength = p.num("trailLength", s.TrailLength); s.TrailSize = p.num("trailSize", s.TrailSize); s.Click = p.str("click", "none");
    s.OverlayOn = s.wantsOverlay();
    s.normalize();
    return applyNow(s);
}

}  // namespace sp
