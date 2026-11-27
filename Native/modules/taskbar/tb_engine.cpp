#include "tb_engine.hpp"
#include <shlobj.h>
#include <algorithm>
#include <cstdio>
#include <ctime>
#include <filesystem>
#include <sstream>
#include "json.hpp"
#include "net.hpp"
#include "tb_settings.hpp"
#include "win.hpp"

namespace fs = std::filesystem;

namespace tb {

using gs::narrow;
using gs::widen;

static const char* kEngineVersion = "7";   // должна совпадать с GS_ENGINE_VERSION в engine/common.h
static const char* kRulesUrl = "https://raw.githubusercontent.com/botsystemtioxsit/launcher/main/Native/modules/taskbar/engine/engine-rules.txt";
static const char* kReportUrl = "https://zolotaya-kletka-default-rtdb.firebaseio.com/pluginReports/taskbar-plus/";

static std::wstring pCfg() { return dataDir() + L"\\taskbar-plus-engine.cfg"; }
static std::wstring pStatus() { return dataDir() + L"\\taskbar-plus-engine-status.txt"; }
static std::wstring pLog() { return dataDir() + L"\\taskbar-plus-engine.log"; }
static std::wstring pTree() { return dataDir() + L"\\taskbar-plus-xaml-tree.txt"; }
static std::wstring pHealth() { return dataDir() + L"\\taskbar-plus-engine-health.json"; }
static std::wstring pRules() { return dataDir() + L"\\taskbar-plus-engine-rules.txt"; }
static std::wstring pGeometry() { return dataDir() + L"\\taskbar-plus-geometry.txt"; }
static std::wstring pRestartMarker() { return dataDir() + L"\\explorer-restart.txt"; }
static std::wstring engineDir() {
    wchar_t b[MAX_PATH * 2] = {}; DWORD n = GetEnvironmentVariableW(L"LOCALAPPDATA", b, (DWORD)std::size(b));
    return (n ? std::wstring(b, n) : L".") + L"\\GhostShell\\TaskbarPlus\\engine";
}

static std::string blockedText() { return "Движок отключён: Проводник два раза подряд закрывался сразу после подключения. Нажмите «Попробовать снова» или сохраните отчёт и отправьте его."; }

static bool readShared(const std::wstring& path, std::string& out) {
    HANDLE h = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) return false;
    out.clear(); char buf[8192]; DWORD n = 0;
    while (ReadFile(h, buf, sizeof buf, &n, nullptr) && n) out.append(buf, n);
    CloseHandle(h);
    return true;
}
static std::string safeRead(const std::wstring& path) { std::string s; return readShared(path, s) ? s : "(нет файла)"; }

static std::vector<std::pair<std::string, std::string>> readStatusFile(bool& ok) {
    std::vector<std::pair<std::string, std::string>> d; std::string t;
    ok = readShared(pStatus(), t);
    std::istringstream in(t); std::string line;
    while (std::getline(in, line)) { while (!line.empty() && (line.back() == '\r')) line.pop_back(); size_t i = line.find('='); if (i != std::string::npos && i > 0) d.push_back({line.substr(0, i), line.substr(i + 1)}); }
    return d;
}
static std::string getv(const std::vector<std::pair<std::string, std::string>>& d, const char* k) { for (auto& p : d) if (p.first == k) return p.second; return ""; }

