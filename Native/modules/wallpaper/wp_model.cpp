#include "wp_model.hpp"
#include <windows.h>
#include <objbase.h>
#include <winhttp.h>
#include <algorithm>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <set>
#include "json.hpp"
#include "win.hpp"

namespace fs = std::filesystem;

namespace gs { struct EmbFile { const char* path; const unsigned char* data; size_t size; }; extern const EmbFile kWpAssets[]; }

namespace wp {

using gs::Json;
using gs::narrow;
using gs::widen;

static const long long kMaxTotal = 1200000000LL, kMaxFile = 700000000LL;
static const int kMaxFiles = 3000;

// ---------- пути и журнал ----------
static std::wstring localAppData() {
    wchar_t b[MAX_PATH * 2] = {};
    DWORD n = GetEnvironmentVariableW(L"LOCALAPPDATA", b, (DWORD)std::size(b));
    return n ? std::wstring(b, n) : L".";
}
std::wstring dataDir() { return localAppData() + L"\\GhostShell\\WallpaperPlus"; }
std::wstring libraryDir() { return dataDir() + L"\\library"; }
std::wstring downloadsDir() { return dataDir() + L"\\downloads"; }
std::wstring systemDir() { return dataDir() + L"\\system"; }
static std::wstring settingsFile() { return gs::appDataDir() + L"\\wallpaper-plus-settings.json"; }

void log(const std::string& text) {
    static CRITICAL_SECTION cs; static bool init = false;
    if (!init) { InitializeCriticalSection(&cs); init = true; }
    EnterCriticalSection(&cs);
    std::wstring f = dataDir() + L"\\log.txt";
    gs::makeDirs(dataDir());
    std::error_code ec;
    if (fs::exists(f, ec) && fs::file_size(f, ec) > 400000) fs::remove(f, ec);
    SYSTEMTIME t; GetLocalTime(&t);
    char head[40]; std::snprintf(head, sizeof head, "%02d:%02d:%02d.%03d  ", t.wHour, t.wMinute, t.wSecond, t.wMilliseconds);
    HANDLE h = CreateFileW(f.c_str(), FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h != INVALID_HANDLE_VALUE) { std::string line = head + text + "\r\n"; DWORD w; WriteFile(h, line.data(), (DWORD)line.size(), &w, nullptr); CloseHandle(h); }
    LeaveCriticalSection(&cs);
}

// ---------- модель ----------
std::string Wallpaper::paramQuery() const {
    auto enc = [](const std::string& s) {
        std::string o; char b[4];
        for (unsigned char c : s) { if (isalnum(c) || c == '-' || c == '_' || c == '.' || c == '~') o += (char)c; else { std::snprintf(b, sizeof b, "%%%02X", c); o += b; } }
        return o;
    };
    std::string q;
    for (auto& p : params) {
        auto it = values.find(p.key);
        std::string v = it != values.end() && !it->second.empty() ? it->second : p.def;
        if (!q.empty()) q += '&';
        q += enc(p.key) + "=" + enc(v);
    }
    return q;
}
const char* Wallpaper::typeLabel() const {
    if (type == "video") return "видео"; if (type == "gif") return "анимация GIF"; if (type == "image") return "картинка"; return "страница";
}

static std::string jstr(const Json& j) {
    if (j.t == Json::Str) return j.s;
    if (j.t == Json::Num) { char b[40]; if (j.n == std::floor(j.n)) std::snprintf(b, sizeof b, "%.0f", j.n); else std::snprintf(b, sizeof b, "%g", j.n); return b; }
    if (j.t == Json::Bool) return j.b ? "true" : "false";
    return "";
}

static Wallpaper fromJson(const Json& j) {
    Wallpaper w;
    w.id = j.str("Id"); w.title = j.str("Title"); w.type = j.str("Type", "html"); w.entry = j.str("Entry", "index.html");
    w.description = j.str("Description"); w.author = j.str("Author"); w.source = j.str("Source"); w.version = j.num("Version", 1);
    w.online = j.boolean("Online"); w.tmpl = j.boolean("Template"); w.overlay = j.boolean("Overlay"); w.background = j.str("Background");
    if (auto* ps = j.get("Params")) if (ps->t == Json::Arr) for (auto& pj : ps->a) {
        Param p; p.key = pj.str("Key"); p.label = pj.str("Label"); p.type = pj.str("Type", "range");
        if (auto* o = pj.get("Options")) if (o->t == Json::Arr) for (auto& x : o->a) p.options.push_back(jstr(x));
        if (auto* m = pj.get("Min")) p.min = m->n; if (auto* m = pj.get("Max")) p.max = m->n; if (auto* m = pj.get("Step")) p.step = m->n;
        if (auto* d = pj.get("Default")) p.def = jstr(*d);
        if (!p.key.empty()) w.params.push_back(p);
    }
    if (auto* vs = j.get("Values")) if (vs->t == Json::Obj) for (auto& kv : vs->o) w.values[kv.first] = jstr(kv.second);
    if (auto* ts = j.get("Telemetry")) if (ts->t == Json::Arr) for (auto& x : ts->a) if (x.t == Json::Str) w.telemetry.push_back(x.s);
    return w;
}

static Json toJson(const Wallpaper& w) {
    Json j = Json::Object();
    j.set("Id", Json::String(w.id)); j.set("Title", Json::String(w.title)); j.set("Type", Json::String(w.type)); j.set("Entry", Json::String(w.entry));
    j.set("Description", Json::String(w.description)); j.set("Author", Json::String(w.author)); j.set("Source", Json::String(w.source));
    j.set("Version", Json::Number(w.version)); j.set("Online", Json::Boolean(w.online));
    Json ps = Json::Array();
    for (auto& p : w.params) {
        Json pj = Json::Object();
        pj.set("Key", Json::String(p.key)); pj.set("Label", Json::String(p.label)); pj.set("Type", Json::String(p.type));
        Json o = Json::Array(); for (auto& x : p.options) o.a.push_back(Json::String(x));
        pj.set("Options", o); pj.set("Min", Json::Number(p.min)); pj.set("Max", Json::Number(p.max)); pj.set("Step", Json::Number(p.step)); pj.set("Default", Json::String(p.def));
        ps.a.push_back(pj);
    }
    j.set("Params", ps);
    Json vs = Json::Object(); for (auto& kv : w.values) vs.set(kv.first, Json::String(kv.second));
    j.set("Values", vs);
    j.set("Template", Json::Boolean(w.tmpl)); j.set("Overlay", Json::Boolean(w.overlay)); j.set("Background", Json::String(w.background));
    Json ts = Json::Array(); for (auto& x : w.telemetry) ts.a.push_back(Json::String(x));
    j.set("Telemetry", ts);
    return j;
}

// ---------- настройки ----------
void Settings::normalize() {
    if (Fit != "cover" && Fit != "contain" && Fit != "fill") Fit = "cover";
    Fps = std::clamp(Fps, 15, 60);
}
Settings Settings::load() {
    Settings s; std::string t; Json j;
    if (gs::readFile(settingsFile(), t) && gs::jsonParse(t, j) && j.t == Json::Obj) {
        s.Active = j.str("Active"); s.AllMonitors = j.boolean("AllMonitors", s.AllMonitors); s.PauseOnFullscreen = j.boolean("PauseOnFullscreen", s.PauseOnFullscreen);
        s.PauseWhenCovered = j.boolean("PauseWhenCovered", s.PauseWhenCovered); s.Mute = j.boolean("Mute", s.Mute); s.Fit = j.str("Fit", s.Fit);
        s.Fps = j.num("Fps", s.Fps); s.Autostart = j.boolean("Autostart"); s.Paused = j.boolean("Paused"); s.TelAudio = j.boolean("TelAudio", true);
        s.TelJournal = j.boolean("TelJournal", true); s.WeatherQuery = j.str("WeatherQuery"); s.WeatherName = j.str("WeatherName");
        if (auto* x = j.get("WeatherLat")) s.WeatherLat = x->n; if (auto* x = j.get("WeatherLon")) s.WeatherLon = x->n;
    }
    s.normalize();
    return s;
}
void Settings::save() const {
    Json j = Json::Object();
    j.set("Active", Json::String(Active)); j.set("AllMonitors", Json::Boolean(AllMonitors)); j.set("PauseOnFullscreen", Json::Boolean(PauseOnFullscreen));
    j.set("PauseWhenCovered", Json::Boolean(PauseWhenCovered)); j.set("Mute", Json::Boolean(Mute)); j.set("Fit", Json::String(Fit)); j.set("Fps", Json::Number(Fps));
    j.set("Autostart", Json::Boolean(Autostart)); j.set("Paused", Json::Boolean(Paused)); j.set("TelAudio", Json::Boolean(TelAudio)); j.set("TelJournal", Json::Boolean(TelJournal));
    j.set("WeatherQuery", Json::String(WeatherQuery)); j.set("WeatherName", Json::String(WeatherName));
    j.set("WeatherLat", Json::Number(WeatherLat)); j.set("WeatherLon", Json::Number(WeatherLon));
    std::string out; gs::jsonDump(j, out);
    gs::makeDirs(gs::appDataDir());
    gs::writeFileAtomic(settingsFile(), out);
}

// ---------- библиотека ----------
static bool idOk(const std::string& id) {
    if (id.empty() || id.size() > 64) return false;
    for (unsigned char c : id) if (!(isalnum(c) || c == '_' || c == '-')) return false;
    return true;
}
static long long dirSize(const fs::path& d) {
    long long n = 0; std::error_code ec;
    for (fs::recursive_directory_iterator it(d, ec), end; !ec && it != end; it.increment(ec)) if (it->is_regular_file(ec)) n += (long long)it->file_size(ec);
    return n;
}
static bool readDir(const fs::path& dir, Wallpaper& w) {
    std::string t; Json j;
    if (!gs::readFile((dir / L"manifest.json").wstring(), t) || !gs::jsonParse(t, j) || j.t != Json::Obj) return false;
    w = fromJson(j);
    if (w.id != narrow(dir.filename().wstring()) || !idOk(w.id)) return false;
    if (w.type != "html" && w.type != "video" && w.type != "gif" && w.type != "image") w.type = "html";
    std::error_code ec;
    if (!fs::exists(dir / fs::path(widen(w.entry)), ec)) return false;
    w.dir = dir.wstring();
    w.sizeBytes = dirSize(dir);
    return true;
}
bool getWallpaper(const std::string& id, Wallpaper& out) {
    if (!idOk(id)) return false;
    return readDir(fs::path(libraryDir()) / widen(id), out);
}
std::vector<Wallpaper> listAll() {
    std::vector<Wallpaper> list; std::error_code ec;
    for (fs::directory_iterator it(libraryDir(), ec), end; !ec && it != end; it.increment(ec)) {
        Wallpaper w;
        if (it->is_directory(ec) && readDir(it->path(), w)) list.push_back(w);
    }
    std::sort(list.begin(), list.end(), [](const Wallpaper& a, const Wallpaper& b) {
        if (a.builtin() != b.builtin()) return a.builtin();
        return _stricmp(a.title.c_str(), b.title.c_str()) < 0;
    });
    return list;
}
void saveWallpaper(const Wallpaper& w) {
    std::string out; gs::jsonDump(toJson(w), out);
    gs::writeFileAtomic(libraryDir() + L"\\" + widen(w.id) + L"\\manifest.json", out);
}
void removeWallpaper(const std::string& id) {
    Wallpaper w;
    if (!getWallpaper(id, w) || w.builtin()) return;
    std::error_code ec; fs::remove_all(w.dir, ec);
    if (ec) log("не удалось удалить " + id + ": " + ec.message());
}

// ---------- вшитые файлы ----------
std::string readAsset(const char* path) {
    for (const gs::EmbFile* f = gs::kWpAssets; f->path; f++) if (!strcmp(f->path, path)) return std::string((const char*)f->data, f->size);
    return "";
}
void ensureSystemFiles() {
    gs::makeDirs(systemDir());
    std::string text = readAsset("res/compose.html"), cur;
    std::wstring f = systemDir() + L"\\compose.html";
    if (!text.empty() && (!gs::readFile(f, cur) || cur != text)) gs::writeFileAtomic(f, text);
}

static void dropTerminalBackdrop(const fs::path& dir) {
    std::string t; Json j; std::wstring mf = (dir / L"manifest.json").wstring();
    if (!gs::readFile(mf, t) || !gs::jsonParse(t, j)) return;
    Wallpaper w = fromJson(j);
    bool changed = false;
    for (size_t i = w.params.size(); i-- > 0;) if (w.params[i].key == "bgc" || w.params[i].key == "fill") { w.params.erase(w.params.begin() + i); changed = true; }
    for (const char* k : {"bgc", "fill"}) changed |= w.values.erase(k) > 0;
    for (auto& p : w.params) if (p.key == "scan" && p.def != "0") { p.def = "0"; changed = true; }
    auto it = w.values.find("scan");
    if (it != w.values.end() && it->second == "0.5") { w.values.erase(it); changed = true; }
    if (changed) saveWallpaper(w);
}

// Свои обои из «Терминала» — копии его папки: код страницы обновляется вместе с программой, манифест и раскладка остаются.
static void refreshTerminalCopies(const std::string& version) {
    std::error_code ec;
    fs::path src = fs::path(libraryDir()) / L"b-terminal-hud";
    if (!fs::exists(src, ec)) return;
    for (fs::directory_iterator it(libraryDir(), ec), end; !ec && it != end; it.increment(ec)) {
        std::string name = narrow(it->path().filename().wstring());
        if (name.rfind("u-", 0) != 0 || !fs::exists(it->path() / L"hud.js", ec)) continue;
        std::string mark; std::wstring mf = (it->path() / L".hud-version").wstring();
        if (gs::readFile(mf, mark) && mark == version) continue;
        for (const wchar_t* f : {L"index.html", L"hud.css", L"hud.js"}) fs::copy_file(src / f, it->path() / f, fs::copy_options::overwrite_existing, ec);
        if (fs::exists(src / L"fonts", ec)) {
            fs::create_directories(it->path() / L"fonts", ec);
            for (fs::directory_iterator f(src / L"fonts", ec), e2; !ec && f != e2; f.increment(ec)) fs::copy_file(f->path(), it->path() / L"fonts" / f->path().filename(), fs::copy_options::overwrite_existing, ec);
        }
        dropTerminalBackdrop(it->path());
        gs::writeFileAtomic(mf, version);
    }
}

void ensureBuiltins(const std::string& version) {
    gs::makeDirs(libraryDir()); gs::makeDirs(downloadsDir());
    std::set<std::string> names;
    for (const gs::EmbFile* f = gs::kWpAssets; f->path; f++) {
        std::string p = f->path;
        if (p.rfind("wp/", 0) != 0) continue;
        size_t s2 = p.find('/', 3); if (s2 == std::string::npos) continue;
        names.insert(p.substr(3, s2 - 3));
    }
    for (auto& name : names) {
        std::string id = "b-" + name;
        fs::path dir = fs::path(libraryDir()) / widen(id);
        std::string stamp;
        if (gs::readFile((dir / L".builtin-version").wstring(), stamp) && stamp == version && fs::exists(dir / L"manifest.json")) continue;
        std::string prefix = "wp/" + name + "/";
        for (const gs::EmbFile* f = gs::kWpAssets; f->path; f++) {
            std::string p = f->path;
            if (p.rfind(prefix, 0) != 0) continue;
            fs::path dest = dir / fs::path(widen(p.substr(prefix.size())));
            std::error_code ec; fs::create_directories(dest.parent_path(), ec);
            gs::writeFileAtomic(dest.wstring(), std::string((const char*)f->data, f->size));
        }
        std::string t; Json j;
        if (gs::readFile((dir / L"manifest.json").wstring(), t) && gs::jsonParse(t, j) && j.t == Json::Obj) {
            Wallpaper w = fromJson(j); w.id = id; w.source = "встроенные"; saveWallpaper(w);
        }
        gs::writeFileAtomic((dir / L".builtin-version").wstring(), version);
    }
    refreshTerminalCopies(version);
}

// ---------- установка ----------
static const std::set<std::string> kAllowed = {
    ".html", ".htm", ".js", ".mjs", ".css", ".json", ".txt", ".xml", ".map", ".glsl", ".frag", ".vert", ".svg",
    ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".ico", ".avif",
    ".mp4", ".webm", ".m4v", ".ogv", ".mov", ".mp3", ".ogg", ".wav", ".m4a", ".flac",
    ".woff", ".woff2", ".ttf", ".otf", ".eot", ".glb", ".gltf", ".bin", ".obj", ".mtl", ".hdr", ".dds", ".ktx2"};
static std::string lowerExt(const fs::path& p) { std::string e = narrow(p.extension().wstring()); for (auto& c : e) c = (char)tolower((unsigned char)c); return e; }
static bool isVideo(const std::string& e) { return e == ".mp4" || e == ".webm" || e == ".m4v" || e == ".ogv" || e == ".mov"; }
static bool isImage(const std::string& e) { return e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".webp" || e == ".bmp" || e == ".avif"; }

static std::string safeName(const std::string& n) {
    std::string o;
    for (unsigned char c : n) o += (isalnum(c) || c >= 0x80 || c == '.' || c == '_' || c == '-' || c == ' ') ? (char)c : '_';
    while (!o.empty() && o.back() == ' ') o.pop_back();
    return o.empty() ? "file" : o;
}

static Wallpaper newWallpaper(std::string title, const std::string& type, const std::string& source) {
    while (!title.empty() && title.back() == ' ') title.pop_back();
    if (title.empty()) title = "Обои";
    if (title.size() > 60) { size_t cut = 60; while (cut > 0 && ((unsigned char)title[cut] & 0xC0) == 0x80) cut--; title.resize(cut); }
    std::string slug; bool dash = false;
    for (unsigned char c : title) { if (isalnum(c) && c < 0x80) { slug += (char)tolower(c); dash = false; } else if (!dash) { slug += '-'; dash = true; } }
    while (!slug.empty() && slug.front() == '-') slug.erase(0, 1);
    while (!slug.empty() && slug.back() == '-') slug.pop_back();
    if (slug.size() > 20) slug.resize(20);
    while (!slug.empty() && slug.back() == '-') slug.pop_back();
    GUID g; CoCreateGuid(&g);
    char hash[16]; std::snprintf(hash, sizeof hash, "%02x%02x%02x", (unsigned)(g.Data1 & 255), (unsigned)((g.Data1 >> 8) & 255), (unsigned)(g.Data4[0]));
    Wallpaper w; w.id = std::string("u-") + (slug.empty() ? "" : slug + "-") + hash; w.title = title; w.type = type; w.source = source;
    w.dir = libraryDir() + L"\\" + widen(w.id);
    return w;
}

static Result done(const Wallpaper& w, const std::string& msg) {
    Wallpaper r;
    if (!getWallpaper(w.id, r)) return {false, "", "Обои не прочитались после установки."};
    return {true, r.id, msg};
}

static Result installSingle(const std::wstring& file, const std::string& type, const std::string& title, const std::string& source) {
    std::error_code ec;
    if ((long long)fs::file_size(file, ec) > kMaxFile) return {false, "", "Файл слишком большой (больше 700 МБ)."};
    Wallpaper w = newWallpaper(title, type, source);
    fs::create_directories(w.dir, ec);
    std::string name = safeName(narrow(fs::path(file).filename().wstring()));
    fs::copy_file(file, fs::path(w.dir) / widen(name), fs::copy_options::overwrite_existing, ec);
    if (ec) { fs::remove_all(w.dir, ec); return {false, "", "Не получилось скопировать файл: " + ec.message()}; }
    if (type == "html") w.entry = name;
    else {
        w.entry = "_player.html"; w.description = name;
        gs::writeFileAtomic(w.dir + L"\\_player.html", readAsset("res/_player.html"));
        gs::writeFileAtomic(w.dir + L"\\_file.txt", name);
    }
    saveWallpaper(w);
    return done(w, "Добавлено: " + w.title);
}

std::string mediaFile(const Wallpaper& w) {
    std::string t;
    if (gs::readFile(w.dir + L"\\_file.txt", t)) { while (!t.empty() && (t.back() == '\r' || t.back() == '\n' || t.back() == ' ')) t.pop_back(); if (!t.empty()) return t; }
    return w.description;
}

struct CopyOut { int copied = 0, skipped = 0; std::string error; };
static CopyOut copyFolder(const fs::path& from, const fs::path& to) {
    CopyOut r; long long total = 0; std::error_code ec;
    for (fs::recursive_directory_iterator it(from, fs::directory_options::skip_permission_denied, ec), end; !ec && it != end; it.increment(ec)) {
        if (it->is_symlink(ec) || !it->is_regular_file(ec)) continue;
        fs::path rel = fs::relative(it->path(), from, ec);
        std::string e = lowerExt(it->path());
        if (ec || rel.empty() || rel.native().rfind(L"..", 0) == 0 || rel.is_absolute() || !kAllowed.count(e)) { r.skipped++; continue; }
        long long sz = (long long)it->file_size(ec);
        if (sz > kMaxFile) { r.error = "В обоях есть файл больше 700 МБ."; return r; }
        total += sz;
        if (total > kMaxTotal || r.copied >= kMaxFiles) { r.error = "Обои слишком большие (больше 1,2 ГБ или 3000 файлов)."; return r; }
        fs::path dest = to / rel;
        fs::create_directories(dest.parent_path(), ec);
        fs::copy_file(it->path(), dest, fs::copy_options::overwrite_existing, ec);
        r.copied++;
    }
    return r;
}

// Lively: LivelyInfo.json (тип — число или строка)
struct Lively { bool has = false; std::string title, desc, author, file, type; };
static Lively readLively(const fs::path& folder) {
    Lively l; std::string t; Json j;
    if (!gs::readFile((folder / L"LivelyInfo.json").wstring(), t) || !gs::jsonParse(t, j) || j.t != Json::Obj) return l;
    l.has = true; l.title = j.str("Title"); l.desc = j.str("Desc"); l.author = j.str("Author"); l.file = j.str("FileName");
    if (auto* x = j.get("Type")) {
        if (x->t == Json::Num) { static const char* n[] = {"web", "webaudio", "url", "video", "gif", "videostream", "picture"}; int k = (int)x->n; l.type = k >= 0 && k < 7 ? n[k] : "app"; }
        else if (x->t == Json::Str) { l.type = x->s; for (auto& c : l.type) c = (char)tolower((unsigned char)c); if (l.type != "web" && l.type != "webaudio" && l.type != "url" && l.type != "video" && l.type != "gif" && l.type != "picture" && l.type != "videostream" && !l.type.empty()) l.type = "app"; }
    }
    return l;
}

static Result installFolder(const fs::path& folder, const std::string& source) {
    Lively info = readLively(folder);
    std::string entry, type = "html", title = narrow(folder.filename().wstring()), desc, author;
    if (info.has) {
        if (!info.title.empty()) title = info.title; desc = info.desc; author = info.author;
        const std::string& t = info.type;
        if (t == "video" || t == "gif" || t == "picture" || t == "web" || t == "webaudio") { type = t == "picture" ? "image" : (t == "video" || t == "gif") ? t : "html"; entry = info.file; }
        else if (!t.empty()) return {false, "", "Это обои-программа или поток ('" + t + "') — такие не поддерживаются."};
    }
    std::error_code ec;
    if (entry.empty()) {
        if (fs::exists(folder / L"index.html", ec)) entry = "index.html";
        else for (fs::directory_iterator it(folder, ec), end; !ec && it != end; it.increment(ec)) { std::string e = lowerExt(it->path()); if (e == ".html" || e == ".htm") { entry = narrow(it->path().filename().wstring()); break; } }
    }
    if (entry.empty()) {
        for (fs::directory_iterator it(folder, ec), end; !ec && it != end; it.increment(ec)) {
            std::string e = lowerExt(it->path());
            if (isVideo(e) || e == ".gif") return installSingle(it->path().wstring(), e == ".gif" ? "gif" : "video", title, source);
        }
        return {false, "", "В папке нет страницы index.html и нет видео или GIF."};
    }
    for (auto& c : entry) if (c == '\\') c = '/';
    Wallpaper w = newWallpaper(title, type, source); w.description = desc; w.author = author;
    fs::create_directories(w.dir, ec);
    CopyOut co = copyFolder(folder, w.dir);
    if (!co.error.empty()) { fs::remove_all(w.dir, ec); return {false, "", co.error}; }
    if (!fs::exists(fs::path(w.dir) / fs::path(widen(entry)), ec)) { fs::remove_all(w.dir, ec); return {false, "", "Главный файл обоев «" + entry + "» не найден."}; }
    if (type == "html") w.entry = entry;
    else { w.entry = "_player.html"; gs::writeFileAtomic(w.dir + L"\\_player.html", readAsset("res/_player.html")); gs::writeFileAtomic(w.dir + L"\\_file.txt", entry); w.description = entry; }
    saveWallpaper(w);
    return done(w, "Добавлено: " + w.title + (co.skipped > 0 ? " (пропущено файлов, которые не бывают частью обоев: " + std::to_string(co.skipped) + ")" : ""));
}

// запуск внешней программы без окна, вывод — в файл
static int runTool(const std::wstring& cmd, const std::wstring& outFile, DWORD timeoutMs) {
    SECURITY_ATTRIBUTES sa{sizeof sa, nullptr, TRUE};
    HANDLE out = CreateFileW(outFile.c_str(), GENERIC_WRITE, FILE_SHARE_READ, &sa, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    STARTUPINFOW si{}; si.cb = sizeof si; si.dwFlags = STARTF_USESTDHANDLES | STARTF_USESHOWWINDOW; si.wShowWindow = SW_HIDE;
    si.hStdOutput = out; si.hStdError = out; si.hStdInput = GetStdHandle(STD_INPUT_HANDLE);
    PROCESS_INFORMATION pi{};
    std::wstring c = cmd;
    BOOL ok = CreateProcessW(nullptr, c.data(), nullptr, nullptr, TRUE, CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi);
    if (out != INVALID_HANDLE_VALUE) CloseHandle(out);
    if (!ok) return -1;
    DWORD w = WaitForSingleObject(pi.hProcess, timeoutMs), code = 1;
    if (w == WAIT_TIMEOUT) { TerminateProcess(pi.hProcess, 1); code = (DWORD)-2; } else GetExitCodeProcess(pi.hProcess, &code);
    CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
    return (int)code;
}

static Result installZip(const std::wstring& zip, const std::string& source) {
    wchar_t sys[MAX_PATH]; GetSystemDirectoryW(sys, MAX_PATH);
    std::wstring tar = std::wstring(L"\"") + sys + L"\\tar.exe\"";
    std::error_code ec;
    if (!fs::exists(std::wstring(sys) + L"\\tar.exe", ec)) return {false, "", "Для архивов нужен tar.exe из Windows 10/11 — он не найден. Распакуйте архив сами и добавьте папку."};
    gs::makeDirs(downloadsDir());
    GUID g; CoCreateGuid(&g); char hx[16]; std::snprintf(hx, sizeof hx, "%08lx", g.Data1);
    fs::path temp = fs::path(downloadsDir()) / widen(std::string("unzip-") + hx);
    fs::path listFile = temp.wstring() + L".list.txt";
    struct Cleanup { fs::path t, l; ~Cleanup() { std::error_code e; fs::remove_all(t, e); fs::remove(l, e); } } cl{temp, listFile};
    // сначала смотрим, что внутри: ни выхода из папки, ни полных путей
    int rc = runTool(tar + L" -tf \"" + zip + L"\"", listFile.wstring(), 60000);
    std::string list; gs::readFile(listFile.wstring(), list);
    if (rc != 0) return {false, "", "Архив повреждён или это не zip."};
    int count = 0; size_t pos = 0;
    while (pos < list.size()) {
        size_t e = list.find('\n', pos); if (e == std::string::npos) e = list.size();
        std::string line = list.substr(pos, e - pos); pos = e + 1;
        while (!line.empty() && (line.back() == '\r')) line.pop_back();
        if (line.empty()) continue;
        for (auto& c : line) if (c == '\\') c = '/';
        if (++count > kMaxFiles * 2) return {false, "", "В архиве слишком много файлов."};
        bool bad = line[0] == '/' || (line.size() > 1 && line[1] == ':');
        size_t s = 0;
        while (s <= line.size()) { size_t n = line.find('/', s); if (n == std::string::npos) n = line.size(); if (line.compare(s, n - s, "..") == 0) bad = true; s = n + 1; }
        if (bad) return {false, "", "В архиве есть недопустимые пути — он не добавлен."};
    }
    fs::create_directories(temp, ec);
    rc = runTool(tar + L" -xf \"" + zip + L"\" -C \"" + temp.wstring() + L"\"", (temp.wstring() + L".x.txt"), 240000);
    fs::remove(temp.wstring() + L".x.txt", ec);
    if (rc != 0) return {false, "", "Не получилось распаковать архив."};
    fs::path root = temp;
    for (int i = 0; i < 3; i++) {
        int files = 0; std::vector<fs::path> dirs;
        for (fs::directory_iterator it(root, ec), end; !ec && it != end; it.increment(ec)) { if (it->is_directory(ec)) dirs.push_back(it->path()); else files++; }
        if (files == 0 && dirs.size() == 1) root = dirs[0]; else break;
    }
    Result r = installFolder(root, source);
    if (r.ok) {   // архив без манифеста: название — из имени архива, а не из временной папки
        Wallpaper w;
        if (getWallpaper(r.id, w) && w.title == narrow(root.filename().wstring())) { w.title = narrow(fs::path(zip).stem().wstring()); saveWallpaper(w); r.message = "Добавлено: " + w.title; }
    }
    return r;
}

Result installPath(const std::wstring& path, const std::string& source) {
    std::error_code ec;
    if (fs::is_directory(path, ec)) return installFolder(path, source);
    if (!fs::exists(path, ec)) return {false, "", "Файл не найден."};
    std::string ext = lowerExt(path);
    if (ext == ".zip") return installZip(path, source);
    static const char* bad[] = {".exe", ".msi", ".bat", ".cmd", ".ps1", ".scr", ".dll", ".lnk"};
    for (auto b : bad) if (ext == b) return {false, "", "Файл " + ext + " — это программа, а не обои. Он не запускается и не добавляется."};
    if (ext == ".7z" || ext == ".rar" || ext == ".pkg") return {false, "", "Формат " + ext + " не поддерживается. Нужны видео (mp4, webm), GIF, картинка, страница (html) или архив zip."};
    std::string title = narrow(fs::path(path).stem().wstring());
    if (isVideo(ext)) return installSingle(path, "video", title, source);
    if (ext == ".gif") return installSingle(path, "gif", title, source);
    if (isImage(ext)) return installSingle(path, "image", title, source);
    if (ext == ".html" || ext == ".htm") return installSingle(path, "html", title, source);
    return {false, "", "Не знаю, как сделать обои из файла " + ext + ". Подходят видео (mp4, webm), GIF, картинки, страницы (html) и архивы zip."};
}

Result installDownloaded(const std::wstring& tempDir, const std::string& id) {
    std::string t; Json j;
    if (!gs::readFile(tempDir + L"\\manifest.json", t)) return {false, "", "В обоях из каталога нет manifest.json."};
    if (!gs::jsonParse(t, j) || j.t != Json::Obj) return {false, "", "manifest.json не читается."};
    if (!idOk(id)) return {false, "", "Недопустимое имя обоев."};
    Wallpaper w = fromJson(j); w.id = id; w.source = "каталог";
    fs::path dir = fs::path(libraryDir()) / widen(id); std::error_code ec;
    fs::remove_all(dir, ec); fs::create_directories(dir, ec);
    CopyOut co = copyFolder(tempDir, dir);
    if (!co.error.empty()) { fs::remove_all(dir, ec); return {false, "", co.error}; }
    w.dir = dir.wstring(); saveWallpaper(w);
    Wallpaper rd;
    if (!getWallpaper(id, rd)) return {false, "", "Главный файл обоев не найден после установки."};
    return {true, id, "Установлено: " + rd.title};
}

Result createFrom(const Wallpaper& tmpl, const std::string& title, const std::map<std::string, std::string>& values, const std::string& background) {
    std::string tt = title; while (!tt.empty() && tt.back() == ' ') tt.pop_back();
    Wallpaper w = newWallpaper(tt.empty() ? tmpl.title + " (мои)" : tt, tmpl.type, "создано в Ghost Style");
    w.entry = tmpl.entry; w.description = tmpl.description; w.params = tmpl.params; w.values = values; w.tmpl = false;
    w.overlay = tmpl.overlay; w.telemetry = tmpl.telemetry; w.background = tmpl.overlay ? background : "";
    std::error_code ec; fs::create_directories(w.dir, ec);
    for (fs::recursive_directory_iterator it(tmpl.dir, ec), end; !ec && it != end; it.increment(ec)) {
        if (!it->is_regular_file(ec)) continue;
        fs::path rel = fs::relative(it->path(), tmpl.dir, ec);
        std::string n = narrow(rel.wstring());
        if (n == "manifest.json" || n == ".builtin-version") continue;
        fs::path dest = fs::path(w.dir) / rel;
        fs::create_directories(dest.parent_path(), ec);
        fs::copy_file(it->path(), dest, fs::copy_options::overwrite_existing, ec);
    }
    saveWallpaper(w);
    return done(w, "Создано: " + w.title);
}

void updateValues(Wallpaper& w, const std::map<std::string, std::string>& values, const std::string& background) {
    if (w.builtin()) return;
    w.values = values;
    if (w.overlay) w.background = background;
    saveWallpaper(w);
}

// ---------- сеть ----------
static bool httpRun(const std::string& url, std::string* body, const std::wstring* file, long long maxBytes, std::string& error) {
    std::wstring wurl = widen(url);
    URL_COMPONENTSW uc{}; uc.dwStructSize = sizeof uc;
    wchar_t host[256], path[2048];
    uc.lpszHostName = host; uc.dwHostNameLength = 256; uc.lpszUrlPath = path; uc.dwUrlPathLength = 2048;
    if (!WinHttpCrackUrl(wurl.c_str(), 0, 0, &uc) || uc.nScheme != INTERNET_SCHEME_HTTPS) { error = "адрес не подходит"; return false; }
    HINTERNET s = WinHttpOpen(L"GhostStyle/1", WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY, nullptr, nullptr, 0);
    if (!s) { error = "нет связи"; return false; }
    WinHttpSetTimeouts(s, 10000, 10000, 20000, 30000);
    HINTERNET c = WinHttpConnect(s, host, uc.nPort, 0);
    HINTERNET r = c ? WinHttpOpenRequest(c, L"GET", path, nullptr, nullptr, nullptr, WINHTTP_FLAG_SECURE) : nullptr;
    bool ok = false;
    HANDLE fh = INVALID_HANDLE_VALUE;
    if (r && WinHttpSendRequest(r, nullptr, 0, nullptr, 0, 0, 0) && WinHttpReceiveResponse(r, nullptr)) {
        DWORD code = 0, sz = sizeof code;
        WinHttpQueryHeaders(r, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER, nullptr, &code, &sz, nullptr);
        if (code != 200) error = "ответ сервера " + std::to_string(code);
        else {
            if (file) fh = CreateFileW(file->c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
            long long total = 0; ok = true;
            for (;;) {
                DWORD avail = 0;
                if (!WinHttpQueryDataAvailable(r, &avail)) { ok = false; error = "обрыв связи"; break; }
                if (!avail) break;
                std::string buf(avail, '\0'); DWORD got = 0;
                if (!WinHttpReadData(r, buf.data(), avail, &got)) { ok = false; error = "обрыв связи"; break; }
                total += got;
                if (total > maxBytes) { ok = false; error = "файл слишком большой"; break; }
                if (body) body->append(buf.data(), got);
                else if (fh != INVALID_HANDLE_VALUE) { DWORD w; WriteFile(fh, buf.data(), got, &w, nullptr); }
            }
        }
    } else error = "нет связи";
    if (fh != INVALID_HANDLE_VALUE) CloseHandle(fh);
    if (r) WinHttpCloseHandle(r); if (c) WinHttpCloseHandle(c); WinHttpCloseHandle(s);
    return ok;
}
bool httpGet(const std::string& url, std::string& body, long long maxBytes, std::string& error) { body.clear(); return httpRun(url, &body, nullptr, maxBytes, error); }
bool httpGetToFile(const std::string& url, const std::wstring& path, long long maxBytes, std::string& error) { return httpRun(url, nullptr, &path, maxBytes, error); }

// ---------- каталог ----------
static const char* kCatalogBase = "https://raw.githubusercontent.com/botsystemtioxsit/launcher/main/Native/modules/wallpaper/catalog/";
static bool catId(const std::string& s) { if (s.empty() || s.size() > 40) return false; for (unsigned char c : s) if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-')) return false; return true; }
static bool catFile(const std::string& f) {
    if (f.empty() || f.size() > 120 || f[0] == '/' || f.find("..") != std::string::npos) return false;
    for (unsigned char c : f) if (!(isalnum(c) || c == '_' || c == '-' || c == '.' || c == '/')) return false;
    static const std::set<std::string> ok = {".json", ".html", ".js", ".css", ".png", ".jpg", ".jpeg", ".webp", ".gif", ".svg", ".mp4", ".webm", ".woff", ".woff2", ".ttf", ".otf", ".txt"};
    return ok.count(lowerExt(fs::path(widen(f))));
}
bool fetchCatalog(std::vector<CatalogItem>& out, std::string& error) {
    out.clear(); std::string text;
    if (!httpGet(std::string(kCatalogBase) + "catalog.json", text, 2000000, error)) return false;
    Json j;
    if (!gs::jsonParse(text, j) || j.t != Json::Obj) { error = "список каталога не читается"; return false; }
    auto* items = j.get("items");
    if (!items || items->t != Json::Arr) return true;
    for (auto& it : items->a) {
        CatalogItem c; c.id = it.str("id"); c.title = it.str("title"); c.description = it.str("description"); c.type = it.str("type", "html");
        if (auto* fl = it.get("files")) if (fl->t == Json::Arr) for (auto& f : fl->a) c.files.push_back(f.s);
        bool ok = catId(c.id) && !c.files.empty() && c.files.size() <= 60;
        for (auto& f : c.files) ok = ok && catFile(f);
        if (ok) out.push_back(c);
    }
    return true;
}
Result installCatalogItem(const CatalogItem& item) {
    GUID g; CoCreateGuid(&g); char hx[16]; std::snprintf(hx, sizeof hx, "%08lx", g.Data1);
    fs::path temp = fs::path(downloadsDir()) / widen("catalog-" + item.id + "-" + hx); std::error_code ec;
    fs::create_directories(temp, ec);
    struct Cl { fs::path p; ~Cl() { std::error_code e; fs::remove_all(p, e); } } cl{temp};
    for (auto& f : item.files) {
        fs::path dest = temp / fs::path(widen(f));
        fs::create_directories(dest.parent_path(), ec);
        std::string err;
        if (!httpGetToFile(std::string(kCatalogBase) + item.id + "/" + f, dest.wstring(), 60000000, err)) return {false, "", "Файл " + f + " не скачался (" + err + ")."};
    }
    return installDownloaded(temp.wstring(), item.libraryId());
}

}  // namespace wp
