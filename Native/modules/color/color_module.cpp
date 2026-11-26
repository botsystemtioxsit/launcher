#include "color_module.hpp"
#include <windows.h>
#include <dwmapi.h>
#include <cmath>
#include <set>
#include <tlhelp32.h>
#include <psapi.h>
#include <algorithm>
#include "cursor_settings.hpp"      // parseHex / toHex
#include "imgui.h"
#include "json.hpp"
#include "loc.hpp"
#include "ui_common.hpp"
#include "win.hpp"

namespace sp {

using gs::L;
using namespace ui;

#ifndef DWMWA_CAPTION_COLOR
#define DWMWA_BORDER_COLOR 34
#define DWMWA_CAPTION_COLOR 35
#define DWMWA_TEXT_COLOR 36
#endif
static const DWORD kColorDefault = 0xFFFFFFFF;        // DWMWA_COLOR_DEFAULT
static const wchar_t* kProp = L"GhostShell.Style.Title";

static std::wstring settingsFile() { return gs::appDataDir() + L"\\color-plus-settings.json"; }
static std::wstring originalsFile() { return gs::appDataDir() + L"\\color-plus-originals.json"; }

// ---------- настройки ----------
void ColorSettings::normalize() { if (!CursorSettings::isHex(Color)) Color = "#FF2A9D"; for (auto& c : Color) c = (char)toupper((unsigned char)c); }

ColorSettings ColorSettings::load() {
    ColorSettings s; std::string t; gs::Json j;
    if (gs::readFile(settingsFile(), t) && gs::jsonParse(t, j) && j.t == gs::Json::Obj) {
        s.Color = j.str("Color", s.Color); s.Windows = j.boolean("Windows", s.Windows); s.Taskbar = j.boolean("Taskbar", s.Taskbar); s.Applied = j.boolean("Applied", s.Applied);
    }
    s.normalize();
    return s;
}
void ColorSettings::save() const {
    using gs::Json;
    Json j = Json::Object();
    j.set("Color", Json::String(Color)); j.set("Windows", Json::Boolean(Windows)); j.set("Taskbar", Json::Boolean(Taskbar)); j.set("Applied", Json::Boolean(Applied));
    std::string out; gs::jsonDump(j, out);
    gs::makeDirs(gs::appDataDir());
    gs::writeFileAtomic(settingsFile(), out);
}

// ---------- цвет ----------
static float lum(Rgb c) { return (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255.f; }
std::string readableOn(const std::string& hex) { return lum(parseHex(hex)) > 0.58f ? "#111111" : "#FFFFFF"; }

static void toHsl(Rgb c, float& h, float& s, float& l) {
    float R = c.r / 255, G = c.g / 255, B = c.b / 255, mx = std::max({R, G, B}), mn = std::min({R, G, B}), d = mx - mn;
    l = (mx + mn) / 2; h = 0; s = 0;
    if (d > 1e-6f) {
        s = l > 0.5f ? d / (2 - mx - mn) : d / (mx + mn);
        if (mx == R) h = 60 * std::fmod((G - B) / d, 6.f); else if (mx == G) h = 60 * ((B - R) / d + 2); else h = 60 * ((R - G) / d + 4);
        if (h < 0) h += 360;
    }
}
static Rgb fromHsl(float h, float s, float l) {
    h = std::fmod(std::fmod(h, 360.f) + 360.f, 360.f); s = std::clamp(s, 0.f, 1.f); l = std::clamp(l, 0.f, 1.f);
    float c = (1 - std::fabs(2 * l - 1)) * s, x = c * (1 - std::fabs(std::fmod(h / 60, 2.f) - 1)), m = l - c / 2, r, g, b;
    if (h < 60) { r = c; g = x; b = 0; } else if (h < 120) { r = x; g = c; b = 0; } else if (h < 180) { r = 0; g = c; b = x; }
    else if (h < 240) { r = 0; g = x; b = c; } else if (h < 300) { r = x; g = 0; b = c; } else { r = c; g = 0; b = x; }
    return {(r + m) * 255, (g + m) * 255, (b + m) * 255};
}
static DWORD colorRef(const std::string& hex) { Rgb c = parseHex(hex); return (DWORD)((int)c.b << 16 | (int)c.g << 8 | (int)c.r); }

// ---------- реестр ----------
namespace {
const wchar_t* kDwm = L"SOFTWARE\\Microsoft\\Windows\\DWM";
const wchar_t* kAccent = L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Accent";
const wchar_t* kPers = L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize";
const wchar_t* kColors = L"Control Panel\\Colors";

bool regDword(const wchar_t* key, const wchar_t* name, DWORD& v) {
    HKEY k; if (RegOpenKeyExW(HKEY_CURRENT_USER, key, 0, KEY_QUERY_VALUE, &k) != ERROR_SUCCESS) return false;
    DWORD sz = sizeof v, t = 0; bool ok = RegQueryValueExW(k, name, nullptr, &t, (BYTE*)&v, &sz) == ERROR_SUCCESS && t == REG_DWORD; RegCloseKey(k); return ok;
}
void setDword(const wchar_t* key, const wchar_t* name, bool has, DWORD v) {
    HKEY k; if (RegCreateKeyExW(HKEY_CURRENT_USER, key, 0, nullptr, 0, KEY_SET_VALUE, nullptr, &k, nullptr) != ERROR_SUCCESS) return;
    if (has) RegSetValueExW(k, name, 0, REG_DWORD, (const BYTE*)&v, sizeof v); else RegDeleteValueW(k, name);
    RegCloseKey(k);
}
bool regBin(const wchar_t* key, const wchar_t* name, std::string& v) {
    HKEY k; if (RegOpenKeyExW(HKEY_CURRENT_USER, key, 0, KEY_QUERY_VALUE, &k) != ERROR_SUCCESS) return false;
    BYTE buf[256]; DWORD sz = sizeof buf, t = 0; bool ok = RegQueryValueExW(k, name, nullptr, &t, buf, &sz) == ERROR_SUCCESS && t == REG_BINARY; RegCloseKey(k);
    if (ok) v.assign((char*)buf, sz); return ok;
}
void setBin(const wchar_t* key, const wchar_t* name, bool has, const std::string& v) {
    HKEY k; if (RegCreateKeyExW(HKEY_CURRENT_USER, key, 0, nullptr, 0, KEY_SET_VALUE, nullptr, &k, nullptr) != ERROR_SUCCESS) return;
    if (has) RegSetValueExW(k, name, 0, REG_BINARY, (const BYTE*)v.data(), (DWORD)v.size()); else RegDeleteValueW(k, name);
    RegCloseKey(k);
}
bool regStr(const wchar_t* key, const wchar_t* name, std::wstring& v) {
    HKEY k; if (RegOpenKeyExW(HKEY_CURRENT_USER, key, 0, KEY_QUERY_VALUE, &k) != ERROR_SUCCESS) return false;
    wchar_t buf[256]; DWORD sz = sizeof buf, t = 0; bool ok = RegQueryValueExW(k, name, nullptr, &t, (BYTE*)buf, &sz) == ERROR_SUCCESS && t == REG_SZ; RegCloseKey(k);
    if (ok) v = buf; return ok;
}
void setStr(const wchar_t* key, const wchar_t* name, bool has, const std::wstring& v) {
    HKEY k; if (RegCreateKeyExW(HKEY_CURRENT_USER, key, 0, nullptr, 0, KEY_SET_VALUE, nullptr, &k, nullptr) != ERROR_SUCCESS) return;
    if (has) RegSetValueExW(k, name, 0, REG_SZ, (const BYTE*)v.c_str(), (DWORD)((v.size() + 1) * 2)); else RegDeleteValueW(k, name);
    RegCloseKey(k);
}

// Что было в системе до нас: запоминаем один раз.
struct Orig { bool has[8]{}; DWORD d[8]{}; bool palHas = false; std::string pal; bool hHas = false, htHas = false; std::wstring hil, hilT; DWORD sysH = 0, sysHT = 0; bool changed = false; };

void saveOrig(const Orig& o) {
    using gs::Json;
    Json j = Json::Object();
    j.set("changed", Json::Boolean(o.changed));
    Json hs = Json::Array(), ds = Json::Array();
    for (int i = 0; i < 4; i++) { hs.a.push_back(Json::Boolean(o.has[i])); ds.a.push_back(Json::Number(o.d[i])); }
    j.set("has", hs); j.set("d", ds);
    j.set("palHas", Json::Boolean(o.palHas));
    std::string hex; for (unsigned char c : o.pal) { char b[4]; std::snprintf(b, sizeof b, "%02x", c); hex += b; }
    j.set("pal", Json::String(hex));
    j.set("hHas", Json::Boolean(o.hHas)); j.set("hil", Json::String(gs::narrow(o.hil)));
    j.set("htHas", Json::Boolean(o.htHas)); j.set("hilT", Json::String(gs::narrow(o.hilT)));
    j.set("sysH", Json::Number(o.sysH)); j.set("sysHT", Json::Number(o.sysHT));
    std::string out; gs::jsonDump(j, out);
    gs::makeDirs(gs::appDataDir());
    gs::writeFileAtomic(originalsFile(), out);
}
Orig loadOrig() {
    Orig o; std::string t; gs::Json j;
    if (!gs::readFile(originalsFile(), t) || !gs::jsonParse(t, j) || j.t != gs::Json::Obj) return o;
    o.changed = j.boolean("changed");
    if (auto* hs = j.get("has")) for (size_t i = 0; i < hs->a.size() && i < 4; i++) o.has[i] = hs->a[i].b;
    if (auto* ds = j.get("d")) for (size_t i = 0; i < ds->a.size() && i < 4; i++) o.d[i] = (DWORD)ds->a[i].n;
    o.palHas = j.boolean("palHas");
    std::string hex = j.str("pal");
    for (size_t i = 0; i + 1 < hex.size(); i += 2) o.pal += (char)std::stoi(hex.substr(i, 2), nullptr, 16);
    o.hHas = j.boolean("hHas"); o.hil = gs::widen(j.str("hil")); o.htHas = j.boolean("htHas"); o.hilT = gs::widen(j.str("hilT"));
    o.sysH = (DWORD)j.num("sysH"); o.sysHT = (DWORD)j.num("sysHT");
    return o;
}

DWORD abgr(const std::string& hex) { Rgb c = parseHex(hex); return 0xFF000000u | (DWORD)c.b << 16 | (DWORD)c.g << 8 | (DWORD)c.r; }
DWORD argb(const std::string& hex) { Rgb c = parseHex(hex); return 0xFF000000u | (DWORD)c.r << 16 | (DWORD)c.g << 8 | (DWORD)c.b; }

// Восемь оттенков акцента: три светлее, сам цвет, три темнее, последний остаётся как в системе
std::string buildPalette(const std::string& hex, const std::string& original) {
    float h, s, l; toHsl(parseHex(hex), h, s, l);
    const float dl[7] = {0.38f, 0.23f, 0.07f, 0.f, -0.04f, -0.13f, -0.22f};
    std::string p(32, 0);
    for (int i = 0; i < 7; i++) {
        Rgb c = i == 3 ? parseHex(hex) : fromHsl(h, s, std::clamp(l + dl[i], 0.04f, 0.96f));
        p[i * 4] = (char)(int)c.r; p[i * 4 + 1] = (char)(int)c.g; p[i * 4 + 2] = (char)(int)c.b; p[i * 4 + 3] = (char)0xFF;
    }
    if (original.size() == 32) memcpy(&p[28], &original[28], 4);
    else { p[28] = (char)0xF7; p[29] = (char)0x63; p[30] = (char)0x0C; p[31] = (char)0xFF; }
    return p;
}

void broadcast(DWORD colorization) {
    std::thread([colorization] {
        DWORD_PTR r;
        SendMessageTimeoutW(HWND_BROADCAST, WM_SETTINGCHANGE, 0, (LPARAM)L"ImmersiveColorSet", SMTO_ABORTIFHUNG, 1500, &r);
        PostMessageW(HWND_BROADCAST, 0x0320 /* WM_DWMCOLORIZATIONCOLORCHANGED */, colorization, 0);
    }).detach();
}

void applyWindows(const std::string& hex) {
    Orig o = loadOrig();
    if (!o.changed) {
        o.has[0] = regDword(kDwm, L"AccentColor", o.d[0]); o.has[1] = regDword(kDwm, L"ColorizationColor", o.d[1]);
        o.has[2] = regDword(kDwm, L"ColorizationAfterglow", o.d[2]); o.has[3] = regDword(kPers, L"ColorPrevalence", o.d[3]);
        o.palHas = regBin(kAccent, L"AccentPalette", o.pal);
        o.hHas = regStr(kColors, L"Hilight", o.hil); o.htHas = regStr(kColors, L"HilightText", o.hilT);
        o.sysH = GetSysColor(COLOR_HIGHLIGHT); o.sysHT = GetSysColor(COLOR_HIGHLIGHTTEXT);
        o.changed = true;
        saveOrig(o);                                   // сначала запоминаем, потом меняем
    }
    setDword(kDwm, L"AccentColor", true, abgr(hex));
    setDword(kDwm, L"ColorizationColor", true, argb(hex));
    setDword(kDwm, L"ColorizationAfterglow", true, argb(hex));
    setDword(kPers, L"ColorPrevalence", true, 0);                       // «Пуск» и панель задач акцентом не красятся
    setBin(kAccent, L"AccentPalette", true, buildPalette(hex, o.palHas ? o.pal : ""));
    std::string text = readableOn(hex);
    Rgb c = parseHex(hex), tc = parseHex(text);
    wchar_t b1[32], b2[32];
    std::swprintf(b1, 32, L"%d %d %d", (int)c.r, (int)c.g, (int)c.b); std::swprintf(b2, 32, L"%d %d %d", (int)tc.r, (int)tc.g, (int)tc.b);
    setStr(kColors, L"Hilight", true, b1); setStr(kColors, L"HilightText", true, b2);
    INT idx[2] = {COLOR_HIGHLIGHT, COLOR_HIGHLIGHTTEXT}; COLORREF cr[2] = {colorRef(hex), colorRef(text)};
    SetSysColors(2, idx, cr);
    broadcast(argb(hex));
}

void restoreWindows() {
    Orig o = loadOrig();
    if (!o.changed) return;
    setDword(kDwm, L"AccentColor", o.has[0], o.d[0]); setDword(kDwm, L"ColorizationColor", o.has[1], o.d[1]);
    setDword(kDwm, L"ColorizationAfterglow", o.has[2], o.d[2]); setDword(kPers, L"ColorPrevalence", o.has[3], o.d[3]);
    setBin(kAccent, L"AccentPalette", o.palHas, o.pal);
    setStr(kColors, L"Hilight", o.hHas, o.hil); setStr(kColors, L"HilightText", o.htHas, o.hilT);
    INT idx[2] = {COLOR_HIGHLIGHT, COLOR_HIGHLIGHTTEXT}; COLORREF cr[2] = {o.sysH, o.sysHT};
    SetSysColors(2, idx, cr);
    DeleteFileW(originalsFile().c_str());
    broadcast(o.has[1] ? o.d[1] : 0);
}

}  // namespace

// ---------- заголовки окон ----------
bool TitleEngine::supported() {
    typedef LONG (WINAPI* Fn)(OSVERSIONINFOW*);
    auto fn = (Fn)(void*)GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "RtlGetVersion");
    OSVERSIONINFOW v{}; v.dwOSVersionInfoSize = sizeof v;
    return fn && fn(&v) == 0 && v.dwBuildNumber >= 22000;
}

static void resetWindow(HWND h) {
    if (!IsWindow(h)) return;
    DWORD d = kColorDefault;
    DwmSetWindowAttribute(h, DWMWA_CAPTION_COLOR, &d, sizeof d);
    DwmSetWindowAttribute(h, DWMWA_TEXT_COLOR, &d, sizeof d);
    DwmSetWindowAttribute(h, DWMWA_BORDER_COLOR, &d, sizeof d);
    RemovePropW(h, kProp);
}

void TitleEngine::recoverLeftovers() {
    if (!supported()) return;
    EnumWindows([](HWND h, LPARAM) -> BOOL { if (GetPropW(h, kProp) && !IsHungAppWindow(h)) resetWindow(h); return TRUE; }, 0);
}

static std::wstring lower(std::wstring s) { for (auto& c : s) c = (wchar_t)towlower(c); return s; }

// Игры и программы, которые рисуют окно через видеокарту, не трогаем никогда
static bool isGameWindow(HWND h, DWORD pid, const std::wstring& proc, const std::wstring& cls) {
    static const wchar_t* prefixes[] = {L"GLFW", L"SDL_app", L"SDL_Window", L"UnityWndClass", L"UnityContainerWndClass", L"UnrealWindow", L"Valve001", L"LWJGL", L"CryENGINE", L"SFML_Window", L"Allegro", L"OGRE", L"FNA"};
    for (auto* p : prefixes) if (cls.rfind(p, 0) == 0) return true;
    if (cls == L"Engine" || cls == L"GodotEngine" || cls == L"RGSS Player" || cls == L"OpenGL Window") return true;
    static const std::set<std::wstring> hard = {L"steam", L"steamwebhelper", L"wallpaper32", L"wallpaper64", L"webwallpaper32", L"webwallpaper64", L"lively", L"livelywpf", L"rainmeter", L"obs64", L"obs32", L"obs", L"ghoststyle"};
    if (hard.count(proc)) return true;
    static const std::set<std::wstring> safe = {L"chrome", L"msedge", L"firefox", L"brave", L"opera", L"opera_gx", L"vivaldi", L"msedgewebview2", L"iexplore", L"explorer", L"windowsterminal", L"code", L"discord", L"slack", L"telegram", L"notepad", L"mspaint"};
    HANDLE ph = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ, FALSE, pid);
    if (!ph) return false;
    bool game = false;
    wchar_t path[MAX_PATH * 2] = {}; DWORD n = (DWORD)std::size(path);
    if (QueryFullProcessImageNameW(ph, 0, path, &n)) {
        std::wstring p = lower(path);
        static const wchar_t* games[] = {L"\\steamapps\\common\\", L"\\epic games\\", L"\\gog galaxy\\games\\", L"\\gog games\\", L"\\riot games\\", L"\\ubisoft game launcher\\games\\", L"\\ea games\\", L"\\origin games\\", L"\\xboxgames\\", L"\\games\\"};
        for (auto* g : games) if (p.find(g) != std::wstring::npos) { game = true; break; }
    }
    if (!game && !safe.count(proc)) {
        HMODULE mods[1024]; DWORD need = 0;
        if (EnumProcessModulesEx(ph, mods, sizeof mods, &need, LIST_MODULES_ALL)) {
            static const std::set<std::wstring> markers = {L"opengl32.dll", L"vulkan-1.dll", L"d3d12.dll", L"d3d12core.dll", L"steam_api.dll", L"steam_api64.dll", L"unityplayer.dll", L"sdl2.dll", L"sdl3.dll", L"glfw.dll", L"glfw3.dll", L"lwjgl.dll", L"lwjgl64.dll"};
            for (DWORD i = 0; i < std::min<DWORD>(need / sizeof(HMODULE), 1024); i++) {
                wchar_t nm[MAX_PATH] = {};
                if (GetModuleBaseNameW(ph, mods[i], nm, MAX_PATH) && markers.count(lower(nm))) { game = true; break; }
            }
        }
    }
    CloseHandle(ph);
    return game;
}

