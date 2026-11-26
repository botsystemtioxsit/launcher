// Season Plus (нативная версия, C++): окно настроек, фоновая работа, служебные режимы для проверки.
#include <windows.h>
#include <shellapi.h>
#include <memory>
#include <algorithm>
#include <cstring>
#include <d3d11.h>
#include <dxgi.h>
#include <chrono>
#include <cstdio>
#include "imgui.h"
#include "imgui_impl_dx11.h"
#include "imgui_impl_win32.h"
#include "loc.hpp"
#include "overlay.hpp"
#include "scanner.hpp"
#include "scenes.hpp"
#include "ui.hpp"
#include "win.hpp"

extern IMGUI_IMPL_API LRESULT ImGui_ImplWin32_WndProcHandler(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam);
namespace gs { extern const char* kSeasonLoc; }

using namespace sp;
using Microsoft::WRL::ComPtr;

namespace {

const wchar_t* kMutex = L"SeasonPlus.Native.SingleInstance.GhostShell";
const wchar_t* kShowEvent = L"SeasonPlus.Native.Show.GhostShell";
const wchar_t* kQuitEvent = L"GhostShell.Plugin.Quit.season-plus";    // имя ждёт лаунчер: "GhostShell.Plugin.Quit." + id плагина
const UINT WM_SHOWUI = WM_APP + 1, WM_QUITAPP = WM_APP + 2;

Overlay gOverlay;
bool gQuitting = false;

float dpiScale() { HDC dc = GetDC(nullptr); int d = GetDeviceCaps(dc, LOGPIXELSX); ReleaseDC(nullptr, dc); return d / 96.f; }

// ---------- окно с настройками ----------
struct Host {
    HWND hwnd = nullptr;
    ComPtr<ID3D11Device> dev;
    ComPtr<ID3D11DeviceContext> ctx;
    ComPtr<IDXGISwapChain> sc;
    ComPtr<ID3D11RenderTargetView> rtv;
    std::unique_ptr<UiApp> ui;
    bool ready = false;
    float scale = 1.f;

    bool createDevice() {
        DXGI_SWAP_CHAIN_DESC d{};
        d.BufferCount = 2; d.BufferDesc.Format = DXGI_FORMAT_B8G8R8A8_UNORM; d.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
        d.OutputWindow = hwnd; d.SampleDesc.Count = 1; d.Windowed = TRUE; d.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;
        D3D_FEATURE_LEVEL lv[] = {D3D_FEATURE_LEVEL_11_0, D3D_FEATURE_LEVEL_10_0};
        UINT fl = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
        D3D_FEATURE_LEVEL got;
        HRESULT r = D3D11CreateDeviceAndSwapChain(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, fl, lv, 2, D3D11_SDK_VERSION, &d, &sc, &dev, &got, &ctx);
        if (FAILED(r)) r = D3D11CreateDeviceAndSwapChain(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, fl, lv, 2, D3D11_SDK_VERSION, &d, &sc, &dev, &got, &ctx);
        return SUCCEEDED(r);
    }
    void makeRtv() {
        rtv.Reset();
        ComPtr<ID3D11Texture2D> back;
        if (SUCCEEDED(sc->GetBuffer(0, IID_PPV_ARGS(&back)))) dev->CreateRenderTargetView(back.Get(), nullptr, &rtv);
    }
    void resize(UINT w, UINT h) {
        if (!sc || w == 0 || h == 0) return;
        rtv.Reset();
        sc->ResizeBuffers(0, w, h, DXGI_FORMAT_UNKNOWN, 0);
        makeRtv();
    }
    bool initUi(UiHooks hooks) {
        if (ready) return true;
        if (!createDevice()) return false;
        makeRtv();
        IMGUI_CHECKVERSION();
        ImGui::CreateContext();
        ImGuiIO& io = ImGui::GetIO();
        io.IniFilename = nullptr;
        scale = dpiScale();
        UiApp::loadFonts(scale);
        UiApp::setupStyle(scale);
        ImGui_ImplWin32_Init(hwnd);
        ImGui_ImplDX11_Init(dev.Get(), ctx.Get());
        ui = std::make_unique<UiApp>(Settings::load(), std::move(hooks));
        if (!ui->init(dev.Get())) return false;
        ready = true;
        return true;
    }
    void shutdown() {
        if (!ready) return;
        ImGui_ImplDX11_Shutdown(); ImGui_ImplWin32_Shutdown(); ImGui::DestroyContext();
        ui.reset(); rtv.Reset(); sc.Reset(); ctx.Reset(); dev.Reset();
        ready = false;
    }
    void frame(double dt) {
        if (!ready || !rtv) return;
        ui->tick(dt);
        ImGui_ImplDX11_NewFrame(); ImGui_ImplWin32_NewFrame(); ImGui::NewFrame();
        ui->frame();
        ImGui::Render();
        float clear[4] = {7 / 255.f, 3 / 255.f, 20 / 255.f, 1.f};
        ID3D11RenderTargetView* r = rtv.Get();
        ctx->OMSetRenderTargets(1, &r, nullptr);
        ctx->ClearRenderTargetView(r, clear);
        ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());
        sc->Present(1, 0);
    }
};

