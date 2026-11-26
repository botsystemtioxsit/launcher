#include "gfx.hpp"
#include <cmath>
#include <cstring>
#include "win.hpp"

namespace sp {

Rgb Rgb::hsv(float h, float s, float v) {
    h -= std::floor(h);
    int i = (int)(h * 6); float f = h * 6 - i;
    float p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s), r, g, b;
    switch (i % 6) { case 0: r = v; g = t; b = p; break; case 1: r = q; g = v; b = p; break; case 2: r = p; g = v; b = t; break; case 3: r = p; g = q; b = v; break; case 4: r = t; g = p; b = v; break; default: r = v; g = p; b = q; break; }
    return {r * 255, g * 255, b * 255};
}

static D2D1_COLOR_F col(Rgb c, float a) { return D2D1::ColorF(c.r / 255.f, c.g / 255.f, c.b / 255.f, a < 0 ? 0 : a > 1 ? 1 : a); }

bool Gfx::init(ID2D1Factory1* factory, IDWriteFactory* dw) {
    fac_ = factory; dw_ = dw;
    D2D1_STROKE_STYLE_PROPERTIES sp = D2D1::StrokeStyleProperties(D2D1_CAP_STYLE_ROUND, D2D1_CAP_STYLE_ROUND, D2D1_CAP_STYLE_ROUND, D2D1_LINE_JOIN_ROUND);
    if (FAILED(fac_->CreateStrokeStyle(sp, nullptr, 0, &round_))) return false;
    // сердечко единичного размера (центр в нуле, высота ≈ 2)
    if (FAILED(fac_->CreatePathGeometry(&heartGeo_))) return false;
    ComPtr<ID2D1GeometrySink> sink;
    if (FAILED(heartGeo_->Open(&sink))) return false;
    const int N = 64;
    for (int i = 0; i <= N; i++) {
        float t = (float)i / N * 6.2831853f;
        float x = 16 * std::pow(std::sin(t), 3.f);
        float y = -(13 * std::cos(t) - 5 * std::cos(2 * t) - 2 * std::cos(3 * t) - std::cos(4 * t));
        D2D1_POINT_2F p = D2D1::Point2F(x / 17.f, y / 17.f + 0.08f);
        if (i == 0) sink->BeginFigure(p, D2D1_FIGURE_BEGIN_FILLED); else sink->AddLine(p);
    }
    sink->EndFigure(D2D1_FIGURE_END_CLOSED);
    return SUCCEEDED(sink->Close());
}

