#include "app.hpp"
#include <windows.h>
#include <shellapi.h>
#include <dwmapi.h>
#include <d3d11.h>
#include <dxgi.h>
#include <wrl/client.h>
#include <algorithm>
#include <chrono>
#include <cstring>
#include <string>
#include "imgui.h"
#include "imgui_impl_dx11.h"
#include "imgui_impl_win32.h"
#include "json.hpp"
#include "loc.hpp"
#include "overlay_host.hpp"
#include "scanner.hpp"
#include "snapshots.hpp"
#include "ui_common.hpp"
#include "win.hpp"

extern IMGUI_IMPL_API LRESULT ImGui_ImplWin32_WndProcHandler(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam);

namespace sp {
namespace {

using Microsoft::WRL::ComPtr;
using gs::L;

const UINT WM_SHOWUI = WM_APP + 1, WM_QUITAPP = WM_APP + 2, WM_TRAY = WM_APP + 3;

struct App;
App* gApp = nullptr;

float dpiScale() { HDC dc = GetDC(nullptr); int d = GetDeviceCaps(dc, LOGPIXELSX); ReleaseDC(nullptr, dc); return d / 96.f; }

struct App {
    AppInfo info;
    std::vector<std::unique_ptr<Module>>* modules = nullptr;
    OverlayHost overlay;
    AppCtx ctx;
    HWND hwnd = nullptr;
    HWND app_hwnd = nullptr;
    bool quitting = false;
    int selected = 0;
    float scale = 1.f;

    // окно настроек (видеокарта создаётся, когда окно показано, и отпускается, когда спрятано)
    ComPtr<ID3D11Device> dev;
    ComPtr<ID3D11DeviceContext> dctx;
    ComPtr<IDXGISwapChain> sc;
    ComPtr<ID3D11RenderTargetView> rtv;
    bool uiReady = false;

    // ---------- свои рамка и верхняя полоса (как на макете): аккаунт справа, рядом — свернуть / развернуть / закрыть ----------
    std::vector<RECT> hot;                      // участки полосы, где нужны нажатия (не перетаскивание окна), в пикселях окна
    std::string acctName, acctRole; bool acctLoaded = false;
    bool acctOpen = false;
    float titleH() const { return 44.f * scale; }
    void loadAccount() {
        if (acctLoaded) return; acctLoaded = true;
        std::string t; gs::Json j;
        if (!gs::readFile(gs::appDataDir() + L"\\launcher.json", t) || !gs::jsonParse(t, j) || j.t != gs::Json::Obj) return;
        std::string active = j.str("ActiveId");
        if (auto* a = j.get("Accounts")) if (a->t == gs::Json::Arr) for (auto& x : a->a) if (x.str("Id") == active) {
            acctName = x.str("Name"); if (acctName.empty()) acctName = x.str("PlayerId"); if (acctName.empty()) acctName = "Аккаунт";
            acctRole = x.str("Role");
        }
    }
    static std::string initials(const std::string& n) {
        std::string o; size_t i = 0; int cnt = 0;
        while (i < n.size() && cnt < 2) { unsigned char c = n[i]; size_t len = c < 0x80 ? 1 : c < 0xE0 ? 2 : c < 0xF0 ? 3 : 4; if (c != ' ') { o += n.substr(i, len); cnt++; } i += len; }
        return o;
    }
    void openLauncher() {
        wchar_t lp[MAX_PATH] = {}; GetEnvironmentVariableW(L"LOCALAPPDATA", lp, MAX_PATH);
        std::wstring exe = std::wstring(lp) + L"\\Programs\\BattleLauncher\\BattleLauncher.exe";
        if (GetFileAttributesW(exe.c_str()) != INVALID_FILE_ATTRIBUTES) ShellExecuteW(nullptr, L"open", exe.c_str(), nullptr, nullptr, SW_SHOWNORMAL);
        else ShellExecuteW(nullptr, L"open", L"https://github.com/botsystemtioxsit/launcher/releases/latest", nullptr, nullptr, SW_SHOWNORMAL);
    }
    void addHot(ImVec2 a, ImVec2 b) { hot.push_back(RECT{(LONG)a.x, (LONG)a.y, (LONG)b.x, (LONG)b.y}); }