void TitleEngine::set(const std::string& hex) {
    if (!supported()) return;
    { std::lock_guard<std::mutex> l(mu_); color_ = hex; }
    if (run_) return;
    run_ = true;
    th_ = std::thread([this] { loop(); });
}

void TitleEngine::stop() {
    run_ = false;
    if (th_.joinable()) th_.join();
}

void TitleEngine::loop() {
    struct Done { std::string hex; };
    std::unordered_map<HWND, std::string> done;
    struct PC { DWORD pid; std::wstring name; ULONGLONG at; };
    std::unordered_map<DWORD, PC> names;
    const DWORD self = GetCurrentProcessId();
    auto procName = [&](DWORD pid) -> std::wstring {
        auto it = names.find(pid);
        if (it != names.end() && GetTickCount64() - it->second.at < 30000) return it->second.name;
        std::wstring n;
        HANDLE ph = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
        if (ph) { wchar_t p[MAX_PATH * 2] = {}; DWORD sz = (DWORD)std::size(p); if (QueryFullProcessImageNameW(ph, 0, p, &sz)) { std::wstring s = p; size_t k = s.find_last_of(L'\\'); s = s.substr(k == std::wstring::npos ? 0 : k + 1); size_t d = s.rfind(L'.'); if (d != std::wstring::npos) s = s.substr(0, d); n = lower(s); } CloseHandle(ph); }
        names[pid] = {pid, n, GetTickCount64()};
        if (names.size() > 400) names.clear();
        return n;
    };
    std::unordered_map<DWORD, std::pair<bool, ULONGLONG>> gameCache;
    while (run_) {
        std::string hex; { std::lock_guard<std::mutex> l(mu_); hex = color_; }
        std::set<HWND> seen;
        struct Ctx { std::string* hex; std::set<HWND>* seen; std::unordered_map<HWND, std::string>* done; std::function<std::wstring(DWORD)>* pn; DWORD self; std::unordered_map<DWORD, std::pair<bool, ULONGLONG>>* games; };
        std::function<std::wstring(DWORD)> pn = procName;
        Ctx c{&hex, &seen, &done, &pn, self, &gameCache};
        EnumWindows([](HWND h, LPARAM lp) -> BOOL {
            auto* c = (Ctx*)lp;
            if (!IsWindowVisible(h)) return TRUE;
            LONG_PTR st = GetWindowLongPtrW(h, GWL_STYLE), ex = GetWindowLongPtrW(h, GWL_EXSTYLE);
            if ((st & 0xC00000) != 0xC00000) return TRUE;                 // нет строки заголовка
            if (ex & WS_EX_TOOLWINDOW) return TRUE;
            int cloaked = 0;
            if (SUCCEEDED(DwmGetWindowAttribute(h, DWMWA_CLOAKED, &cloaked, sizeof cloaked)) && cloaked) return TRUE;
            if (IsHungAppWindow(h)) return TRUE;
            DWORD pid = 0; GetWindowThreadProcessId(h, &pid);
            if (!pid || pid == c->self) return TRUE;
            std::wstring proc = (*c->pn)(pid);
            if (proc.empty()) return TRUE;
            wchar_t cls[128] = {}; GetClassNameW(h, cls, 128);
            auto git = c->games->find(pid);
            bool game;
            if (git != c->games->end() && GetTickCount64() - git->second.second < 30000) game = git->second.first;
            else { game = isGameWindow(h, pid, proc, cls); (*c->games)[pid] = {game, GetTickCount64()}; }
            if (game) return TRUE;
            c->seen->insert(h);
            auto it = c->done->find(h);
            if (it != c->done->end() && it->second == *c->hex) return TRUE;
            DWORD cap = colorRef(*c->hex), tx = colorRef(readableOn(*c->hex));
            DwmSetWindowAttribute(h, DWMWA_CAPTION_COLOR, &cap, sizeof cap);
            DwmSetWindowAttribute(h, DWMWA_TEXT_COLOR, &tx, sizeof tx);
            DwmSetWindowAttribute(h, DWMWA_BORDER_COLOR, &cap, sizeof cap);
            SetPropW(h, kProp, (HANDLE)1);
            (*c->done)[h] = *c->hex;
            return TRUE;
        }, (LPARAM)&c);
        for (auto it = done.begin(); it != done.end();) {
            if (!seen.count(it->first)) { resetWindow(it->first); it = done.erase(it); } else ++it;
        }
        painted_ = (int)done.size();
        for (int i = 0; i < 6 && run_; i++) Sleep(100);
    }
    for (auto& kv : done) resetWindow(kv.first);
    painted_ = 0;
}

