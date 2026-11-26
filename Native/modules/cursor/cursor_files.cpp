#include "cursor_files.hpp"
#include <windows.h>
#include <d2d1_1.h>
#include <dwrite.h>
#include <wincodec.h>
#include <wrl/client.h>
#include <algorithm>
#include <cmath>
#include <cstring>
#include "cursor_shapes.hpp"

using Microsoft::WRL::ComPtr;

namespace sp {

bool renderShapeImg(const std::string& shape, int px, Rgb fill, Rgb line, CurImg& out) {
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    ComPtr<ID2D1Factory1> fac; ComPtr<IDWriteFactory> dw; ComPtr<IWICImagingFactory> wic;
    D2D1_FACTORY_OPTIONS fo{};
    if (FAILED(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, __uuidof(ID2D1Factory1), &fo, (void**)fac.GetAddressOf()))) return false;
    DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory), (IUnknown**)dw.GetAddressOf());
    if (FAILED(CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&wic)))) return false;
    Gfx gfx;
    if (!gfx.init(fac.Get(), dw.Get())) return false;
    ComPtr<IWICBitmap> bmp;
    if (FAILED(wic->CreateBitmap(px, px, GUID_WICPixelFormat32bppPBGRA, WICBitmapCacheOnLoad, &bmp))) return false;
    ComPtr<ID2D1RenderTarget> rt;
    D2D1_RENDER_TARGET_PROPERTIES rp = D2D1::RenderTargetProperties(D2D1_RENDER_TARGET_TYPE_SOFTWARE, D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED));
    if (FAILED(fac->CreateWicBitmapRenderTarget(bmp.Get(), rp, &rt))) return false;
    rt->BeginDraw();
    rt->Clear(D2D1::ColorF(0, 0, 0, 0));
    gfx.begin(rt.Get());
    float k = px / 32.f;
    gfx.setTransform(D2D1::Matrix3x2F::Scale(k, k));
    drawShape(gfx, shape, fill, line);
    HRESULT hr = rt->EndDraw();
    gfx.begin(nullptr);
    if (FAILED(hr)) return false;
    out.w = out.h = px;
    out.bgra.assign((size_t)px * px * 4, 0);
    if (FAILED(bmp->CopyPixels(nullptr, px * 4, (UINT)out.bgra.size(), out.bgra.data()))) return false;
    // из «предумноженной» в обычную прозрачность
    for (size_t i = 0; i < out.bgra.size(); i += 4) {
        unsigned a = out.bgra[i + 3];
        if (a > 0 && a < 255) for (int c = 0; c < 3; c++) out.bgra[i + c] = (unsigned char)std::min(255u, (out.bgra[i + c] * 255u + a / 2) / a);
    }
    float hx, hy; shapeHot(shape, hx, hy);
    out.hx = (int)std::lround(hx * px); out.hy = (int)std::lround(hy * px);
    return true;
}

// Рисуем значок на чёрном и на белом: по разнице получаем настоящую прозрачность и цвет (работает и для старых курсоров без альфа-канала).
bool loadSystemCursorImg(const std::wstring& file, int px, CurImg& out) {
    HCURSOR cur = (HCURSOR)LoadImageW(nullptr, file.c_str(), IMAGE_CURSOR, px, px, LR_LOADFROMFILE);
    if (!cur) return false;
    ICONINFO ii{};
    if (!GetIconInfo(cur, &ii)) { DestroyCursor(cur); return false; }
    int hx = (int)ii.xHotspot, hy = (int)ii.yHotspot;
    BITMAP bm{};
    int cw = px, ch = px;
    if (ii.hbmColor && GetObjectW(ii.hbmColor, sizeof bm, &bm)) { cw = bm.bmWidth; ch = bm.bmHeight; }
    else if (ii.hbmMask && GetObjectW(ii.hbmMask, sizeof bm, &bm)) { cw = bm.bmWidth; ch = bm.bmHeight / 2; }
    if (ii.hbmColor) DeleteObject(ii.hbmColor);
    if (ii.hbmMask) DeleteObject(ii.hbmMask);
    HDC sdc = GetDC(nullptr);
    std::vector<unsigned char> onB, onW;
    for (int pass = 0; pass < 2; pass++) {
        BITMAPINFO bi{}; bi.bmiHeader.biSize = sizeof bi.bmiHeader; bi.bmiHeader.biWidth = px; bi.bmiHeader.biHeight = -px; bi.bmiHeader.biPlanes = 1; bi.bmiHeader.biBitCount = 32;
        void* bits = nullptr;
        HBITMAP dib = CreateDIBSection(sdc, &bi, DIB_RGB_COLORS, &bits, nullptr, 0);
        HDC mdc = CreateCompatibleDC(sdc);
        HGDIOBJ old = SelectObject(mdc, dib);
        memset(bits, pass == 0 ? 0x00 : 0xFF, (size_t)px * px * 4);
        DrawIconEx(mdc, 0, 0, cur, px, px, 0, nullptr, DI_NORMAL);
        GdiFlush();
        (pass == 0 ? onB : onW).assign((unsigned char*)bits, (unsigned char*)bits + (size_t)px * px * 4);
        SelectObject(mdc, old); DeleteDC(mdc); DeleteObject(dib);
    }
    ReleaseDC(nullptr, sdc);
    DestroyCursor(cur);
    out.w = out.h = px; out.hx = hx * px / std::max(1, cw); out.hy = hy * px / std::max(1, ch);
    out.bgra.assign((size_t)px * px * 4, 0);
    bool any = false;
    for (size_t i = 0; i < out.bgra.size(); i += 4) {
        // для каждого цвета: на белом − на чёрном = 255 − альфа
        int a = 0;
        for (int c = 0; c < 3; c++) a = std::max(a, 255 - (onW[i + c] - onB[i + c]));
        a = std::clamp(a, 0, 255);
        if (a > 0) {
            for (int c = 0; c < 3; c++) out.bgra[i + c] = (unsigned char)std::min(255, onB[i + c] * 255 / a);
            any = true;
        }
        out.bgra[i + 3] = (unsigned char)a;
    }
    return any;
}