    void drawTitleBar() {
        ImGuiIO& io = ImGui::GetIO(); ImDrawList* dl = ImGui::GetWindowDrawList();
        float H = titleH(), W = io.DisplaySize.x; hot.clear();
        dl->AddRectFilled(ImVec2(0, 0), ImVec2(W, H), IM_COL32(43, 44, 48, 255));
        dl->AddLine(ImVec2(0, H), ImVec2(W, H), IM_COL32(74, 34, 160, 255));
        // справа: кнопки окна (плашка) и левее — аккаунт
        float bw = 38 * scale, bh = 26 * scale, pad = 10 * scale, y0 = (H - bh) / 2;
        float groupW = bw * 3, gx = W - pad - groupW;
        dl->AddRectFilled(ImVec2(gx, y0), ImVec2(gx + groupW, y0 + bh), IM_COL32(112, 116, 128, 255), bh / 2);
        bool zoomed = app_hwnd && IsZoomed(app_hwnd);
        const char* ids[3] = {"##min", "##max", "##close"};
        for (int i = 0; i < 3; i++) {
            ImVec2 a(gx + i * bw, y0), b(gx + (i + 1) * bw, y0 + bh);
            ImGui::SetCursorScreenPos(a);
            bool clicked = ImGui::InvisibleButton(ids[i], ImVec2(bw, bh));
            bool hov = ImGui::IsItemHovered();
            if (hov) dl->AddRectFilled(a, b, i == 2 ? IM_COL32(255, 46, 90, 255) : IM_COL32(138, 143, 156, 255), bh / 2);
            ImVec2 c((a.x + b.x) / 2, (a.y + b.y) / 2); float r = 5 * scale; ImU32 col = IM_COL32(234, 248, 255, 255);
            if (i == 0) dl->AddLine(ImVec2(c.x - r, c.y + r * 0.6f), ImVec2(c.x + r, c.y + r * 0.6f), col, 1.5f * scale);
            else if (i == 1) {
                if (zoomed) { dl->AddRect(ImVec2(c.x - r + 2 * scale, c.y - r), ImVec2(c.x + r, c.y + r - 2 * scale), col, 0, 0, 1.4f * scale); dl->AddRect(ImVec2(c.x - r, c.y - r + 2 * scale), ImVec2(c.x + r - 2 * scale, c.y + r), col, 0, 0, 1.4f * scale); }
                else dl->AddRect(ImVec2(c.x - r, c.y - r), ImVec2(c.x + r, c.y + r), col, 0, 0, 1.4f * scale);
            } else { dl->AddLine(ImVec2(c.x - r, c.y - r), ImVec2(c.x + r, c.y + r), col, 1.5f * scale); dl->AddLine(ImVec2(c.x - r, c.y + r), ImVec2(c.x + r, c.y - r), col, 1.5f * scale); }
            if (clicked && app_hwnd) { if (i == 0) ShowWindow(app_hwnd, SW_MINIMIZE); else if (i == 1) ShowWindow(app_hwnd, zoomed ? SW_RESTORE : SW_MAXIMIZE); else PostMessageW(app_hwnd, WM_CLOSE, 0, 0); }
        }
        addHot(ImVec2(gx, y0), ImVec2(gx + groupW, y0 + bh));
        // аккаунт
        loadAccount();
        std::string label = acctName.empty() ? std::string(L("Войти")) : acctName;
        float aw = std::min(220.f * scale, ImGui::CalcTextSize(label.c_str()).x + 54 * scale), ax = gx - 12 * scale - aw;
        ImVec2 a(ax, y0), b(ax + aw, y0 + bh);
        ImGui::SetCursorScreenPos(a);
        bool clicked = ImGui::InvisibleButton("##acct", ImVec2(aw, bh));
        bool hov = ImGui::IsItemHovered();
        dl->AddRectFilled(a, b, hov ? IM_COL32(138, 143, 156, 255) : IM_COL32(112, 116, 128, 255), bh / 2);
        ImVec2 cc(a.x + bh / 2, a.y + bh / 2);
        dl->AddCircleFilled(cc, bh / 2 - 3 * scale, acctName.empty() ? IM_COL32(74, 34, 160, 255) : IM_COL32(0, 240, 255, 255));
        std::string ini = acctName.empty() ? "?" : initials(acctName);
        ImVec2 ts = ImGui::CalcTextSize(ini.c_str());
        dl->AddText(ImVec2(cc.x - ts.x / 2, cc.y - ts.y / 2), IM_COL32(6, 5, 13, 255), ini.c_str());
        dl->PushClipRect(ImVec2(a.x + bh, a.y), ImVec2(b.x - 6 * scale, b.y), true);
        dl->AddText(ImVec2(a.x + bh + 4 * scale, a.y + (bh - ImGui::GetTextLineHeight()) / 2), IM_COL32(234, 248, 255, 255), label.c_str());
        dl->PopClipRect();
        addHot(a, b);
        if (clicked) { acctOpen = true; ImGui::OpenPopup("##acctpop"); }
        ImGui::SetNextWindowPos(ImVec2(b.x - 240 * scale, b.y + 6 * scale));
        if (ImGui::BeginPopup("##acctpop")) {
            ImGui::PushFont(ui::mono()); ImGui::TextUnformatted(acctName.empty() ? L("Вы не вошли") : acctName.c_str()); ImGui::PopFont();
            if (!acctRole.empty()) { ImGui::PushStyleColor(ImGuiCol_Text, ui::muted()); ImGui::TextUnformatted(acctRole.c_str()); ImGui::PopStyleColor(); }
            ImGui::PushStyleColor(ImGuiCol_Text, ui::muted()); ImGui::PushTextWrapPos(260 * scale);
            ImGui::TextUnformatted(L("Аккаунт общий для всех приложений Ghost Shell: вход и выход — в Ghost Shell Launcher."));
            ImGui::PopTextWrapPos(); ImGui::PopStyleColor();
            if (ImGui::Button(L("Открыть Ghost Shell Launcher"))) { openLauncher(); ImGui::CloseCurrentPopup(); }
            ImGui::EndPopup();
        }
        // название слева
        ImGui::SetCursorScreenPos(ImVec2(18 * scale, (H - ImGui::GetTextLineHeight()) / 2));
        ImGui::PushFont(ui::mono()); ImGui::PushStyleColor(ImGuiCol_Text, ui::rgb(200, 208, 218)); ImGui::TextUnformatted("GHOST STYLE"); ImGui::PopStyleColor(); ImGui::PopFont();
    }