// ---------- модуль ----------
ColorModule::ColorModule() {
    draft_ = ColorSettings::load();
    saved_ = draft_;
    TitleEngine::recoverLeftovers();
    if (saved_.Applied && saved_.Windows) titles_.set(saved_.Color);
}

void ColorModule::init(ID3D11Device* dev) { canvas_.init(dev, 640, 250); }

// Цвет островов панели: модуль «Панель» читает этот файл (тот же формат, что у прежнего Color Plus)
static void writeTaskbarLink(bool active, const std::string& color) {
    using gs::Json;
    Json j = Json::Object();
    j.set("Active", Json::Boolean(active)); j.set("BgMode", Json::String(active ? "solid" : "default")); j.set("BgColor", Json::String(color));
    j.set("BgOpacity", Json::Number(85)); j.set("HideStroke", Json::Boolean(false));
    std::string out; gs::jsonDump(j, out);
    gs::makeDirs(gs::appDataDir());
    gs::writeFileAtomic(gs::appDataDir() + L"\\color-plus-taskbar.json", out);
}

std::string ColorModule::applyNow(const ColorSettings& s) {
    ColorSettings n = s; n.normalize();
    std::string err;
    try { if (n.Windows) applyWindows(n.Color); else restoreWindows(); } catch (...) { err = "окна: ошибка"; }
    if (n.Windows && TitleEngine::supported()) titles_.set(n.Color); else titles_.stop();
    writeTaskbarLink(n.Taskbar, n.Color);
    n.Applied = true;
    saved_ = n; draft_ = n; saved_.save();
    if (app_ && app_->changed) app_->changed();
    return err;
}