// ---------- связь с цветом ----------
static std::wstring colorLinkPath() { return dataDir() + L"\\color-plus-taskbar.json"; }
// Свой цвет плашек, выбранный прямо в «Панели» (важнее цвета из вкладки «Цвет», чтобы одно не стирало другое)
static std::wstring ownColorPath() { return dataDir() + L"\\taskbar-own-color.json"; }
static std::mutex gLinkMu; static FILETIME gLinkStamp{}, gOwnStamp{}; static ColorLink gLink;
static bool readOwnColor(ColorLink& out) {
    std::string t; gs::Json j;
    if (!gs::readFile(ownColorPath(), t) || !gs::jsonParse(t, j) || j.t != gs::Json::Obj || !j.boolean("On")) return false;
    std::string c = j.str("Color", "#FF2A9D");
    if (c.size() != 7 || c[0] != '#') return false;
    out.active = true; out.mode = "solid"; out.color = c; out.opacity = std::clamp(j.num("Opacity", 85), 5, 100);
    return true;
}
bool colorLinkRefresh() {
    WIN32_FILE_ATTRIBUTE_DATA fa{}, fo{}; FILETIME stamp{}, ostamp{};
    bool exists = GetFileAttributesExW(colorLinkPath().c_str(), GetFileExInfoStandard, &fa) != 0;
    if (exists) stamp = fa.ftLastWriteTime;
    if (GetFileAttributesExW(ownColorPath().c_str(), GetFileExInfoStandard, &fo) != 0) ostamp = fo.ftLastWriteTime;
    std::lock_guard<std::mutex> g(gLinkMu);
    if (CompareFileTime(&stamp, &gLinkStamp) == 0 && CompareFileTime(&ostamp, &gOwnStamp) == 0) return false;
    gLinkStamp = stamp; gOwnStamp = ostamp;
    { ColorLink own; if (readOwnColor(own)) { gLink = own; return true; } }
    if (!exists) { gLink = ColorLink{}; return true; }
    std::string t; gs::Json j;
    if (!gs::readFile(colorLinkPath(), t) || !gs::jsonParse(t, j) || j.t != gs::Json::Obj) { gLinkStamp = FILETIME{}; return false; }
    ColorLink c;
    c.mode = j.str("BgMode", "default");
    if (c.mode != "default" && c.mode != "solid" && c.mode != "clear" && c.mode != "acrylic") c.mode = "default";
    c.color = j.str("BgColor", "#101018");
    auto hexOk = [](const std::string& s) { if (s.size() != 7 || s[0] != '#') return false; for (size_t i = 1; i < 7; i++) if (!isxdigit((unsigned char)s[i])) return false; return true; };
    if (!hexOk(c.color)) c.color = "#101018";
    c.opacity = std::clamp(j.num("BgOpacity", 60), 0, 100);
    c.active = j.boolean("Active"); c.hideStroke = j.boolean("HideStroke");
    gLink = c;
    return true;
}
ColorLink colorLinkCurrent() { colorLinkRefresh(); std::lock_guard<std::mutex> g(gLinkMu); return gLink; }

// ---------- что умеет эта машина ----------
static DWORD buildNumber() {
    typedef LONG (WINAPI* Fn)(OSVERSIONINFOEXW*);
    static DWORD b = 0;
    if (!b) { auto fn = (Fn)GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "RtlGetVersion"); OSVERSIONINFOEXW v{}; v.dwOSVersionInfoSize = sizeof v; if (fn && fn(&v) == 0) b = v.dwBuildNumber; }
    return b;
}
bool Engine::osSupported() { return buildNumber() >= 22000 && sizeof(void*) == 8; }
bool Engine::dllIncluded() { return FindResourceW(nullptr, L"GSTAPDLL", MAKEINTRESOURCEW(10)) != nullptr; }
std::string Engine::unavailableReason() {
    if (!osSupported()) return "Панель задач с формами работает только в Windows 11 (64-бит).";
    if (!dllIncluded()) return "В этой сборке нет файла движка.";
    return "";
}

Engine::Engine() { loadHealth(); { std::string t; if (gs::readFile(pRules(), t)) { std::istringstream in(t); std::string l; while (std::getline(in, l)) { while (!l.empty() && isspace((unsigned char)l.back())) l.pop_back(); if (!l.empty() && l[0] != '#' && std::count(l.begin(), l.end(), '|') >= 2 && rules_.size() < 100) rules_.push_back(l); } } } dumpCounter_ = (int)(GetTickCount() & 0xFFFFFF); }
Engine::~Engine() { shutdown(); if (rulesTh_.joinable()) rulesTh_.join(); }