    bool anyActive() const { for (auto& m : *modules) if (m->needsBackground()) return true; return false; }
    void changed() {
        gs::setAutostart(info.autostartName, L"--background", anyActive());
        overlay.wake();
        updateTray();
    }

    // ---------- значок в трее ----------
    bool trayAdded = false;
    void updateTray() {
        NOTIFYICONDATAW n{}; n.cbSize = sizeof n; n.hWnd = hwnd; n.uID = 1;
        n.uFlags = NIF_ICON | NIF_TIP | NIF_MESSAGE; n.uCallbackMessage = WM_TRAY;
        n.hIcon = LoadIconW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(1));
        wcsncpy(n.szTip, info.name, 60);
        if (!trayAdded) trayAdded = Shell_NotifyIconW(NIM_ADD, &n);
        else Shell_NotifyIconW(NIM_MODIFY, &n);
    }
    void removeTray() { if (!trayAdded) return; NOTIFYICONDATAW n{}; n.cbSize = sizeof n; n.hWnd = hwnd; n.uID = 1; Shell_NotifyIconW(NIM_DELETE, &n); trayAdded = false; }
    void trayMenu() {
        HMENU m = CreatePopupMenu();
        AppendMenuW(m, MF_STRING, 1, gs::widen(L("Открыть")).c_str());
        AppendMenuW(m, MF_STRING, 2, gs::widen(L("Выключить всё")).c_str());
        AppendMenuW(m, MF_SEPARATOR, 0, nullptr);
        AppendMenuW(m, MF_STRING, 3, gs::widen(L("Выход")).c_str());
        POINT p; GetCursorPos(&p);
        SetForegroundWindow(hwnd);
        int cmd = TrackPopupMenu(m, TPM_RETURNCMD | TPM_RIGHTBUTTON, p.x, p.y, 0, hwnd, nullptr);
        DestroyMenu(m);
        if (cmd == 1) PostMessageW(hwnd, WM_SHOWUI, 0, 0);
        else if (cmd == 2) revertAll();
        else if (cmd == 3) PostMessageW(hwnd, WM_QUITAPP, 0, 0);
    }
    void revertAll() { for (auto& m : *modules) m->revert(); changed(); }