Host gHost;

LRESULT CALLBACK mainProc(HWND h, UINT m, WPARAM w, LPARAM l) {
    if (gHost.ready && ImGui_ImplWin32_WndProcHandler(h, m, w, l)) return true;
    switch (m) {
        case WM_SIZE: if (w != SIZE_MINIMIZED) gHost.resize(LOWORD(l), HIWORD(l)); return 0;
        case WM_CLOSE:
            if (gOverlay.running() && !gQuitting) { ShowWindow(h, SW_HIDE); return 0; }   // окно закрыли, а эффекты работают — прячем в фон
            DestroyWindow(h); return 0;
        case WM_SHOWUI: ShowWindow(h, SW_SHOW); if (IsIconic(h)) ShowWindow(h, SW_RESTORE); SetForegroundWindow(h); return 0;
        case WM_QUITAPP: gQuitting = true; gOverlay.stop(); DestroyWindow(h); return 0;
        case WM_DESTROY: PostQuitMessage(0); return 0;
    }
    return DefWindowProcW(h, m, w, l);
}

void startOverlay(const Settings& s) { if (gOverlay.running()) gOverlay.setSettings(s); else gOverlay.start(s); }

UiHooks makeHooks() {
    UiHooks h;
    h.apply = [](const Settings& s) { s.save(); startOverlay(s); gs::setAutostart(L"SeasonPlusNative", L"--background", true); };
    h.revert = [] {
        gOverlay.stop();
        Settings s = Settings::load(); s.Active = false; s.save();
        gs::setAutostart(L"SeasonPlusNative", L"--background", false);
    };
    h.fireworks = [] { gOverlay.fireworks(); };
    return h;
}

// ---------- снимок окна без показа ----------
bool renderUiPng(const std::wstring& path, const Settings& s, int w, int h, std::string& log) {
    ComPtr<ID3D11Device> dev; ComPtr<ID3D11DeviceContext> ctx;
    D3D_FEATURE_LEVEL lv[] = {D3D_FEATURE_LEVEL_11_0, D3D_FEATURE_LEVEL_10_0}, got;
    UINT fl = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
    HRESULT r = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, fl, lv, 2, D3D11_SDK_VERSION, &dev, &got, &ctx);
    if (FAILED(r)) r = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, fl, lv, 2, D3D11_SDK_VERSION, &dev, &got, &ctx);
    if (FAILED(r)) { log += "D3D11 недоступен\n"; return false; }
    D3D11_TEXTURE2D_DESC td{};
    td.Width = w; td.Height = h; td.MipLevels = 1; td.ArraySize = 1; td.Format = DXGI_FORMAT_B8G8R8A8_UNORM; td.SampleDesc.Count = 1; td.Usage = D3D11_USAGE_DEFAULT; td.BindFlags = D3D11_BIND_RENDER_TARGET;
    ComPtr<ID3D11Texture2D> tex, stage; ComPtr<ID3D11RenderTargetView> rtv;
    if (FAILED(dev->CreateTexture2D(&td, nullptr, &tex)) || FAILED(dev->CreateRenderTargetView(tex.Get(), nullptr, &rtv))) { log += "текстура не создалась\n"; return false; }
    td.Usage = D3D11_USAGE_STAGING; td.BindFlags = 0; td.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    if (FAILED(dev->CreateTexture2D(&td, nullptr, &stage))) return false;

    IMGUI_CHECKVERSION();
    ImGui::CreateContext();
    ImGuiIO& io = ImGui::GetIO();
    io.IniFilename = nullptr;
    UiApp::loadFonts(1.f);
    UiApp::setupStyle(1.f);
    ImGui_ImplDX11_Init(dev.Get(), ctx.Get());
    UiApp ui(s, UiHooks{});
    bool ok = ui.init(dev.Get());
    if (ok) {
        for (int f = 0; f < 6; f++) {
            ui.tick(0.1);
            ImGui_ImplDX11_NewFrame();
            io.DisplaySize = ImVec2((float)w, (float)h);
            io.DeltaTime = 1.f / 30.f;
            ImGui::NewFrame();
            ui.frame();
            ImGui::Render();
            float clear[4] = {7 / 255.f, 3 / 255.f, 20 / 255.f, 1.f};
            ID3D11RenderTargetView* rr = rtv.Get();
            ctx->OMSetRenderTargets(1, &rr, nullptr);
            ctx->ClearRenderTargetView(rr, clear);
            ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());
        }
        ctx->CopyResource(stage.Get(), tex.Get());
        D3D11_MAPPED_SUBRESOURCE mp{};
        if (SUCCEEDED(ctx->Map(stage.Get(), 0, D3D11_MAP_READ, 0, &mp))) {
            std::vector<unsigned char> px((size_t)w * h * 4);
            for (int y = 0; y < h; y++) memcpy(&px[(size_t)y * w * 4], (unsigned char*)mp.pData + (size_t)y * mp.RowPitch, (size_t)w * 4);
            for (size_t i = 3; i < px.size(); i += 4) px[i] = 255;
            ctx->Unmap(stage.Get(), 0);
            ok = savePngBgra(path, w, h, px.data(), w * 4);
        } else ok = false;
    } else log += "предпросмотр не подготовился\n";
    ImGui_ImplDX11_Shutdown();
    ImGui::DestroyContext();
    return ok;
}