void Engine::setState(const std::string& text, bool isReady) {
    bool fire;
    { std::lock_guard<std::mutex> g(mu_); fire = status_ != text || ready_ != isReady; status_ = text; ready_ = isReady; }
    if (fire && onChanged) onChanged();
}

void Engine::loadHealth() {
    std::string t; gs::Json j; health_ = Health{};
    if (gs::readFile(pHealth(), t) && gs::jsonParse(t, j) && j.t == gs::Json::Obj) {
        health_.crashes = j.num("Crashes"); health_.blocked = j.boolean("Blocked");
        if (auto* x = j.get("BlockedAtMs")) health_.blockedAtMs = (long long)x->n;
    }
}
void Engine::saveHealth() {
    gs::Json j = gs::Json::Object();
    j.set("Crashes", gs::Json::Number(health_.crashes)); j.set("Blocked", gs::Json::Boolean(health_.blocked)); j.set("BlockedAtMs", gs::Json::Number((double)health_.blockedAtMs));
    std::string out; gs::jsonDump(j, out); gs::makeDirs(dataDir()); gs::writeFileAtomic(pHealth(), out);
}
static long long nowMs() { FILETIME ft; GetSystemTimeAsFileTime(&ft); ULARGE_INTEGER u{}; u.LowPart = ft.dwLowDateTime; u.HighPart = ft.dwHighDateTime; return (long long)((u.QuadPart - 116444736000000000ULL) / 10000); }
bool Engine::blocked() {
    std::lock_guard<std::mutex> g(mu_);
    if (health_.blocked && health_.blockedAtMs > 0 && nowMs() - health_.blockedAtMs > 30LL * 60000) { health_ = Health{}; health_.crashes = 1; saveHealth(); }   // блокировка не вечная
    return health_.blocked;
}
void Engine::clearBlock() { { std::lock_guard<std::mutex> g(mu_); health_ = Health{}; } saveHealth(); }
void Engine::noteExplorerRestart() { expectedRestartUntil_ = GetTickCount64() + 40000; }

// ---------- управление ----------
void Engine::update(const Appearance& s) {
    { std::lock_guard<std::mutex> g(mu_); settings_ = s; }
    bool want = s.UseXaml && unavailableReason().empty();
    if (want && !blocked()) { writeCfg(true); start(); }
    else {
        bool wasRunning = run_;
        stop();
        barTransparent_ = false;
        DWORD a = GetFileAttributesW(pCfg().c_str());
        if (wasRunning || a != INVALID_FILE_ATTRIBUTES) writeCfg(false);
        std::string un = unavailableReason();
        setState(s.UseXaml ? (un.empty() ? blockedText() : un) : "", false);
    }
}

void Engine::start() {
    if (run_) return;
    run_ = true;
    th_ = std::thread([this] { loop(); });
    setState("Подключаюсь к панели задач…", false);
    if (rulesTh_.joinable()) rulesTh_.join();
    rulesTh_ = std::thread([this] { refreshRules(); });
}
void Engine::stop() { run_ = false; if (th_.joinable()) th_.join(); unhook(); }

void Engine::loop() {
    while (run_) {
        try { tick(); } catch (...) { setState("Движок: ошибка", false); }
        for (int i = 0; i < 10 && run_; i++) Sleep(100);
    }
}