    // ---------- видеокарта окна ----------
    bool createDevice() {
        DXGI_SWAP_CHAIN_DESC d{};
        d.BufferCount = 2; d.BufferDesc.Format = DXGI_FORMAT_B8G8R8A8_UNORM; d.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
        d.OutputWindow = hwnd; d.SampleDesc.Count = 1; d.Windowed = TRUE; d.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;
        D3D_FEATURE_LEVEL lv[] = {D3D_FEATURE_LEVEL_11_0, D3D_FEATURE_LEVEL_10_0}, got;
        UINT fl = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
        HRESULT r = D3D11CreateDeviceAndSwapChain(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, fl, lv, 2, D3D11_SDK_VERSION, &d, &sc, &dev, &got, &dctx);
        if (FAILED(r)) r = D3D11CreateDeviceAndSwapChain(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, fl, lv, 2, D3D11_SDK_VERSION, &d, &sc, &dev, &got, &dctx);
        return SUCCEEDED(r);
    }
    void makeRtv() {
        rtv.Reset();
        ComPtr<ID3D11Texture2D> back;
        if (SUCCEEDED(sc->GetBuffer(0, IID_PPV_ARGS(&back)))) dev->CreateRenderTargetView(back.Get(), nullptr, &rtv);
    }
    void resize(UINT w, UINT h) { if (!sc || !uiReady || w == 0 || h == 0) return; rtv.Reset(); sc->ResizeBuffers(0, w, h, DXGI_FORMAT_UNKNOWN, 0); makeRtv(); }
    bool initUi() {
        if (uiReady) return true;
        if (!createDevice()) return false;
        makeRtv();
        IMGUI_CHECKVERSION();
        ImGui::CreateContext();
        ImGui::GetIO().IniFilename = nullptr;
        scale = dpiScale();
        ui::loadFonts(scale); ui::setupStyle(scale);
        ImGui_ImplWin32_Init(hwnd);
        ImGui_ImplDX11_Init(dev.Get(), dctx.Get());
        for (auto& m : *modules) m->init(dev.Get());
        uiReady = true;
        return true;
    }
    void shutdownUi() {
        if (!uiReady) return;
        for (auto& m : *modules) m->shutdownUi();
        ImGui_ImplDX11_Shutdown(); ImGui_ImplWin32_Shutdown(); ImGui::DestroyContext();
        rtv.Reset(); sc.Reset(); dctx.Reset(); dev.Reset();
        uiReady = false;
    }

