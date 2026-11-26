#include "scenes.hpp"
#include "snapshots.hpp"
#include <d2d1_1.h>
#include <dwrite.h>
#include <wincodec.h>
#include <wrl/client.h>
#include <functional>
#include "draw.hpp"
#include "win.hpp"

using Microsoft::WRL::ComPtr;

namespace sp {

std::vector<Surf> demoSurfaces(int w, int h) {
    float k = w / 900.f;
    std::vector<Surf> v(3);
    v[0].id = 1; v[0].left = 60 * k; v[0].right = 420 * k; v[0].top = 90 * k; v[0].radius = 8; v[0].vis = {{v[0].left, v[0].right}};
    v[1].id = 2; v[1].left = 330 * k; v[1].right = 800 * k; v[1].top = 160 * k; v[1].radius = 8; v[1].vis = {{v[1].left, v[1].right}};
    v[2].id = 3; v[2].left = 0; v[2].right = w; v[2].top = h - 40 * k; v[2].radius = 0; v[2].taskbar = true; v[2].vis = {{0, (double)w}};
    return v;
}

void drawBackdrop(Gfx& g, int w, int h, int season, const std::string& holiday) {
    Rgb a, b;
    switch (season) {
        case 0: a = {42, 58, 92}; b = {120, 146, 190}; break;
        case 1: a = {120, 182, 232}; b = {214, 236, 208}; break;
        case 2: a = {74, 155, 224}; b = {191, 227, 168}; break;
        case 3: a = {224, 160, 96}; b = {122, 90, 74}; break;
        default: a = {70, 74, 92}; b = {110, 116, 140}; break;
    }
    if (holiday == "halloween") { a = {30, 20, 50}; b = {90, 50, 80}; }
    int rows = 48;
    for (int i = 0; i < rows; i++) g.rect(0, h * (float)i / rows, (float)w, h * (float)(i + 1) / rows + 1, Rgb::lerp(a, b, i / (float)(rows - 1)), 1.f);
    float k = w / 900.f;
    auto win = [&](float x0, float y0, float x1, float y1) {
        g.roundRect(x0, y0, x1, y1 + 16, 8, {240, 242, 248}, 1.f);
        g.roundRect(x0, y0, x1, y0 + 26, 8, {214, 220, 236}, 1.f);
        g.rect(x0, y0 + 14, x1, y0 + 26, {214, 220, 236}, 1.f);
        for (int i = 0; i < 5; i++) g.rect(x0 + 14, y0 + 44 + i * 22.f * k, x0 + 140 + i * 36.f, y0 + 52 + i * 22.f * k, {200, 204, 218}, 1.f);
    };
    win(60 * k, 90 * k, 420 * k, h - 40 * k);
    win(330 * k, 160 * k, 800 * k, h - 40 * k);
    g.rect(0, h - 40 * k, (float)w, (float)h, {24, 24, 34}, 1.f);
    for (int i = 0; i < 6; i++) g.rect((300 + i * 40) * k, h - 31 * k, (318 + i * 40) * k, h - 13 * k, {80.f + i * 25, 130, 210}, 1.f);
}

namespace {

bool savePng(IWICImagingFactory* wic, IWICBitmap* bmp, const std::wstring& path, int w, int h) {
    ComPtr<IWICStream> stream;
    if (FAILED(wic->CreateStream(&stream)) || FAILED(stream->InitializeFromFilename(path.c_str(), GENERIC_WRITE))) return false;
    ComPtr<IWICBitmapEncoder> enc;
    if (FAILED(wic->CreateEncoder(GUID_ContainerFormatPng, nullptr, &enc)) || FAILED(enc->Initialize(stream.Get(), WICBitmapEncoderNoCache))) return false;
    ComPtr<IWICBitmapFrameEncode> frame;
    if (FAILED(enc->CreateNewFrame(&frame, nullptr)) || FAILED(frame->Initialize(nullptr))) return false;
    frame->SetSize(w, h);
    WICPixelFormatGUID fmt = GUID_WICPixelFormat32bppBGRA;
    frame->SetPixelFormat(&fmt);
    if (FAILED(frame->WriteSource(bmp, nullptr))) return false;
    return SUCCEEDED(frame->Commit()) && SUCCEEDED(enc->Commit());
}

}  // namespace

int renderSeasonScenes(const std::wstring& dir, std::string& log) {
    gs::makeDirs(dir);
    gs::trace("renderScenes: старт");
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    ComPtr<ID2D1Factory1> fac; ComPtr<IDWriteFactory> dw; ComPtr<IWICImagingFactory> wic;
    D2D1_FACTORY_OPTIONS fo{};
    if (FAILED(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, __uuidof(ID2D1Factory1), &fo, (void**)fac.GetAddressOf()))) { log += "Direct2D недоступен\n"; return 0; }
    DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory), (IUnknown**)dw.GetAddressOf());
    if (FAILED(CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&wic)))) { log += "WIC недоступен\n"; return 0; }
    gs::trace("renderScenes: фабрики готовы");
    Gfx gfx;
    if (!gfx.init(fac.Get(), dw.Get())) { log += "не удалось подготовить рисование\n"; return 0; }
    gs::trace("renderScenes: Gfx.init готов");

    struct Scene { const char* name; const char* season; const char* holiday; double warm; std::function<void(Engine&)> extra; };
    const int W = 900, H = 420;
    std::vector<Scene> scenes = {
        {"winter", "winter", "off", 260, nullptr}, {"spring", "spring", "off", 90, nullptr}, {"summer", "summer", "off", 100, nullptr},
        {"autumn", "autumn", "off", 260, nullptr}, {"halloween", "autumn", "halloween", 80, nullptr}, {"newyear", "winter", "newyear", 200, nullptr},
        {"valentine", "winter", "valentine", 40, nullptr}, {"women", "spring", "women", 60, nullptr}, {"easter", "spring", "easter", 60, nullptr},
        {"tet", "winter", "tet", 40, nullptr}, {"maslenitsa", "winter", "maslenitsa", 40, nullptr},
        {"fireworks", "winter", "off", 7, [](Engine& e) { e.startFireworks(12); }},
        {"countdown", "winter", "newyear", 3, [](Engine& e) { e.nyDemo = 4; }},
        {"greeting", "winter", "newyear", 3, [](Engine& e) { e.nyDemo = 12; }},
        {"rain", "autumn", "off", 25, [](Engine& e) { e.forceRain(20); }},
    };
    int written = 0;
    for (auto& sc : scenes) {
        Engine e(11); e.W = W; e.H = H;
        e.S.Season = sc.season; e.S.Holiday = sc.holiday; e.S.Intensity = 70; e.S.Wind = 10; e.S.normalize();
        e.setSurfaces(demoSurfaces(W, H));
        if (sc.extra) sc.extra(e);
        gs::trace((std::string("сцена ") + sc.name + ": движок").c_str());
        e.warm(sc.warm);
        e.setSurfaces(demoSurfaces(W, H));
        ComPtr<IWICBitmap> bmp;
        if (FAILED(wic->CreateBitmap(W, H, GUID_WICPixelFormat32bppPBGRA, WICBitmapCacheOnLoad, &bmp))) continue;
        ComPtr<ID2D1RenderTarget> rt;
        D2D1_RENDER_TARGET_PROPERTIES rp = D2D1::RenderTargetProperties(D2D1_RENDER_TARGET_TYPE_SOFTWARE, D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED));
        if (FAILED(fac->CreateWicBitmapRenderTarget(bmp.Get(), rp, &rt))) { log += std::string("не вышло: ") + sc.name + "\n"; continue; }
        gs::trace((std::string("сцена ") + sc.name + ": рисую").c_str());
        rt->BeginDraw();
        gfx.begin(rt.Get());
        drawBackdrop(gfx, W, H, e.season, e.holiday);
        drawScene(gfx, e);
        HRESULT hr = rt->EndDraw();
        gfx.begin(nullptr);
        if (FAILED(hr)) { log += std::string("EndDraw не вышел: ") + sc.name + "\n"; continue; }
        if (savePng(wic.Get(), bmp.Get(), dir + L"\\season-" + gs::widen(sc.name) + L".png", W, H)) written++;
        else log += std::string("не записался: ") + sc.name + "\n";
    }
    return written;
}

}  // namespace sp
