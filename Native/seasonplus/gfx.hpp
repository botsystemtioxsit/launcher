// Рисование на видеокарте (Direct2D). Все фигуры — как в версии на C#, только вместо пикселей на процессоре — готовые примитивы.
#pragma once
#include <d2d1_1.h>
#include <dwrite.h>
#include <wrl/client.h>
#include <string>
#include <unordered_map>
#include <vector>

namespace sp {

using Microsoft::WRL::ComPtr;

struct Rgb {
    float r = 0, g = 0, b = 0;              // 0…255
    static Rgb hex(int v) { return {(float)((v >> 16) & 255), (float)((v >> 8) & 255), (float)(v & 255)}; }
    static Rgb lerp(Rgb a, Rgb b, float t) { return {a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t}; }
    Rgb mul(float k) const { return {r * k > 255 ? 255 : r * k, g * k > 255 ? 255 : g * k, b * k > 255 ? 255 : b * k}; }
    static Rgb hsv(float h, float s, float v);
};

class Gfx {
public:
    bool init(ID2D1Factory1* factory, IDWriteFactory* dw);
    void begin(ID2D1RenderTarget* rt);       // перед кадром; цель живёт, пока не вызвали end
    void end();

    void disc(float cx, float cy, float r, Rgb c, float a);
    void glow(float cx, float cy, float r, Rgb c, float a);
    void ring(float cx, float cy, float r, float w, Rgb c, float a);
    void ellipse(float cx, float cy, float rx, float ry, float ang, Rgb c, float a);
    void line(float x1, float y1, float x2, float y2, float w, Rgb c, float a);
    void rect(float x0, float y0, float x1, float y1, Rgb c, float a);
    void roundRect(float x0, float y0, float x1, float y1, float r, Rgb c, float a);
    void tri(float x1, float y1, float x2, float y2, float x3, float y3, Rgb c, float a);
    void heart(float cx, float cy, float s, Rgb c, float a);
    // Заливка «горки» (сугроб): верхняя кромка по высотам, внизу ровная линия baseY(i). Цвет плавно меняется сверху вниз.
    void hill(float x0, float step, const std::vector<float>& top, const std::vector<float>& base, Rgb cTop, Rgb cBot, float a);
    void text(const std::string& utf8, float size, Rgb c, float a, float cx, float cy);   // по центру точки

    ID2D1RenderTarget* target() const { return rt_; }
    ID2D1Factory1* factory() const { return fac_; }

private:
    ID2D1Factory1* fac_ = nullptr;
    IDWriteFactory* dw_ = nullptr;
    ID2D1RenderTarget* rt_ = nullptr;
    ComPtr<ID2D1SolidColorBrush> solid_;
    ComPtr<ID2D1StrokeStyle> round_;
    ComPtr<ID2D1PathGeometry> heartGeo_;
    std::unordered_map<unsigned, ComPtr<ID2D1RadialGradientBrush>> glowBrush_;
    std::unordered_map<int, ComPtr<IDWriteTextFormat>> fonts_;

    ID2D1SolidColorBrush* brush(Rgb c, float a);
    ID2D1RadialGradientBrush* glowFor(Rgb c);
};

}  // namespace sp