    // ---------- содержимое окна ----------
    void drawShell() {
        ImGuiIO& io = ImGui::GetIO();
        ImGui::SetNextWindowPos(ImVec2(0, 0));
        ImGui::SetNextWindowSize(io.DisplaySize);
        ImGui::PushStyleVar(ImGuiStyleVar_WindowPadding, ImVec2(0, 0));
        ImGui::Begin("##shell", nullptr, ImGuiWindowFlags_NoDecoration | ImGuiWindowFlags_NoMove | ImGuiWindowFlags_NoSavedSettings | ImGuiWindowFlags_NoBringToFrontOnFocus | ImGuiWindowFlags_NoScrollbar);
        ImGui::PopStyleVar();
        app_hwnd = hwnd;
        drawTitleBar();
        ImGui::SetCursorPos(ImVec2(0, titleH() + 1));
        float side = 220.f * scale;
        ImGui::PushStyleColor(ImGuiCol_ChildBg, ui::rgb(58, 60, 65));
        ImGui::BeginChild("##side", ImVec2(side, 0), ImGuiChildFlags_None, ImGuiWindowFlags_NoScrollbar);
        ImGui::SetCursorPos(ImVec2(18 * scale, 12 * scale));
        ImGui::PushStyleColor(ImGuiCol_Text, ui::muted()); ImGui::TextUnformatted("made by AI"); ImGui::PopStyleColor();
        ImGui::Dummy(ImVec2(0, 8 * scale));
        for (int i = 0; i < (int)modules->size(); i++) {
            Module& m = *(*modules)[i];
            ImGui::SetCursorPosX(10 * scale);
            ImGui::PushID(i);
            bool on = selected == i;
            ImGui::PushStyleColor(ImGuiCol_Button, on ? ui::rgb(141, 184, 255) : ui::rgb(200, 208, 218)); ImGui::PushStyleColor(ImGuiCol_ButtonHovered, on ? ui::rgb(160, 198, 255) : ui::rgb(220, 226, 234)); ImGui::PushStyleColor(ImGuiCol_Text, ui::rgb(30, 36, 48));
            ImGui::PushStyleVar(ImGuiStyleVar_FrameRounding, 17 * scale);
            std::string label = std::string(m.active() ? "\xE2\x97\x8F " : "\xE2\x97\x8B ") + L(m.title());
            if (ImGui::Button(label.c_str(), ImVec2(side - 20 * scale, 36 * scale))) selected = i;
            ImGui::PopStyleVar(); ImGui::PopStyleColor(3);
            ImGui::PopID();
            ImGui::Dummy(ImVec2(0, 2 * scale));
        }
        // низ: язык и «выключить всё»
        ImGui::SetCursorPos(ImVec2(10 * scale, ImGui::GetWindowHeight() - 112 * scale));
        {
            int lang = gs::locLang();
            const char* lab[] = {"RU", "EN", "DE"};
            ImGui::BeginGroup();
            if (ui::seg("lang", lab, 3, lang)) gs::locSet(lang, true);
            ImGui::EndGroup();
        }
        ImGui::SetCursorPosX(10 * scale);
        if (ImGui::Button(L("Выключить всё"), ImVec2(side - 20 * scale, 0))) revertAll();
        ImGui::EndChild();
        ImGui::PopStyleColor();
        ImGui::SameLine(0, 0);
        ImGui::BeginChild("##content", ImVec2(0, 0), ImGuiChildFlags_AlwaysUseWindowPadding);
        if (!modules->empty()) {
            Module& cur = *(*modules)[std::clamp(selected, 0, (int)modules->size() - 1)];
            if (cur.hasPreview()) {
                float avail = ImGui::GetContentRegionAvail().x, pw = std::clamp(avail * 0.44f, 340.f * scale, 640.f * scale);
                ImGui::BeginChild("##left", ImVec2(avail - pw - 10 * scale, 0), ImGuiChildFlags_None);
                cur.drawPanel();
                ImGui::EndChild();
                ImGui::SameLine();
                ImGui::BeginChild("##right", ImVec2(0, 0), ImGuiChildFlags_Borders | ImGuiChildFlags_AlwaysUseWindowPadding);
                ImGui::PushFont(ui::mono()); ImGui::TextUnformatted(L("Так будет выглядеть")); ImGui::PopFont();
                ImGui::Spacing();
                cur.drawPreview();
                ImGui::EndChild();
            } else cur.drawPanel();
        }
        ImGui::EndChild();
        ImGui::End();
    }

    void frame(double dt) {
        if (!uiReady || !rtv) return;
        for (auto& m : *modules) m->tick(dt);
        ImGui_ImplDX11_NewFrame(); ImGui_ImplWin32_NewFrame(); ImGui::NewFrame();
        drawShell();
        ImGui::Render();
        float clear[4] = {58 / 255.f, 60 / 255.f, 65 / 255.f, 1.f};
        ID3D11RenderTargetView* r = rtv.Get();
        dctx->OMSetRenderTargets(1, &r, nullptr);
        dctx->ClearRenderTargetView(r, clear);
        ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());
        sc->Present(1, 0);
    }
};

