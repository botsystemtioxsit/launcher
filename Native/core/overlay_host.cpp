#include "overlay_host.hpp"
#include <d3d11.h>
#include <dxgi1_2.h>
#include <dcomp.h>
#include <d2d1_1.h>
#include <dwrite.h>
#include <wrl/client.h>
#include <chrono>
#include <mmsystem.h>
#include "scanner.hpp"
#include "snapshots.hpp"
#include "win.hpp"

using Microsoft::WRL::ComPtr;

namespace sp {
namespace {

const wchar_t* kClass = L"GhostShellStyleOverlay";

LRESULT CALLBACK wndProc(HWND h, UINT m, WPARAM w, LPARAM l) {
    if (m == WM_NCHITTEST) return HTTRANSPARENT;
    if (m == WM_MOUSEACTIVATE) return MA_NOACTIVATE;
    return DefWindowProcW(h, m, w, l);
}

std::string hr(const char* what, HRESULT r) { char b[96]; std::snprintf(b, sizeof b, "%s: 0x%08lX", what, (unsigned long)r); return b; }

// Всё, что нужно для рисования на видеокарте. Пересоздаётся, если Windows забрала видеокарту (device lost) или поменялся размер экрана.
struct Target {
    ComPtr<ID3D11Device> d3d;
    ComPtr<IDXGISwapChain1> swap;
    ComPtr<IDCompositionDevice> dcomp;
    ComPtr<IDCompositionTarget> dtarget;
    ComPtr<IDCompositionVisual> visual;
    ComPtr<ID2D1Device> d2dDev;
    ComPtr<ID2D1DeviceContext> ctx;
    ComPtr<ID2D1Bitmap1> bmp;
    int w = 0, h = 0;

    std::string create(HWND hwnd, ID2D1Factory1* fac, int W, int H, bool preferGpu) {
        w = W; h = H;
        UINT flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
        D3D_FEATURE_LEVEL lv[] = {D3D_FEATURE_LEVEL_11_0, D3D_FEATURE_LEVEL_10_1, D3D_FEATURE_LEVEL_10_0};
        HRESULT r = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, flags, lv, 3, D3D11_SDK_VERSION, &d3d, nullptr, nullptr);
        if (FAILED(r)) r = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, flags, lv, 3, D3D11_SDK_VERSION, &d3d, nullptr, nullptr);
        if (FAILED(r)) return hr("D3D11CreateDevice", r);
        ComPtr<IDXGIDevice> dxgi;
        if (FAILED(r = d3d.As(&dxgi))) return hr("IDXGIDevice", r);
        ComPtr<IDXGIAdapter> ad; dxgi->GetAdapter(&ad);
        ComPtr<IDXGIFactory2> f2;
        if (FAILED(r = ad->GetParent(IID_PPV_ARGS(&f2)))) return hr("IDXGIFactory2", r);
        DXGI_SWAP_CHAIN_DESC1 d{};
        d.Width = W; d.Height = H; d.Format = DXGI_FORMAT_B8G8R8A8_UNORM; d.SampleDesc.Count = 1;
        d.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT; d.BufferCount = 2; d.SwapEffect = DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL; d.AlphaMode = DXGI_ALPHA_MODE_PREMULTIPLIED;
        if (FAILED(r = f2->CreateSwapChainForComposition(d3d.Get(), &d, nullptr, &swap))) return hr("CreateSwapChainForComposition", r);
        if (FAILED(r = DCompositionCreateDevice(dxgi.Get(), IID_PPV_ARGS(&dcomp)))) return hr("DCompositionCreateDevice", r);
        if (FAILED(r = dcomp->CreateTargetForHwnd(hwnd, TRUE, &dtarget))) return hr("CreateTargetForHwnd", r);
        if (FAILED(r = dcomp->CreateVisual(&visual))) return hr("CreateVisual", r);
        visual->SetContent(swap.Get());
        dtarget->SetRoot(visual.Get());
        dcomp->Commit();
        if (FAILED(r = fac->CreateDevice(dxgi.Get(), &d2dDev))) return hr("D2D CreateDevice", r);
        if (FAILED(r = d2dDev->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE, &ctx))) return hr("CreateDeviceContext", r);
        ComPtr<IDXGISurface> surf;
        if (FAILED(r = swap->GetBuffer(0, IID_PPV_ARGS(&surf)))) return hr("GetBuffer", r);
        D2D1_BITMAP_PROPERTIES1 bp = D2D1::BitmapProperties1(D2D1_BITMAP_OPTIONS_TARGET | D2D1_BITMAP_OPTIONS_CANNOT_DRAW, D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED));
        if (FAILED(r = ctx->CreateBitmapFromDxgiSurface(surf.Get(), &bp, &bmp))) return hr("CreateBitmapFromDxgiSurface", r);
        ctx->SetTarget(bmp.Get());
        (void)preferGpu;
        return "";
    }
};