// ---------- служебные режимы для проверки в CI ----------
int runScreenshot(const std::wstring& dir) {
    std::string log;
    int n = renderScenes(dir, log);
    bool ui = renderUiPng(dir + L"\\season-plus-ui.png", Settings::load(), 980, 2200, log);
    log += "сцен записано: " + std::to_string(n) + "; окно: " + (ui ? "да" : "нет") + "\n";
    gs::writeFileAtomic(dir + L"\\screenshot-log.txt", log);
    return n > 0 ? 0 : 1;
}

int runOverlayTest(const std::wstring& dir) {
    gs::makeDirs(dir);
    std::string out;
    Settings s; s.Season = "winter"; s.Holiday = "off"; s.Intensity = 80; s.Active = true; s.normalize();
    FILETIME c0, e0, k0, u0;
    GetProcessTimes(GetCurrentProcess(), &c0, &e0, &k0, &u0);
    auto t0 = std::chrono::steady_clock::now();
    Overlay ov;
    ov.start(s);
    Sleep(9000);
    double secs = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
    FILETIME c1, e1, k1, u1;
    GetProcessTimes(GetCurrentProcess(), &c1, &e1, &k1, &u1);
    auto ft = [](FILETIME a, FILETIME b) { ULARGE_INTEGER x, y; x.LowPart = a.dwLowDateTime; x.HighPart = a.dwHighDateTime; y.LowPart = b.dwLowDateTime; y.HighPart = b.dwHighDateTime; return (double)(y.QuadPart - x.QuadPart) / 1e7; };
    double cpu = ft(k0, k1) + ft(u0, u1);
    char b[256];
    out += std::string("окно работает: ") + (ov.running() ? "да" : "нет") + "\n";
    std::snprintf(b, sizeof b, "частиц: %d\nкадров нарисовано: %d (за %.1f с)\nсреднее время кадра: %.2f мс\nнагрузка процессора: %.1f %% одного ядра\n", ov.particles.load(), ov.framesDrawn.load(), secs, ov.avgFrameMs.load(), cpu / secs * 100.0);
    out += b;
    out += "здоровье: " + std::string(ov.healthy ? "да" : "нет") + "\nошибка: " + (ov.lastError().empty() ? "нет" : ov.lastError()) + "\n";
    auto list = scanWindows(GetCurrentProcessId(), 0);
    out += "найдено краёв окон: " + std::to_string(list.size()) + "\n";
    for (auto& x : list) { std::snprintf(b, sizeof b, "  %s %.0f..%.0f y=%.0f r=%.0f кусков %d\n", x.taskbar ? "панель" : "окно", x.left, x.right, x.top, x.radius, (int)x.vis.size()); out += b; }
    ov.stop();
    out += std::string("остановлено: ") + (ov.running() ? "нет" : "да") + "\n";
    gs::writeFileAtomic(dir + L"\\overlay-test.txt", out);
    return ov.healthy ? 0 : 1;
}

}  // namespace