void ColorModule::revert() {
    titles_.stop();
    writeTaskbarLink(false, saved_.Color);
    restoreWindows();
    saved_.Applied = false; draft_.Applied = false; saved_.save();
    if (app_ && app_->changed) app_->changed();
}

void ColorModule::drawPreview(Gfx& g) {
    Rgb acc = parseHex(draft_.Color), txt = parseHex(readableOn(draft_.Color));
    for (int i = 0; i < 25; i++) g.rect(0, 250.f * i / 25, 640, 250.f * (i + 1) / 25 + 1, Rgb::lerp({30, 30, 50}, {60, 60, 90}, i / 24.f), 1.f);
    auto win = [&](float x, float y, float w, float h, bool colored) {
        g.roundRect(x, y, x + w, y + h, 8, {240, 242, 248}, 1.f);
        Rgb cap = colored ? acc : Rgb{214, 220, 236};
        g.roundRect(x, y, x + w, y + 30, 8, cap, 1.f); g.rect(x, y + 14, x + w, y + 30, cap, 1.f);
        Rgb tc = colored ? txt : Rgb{30, 30, 40};
        g.rect(x + 14, y + 11, x + 90, y + 19, tc, 0.9f);
        g.disc(x + w - 18, y + 15, 4, tc, 0.9f); g.disc(x + w - 36, y + 15, 4, tc, 0.9f);
        if (colored) { g.rect(x, y, x + 2, y + h, acc, 1.f); g.rect(x + w - 2, y, x + w, y + h, acc, 1.f); g.rect(x, y + h - 2, x + w, y + h, acc, 1.f); }
        for (int i = 0; i < 4; i++) g.rect(x + 16, y + 48 + i * 24.f, x + 16 + 160 + i * 30.f, y + 56 + i * 24.f, {200, 204, 218}, 1.f);
        g.rect(x + 16, y + 48 + 24, x + w - 24, y + 56 + 24, acc, 0.9f);   // выделенная строка
        g.rect(x + 16, y + 48 + 24, x + 16 + 120, y + 56 + 24, txt, 0.9f);
    };
    win(30, 30, 270, 190, draft_.Windows);
    win(340, 60, 270, 170, draft_.Windows);
}