LRESULT CALLBACK mainProc(HWND h, UINT m, WPARAM w, LPARAM l) {
    App* a = gApp;
    if (a && a->uiReady && ImGui_ImplWin32_WndProcHandler(h, m, w, l)) return true;
    switch (m) {
        case WM_NCCALCSIZE:   // своя рамка: вся площадь — наша; в развёрнутом виде не вылезаем за край монитора
            if (w) {
                if (IsZoomed(h)) { MONITORINFO mi{sizeof mi}; if (GetMonitorInfoW(MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST), &mi)) ((NCCALCSIZE_PARAMS*)l)->rgrc[0] = mi.rcWork; }
                return 0;
            }
            break;
        case WM_NCHITTEST: {
            if (!a) break;
            POINT pt{(short)LOWORD(l), (short)HIWORD(l)}; RECT wr; GetWindowRect(h, &wr);
            int x = pt.x - wr.left, y = pt.y - wr.top, W = wr.right - wr.left, H = wr.bottom - wr.top, b = (int)(6 * a->scale);
            if (!IsZoomed(h)) {
                bool L_ = x < b, R_ = x >= W - b, T_ = y < b, B_ = y >= H - b;
                if (T_ && L_) return HTTOPLEFT; if (T_ && R_) return HTTOPRIGHT; if (B_ && L_) return HTBOTTOMLEFT; if (B_ && R_) return HTBOTTOMRIGHT;
                if (L_) return HTLEFT; if (R_) return HTRIGHT; if (T_) return HTTOP; if (B_) return HTBOTTOM;
            }
            if (y < a->titleH()) { for (auto& r : a->hot) if (x >= r.left && x < r.right && y >= r.top && y < r.bottom) return HTCLIENT; return HTCAPTION; }
            return HTCLIENT;
        }
        case WM_GETMINMAXINFO: { auto* mm = (MINMAXINFO*)l; mm->ptMinTrackSize.x = 720; mm->ptMinTrackSize.y = 480; return 0; }
        case WM_SIZE: if (a && w != SIZE_MINIMIZED) a->resize(LOWORD(l), HIWORD(l)); return 0;
        case WM_CLOSE: ShowWindow(h, SW_HIDE); return 0;                            // закрыли окно — приложение остаётся в трее
        case WM_SHOWUI: ShowWindow(h, SW_SHOW); if (IsIconic(h)) ShowWindow(h, SW_RESTORE); SetForegroundWindow(h); return 0;
        case WM_QUITAPP: if (a) a->quitting = true; DestroyWindow(h); return 0;
        case WM_TRAY:
            if (a && (LOWORD(l) == WM_LBUTTONUP || LOWORD(l) == WM_LBUTTONDBLCLK)) PostMessageW(h, WM_SHOWUI, 0, 0);
            else if (a && LOWORD(l) == WM_RBUTTONUP) a->trayMenu();
            return 0;
        case WM_DESTROY: PostQuitMessage(0); return 0;
    }
    return DefWindowProcW(h, m, w, l);
}

// ---------- снимок окна без показа: по вкладке на модуль ----------
bool renderUiPng(App& app, int moduleIndex, const std::wstring& path, int w, int h, std::string& log) {
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
    ui::loadFonts(1.f); ui::setupStyle(1.f);
    ImGui_ImplDX11_Init(dev.Get(), ctx.Get());
    float oldScale = app.scale; app.scale = 1.f;
    for (auto& m : *app.modules) m->init(dev.Get());
    app.selected = moduleIndex;
    for (int f = 0; f < 6; f++) {
        for (auto& m : *app.modules) m->tick(0.1);
        ImGui_ImplDX11_NewFrame();
        io.DisplaySize = ImVec2((float)w, (float)h);
        io.DeltaTime = 1.f / 30.f;
        ImGui::NewFrame();
        app.drawShell();
        ImGui::Render();
        float clear[4] = {58 / 255.f, 60 / 255.f, 65 / 255.f, 1.f};
        ID3D11RenderTargetView* rr = rtv.Get();
        ctx->OMSetRenderTargets(1, &rr, nullptr);
        ctx->ClearRenderTargetView(rr, clear);
        ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());
    }
    ctx->CopyResource(stage.Get(), tex.Get());
    bool ok = false;
    D3D11_MAPPED_SUBRESOURCE mp{};
    if (SUCCEEDED(ctx->Map(stage.Get(), 0, D3D11_MAP_READ, 0, &mp))) {
        std::vector<unsigned char> px((size_t)w * h * 4);
        for (int y = 0; y < h; y++) memcpy(&px[(size_t)y * w * 4], (unsigned char*)mp.pData + (size_t)y * mp.RowPitch, (size_t)w * 4);
        for (size_t i = 3; i < px.size(); i += 4) px[i] = 255;
        ctx->Unmap(stage.Get(), 0);
        ok = savePngBgra(path, w, h, px.data(), w * 4);
    }
    for (auto& m : *app.modules) m->shutdownUi();
    ImGui_ImplDX11_Shutdown();
    ImGui::DestroyContext();
    app.scale = oldScale;
    return ok;
}