int WINAPI wWinMain(HINSTANCE inst, HINSTANCE, PWSTR, int) {
    int argc = 0;
    wchar_t** argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    gs::installCrashHandler();
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    gs::locInit(gs::kSeasonLoc, argc, argv);
    auto arg = [&](const wchar_t* prefix) -> std::wstring {
        for (int i = 1; i < argc; i++) { std::wstring a = argv[i]; if (a.rfind(prefix, 0) == 0) return a.substr(wcslen(prefix)); }
        return L"";
    };
    auto has = [&](const wchar_t* flag) { for (int i = 1; i < argc; i++) if (std::wstring(argv[i]) == flag) return true; return false; };

    if (has(L"--revert")) {
        gs::SingleInstance::signal(kQuitEvent);
        Sleep(2500);
        Settings s = Settings::load(); s.Active = false; s.save();
        gs::setAutostart(L"SeasonPlusNative", L"--background", false);
        return 0;
    }
    if (auto d = arg(L"--screenshot="); !d.empty()) return runScreenshot(d);
    if (auto d = arg(L"--selftest="); !d.empty()) { std::string log; int n = renderScenes(d, log); gs::writeFileAtomic(d + L"\\graphics-selftest.txt", "сцен записано: " + std::to_string(n) + "\n" + log); return n >= 10 ? 0 : 1; }
    if (auto d = arg(L"--overlay-test="); !d.empty()) return runOverlayTest(d);
    if (has(L"--apply")) { Settings s = Settings::load(); s.Active = true; s.save(); gs::setAutostart(L"SeasonPlusNative", L"--background", true); }

    // одна копия: повторный запуск просто показывает окно уже работающей
    gs::SingleInstance single;
    HWND hwnd = nullptr;
    if (!single.acquire(kMutex, kShowEvent, kQuitEvent, [&] { if (hwnd) PostMessageW(hwnd, WM_SHOWUI, 0, 0); }, [&] { if (hwnd) PostMessageW(hwnd, WM_QUITAPP, 0, 0); })) return 0;

    WNDCLASSEXW wc{}; wc.cbSize = sizeof wc; wc.lpfnWndProc = mainProc; wc.hInstance = inst; wc.lpszClassName = L"GhostShellSeasonPlusWindow";
    wc.hIcon = LoadIconW(inst, MAKEINTRESOURCEW(1)); wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    RegisterClassExW(&wc);
    float sc = dpiScale();
    RECT wa{}; SystemParametersInfoW(SPI_GETWORKAREA, 0, &wa, 0);
    int w = std::min((int)(1000 * sc), (int)(wa.right - wa.left) - 40), h = std::min((int)(900 * sc), (int)(wa.bottom - wa.top) - 40);
    hwnd = CreateWindowExW(0, wc.lpszClassName, L"Season Plus", WS_OVERLAPPEDWINDOW, CW_USEDEFAULT, CW_USEDEFAULT, w, h, nullptr, nullptr, inst, nullptr);
    gHost.hwnd = hwnd;

    Settings saved = Settings::load();
    if (saved.Active) startOverlay(saved);
    bool background = has(L"--background") || has(L"--apply");
    if (background && !gOverlay.running()) return 0;       // фоновый запуск: если выключено — сразу выходим
    if (!background) PostMessageW(hwnd, WM_SHOWUI, 0, 0);

    auto last = std::chrono::steady_clock::now();
    MSG m;
    for (;;) {
        bool visible = IsWindowVisible(hwnd) && !IsIconic(hwnd);
        if (visible) { while (PeekMessageW(&m, nullptr, 0, 0, PM_REMOVE)) { if (m.message == WM_QUIT) goto done; TranslateMessage(&m); DispatchMessageW(&m); } }
        else { if (GetMessageW(&m, nullptr, 0, 0) <= 0) goto done; TranslateMessage(&m); DispatchMessageW(&m); last = std::chrono::steady_clock::now(); }
        if (IsWindowVisible(hwnd) && !IsIconic(hwnd)) {
            if (!gHost.ready && !gHost.initUi(makeHooks())) { MessageBoxW(hwnd, L"Не удалось подготовить рисование (Direct3D 11).", L"Season Plus", MB_ICONERROR); DestroyWindow(hwnd); continue; }
            auto now = std::chrono::steady_clock::now();
            double dt = std::min(0.1, std::chrono::duration<double>(now - last).count()); last = now;
            gHost.frame(dt);
        } else if (gHost.ready) {
            gHost.shutdown();      // окно спрятано: видеокарту не держим
        }
    }
done:
    gHost.shutdown();
    gOverlay.stop();
    single.stop();
    return 0;
}