void Gfx::begin(ID2D1RenderTarget* rt) {
    if (!rt) { rt_ = nullptr; solid_.Reset(); glowBrush_.clear(); return; }     // отпустить цель (перед закрытием)
    if (rt != rt_) { rt_ = rt; solid_.Reset(); glowBrush_.clear(); }
    if (!solid_) rt_->CreateSolidColorBrush(D2D1::ColorF(1, 1, 1, 1), &solid_);
    rt_->SetAntialiasMode(D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
}
void Gfx::end() {}

ID2D1SolidColorBrush* Gfx::brush(Rgb c, float a) { solid_->SetColor(col(c, 1.f)); solid_->SetOpacity(a < 0 ? 0 : a > 1 ? 1 : a); return solid_.Get(); }

void Gfx::disc(float cx, float cy, float r, Rgb c, float a) { rt_->FillEllipse(D2D1::Ellipse(D2D1::Point2F(cx, cy), r, r), brush(c, a)); }

ID2D1RadialGradientBrush* Gfx::glowFor(Rgb c) {
    unsigned key = ((unsigned)c.r >> 2) << 16 | ((unsigned)c.g >> 2) << 8 | ((unsigned)c.b >> 2);
    auto it = glowBrush_.find(key);
    if (it != glowBrush_.end()) return it->second.Get();
    D2D1_GRADIENT_STOP st[4] = {{0.f, col(c, 1.f)}, {0.35f, col(c, 0.42f)}, {0.7f, col(c, 0.1f)}, {1.f, col(c, 0.f)}};
    ComPtr<ID2D1GradientStopCollection> coll;
    if (FAILED(rt_->CreateGradientStopCollection(st, 4, &coll))) return nullptr;
    ComPtr<ID2D1RadialGradientBrush> br;
    if (FAILED(rt_->CreateRadialGradientBrush(D2D1::RadialGradientBrushProperties(D2D1::Point2F(0, 0), D2D1::Point2F(0, 0), 1, 1), coll.Get(), &br))) return nullptr;
    auto* raw = br.Get();
    glowBrush_[key] = std::move(br);
    return raw;
}

void Gfx::glow(float cx, float cy, float r, Rgb c, float a) {
    auto* b = glowFor(c);
    if (!b) return;
    b->SetCenter(D2D1::Point2F(cx, cy)); b->SetRadiusX(r); b->SetRadiusY(r); b->SetOpacity(a < 0 ? 0 : a > 1 ? 1 : a);
    rt_->FillEllipse(D2D1::Ellipse(D2D1::Point2F(cx, cy), r, r), b);
}

void Gfx::ring(float cx, float cy, float r, float w, Rgb c, float a) { rt_->DrawEllipse(D2D1::Ellipse(D2D1::Point2F(cx, cy), r, r), brush(c, a), w); }

void Gfx::ellipse(float cx, float cy, float rx, float ry, float ang, Rgb c, float a) {
    D2D1_MATRIX_3X2_F old; rt_->GetTransform(&old);
    rt_->SetTransform(D2D1::Matrix3x2F::Rotation(ang * 57.29578f, D2D1::Point2F(cx, cy)) * old);
    rt_->FillEllipse(D2D1::Ellipse(D2D1::Point2F(cx, cy), rx, ry), brush(c, a));
    rt_->SetTransform(old);
}

void Gfx::line(float x1, float y1, float x2, float y2, float w, Rgb c, float a) { rt_->DrawLine(D2D1::Point2F(x1, y1), D2D1::Point2F(x2, y2), brush(c, a), w, round_.Get()); }

void Gfx::rect(float x0, float y0, float x1, float y1, Rgb c, float a) { rt_->FillRectangle(D2D1::RectF(x0, y0, x1, y1), brush(c, a)); }
void Gfx::roundRect(float x0, float y0, float x1, float y1, float r, Rgb c, float a) { rt_->FillRoundedRectangle(D2D1::RoundedRect(D2D1::RectF(x0, y0, x1, y1), r, r), brush(c, a)); }

void Gfx::tri(float x1, float y1, float x2, float y2, float x3, float y3, Rgb c, float a) {
    ComPtr<ID2D1PathGeometry> g;
    if (FAILED(fac_->CreatePathGeometry(&g))) return;
    ComPtr<ID2D1GeometrySink> s;
    if (FAILED(g->Open(&s))) return;
    s->BeginFigure(D2D1::Point2F(x1, y1), D2D1_FIGURE_BEGIN_FILLED);
    s->AddLine(D2D1::Point2F(x2, y2)); s->AddLine(D2D1::Point2F(x3, y3));
    s->EndFigure(D2D1_FIGURE_END_CLOSED);
    s->Close();
    rt_->FillGeometry(g.Get(), brush(c, a));
}

void Gfx::heart(float cx, float cy, float s, Rgb c, float a) {
    D2D1_MATRIX_3X2_F old; rt_->GetTransform(&old);
    rt_->SetTransform(D2D1::Matrix3x2F::Scale(s, s) * D2D1::Matrix3x2F::Translation(cx, cy) * old);
    rt_->FillGeometry(heartGeo_.Get(), brush(c, a));
    rt_->SetTransform(old);
}

static ComPtr<ID2D1PathGeometry> pathOf(ID2D1Factory1* f, const std::vector<Gfx::Pt>& p, bool closed) {
    ComPtr<ID2D1PathGeometry> g;
    if (p.size() < 2 || FAILED(f->CreatePathGeometry(&g))) return nullptr;
    ComPtr<ID2D1GeometrySink> s;
    if (FAILED(g->Open(&s))) return nullptr;
    s->BeginFigure(D2D1::Point2F(p[0].x, p[0].y), D2D1_FIGURE_BEGIN_FILLED);
    for (size_t i = 1; i < p.size(); i++) s->AddLine(D2D1::Point2F(p[i].x, p[i].y));
    s->EndFigure(closed ? D2D1_FIGURE_END_CLOSED : D2D1_FIGURE_END_OPEN);
    s->Close();
    return g;
}

void Gfx::polyFill(const std::vector<Pt>& p, Rgb c, float a) { auto g = pathOf(fac_, p, true); if (g) rt_->FillGeometry(g.Get(), brush(c, a)); }

void Gfx::polyStroke(const std::vector<Pt>& p, bool closed, float w, Rgb c, float a, bool roundJoin) {
    auto g = pathOf(fac_, p, closed);
    if (!g) return;
    if (!roundJoin) {
        ComPtr<ID2D1StrokeStyle> miter;
        D2D1_STROKE_STYLE_PROPERTIES sp = D2D1::StrokeStyleProperties(D2D1_CAP_STYLE_FLAT, D2D1_CAP_STYLE_FLAT, D2D1_CAP_STYLE_FLAT, D2D1_LINE_JOIN_MITER);
        if (SUCCEEDED(fac_->CreateStrokeStyle(sp, nullptr, 0, &miter))) { rt_->DrawGeometry(g.Get(), brush(c, a), w, miter.Get()); return; }
    }
    rt_->DrawGeometry(g.Get(), brush(c, a), w, round_.Get());
}

void Gfx::ringXY(float cx, float cy, float rx, float ry, float w, Rgb c, float a) { rt_->DrawEllipse(D2D1::Ellipse(D2D1::Point2F(cx, cy), rx, ry), brush(c, a), w); }

void Gfx::hill(float x0, float step, const std::vector<float>& top, const std::vector<float>& base, Rgb cTop, Rgb cBot, float a) {
    if (top.size() < 2) return;
    ComPtr<ID2D1PathGeometry> g;
    if (FAILED(fac_->CreatePathGeometry(&g))) return;
    ComPtr<ID2D1GeometrySink> s;
    if (FAILED(g->Open(&s))) return;
    float minY = top[0], maxY = base[0];
    s->BeginFigure(D2D1::Point2F(x0, top[0]), D2D1_FIGURE_BEGIN_FILLED);
    for (size_t i = 1; i < top.size(); i++) { s->AddLine(D2D1::Point2F(x0 + step * i, top[i])); if (top[i] < minY) minY = top[i]; }
    for (size_t i = top.size(); i-- > 0;) { s->AddLine(D2D1::Point2F(x0 + step * i, base[i])); if (base[i] > maxY) maxY = base[i]; }
    s->EndFigure(D2D1_FIGURE_END_CLOSED);
    s->Close();
    D2D1_GRADIENT_STOP st[2] = {{0.f, col(cTop, 1.f)}, {1.f, col(cBot, 1.f)}};
    ComPtr<ID2D1GradientStopCollection> coll;
    if (FAILED(rt_->CreateGradientStopCollection(st, 2, &coll))) return;
    ComPtr<ID2D1LinearGradientBrush> br;
    if (FAILED(rt_->CreateLinearGradientBrush(D2D1::LinearGradientBrushProperties(D2D1::Point2F(0, minY), D2D1::Point2F(0, maxY)), coll.Get(), &br))) return;
    br->SetOpacity(a);
    rt_->FillGeometry(g.Get(), br.Get());
}

void Gfx::text(const std::string& utf8, float size, Rgb c, float a, float cx, float cy) {
    int key = (int)size;
    auto it = fonts_.find(key);
    if (it == fonts_.end()) {
        ComPtr<IDWriteTextFormat> f;
        if (FAILED(dw_->CreateTextFormat(L"Segoe UI", nullptr, DWRITE_FONT_WEIGHT_BOLD, DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH_NORMAL, size, L"", &f))) return;
        f->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_CENTER);
        f->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        it = fonts_.emplace(key, std::move(f)).first;
    }
    std::wstring w = gs::widen(utf8);
    D2D1_RECT_F box = D2D1::RectF(cx - 900, cy - size, cx + 900, cy + size);
    D2D1_RECT_F sh = D2D1::RectF(box.left + 3, box.top + 3, box.right + 3, box.bottom + 3);
    rt_->DrawText(w.c_str(), (UINT32)w.size(), it->second.Get(), sh, brush({0, 0, 0}, 0.6f * a));
    rt_->DrawText(w.c_str(), (UINT32)w.size(), it->second.Get(), box, brush(c, a));
}

}  // namespace sp