void ColorModule::tick(double dt) { if (canvas_.ready()) canvas_.draw([this](Gfx& g) { drawPreview(g); }); }

void ColorModule::drawPanel() {
    ImGui::PushFont(mono());
    ImGui::PushStyleColor(ImGuiCol_Text, rgb(0, 240, 255));
    ImGui::TextUnformatted(L("Цвет"));
    ImGui::PopStyleColor(); ImGui::PopFont();
    note("Один цвет для всего: окна и выделение (акцент Windows) и панель задач. Выберите цвет, посмотрите на предпросмотре и нажмите «Применить». Пока не нажали, в Windows ничего не меняется.");
    ImGui::Spacing();
    if (beginCard("1. Так будет выглядеть")) {
        if (canvas_.ready()) ImGui::Image((ImTextureID)(intptr_t)canvas_.srv(), ImVec2(640, 250));
    }
    endCard();
    if (beginCard("2. Выберите цвет")) {
        Rgb c = parseHex(draft_.Color);
        float col[3] = {c.r / 255.f, c.g / 255.f, c.b / 255.f};
        if (ImGui::ColorPicker3("##c", col, ImGuiColorEditFlags_NoSidePreview | ImGuiColorEditFlags_NoInputs | ImGuiColorEditFlags_PickerHueWheel)) draft_.Color = toHex({col[0] * 255, col[1] * 255, col[2] * 255});
        static const char* sw[] = {"#FF2A9D", "#FF4D4D", "#FF8A3D", "#FFB020", "#F5D90A", "#20C27A", "#00F0FF", "#1E6BFF", "#6C4DFF", "#C04DFF", "#F2F2F2", "#101018"};
        for (int i = 0; i < 12; i++) {
            if (i) ImGui::SameLine();
            ImGui::PushID(i);
            Rgb s = parseHex(sw[i]);
            if (ImGui::ColorButton("##s", ImVec4(s.r / 255.f, s.g / 255.f, s.b / 255.f, 1), ImGuiColorEditFlags_NoTooltip, ImVec2(28, 28))) draft_.Color = sw[i];
            ImGui::PopID();
        }
        ImGui::Spacing();
        toggle("Окна и выделение", "заголовки и рамки окон, выделение, акцент Windows (нужна Windows 11 для заголовков)", draft_.Windows);
        toggle("Панель задач", "цвет получают острова панели (модуль «Панель»)", draft_.Taskbar);
    }
    endCard();
    ImGui::PushStyleColor(ImGuiCol_Button, rgb(0, 240, 255)); ImGui::PushStyleColor(ImGuiCol_Text, rgb(6, 5, 13));
    if (ImGui::Button(L("Применить"))) { std::string e = applyNow(draft_); status_ = e.empty() ? L("Применено. Приложение остаётся в фоне, чтобы окрашивать новые окна.") : e; }
    ImGui::PopStyleColor(2);
    ImGui::SameLine();
    if (ImGui::Button(L("Вернуть как было"))) { revert(); status_ = L("Всё возвращено: цвета Windows как были."); }
    ImGui::PushStyleColor(ImGuiCol_Text, muted());
    ImGui::TextWrapped("%s", status_.empty() ? L(saved_.Applied ? "Применено." : "Не применено.") : status_.c_str());
    ImGui::PopStyleColor();
}