void Engine::tick() {
    HWND hwnd = FindWindowW(L"Shell_TrayWnd", nullptr);
    if (!hwnd) { setState("Панель задач не найдена (Проводник перезапускается?)", false); return; }
    DWORD pid = 0; GetWindowThreadProcessId(hwnd, &pid);
    if (lastPid_ != 0 && pid != lastPid_) onExplorerChanged(pid);
    lastPid_ = pid;
    if (blocked()) { setState(blockedText(), false); return; }

    ULONGLONG now = GetTickCount64();
    // Раз в ~3 секунды переписываем файл настроек: движок считает его «пульсом» и без него (через 20 с) сам возвращает панель.
    if (colorLinkRefresh() || now - lastCfgWrite_ > 3000) writeCfg(true);
    bool ok = false; auto st = readStatusFile(ok);
    Appearance s; { std::lock_guard<std::mutex> g(mu_); s = settings_; }
    if (ok && getv(st, "pid") == std::to_string(pid)) {
        std::string state = getv(st, "state");
        long long tickv = std::atoll(getv(st, "tick").c_str());
        bool fresh = (long long)now - tickv < 12000;
        int bg = std::atoi(getv(st, "bgfill").c_str()), apps = std::atoi(getv(st, "apps").c_str()), nodes = std::atoi(getv(st, "nodes").c_str()), gbars = std::atoi(getv(st, "geobars").c_str());
        bool wantTransparent = s.Shape != "none";
        // Старая версия движка остаётся в Проводнике до его перезапуска; новая рядом с ней не запускается.
        if (state == "duplicate" || (state == "ready" && getv(st, "engine") != kEngineVersion)) {
            { std::lock_guard<std::mutex> g(mu_); needsRestart_ = true; }
            barTransparent_ = false;
            setState("Обновился движок. Чтобы подключить новую версию, нужно перезапустить панель задач (кнопка внизу окна).", false);
            return;
        }
        { std::lock_guard<std::mutex> g(mu_); needsRestart_ = false; }
        if (state == "ready" && fresh) {
            if (connectedSince_ == 0) connectedSince_ = now;
            unhook();
            barTransparent_ = bg > 0 && getv(st, "applied") == "1" && getv(st, "alive") == "1" && wantTransparent;
            if (bg > 0) {
                char b[200];
                if (gbars > 0) std::snprintf(b, sizeof b, "Движок работает. Значков на панели: %d.", apps);
                else if (wantTransparent && now - connectedSince_ > 10000) std::snprintf(b, sizeof b, "Движок подключился, но не нашёл значки панели (элементов %d). Отчёт уйдёт разработчику сам.", nodes);
                else std::snprintf(b, sizeof b, "Движок работает. Элементов панели: %d.", nodes);
                setState(b, true);
                if (now - connectedSince_ > 90000 && health_.crashes != 0) { health_.crashes = 0; saveHealth(); }
                if (!autoReported_ && now - connectedSince_ > 45000) { autoReported_ = true; if (s.AutoReport) uploadReport(); }
            } else if (now - connectedSince_ > 10000) {
                char b[400]; std::snprintf(b, sizeof b, "Движок подключился (окон XAML: %s, элементов %d, значков %d, системная область %s, виджеты %s), но фон панели не нашёл. Панель остаётся как в Windows; отчёт уйдёт разработчику сам.",
                    getv(st, "sessions").c_str(), nodes, apps, getv(st, "system").c_str(), getv(st, "widgets").c_str());
                setState(b, false);
            } else setState("Подключился, ищу элементы панели…", false);
            return;
        }
        barTransparent_ = false;
        if (state == "error") {
            std::string e = getv(st, "error"); setState("Движок: " + (e.empty() ? "ошибка" : e) + ".", false);
            if (attempts_ >= 3) return;
        } else if (state == "connecting" && fresh && now - injectAt_ < 60000) { setState("Подключаюсь к панели задач…", false); return; }
    }
    // движка в этом Проводнике нет (или он не отвечает) — подключаем
    barTransparent_ = false;
    if (injectedPid_ == pid && attempts_ >= 3 && now - injectAt_ < 120000) return;
    if (injectedPid_ != pid) attempts_ = 0;
    inject(hwnd, pid);
}

static bool markerRecent() {
    WIN32_FILE_ATTRIBUTE_DATA fa{};
    if (!GetFileAttributesExW(pRestartMarker().c_str(), GetFileExInfoStandard, &fa)) return false;
    FILETIME now; GetSystemTimeAsFileTime(&now);
    ULARGE_INTEGER a{}, b{}; a.LowPart = now.dwLowDateTime; a.HighPart = now.dwHighDateTime; b.LowPart = fa.ftLastWriteTime.dwLowDateTime; b.HighPart = fa.ftLastWriteTime.dwHighDateTime;
    return a.QuadPart > b.QuadPart && (a.QuadPart - b.QuadPart) < 90ULL * 10000000ULL;
}