void tintImg(CurImg& img, Rgb fill, Rgb line) {
    for (size_t i = 0; i < img.bgra.size(); i += 4) {
        if (img.bgra[i + 3] == 0) continue;
        float lum = 0.114f * img.bgra[i] + 0.587f * img.bgra[i + 1] + 0.299f * img.bgra[i + 2];
        float t = std::clamp((lum - 60.f) / 140.f, 0.f, 1.f);
        Rgb c = Rgb::lerp(line, fill, t);
        img.bgra[i] = (unsigned char)c.b; img.bgra[i + 1] = (unsigned char)c.g; img.bgra[i + 2] = (unsigned char)c.r;
    }
}

bool encodePng(const CurImg& img, std::string& out) {
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    ComPtr<IWICImagingFactory> wic;
    if (FAILED(CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&wic)))) return false;
    ComPtr<IWICBitmap> bmp;
    if (FAILED(wic->CreateBitmapFromMemory(img.w, img.h, GUID_WICPixelFormat32bppBGRA, img.w * 4, (UINT)img.bgra.size(), const_cast<BYTE*>(img.bgra.data()), &bmp))) return false;
    ComPtr<IStream> stream;
    if (FAILED(CreateStreamOnHGlobal(nullptr, TRUE, &stream))) return false;
    ComPtr<IWICBitmapEncoder> enc;
    if (FAILED(wic->CreateEncoder(GUID_ContainerFormatPng, nullptr, &enc)) || FAILED(enc->Initialize(stream.Get(), WICBitmapEncoderNoCache))) return false;
    ComPtr<IWICBitmapFrameEncode> frame;
    if (FAILED(enc->CreateNewFrame(&frame, nullptr)) || FAILED(frame->Initialize(nullptr))) return false;
    frame->SetSize(img.w, img.h);
    WICPixelFormatGUID fmt = GUID_WICPixelFormat32bppBGRA;
    frame->SetPixelFormat(&fmt);
    if (FAILED(frame->WriteSource(bmp.Get(), nullptr)) || FAILED(frame->Commit()) || FAILED(enc->Commit())) return false;
    STATSTG st{};
    if (FAILED(stream->Stat(&st, STATFLAG_NONAME))) return false;
    out.resize((size_t)st.cbSize.QuadPart);
    LARGE_INTEGER z{}; stream->Seek(z, STREAM_SEEK_SET, nullptr);
    ULONG got = 0;
    if (FAILED(stream->Read(out.data(), (ULONG)out.size(), &got))) return false;
    out.resize(got);
    return true;
}

std::string buildCur(const std::vector<CurImg>& sizes) {
    std::vector<std::string> pngs;
    for (auto& im : sizes) { std::string p; if (encodePng(im, p)) pngs.push_back(std::move(p)); else pngs.push_back(""); }
    std::string out;
    auto w16 = [&](unsigned v) { out += (char)(v & 255); out += (char)((v >> 8) & 255); };
    auto w32 = [&](unsigned v) { w16(v & 0xFFFF); w16(v >> 16); };
    size_t n = 0; for (auto& p : pngs) if (!p.empty()) n++;
    w16(0); w16(2); w16((unsigned)n);
    unsigned offset = 6 + 16 * (unsigned)n;
    for (size_t i = 0; i < sizes.size(); i++) {
        if (pngs[i].empty()) continue;
        out += (char)(sizes[i].w >= 256 ? 0 : sizes[i].w); out += (char)(sizes[i].h >= 256 ? 0 : sizes[i].h); out += (char)0; out += (char)0;
        w16((unsigned)sizes[i].hx); w16((unsigned)sizes[i].hy);
        w32((unsigned)pngs[i].size()); w32(offset);
        offset += (unsigned)pngs[i].size();
    }
    for (size_t i = 0; i < sizes.size(); i++) if (!pngs[i].empty()) out += pngs[i];
    return out;
}

}  // namespace sp