int runScreenshot(App& app, const std::wstring& dir) {
    gs::makeDirs(dir);
    std::string log;
    int scenes = 0, uis = 0;
    for (auto& m : *app.modules) scenes += m->snapshots(dir, log);
    for (int i = 0; i < (int)app.modules->size(); i++)
        if (renderUiPng(app, i, dir + L"\\ui-" + gs::widen((*app.modules)[i]->id()) + L".png", 1240, 2300, log)) uis++;
    log += "сцен записано: " + std::to_string(scenes) + "; окон: " + std::to_string(uis) + "\n";
    gs::writeFileAtomic(dir + L"\\screenshot-log.txt", log);
    return scenes > 0 || uis > 0 ? 0 : 1;
}

int runOverlayTest(App& app, const std::wstring& dir) {
    gs::makeDirs(dir);
    for (auto& m : *app.modules) m->prepareOverlayTest();
    for (auto& m : *app.modules) if (auto* l = m->layer()) app.overlay.addLayer(l);
    FILETIME c0, e0, k0, u0;
    GetProcessTimes(GetCurrentProcess(), &c0, &e0, &k0, &u0);
    auto t0 = std::chrono::steady_clock::now();
    app.overlay.start();
    Sleep(9000);
    double secs = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
    FILETIME c1, e1, k1, u1;
    GetProcessTimes(GetCurrentProcess(), &c1, &e1, &k1, &u1);
    auto ft = [](FILETIME a, FILETIME b) { ULARGE_INTEGER x, y; x.LowPart = a.dwLowDateTime; x.HighPart = a.dwHighDateTime; y.LowPart = b.dwLowDateTime; y.HighPart = b.dwHighDateTime; return (double)(y.QuadPart - x.QuadPart) / 1e7; };
    double cpu = ft(k0, k1) + ft(u0, u1);
    char b[256];
    std::string out = std::string("окно работает: ") + (app.overlay.running() ? "да" : "нет") + "\n";
    std::snprintf(b, sizeof b, "кадров нарисовано: %d (за %.1f с)\nсреднее время кадра: %.2f мс\nнагрузка процессора: %.1f %% одного ядра\n", app.overlay.framesDrawn.load(), secs, app.overlay.avgFrameMs.load(), cpu / secs * 100.0);
    out += b;
    out += "здоровье: " + std::string(app.overlay.healthy ? "да" : "нет") + "\nошибка: " + (app.overlay.lastError().empty() ? "нет" : app.overlay.lastError()) + "\n";
    auto list = scanWindows(GetCurrentProcessId(), 0);
    out += "найдено краёв окон: " + std::to_string(list.size()) + "\n";
    for (auto& x : list) { std::snprintf(b, sizeof b, "  %s %.0f..%.0f y=%.0f r=%.0f кусков %d\n", x.taskbar ? "панель" : "окно", x.left, x.right, x.top, x.radius, (int)x.vis.size()); out += b; }
    app.overlay.stop();
    out += std::string("остановлено: ") + (app.overlay.running() ? "нет" : "да") + "\n";
    gs::writeFileAtomic(dir + L"\\overlay-test.txt", out);
    return app.overlay.healthy ? 0 : 1;
}

}  // namespace