void Engine::onExplorerChanged(DWORD) {
    ULONGLONG now = GetTickCount64();
    bool expected = now < expectedRestartUntil_ || markerRecent();
    bool soon = injectedPid_ == lastPid_ && injectAt_ != 0 && now - injectAt_ < 90000;
    if (!expected && soon && !crashCounted_) {
        std::lock_guard<std::mutex> g(mu_);
        health_.crashes++;
        if (health_.crashes >= 2) { health_.blocked = true; health_.blockedAtMs = nowMs(); }
        saveHealth();
    }
    crashCounted_ = false; connectedSince_ = 0; attempts_ = 0;
    unhook();
    setState("Проводник перезапустился, подключаюсь заново…", false);
}

// DLL вложена в exe; достаём её в папку с именем по содержимому — после обновления появится новый файл, а старый, пока он нужен Проводнику, никто не трогает.
static std::wstring extractDll() {
    HRSRC r = FindResourceW(nullptr, L"GSTAPDLL", MAKEINTRESOURCEW(10));
    if (!r) return L"";
    HGLOBAL g = LoadResource(nullptr, r); DWORD sz = SizeofResource(nullptr, r); const unsigned char* data = (const unsigned char*)LockResource(g);
    if (!data || !sz) return L"";
    unsigned long long h = 1469598103934665603ULL;
    for (DWORD i = 0; i < sz; i++) { h ^= data[i]; h *= 1099511628211ULL; }
    char name[64]; std::snprintf(name, sizeof name, "GhostShellTap-%012llx.dll", h & 0xFFFFFFFFFFFFULL);
    std::wstring dir = engineDir(), path = dir + L"\\" + widen(name);
    gs::makeDirs(dir);
    if (GetFileAttributesW(path.c_str()) == INVALID_FILE_ATTRIBUTES) {
        gs::writeFileAtomic(path, std::string((const char*)data, sz));
        std::error_code ec;
        for (fs::directory_iterator it(dir, ec), end; !ec && it != end; it.increment(ec)) {
            std::wstring n = it->path().filename().wstring();
            if (n.rfind(L"GhostShellTap-", 0) == 0 && it->path().wstring() != path) fs::remove(it->path(), ec);   // ещё нужен Проводнику — не удалится
        }
    }
    return path;
}

void Engine::inject(HWND hwnd, DWORD pid) {
    std::wstring dll = extractDll();
    if (dll.empty()) { std::string u = unavailableReason(); setState(u.empty() ? "Не удалось достать файл движка" : u, false); return; }
    writeCfg(true);   // к моменту подключения настройки уже лежат на месте
    DWORD tid = GetWindowThreadProcessId(hwnd, nullptr);
    if (!hookModule_) hookModule_ = LoadLibraryW(dll.c_str());
    if (!hookModule_) { setState("Не удалось загрузить файл движка (ошибка " + std::to_string(GetLastError()) + ")", false); return; }
    HOOKPROC proc = (HOOKPROC)(void*)GetProcAddress(hookModule_, "GhostShellHookProc");
    if (!proc) { setState("В файле движка нет нужной точки входа", false); return; }
    unhook();
    hook_ = SetWindowsHookExW(WH_CALLWNDPROC, proc, hookModule_, tid);
    if (!hook_) { setState("Не удалось подключиться к панели задач (ошибка " + std::to_string(GetLastError()) + ")", false); return; }
    DWORD_PTR res; SendMessageTimeoutW(hwnd, WM_NULL, 0, 0, SMTO_ABORTIFHUNG, 2000, &res);   // любое сообщение окну панели запускает хук
    injectedPid_ = pid; injectAt_ = GetTickCount64(); attempts_++;
    setState("Подключаюсь к панели задач…", false);
}
void Engine::unhook() { if (hook_) { UnhookWindowsHookEx(hook_); hook_ = nullptr; } }

