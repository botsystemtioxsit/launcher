#include "wp_controller.hpp"
#include <algorithm>
#include <cstdio>
#include <tlhelp32.h>
#include "win.hpp"

namespace wp {

using gs::narrow;
using gs::widen;

static const wchar_t* kHost = L"wallpaper.example";
static const wchar_t* kBgHost = L"bg.wallpaper.example";
static const wchar_t* kFgHost = L"fg.wallpaper.example";

// ---------- мониторы ----------
std::vector<MonitorInfoLite> monitors() {
    std::vector<MonitorInfoLite> v;
    EnumDisplayMonitors(nullptr, nullptr, [](HMONITOR m, HDC, LPRECT, LPARAM l) -> BOOL {
        MONITORINFO mi{sizeof mi};
        if (GetMonitorInfoW(m, &mi)) ((std::vector<MonitorInfoLite>*)l)->push_back({mi.rcMonitor, (mi.dwFlags & MONITORINFOF_PRIMARY) != 0});
        return TRUE;
    }, (LPARAM)&v);
    std::sort(v.begin(), v.end(), [](const MonitorInfoLite& a, const MonitorInfoLite& b) { return a.primary > b.primary; });
    return v;
}

// ---------- рабочий стол ----------
static std::wstring className(HWND h) { wchar_t b[128] = {}; GetClassNameW(h, b, 128); return b; }

// Windows 10 и 11 до 24H2: сообщение 0x052C заставляет Progman создать WorkerW позади значков — наше окно становится его дочерним.
// Windows 11 24H2+: WorkerW — дочернее окно Progman; наше тоже дочернее Progman, сразу под окном значков (SHELLDLL_DefView).
DesktopTarget findDesktop() {
    DesktopTarget t;
    HWND progman = FindWindowW(L"Progman", nullptr);
    if (!progman) { t.mode = "нет Progman"; return t; }
    for (int i = 0; i < 2; i++) { DWORD_PTR r; SendMessageTimeoutW(progman, 0x052C, 0xD, 1, SMTO_NORMAL, 1000, &r); Sleep(80); }
    HWND childWorker = FindWindowExW(progman, nullptr, L"WorkerW", nullptr);
    HWND defView = FindWindowExW(progman, nullptr, L"SHELLDLL_DefView", nullptr);
    if (childWorker && defView) { t.parent = progman; t.insertAfter = defView; t.mode = "Progman (24H2)"; return t; }
    HWND worker = nullptr;
    struct Ctx { HWND* w; } c{&worker};
    EnumWindows([](HWND top, LPARAM l) -> BOOL {
        HWND dv = FindWindowExW(top, nullptr, L"SHELLDLL_DefView", nullptr);
        if (dv) *((Ctx*)l)->w = FindWindowExW(nullptr, top, L"WorkerW", nullptr);
        return TRUE;
    }, (LPARAM)&c);
    if (worker) { t.parent = worker; t.insertAfter = HWND_TOP; t.mode = "WorkerW"; return t; }
    t.parent = progman; t.insertAfter = defView ? defView : HWND_BOTTOM; t.mode = "Progman (запасной способ)";
    return t;
}

bool attachToDesktop(HWND hwnd, const RECT& monitor, std::string* mode) {
    DesktopTarget t = findDesktop();
    if (!t.parent) { log("рабочий стол не найден: " + t.mode); return false; }
    LONG_PTR style = GetWindowLongPtrW(hwnd, GWL_STYLE);
    style &= ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
    style |= WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN;
    SetWindowLongPtrW(hwnd, GWL_STYLE, style);
    LONG_PTR ex = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
    ex = (ex & ~WS_EX_APPWINDOW) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
    SetWindowLongPtrW(hwnd, GWL_EXSTYLE, ex);
    SetParent(hwnd, t.parent);
    RECT pr{}; GetWindowRect(t.parent, &pr);
    int x = monitor.left - pr.left, y = monitor.top - pr.top, w = monitor.right - monitor.left, h = monitor.bottom - monitor.top;
    SetWindowPos(hwnd, t.insertAfter, x, y, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED);
    char b[256]; std::snprintf(b, sizeof b, "окно %p прикреплено: %s, родитель %p, место %d,%d %dx%d", (void*)hwnd, t.mode.c_str(), (void*)t.parent, x, y, w, h);
    log(b);
    if (mode) *mode = t.mode;
    return true;
}

bool stillAttached(HWND hwnd) {
    if (!IsWindow(hwnd)) return false;
    HWND p = GetParent(hwnd);
    if (!p || !IsWindow(p)) return false;
    std::wstring c = className(p);
    return c == L"Progman" || c == L"WorkerW";
}

static std::string rectText(HWND h) { RECT r; if (!GetWindowRect(h, &r)) return "[?]"; char b[80]; std::snprintf(b, sizeof b, "[%ld,%ld %ldx%ld]", r.left, r.top, r.right - r.left, r.bottom - r.top); return b; }
std::string dumpDesktop() {
    std::string o; HWND progman = FindWindowW(L"Progman", nullptr);
    o += "Progman " + rectText(progman) + "\n";
    HWND c = nullptr;
    while ((c = FindWindowExW(progman, c, nullptr, nullptr))) o += "  " + narrow(className(c)) + " " + rectText(c) + (IsWindowVisible(c) ? " видимо\n" : " скрыто\n");
    return o;
}

void repaintDesktop() {
    HWND progman = FindWindowW(L"Progman", nullptr);
    if (progman) RedrawWindow(progman, nullptr, nullptr, RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_FRAME);
    RedrawWindow(nullptr, nullptr, nullptr, RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN);
}

// Окно переднего плана закрывает монитор почти целиком — обои под ним не видны.
bool desktopCovered(const RECT& m) {
    HWND fg = GetForegroundWindow();
    if (!fg || !IsWindowVisible(fg) || IsIconic(fg)) return false;
    std::wstring cls = className(fg);
    for (auto c : {L"Progman", L"WorkerW", L"Shell_TrayWnd", L"Shell_SecondaryTrayWnd", L"SHELLDLL_DefView", L"XamlExplorerHostIslandWindow", L"Windows.UI.Core.CoreWindow"}) if (cls == c) return false;
    RECT r{}; if (!GetWindowRect(fg, &r)) return false;
    double cx = std::max<LONG>(0, std::min(r.right, m.right) - std::max(r.left, m.left)), cy = std::max<LONG>(0, std::min(r.bottom, m.bottom) - std::max(r.top, m.top));
    double area = std::max<double>(1, (double)(m.right - m.left) * (m.bottom - m.top));
    return cx * cy / area >= 0.92;
}

// ---------- адрес страницы ----------
static std::string enc(const std::string& s) {
    std::string o; char b[4];
    for (unsigned char c : s) { if (isalnum(c) || c == '-' || c == '_' || c == '.' || c == '~') o += (char)c; else { std::snprintf(b, sizeof b, "%%%02X", c); o += b; } }
    return o;
}
static std::string encPath(const std::string& s) {
    std::string o; char b[4];
    for (unsigned char c : s) { if (isalnum(c) || c == '-' || c == '_' || c == '.' || c == '~' || c == '/') o += (char)c; else { std::snprintf(b, sizeof b, "%%%02X", c); o += b; } }
    return o;
}

std::wstring entryUrl(const Wallpaper& w, const Settings& s, const std::map<std::string, std::string>* valuesOverride, const std::wstring& host, const std::string& extra) {
    std::string q = "fit=" + s.Fit + "&fps=" + std::to_string(s.Fps) + "&muted=" + (s.Mute ? "1" : "0");
    Wallpaper tmp; tmp.id = w.id; tmp.params = w.params; tmp.values = valuesOverride ? *valuesOverride : w.values;
    std::string pq = tmp.paramQuery();
    if (!pq.empty()) q += "&" + pq;
    if (!extra.empty()) q += "&" + extra;
    std::string path;
    if (w.type == "html") { std::string e = w.entry; for (auto& c : e) if (c == '\\') c = '/'; path = encPath(e); }
    else { path = "_player.html"; q += "&f=" + enc(mediaFile(w)) + "&t=" + w.type; }
    return L"https://" + host + L"/" + widen(path) + L"?" + widen(q);
}

// ---------- страница в окне ----------
void PageHost::open(HWND hwnd, const Wallpaper& w, const Settings& s, const LoadOpts& o, Telemetry* tel, std::function<void(const std::string&)> onMessage, std::function<void(bool)> done) {
    tel_ = tel;
    std::wstring userData = dataDir() + L"\\webview-host";
    auto al = alive;
    web.create(hwnd, userData, [this, al, hwnd, w, s, o, onMessage, done](bool ok) {
        if (!*al) return;
        if (!ok) { failed = true; error = web.lastError(); if (done) done(false); return; }
        RECT r; GetClientRect(hwnd, &r); web.resize(r.right - r.left, r.bottom - r.top);
        if (onMessage) web.setOnMessage(onMessage);
        web.restrictTo(kHost);
        web.setOnNavigated([this, al] { if (*al && tel_) for (auto& m : tel_->resync(kinds)) web.postJson(m); });
        web.addInitScript(widen(readAsset("res/gs.js")), [this, al, w, s, o, done] {
            if (!*al) return;
            ready = true;
            navigateTo(w, s, o, nullptr);
            if (done) done(true);
        });
    });
}

void PageHost::navigateTo(const Wallpaper& w, const Settings& s, const LoadOpts& o, const std::map<std::string, std::string>* values) {
    Wallpaper bgW; bool hasBg = false;
    std::string bgId = o.useBackground ? o.background : w.background;
    if (w.overlay && !bgId.empty() && bgId != w.id) hasBg = getWallpaper(bgId, bgW);
    kinds.clear();
    for (auto& k : w.telemetry) kinds.insert(k);
    if (hasBg) for (auto& k : bgW.telemetry) kinds.insert(k);
    web.setMuted(s.Mute);
    web.blockNetwork(!(w.online || (hasBg && bgW.online)), kHost);
    std::string extra = o.edit ? "edit=1" : "";
    std::wstring url;
    if (hasBg) {
        ensureSystemFiles();
        web.mapFolder(kHost, systemDir()); web.mapFolder(kBgHost, bgW.dir); web.mapFolder(kFgHost, w.dir);
        std::wstring bgUrl = entryUrl(bgW, s, nullptr, kBgHost, "");
        std::wstring fgUrl = entryUrl(w, s, values, kFgHost, extra.empty() ? "compose=1" : "compose=1&" + extra);
        url = L"https://" + std::wstring(kHost) + L"/compose.html?bg=" + widen(enc(narrow(bgUrl))) + L"&fg=" + widen(enc(narrow(fgUrl)));
    } else {
        web.mapFolder(kHost, w.dir);
        url = entryUrl(w, s, values, kHost, extra);
    }
    web.navigate(url);
}

// ---------- окно обоев на мониторе ----------
struct Controller::Host {
    HWND hwnd = nullptr;
    RECT rect{};
    PageHost page;
    bool paused = false;
    bool visibleSet = true;
};

static const wchar_t* kHostClass = L"GhostStyleWallpaperHost";
static LRESULT CALLBACK hostProc(HWND h, UINT m, WPARAM w, LPARAM l) {
    if (m == WM_ERASEBKGND) { RECT r; GetClientRect(h, &r); FillRect((HDC)w, &r, (HBRUSH)GetStockObject(BLACK_BRUSH)); return 1; }
    if (m == WM_MOUSEACTIVATE) return MA_NOACTIVATE;
    return DefWindowProcW(h, m, w, l);
}
static void registerHostClass() {
    static bool done = false; if (done) return; done = true;
    WNDCLASSEXW wc{}; wc.cbSize = sizeof wc; wc.lpfnWndProc = hostProc; wc.hInstance = GetModuleHandleW(nullptr); wc.lpszClassName = kHostClass; wc.hbrBackground = (HBRUSH)GetStockObject(BLACK_BRUSH);
    RegisterClassExW(&wc);
}

Controller::Controller() {
    WNDCLASSEXW wc{}; wc.cbSize = sizeof wc; wc.lpfnWndProc = timerProc; wc.hInstance = GetModuleHandleW(nullptr); wc.lpszClassName = L"GhostStyleWpTimer";
    RegisterClassExW(&wc);
    timerWin_ = CreateWindowExW(0, wc.lpszClassName, L"", 0, 0, 0, 0, 0, HWND_MESSAGE, nullptr, wc.hInstance, this);
    SetWindowLongPtrW(timerWin_, GWLP_USERDATA, (LONG_PTR)this);
    SetTimer(timerWin_, 1, 40, nullptr);
}
Controller::~Controller() { if (timerWin_) { KillTimer(timerWin_, 1); DestroyWindow(timerWin_); } stopAll(); }

LRESULT CALLBACK Controller::timerProc(HWND h, UINT m, WPARAM w, LPARAM l) {
    if (m == WM_TIMER) { auto* c = (Controller*)GetWindowLongPtrW(h, GWLP_USERDATA); if (c) c->onTimer(); return 0; }
    return DefWindowProcW(h, m, w, l);
}

static std::string sigMon(const std::vector<MonitorInfoLite>& mons) {
    std::string r;
    for (auto& m : mons) { char b[80]; std::snprintf(b, sizeof b, ";%ld,%ld,%ld,%ld", m.rect.left, m.rect.top, m.rect.right, m.rect.bottom); r += b; }
    return r;
}

static std::string sig(const Settings& s, const Wallpaper& w, const std::vector<MonitorInfoLite>& mons) {
    std::string r = w.id + "|" + w.background + "|";
    for (auto& kv : w.values) r += kv.first + "=" + kv.second + ",";
    r += "|" + s.Fit + "|" + std::to_string(s.Fps) + "|" + (s.Mute ? "1" : "0") + "|" + (s.AllMonitors ? "1" : "0") + "|" + (w.online ? "1" : "0") + "|";
    Wallpaper bg;
    if (w.overlay && !w.background.empty() && getWallpaper(w.background, bg)) { r += bg.id + ":"; for (auto& kv : bg.values) r += kv.first + "=" + kv.second + ","; r += bg.online ? "o" : "n"; }
    for (auto& m : mons) { char b[80]; std::snprintf(b, sizeof b, ";%ld,%ld,%ld,%ld", m.rect.left, m.rect.top, m.rect.right, m.rect.bottom); r += b; }
    return r;
}

void Controller::apply(const Settings& s) {
    settings_ = s;
    tel_.setConfig(settings_);
    if (busy_) return;
    busy_ = true;
    Wallpaper w;
    if (s.Active.empty() || !getWallpaper(s.Active, w)) {
        stopAll();
        setStatus(s.Active.empty() ? "Обои выключены." : "Выбранные обои не найдены.");
        busy_ = false; return;
    }
    auto mons = monitors();
    if (!s.AllMonitors && mons.size() > 1) mons.resize(1);
    std::string sg = sig(s, w, mons);
    bool alive = hosts_.size() == mons.size();
    for (auto& h : hosts_) alive = alive && stillAttached(h->hwnd);
    if (sg == signature_ && alive) { for (auto& h : hosts_) h->page.web.setMuted(s.Mute); busy_ = false; updateWanted(); return; }
    // тот же набор мониторов, сменились только обои или их настройки: страницы открываются заново в тех же окнах
    if (alive && !hosts_.empty() && sigMon(mons) == monSig_) {
        signature_ = sg;
        for (auto& h : hosts_) if (h->page.ready) h->page.navigateTo(w, s, LoadOpts{}, nullptr);
        setStatus("Обои «" + w.title + "» на рабочем столе.");
        busy_ = false; updateWanted(); return;
    }
    stopAll();
    signature_ = sg; monSig_ = sigMon(mons);
    ensureSystemFiles();
    registerHostClass();
    for (auto& m : mons) {
        auto h = std::make_unique<Host>();
        h->rect = m.rect;
        h->hwnd = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, kHostClass, L"Ghost Style wallpaper", WS_POPUP, -20000, -20000, 640, 360, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
        if (!h->hwnd) continue;
        attachToDesktop(h->hwnd, m.rect);
        Host* hp = h.get();
        hosts_.push_back(std::move(h));
        LoadOpts lo;
        hp->page.open(hp->hwnd, w, s, lo, &tel_, nullptr, [this](bool ok) { if (!ok) setStatus("Обои не запустились (нужен «Microsoft Edge WebView2 Runtime» — он есть в Windows 11)."); updateWanted(); });
    }
    log("обои «" + w.title + "» (" + w.id + ") на мониторах: " + std::to_string(hosts_.size()) + "; рабочий стол:\n" + dumpDesktop());
    setStatus("Обои «" + w.title + "» на рабочем столе.");
    busy_ = false;
}

void Controller::stopAll() {
    for (auto& h : hosts_) { h->page.web.close(); if (h->hwnd) DestroyWindow(h->hwnd); }
    bool had = !hosts_.empty();
    hosts_.clear(); signature_.clear();
    tel_.stopAll();
    if (had) repaintDesktop();
}

void Controller::updateWanted() {
    std::set<std::string> want;
    for (auto& h : hosts_) if (!h->paused && h->page.ready) for (auto& k : h->page.kinds) want.insert(k);
    tel_.setWanted(want);
}

bool Controller::allReady() const { if (hosts_.empty()) return false; for (auto& h : hosts_) if (!h->page.ready) return false; return true; }
bool Controller::anyFailed() const { for (auto& h : hosts_) if (h->page.failed) return true; return false; }
bool Controller::pausedAll() const { if (hosts_.empty()) return false; for (auto& h : hosts_) if (!h->paused) return false; return true; }
std::wstring Controller::lastErrors() const { std::wstring e; for (auto& h : hosts_) if (h->page.failed) e += h->page.error + L" "; return e; }
void Controller::executeOnFirst(const std::wstring& js, std::function<void(const std::string&)> done) {
    if (hosts_.empty()) { if (done) done(""); return; }
    hosts_[0]->page.web.execute(js, std::move(done));
}
std::string Controller::describe() const {
    std::string o;
    for (auto& h : hosts_) {
        RECT r{}; GetWindowRect(h->hwnd, &r);
        char b[200]; std::snprintf(b, sizeof b, "окно %p: родитель %s, место %ld,%ld %ldx%ld, видимо %d, страница %s\n", (void*)h->hwnd, h->hwnd && GetParent(h->hwnd) ? narrow(className(GetParent(h->hwnd))).c_str() : "-", r.left, r.top, r.right - r.left, r.bottom - r.top, IsWindowVisible(h->hwnd) ? 1 : 0, h->page.ready ? "готова" : "грузится");
        o += b;
    }
    return o;
}

void Controller::onTimer() {
    if (hosts_.empty()) return;
    tel_.pump([this](const char* kind, const std::string& m) { for (auto& h : hosts_) if (h->page.ready && !h->paused && h->page.kinds.count(kind)) h->page.web.postJson(m); });
    if (++tickN_ >= 25) { tickN_ = 0; slowTick(); }
}

static bool otherEngine(std::string& which) {
    static const char* names[] = {"wallpaper32.exe", "wallpaper64.exe", "lively.exe", "livelywpf.exe", "WallpaperPlus.exe"};
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return false;
    PROCESSENTRY32W pe{sizeof pe}; bool found = false;
    for (BOOL ok = Process32FirstW(snap, &pe); ok && !found; ok = Process32NextW(snap, &pe))
        for (auto n : names) if (_wcsicmp(pe.szExeFile, widen(n).c_str()) == 0) { which = n; found = true; }
    CloseHandle(snap);
    return found;
}

void Controller::slowTick() {
    if (hosts_.empty() || busy_) return;
    bool lost = false;
    for (auto& h : hosts_) if (!stillAttached(h->hwnd)) lost = true;
    if (lost) {   // Проводник перезапустился — окна рабочего стола пропали: поднимаем обои заново
        log("рабочий стол изменился (перезапуск Проводника?) — обои поднимаются заново");
        signature_.clear();
        Settings s = settings_;
        stopAll();
        apply(s);
        return;
    }
    bool full = settings_.PauseOnFullscreen && gs::fullscreenBusy();
    for (auto& h : hosts_) {
        bool covered = settings_.PauseWhenCovered && desktopCovered(h->rect);
        bool p = settings_.Paused || full || covered;
        if (p != h->paused && h->page.ready) { h->paused = p; h->page.web.suspend(p); }
    }
    updateWanted();
    std::string other;
    Wallpaper w; getWallpaper(settings_.Active, w);
    std::string text = pausedAll() ? "Пауза: обои не видны (полноэкранное окно) или поставлены на паузу." : "Обои «" + w.title + "» на рабочем столе.";
    if (otherEngine(other)) text += " Внимание: запущена другая программа с живыми обоями (" + other + ") — выключите её, иначе обои будут мигать.";
    setStatus(text);
}

}  // namespace wp