// Для проверки: записать то, что нарисовано в окне поверх экрана (с прозрачностью), в PNG.
void dumpFrame(Target& tg, const std::wstring& path) {
    ComPtr<ID3D11Texture2D> back;
    if (FAILED(tg.swap->GetBuffer(0, IID_PPV_ARGS(&back)))) return;
    D3D11_TEXTURE2D_DESC d{}; back->GetDesc(&d);
    d.Usage = D3D11_USAGE_STAGING; d.BindFlags = 0; d.CPUAccessFlags = D3D11_CPU_ACCESS_READ; d.MiscFlags = 0;
    ComPtr<ID3D11Texture2D> stage;
    if (FAILED(tg.d3d->CreateTexture2D(&d, nullptr, &stage))) return;
    ComPtr<ID3D11DeviceContext> ic; tg.d3d->GetImmediateContext(&ic);
    ic->CopyResource(stage.Get(), back.Get());
    D3D11_MAPPED_SUBRESOURCE mp{};
    if (FAILED(ic->Map(stage.Get(), 0, D3D11_MAP_READ, 0, &mp))) return;
    std::vector<unsigned char> px((size_t)d.Width * d.Height * 4);
    for (UINT y = 0; y < d.Height; y++) memcpy(&px[(size_t)y * d.Width * 4], (unsigned char*)mp.pData + (size_t)y * mp.RowPitch, (size_t)d.Width * 4);
    ic->Unmap(stage.Get(), 0);
    // сколько пикселей что-то рисует (прозрачность > 0)
    size_t lit = 0; for (size_t i = 3; i < px.size(); i += 4) if (px[i] > 0) lit++;
    gs::trace(("overlay: в кадре закрашено пикселей: " + std::to_string(lit) + " из " + std::to_string((size_t)d.Width * d.Height)).c_str());
    savePngBgra(path, (int)d.Width, (int)d.Height, px.data(), (int)d.Width * 4);
}

}  // namespace

void OverlayHost::addLayer(Layer* l) { std::lock_guard<std::mutex> g(mu_); layers_.push_back(l); }

void OverlayHost::start() {
    if (running()) return;
    run_ = true; healthy = true;
    th_ = std::thread([this] { loop(); });
}

void OverlayHost::stop() {
    run_ = false;
    if (th_.joinable()) th_.join();
}