// ---------- проверка ----------
int ColorModule::selfTest(const std::wstring& dir, std::string& log) {
    int fails = 0;
    auto check = [&](const std::string& name, bool ok, const std::string& extra = "") { log += (ok ? "PASS " : "FAIL ") + name + (extra.empty() ? "" : " — " + extra) + "\n"; if (!ok) fails++; };
    check("читаемый текст: на тёмном — белый", readableOn("#101018") == "#FFFFFF");
    check("читаемый текст: на светлом — чёрный", readableOn("#F5D90A") == "#111111");
    std::string pal = buildPalette("#FF2A9D", "");
    check("палитра акцента: 32 байта, седьмой оттенок — сам цвет", pal.size() == 32 && (unsigned char)pal[12] == 255 && (unsigned char)pal[13] == 42 && (unsigned char)pal[14] == 157);
    float h, s, l; toHsl({255, 0, 0}, h, s, l);
    Rgb back = fromHsl(h, s, l);
    check("цвет туда-обратно через HSL", std::fabs(back.r - 255) < 1 && std::fabs(back.g) < 1);

    DWORD acc0 = 0, col0 = 0, prev0 = 0; bool a = regDword(kDwm, L"AccentColor", acc0), c = regDword(kDwm, L"ColorizationColor", col0), p = regDword(kPers, L"ColorPrevalence", prev0);
    std::wstring hil0; bool hh = regStr(kColors, L"Hilight", hil0);
    COLORREF sysH0 = GetSysColor(COLOR_HIGHLIGHT);

    ColorSettings t; t.Color = "#20C27A"; t.Windows = true; t.Taskbar = false;
    std::string err = applyNow(t);
    check("применение без ошибок", err.empty(), err);
    DWORD acc1 = 0; bool a1 = regDword(kDwm, L"AccentColor", acc1);
    check("акцент Windows записан", a1 && acc1 == abgr("#20C27A"), std::to_string(acc1));
    std::wstring hil1; regStr(kColors, L"Hilight", hil1);
    check("цвет выделения записан", hil1 == L"32 194 122", gs::narrow(hil1));
    check("системный цвет выделения поменялся", GetSysColor(COLOR_HIGHLIGHT) == colorRef("#20C27A"));
    check("есть файл прежних значений", GetFileAttributesW(originalsFile().c_str()) != INVALID_FILE_ATTRIBUTES);
    if (TitleEngine::supported()) {
        // открываем обычное окно (блокнот) и ждём, пока заголовок перекрасится
        STARTUPINFOW si{}; si.cb = sizeof si; PROCESS_INFORMATION pi{};
        wchar_t cmd[] = L"notepad.exe";
        bool started = CreateProcessW(nullptr, cmd, nullptr, nullptr, FALSE, 0, nullptr, nullptr, &si, &pi);
        int painted = 0;
        if (started) { for (int i = 0; i < 40 && painted == 0; i++) { Sleep(250); painted = titles_.painted(); } }
        check("заголовки окон красятся", painted > 0, "окон: " + std::to_string(painted));
        if (started) { TerminateProcess(pi.hProcess, 0); CloseHandle(pi.hProcess); CloseHandle(pi.hThread); }
    } else log += "SKIP заголовки: нужна Windows 11\n";

    revert();
    DWORD acc2 = 0, col2 = 0, prev2 = 0; bool a2 = regDword(kDwm, L"AccentColor", acc2), c2 = regDword(kDwm, L"ColorizationColor", col2), p2 = regDword(kPers, L"ColorPrevalence", prev2);
    std::wstring hil2; bool h2 = regStr(kColors, L"Hilight", hil2);
    check("после «Вернуть» акцент как был", a2 == a && (!a || acc2 == acc0));
    check("после «Вернуть» окраска как была", c2 == c && (!c || col2 == col0) && p2 == p && (!p || prev2 == prev0));
    check("после «Вернуть» выделение как было", h2 == hh && (!hh || hil2 == hil0) && GetSysColor(COLOR_HIGHLIGHT) == sysH0);
    check("файл прежних значений убран", GetFileAttributesW(originalsFile().c_str()) == INVALID_FILE_ATTRIBUTES);
    (void)dir;
    return fails;
}

}  // namespace sp