// ---------- правила: какой элемент панели к какой группе относится ----------
// Встроенные правила лежат в самом движке. Файл engine-rules.txt в репозитории позволяет подправить названия элементов под другую
// сборку Windows без нового выпуска: приложение скачивает его при запуске и кладёт в настройки движка.
void Engine::refreshRules() {
    std::string text, err;
    if (!gs::httpFetch("GET", std::string(kRulesUrl) + "?_=" + std::to_string(GetTickCount64()), "", "", &text, nullptr, 200000, err)) return;
    std::vector<std::string> parsed; std::istringstream in(text); std::string l;
    while (std::getline(in, l)) {
        while (!l.empty() && isspace((unsigned char)l.back())) l.pop_back();
        size_t a = 0; while (a < l.size() && isspace((unsigned char)l[a])) a++;
        l = l.substr(a);
        if (l.empty() || l[0] == '#' || std::count(l.begin(), l.end(), '|') < 2) continue;
        std::string kind = l.substr(0, l.find('|'));
        if (kind == "bgfill" || kind == "bgstroke" || kind == "app" || kind == "widgets" || kind == "system" || kind == "frame") if (parsed.size() < 100) parsed.push_back(l);
    }
    bool changed;
    { std::lock_guard<std::mutex> g(mu_); changed = rules_ != parsed; rules_ = parsed; }
    gs::makeDirs(dataDir()); gs::writeFileAtomic(pRules(), text);
    if (changed && run_) writeCfg(true);
}

// ---------- файлы обмена с DLL ----------
void Engine::writeCfg(bool enabled) {
    Appearance s; std::vector<std::string> rs;
    { std::lock_guard<std::mutex> g(mu_); s = settings_; rs = rules_; }
    std::string o;
    o += std::string("enabled=") + (enabled && s.Shape != "none" ? "1" : "0") + "\n";
    o += std::string("hideStroke=") + (s.ShowStroke ? "0" : "1") + "\n";    // тонкая линия по краю панели скрыта, если её не просили вернуть
    o += "dump=" + std::to_string(dumpCounter_) + "\n";
    for (size_t i = 0; i < rs.size(); i++) o += "rule." + std::to_string(i) + "=" + rs[i] + "\n";
    gs::makeDirs(dataDir());
    if (gs::writeFileAtomic(pCfg(), o)) lastCfgWrite_ = GetTickCount64();
}

void Engine::shutdown() {
    bool had = run_ || ready();
    stop();
    if (had || GetFileAttributesW(pCfg().c_str()) != INVALID_FILE_ATTRIBUTES) {
        writeCfg(false);
        for (int i = 0; i < 12; i++) {
            Sleep(150);
            bool ok = false; auto st = readStatusFile(ok);
            if (!ok || getv(st, "applied") == "0") break;
        }
    }
    barTransparent_ = false;
}

// ---------- отчёт для разбора ----------
std::string Engine::engineLogTail() {
    std::string t = safeRead(pLog());
    std::vector<std::string> lines; std::istringstream in(t); std::string l;
    while (std::getline(in, l)) lines.push_back(l);
    std::string out; size_t from = lines.size() > 80 ? lines.size() - 80 : 0;
    for (size_t i = from; i < lines.size(); i++) out += lines[i] + "\n";
    return out;
}