int runApp(const AppInfo& info, std::vector<std::unique_ptr<Module>>& modules, int argc, wchar_t** argv) {
    gs::installCrashHandler();
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    gs::locInit(info.locText, argc, argv);
    App app; gApp = &app;
    app.info = info; app.modules = &modules;
    std::vector<Module*> raw; for (auto& m : modules) raw.push_back(m.get());
    app.ctx.overlay = &app.overlay; app.ctx.changed = [&app] { app.changed(); }; app.ctx.all = &raw; app.ctx.revertAll = [&app] { app.revertAll(); };
    app.ctx.showWindow = [&app](bool show) { if (!app.hwnd) return; if (show) PostMessageW(app.hwnd, WM_SHOWUI, 0, 0); else ShowWindow(app.hwnd, SW_MINIMIZE); };
    for (auto& m : modules) m->attach(&app.ctx);

    auto arg = [&](const wchar_t* prefix) -> std::wstring {
        for (int i = 1; i < argc; i++) { std::wstring a = argv[i]; if (a.rfind(prefix, 0) == 0) return a.substr(wcslen(prefix)); }
        return L"";
    };
    auto has = [&](const wchar_t* flag) { for (int i = 1; i < argc; i++) if (std::wstring(argv[i]) == flag) return true; return false; };

    if (has(L"--revert")) {
        gs::SingleInstance::signal(info.quitEvent);
        Sleep(2500);
        for (auto& m : modules) m->revert();
        gs::setAutostart(info.autostartName, L"--background", false);
        return 0;
    }
    if (auto d = arg(L"--selftest="); !d.empty()) {
        gs::makeDirs(d);
        std::string log; int fails = 0;
        for (auto& m : modules) { log += std::string("== ") + m->id() + " ==\n"; fails += m->selfTest(d, log); }
        log += fails == 0 ? "ВСЁ В ПОРЯДКЕ\n" : "ЕСТЬ ОШИБКИ: " + std::to_string(fails) + "\n";
        gs::writeFileAtomic(d + L"\\selftest.txt", log);
        return fails == 0 ? 0 : 1;
    }
    if (auto d = arg(L"--screenshot="); !d.empty()) return runScreenshot(app, d);
    if (auto d = arg(L"--overlay-test="); !d.empty()) return runOverlayTest(app, d);

    gs::SingleInstance single;
    if (!single.acquire(info.mutexName, info.showEvent, info.quitEvent, [&] { if (app.hwnd) PostMessageW(app.hwnd, WM_SHOWUI, 0, 0); }, [&] { if (app.hwnd) PostMessageW(app.hwnd, WM_QUITAPP, 0, 0); })) return 0;

    HINSTANCE inst = GetModuleHandleW(nullptr);
    WNDCLASSEXW wc{}; wc.cbSize = sizeof wc; wc.lpfnWndProc = mainProc; wc.hInstance = inst; wc.lpszClassName = L"GhostShellStyleWindow";
    wc.hIcon = LoadIconW(inst, MAKEINTRESOURCEW(1)); wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    RegisterClassExW(&wc);
    float sc = dpiScale();
    RECT wa{}; SystemParametersInfoW(SPI_GETWORKAREA, 0, &wa, 0);
    int w = std::min((int)(1180 * sc), (int)(wa.right - wa.left) - 40), h = std::min((int)(900 * sc), (int)(wa.bottom - wa.top) - 40);
    app.hwnd = CreateWindowExW(0, wc.lpszClassName, info.name, WS_POPUP | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU | WS_CLIPCHILDREN, (wa.left + wa.right - w) / 2, (wa.top + wa.bottom - h) / 2, w, h, nullptr, nullptr, inst, nullptr);
    { int pref = 2; DwmSetWindowAttribute(app.hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE: скруглённые углы Windows 11 */, &pref, sizeof pref); }

    for (auto& m : modules) if (auto* l = m->layer()) app.overlay.addLayer(l);
    app.overlay.start();
    bool background = has(L"--background");
    bool any = app.anyActive();
    if (background && !any) { single.stop(); return 0; }       // фоновый запуск, а всё выключено — сразу выходим
    for (auto& m : modules) m->start();
    app.changed();                                              // значок в трее и автозапуск
    if (!background) PostMessageW(app.hwnd, WM_SHOWUI, 0, 0);

    auto last = std::chrono::steady_clock::now();
    MSG m;
    for (;;) {
        bool visible = IsWindowVisible(app.hwnd) && !IsIconic(app.hwnd);
        if (visible) { while (PeekMessageW(&m, nullptr, 0, 0, PM_REMOVE)) { if (m.message == WM_QUIT) goto done; TranslateMessage(&m); DispatchMessageW(&m); } }
        else { if (GetMessageW(&m, nullptr, 0, 0) <= 0) goto done; TranslateMessage(&m); DispatchMessageW(&m); last = std::chrono::steady_clock::now(); }
        if (IsWindowVisible(app.hwnd) && !IsIconic(app.hwnd)) {
            if (!app.uiReady && !app.initUi()) { MessageBoxW(app.hwnd, L"Не удалось подготовить рисование (Direct3D 11).", info.name, MB_ICONERROR); DestroyWindow(app.hwnd); continue; }
            auto now = std::chrono::steady_clock::now();
            double dt = std::min(0.1, std::chrono::duration<double>(now - last).count()); last = now;
            app.frame(dt);
        } else if (app.uiReady) {
            app.shutdownUi();                                   // окно спрятано: видеокарту не держим
        }
    }
done:
    for (auto& m : modules) m->stop();
    app.shutdownUi();
    app.overlay.stop();
    app.removeTray();
    single.stop();
    gApp = nullptr;
    return 0;
}

}  // namespace sp