void OverlayHost::loop() {
    gs::trace("overlay: поток запущен");
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    timeBeginPeriod(1);
    HINSTANCE inst = GetModuleHandleW(nullptr);
    WNDCLASSEXW wc{}; wc.cbSize = sizeof wc; wc.lpfnWndProc = wndProc; wc.hInstance = inst; wc.lpszClassName = kClass;
    RegisterClassExW(&wc);
    HWND hwnd = CreateWindowExW(WS_EX_NOREDIRECTIONBITMAP | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                                kClass, L"Ghost Style Overlay", WS_POPUP, 0, 0, 1, 1, nullptr, nullptr, inst, nullptr);
    if (!hwnd) { fail("не удалось создать окно"); CoUninitialize(); return; }
    SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);

    ComPtr<ID2D1Factory1> fac;
    ComPtr<IDWriteFactory> dw;
    D2D1_FACTORY_OPTIONS fo{};
    if (FAILED(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, __uuidof(ID2D1Factory1), &fo, (void**)fac.GetAddressOf())) ||
        FAILED(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory), (IUnknown**)dw.GetAddressOf()))) {
        fail("Direct2D недоступен"); DestroyWindow(hwnd); CoUninitialize(); return;
    }
    gs::trace("overlay: окно и фабрики готовы");
    Gfx gfx;
    if (!gfx.init(fac.Get(), dw.Get())) { fail("не удалось подготовить рисование"); DestroyWindow(hwnd); CoUninitialize(); return; }

    Target tg;
    bool haveTarget = false, visible = false;
    int vx = 0, vy = 0, vw = 0, vh = 0;
    auto t0 = std::chrono::steady_clock::now();
    double last = 0, nextScan = 0, nextCheck = 0, nextMetrics = 0, topAt = 0;
    bool paused = false, lPrev = false, rPrev = false;
    double ms = 0;
    std::vector<Surf> surfaces;
    const DWORD pid = GetCurrentProcessId();

    while (run_) {
        double now = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
        double dt = std::min(0.05, now - last); last = now;
        MSG m; while (PeekMessageW(&m, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&m); DispatchMessageW(&m); }

        std::vector<Layer*> layers;
        { std::lock_guard<std::mutex> l(mu_); layers = layers_; }
        bool any = false, needSurf = false;
        for (auto* ly : layers) if (ly->wantsFrame()) { any = true; if (ly->needsSurfaces()) needSurf = true; }
        if (!any) {                                    // всё выключено: окно спрятано, ничего не рисуется и почти ничего не стоит
            if (visible) { ShowWindow(hwnd, SW_HIDE); visible = false; }
            for (int i = 0; i < 10 && run_ && !wake_; i++) Sleep(20);
            wake_ = false;
            last = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
            continue;
        }

        if (now >= nextMetrics) {
            nextMetrics = now + 2;
            int nx = GetSystemMetrics(SM_XVIRTUALSCREEN), ny = GetSystemMetrics(SM_YVIRTUALSCREEN), nw = GetSystemMetrics(SM_CXVIRTUALSCREEN), nh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            nw = std::min(8192, std::max(100, nw)); nh = std::min(8192, std::max(100, nh));
            if (nx != vx || ny != vy || nw != vw || nh != vh || !haveTarget) {
                vx = nx; vy = ny; vw = nw; vh = nh;
                for (auto* ly : layers) ly->onResize(vx, vy, vw, vh);
                SetWindowPos(hwnd, HWND_TOPMOST, vx, vy, vw, vh, SWP_NOACTIVATE);
                gfx.begin(nullptr);
                tg = Target();
                gs::trace("overlay: создаю цель");
                std::string e = tg.create(hwnd, fac.Get(), vw, vh, true);
                gs::trace("overlay: цель создана");
                if (!e.empty()) { fail(e); break; }
                haveTarget = true;
            }
        }
        if (now >= nextCheck) { paused = gs::fullscreenBusy(); nextCheck = now + 0.5; }
        if (paused) {
            if (visible) { ShowWindow(hwnd, SW_HIDE); visible = false; }
            Sleep(150);
            continue;
        }

        Env env;
        env.dt = dt; env.time = now; env.vx = vx; env.vy = vy; env.vw = vw; env.vh = vh;
        if (needSurf && now >= nextScan) { nextScan = now + 0.12; surfaces = scanWindows(pid, vy); env.surfacesChanged = true; }
        env.surfaces = &surfaces;
        GetCursorPos(&env.cursor);
        env.lbtn = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0; env.rbtn = (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;
        env.lclick = env.lbtn && !lPrev; env.rclick = env.rbtn && !rPrev; lPrev = env.lbtn; rPrev = env.rbtn;
        for (auto* ly : layers) if (ly->wantsFrame()) ly->update(env);

        if (haveTarget) {
            auto f0 = std::chrono::steady_clock::now();
            tg.ctx->BeginDraw();
            tg.ctx->Clear(D2D1::ColorF(0, 0, 0, 0));
            gfx.begin(tg.ctx.Get());
            for (auto* ly : layers) if (ly->wantsFrame()) ly->draw(gfx, env);
            HRESULT r = tg.ctx->EndDraw();
            if (r == (HRESULT)D2DERR_RECREATE_TARGET) { haveTarget = false; nextMetrics = 0; vw = 0; Sleep(200); continue; }
            if (framesDrawn == 240) { char dp[MAX_PATH * 2]; DWORD n = GetEnvironmentVariableA("GS_DUMP", dp, (DWORD)sizeof dp); if (n && n < sizeof dp) dumpFrame(tg, gs::widen(dp)); }
            r = tg.swap->Present(1, 0);
            if (r == DXGI_ERROR_DEVICE_REMOVED || r == DXGI_ERROR_DEVICE_RESET) { haveTarget = false; nextMetrics = 0; vw = 0; Sleep(200); continue; }
            tg.dcomp->Commit();
            if (!visible) { ShowWindow(hwnd, SW_SHOWNOACTIVATE); visible = true; topAt = now; }
            else if (now - topAt > 1.5) { SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE); topAt = now; }
            ms = ms * 0.9 + 0.1 * std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - f0).count();
            avgFrameMs = ms;
            framesDrawn++;
        }
        Sleep(1);
    }
    gfx.begin(nullptr);
    tg = Target();
    DestroyWindow(hwnd);
    UnregisterClassW(kClass, inst);
    timeEndPeriod(1);
    CoUninitialize();
}

}  // namespace sp