std::string Engine::buildReport() {
    dumpCounter_++;
    Appearance s; { std::lock_guard<std::mutex> g(mu_); s = settings_; }
    if (ready() || run_) writeCfg(s.UseXaml);
    Sleep(1500);   // движок читает файл настроек примерно раз в 0,3 с
    std::string r;
    r += "Taskbar Plus (Ghost Style) — отчёт\n";
    SYSTEMTIME t; GetLocalTime(&t); char tb[64]; std::snprintf(tb, sizeof tb, "%04d-%02d-%02d %02d:%02d:%02d", t.wYear, t.wMonth, t.wDay, t.wHour, t.wMinute, t.wSecond);
    r += std::string("Время: ") + tb + "\n";
    r += "Windows: сборка " + std::to_string(buildNumber()) + "\n";
    r += "Состояние: " + status() + "\n\n--- настройки ---\n" + appearanceJson(s) + "\n\n--- статус движка ---\n" + safeRead(pStatus()) + "\n";
    r += "--- измерения значков (файл движка) ---\n" + safeRead(pGeometry()) + "\n--- окно с плашками ---\n";
    try { r += debugText ? debugText() : "(нет)"; } catch (...) { r += "(ошибка)"; }
    r += "\n--- здоровье ---\n" + safeRead(pHealth()) + "\n--- журнал движка (последние строки) ---\n" + engineLogTail() + "--- дерево элементов панели ---\n" + safeRead(pTree()) + "\n";
    return r;
}

bool Engine::saveReport(std::wstring& path) {
    std::string text = buildReport();
    wchar_t desk[MAX_PATH] = {};
    if (FAILED(SHGetFolderPathW(nullptr, CSIDL_DESKTOPDIRECTORY, nullptr, 0, desk))) return false;
    path = std::wstring(desk) + L"\\TaskbarPlus-report.txt";
    return gs::writeFileAtomic(path, "\xEF\xBB\xBF" + text);
}

static std::string sanitize(std::string text) {
    auto repl = [&](const std::string& what, const std::string& with) {
        if (what.size() < 3) return;
        size_t i = 0;
        std::string lw = what, lt;
        for (auto& c : lw) c = (char)tolower((unsigned char)c);
        lt = text; for (auto& c : lt) c = (char)tolower((unsigned char)c);
        while ((i = lt.find(lw, i)) != std::string::npos) { text.replace(i, what.size(), with); lt.replace(i, what.size(), std::string(with.size(), '\x01')); i += with.size(); }
    };
    wchar_t b[MAX_PATH]; DWORD n = MAX_PATH;
    if (GetEnvironmentVariableW(L"USERPROFILE", b, MAX_PATH)) repl(narrow(b), "%USERPROFILE%");
    if (GetUserNameW(b, &n)) repl(narrow(b), "user");
    n = MAX_PATH; if (GetComputerNameW(b, &n)) repl(narrow(b), "pc");
    return text;
}

std::string Engine::uploadReport() {
    std::string text = sanitize(buildReport());
    if (text.size() > 180000) text = text.substr(0, 180000) + "\n…(обрезано)";
    wchar_t cn[MAX_PATH], un[MAX_PATH]; DWORD a = MAX_PATH, b = MAX_PATH; GetComputerNameW(cn, &a); GetUserNameW(un, &b);
    std::string key = narrow(cn) + "|" + narrow(un);
    unsigned long long h = 1469598103934665603ULL; for (unsigned char c : key) { h ^= c; h *= 1099511628211ULL; }
    char id[24]; std::snprintf(id, sizeof id, "%08llx", h & 0xFFFFFFFFULL);
    gs::Json j = gs::Json::Object();
    SYSTEMTIME t; GetSystemTime(&t); char at[48]; std::snprintf(at, sizeof at, "%04d-%02d-%02dT%02d:%02d:%02dZ", t.wYear, t.wMonth, t.wDay, t.wHour, t.wMinute, t.wSecond);
    j.set("at", gs::Json::String(at)); j.set("plugin", gs::Json::String("ghost-style")); j.set("windows", gs::Json::String(std::to_string(buildNumber()))); j.set("report", gs::Json::String(text));
    std::string body; gs::jsonDump(j, body);
    std::string e2;
    return gs::httpFetch("PUT", std::string(kReportUrl) + id + ".json", body, "application/json", nullptr, nullptr, 100000, e2) ? "" : (e2.empty() ? "не отправилось" : e2);
}

}  // namespace tb
