#include "ic_core.hpp"
#include <windows.h>
#include <shellapi.h>
#include <objbase.h>
#include <shlobj.h>
#include <tlhelp32.h>
#include <shlwapi.h>
#include <shobjidl.h>
#include <wincodec.h>
#include <wrl/client.h>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <filesystem>
#include <mutex>
#include <set>
#include <stdexcept>
#include <tuple>
#include "json.hpp"
#include "win.hpp"

namespace fs = std::filesystem;
using Microsoft::WRL::ComPtr;
using gs::Json;
using gs::narrow;
using gs::widen;

namespace ic {

// ---------- пути ----------
std::wstring rootDir() { return gs::appDataDir() + L"\\icon-plus"; }
static std::wstring settingsFile() { return rootDir() + L"\\settings.json"; }
static std::wstring stateFile() { return rootDir() + L"\\state.json"; }
static std::wstring generatedDir() { return rootDir() + L"\\generated"; }
static std::wstring originalsDir() { return rootDir() + L"\\originals"; }
static void ensureDirs() { gs::makeDirs(rootDir()); gs::makeDirs(generatedDir()); gs::makeDirs(originalsDir()); }
static const char* kSalt = "v3";

// ---------- цели ----------
const std::vector<Target>& targets() {
    static const std::vector<Target> t = {
        {"folder", "Папки и диски", "Папка (спросит разрешение администратора)", Kind::Folder, {}, nullptr, nullptr, true},
        {"drive-fixed", "Папки и диски", "Жёсткий диск", Kind::Drive, {}},
        {"drive-removable", "Папки и диски", "Флешка (USB)", Kind::Drive, {}},
        {"drive-optical", "Папки и диски", "DVD / CD", Kind::Drive, {}},
        {"drive-network", "Папки и диски", "Сетевой диск", Kind::Drive, {}},
        {"kf", "Папки и диски", "Папки пользователя: Документы, Загрузки, Рабочий стол…", Kind::KnownFolders, {}},
        {"thispc", "Рабочий стол и система", "Этот компьютер", Kind::Clsid, {}, "{20D04FE0-3AEA-1069-A2D8-08002B30309D}"},
        {"recycle-empty", "Рабочий стол и система", "Корзина (пустая)", Kind::Clsid, {}, "{645FF040-5081-101B-9F08-00AA002F954E}", "empty"},
        {"recycle-full", "Рабочий стол и система", "Корзина (полная)", Kind::Clsid, {}, "{645FF040-5081-101B-9F08-00AA002F954E}", "full"},
        {"network", "Рабочий стол и система", "Сеть", Kind::Clsid, {}, "{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}"},
        {"userfiles", "Рабочий стол и система", "Папка пользователя", Kind::Clsid, {}, "{59031a47-3f72-44a7-89c5-5595fe6b30ee}"},
        {"ft-text", "Типы файлов", "Текст (txt, log, ini, md)", Kind::FileType, {"txt", "log", "ini", "cfg", "md"}},
        {"ft-doc", "Типы файлов", "Документ (doc, docx, rtf)", Kind::FileType, {"doc", "docx", "rtf", "odt"}},
        {"ft-sheet", "Типы файлов", "Таблица (xls, xlsx, csv)", Kind::FileType, {"xls", "xlsx", "csv", "ods"}},
        {"ft-slides", "Типы файлов", "Презентация (ppt, pptx)", Kind::FileType, {"ppt", "pptx", "odp"}},
        {"ft-pdf", "Типы файлов", "PDF", Kind::FileType, {"pdf"}},
        {"ft-archive", "Типы файлов", "Архив (zip, rar, 7z)", Kind::FileType, {"zip", "rar", "7z", "tar", "gz"}},
        {"ft-image", "Типы файлов", "Картинка (png, jpg, gif)", Kind::FileType, {"png", "jpg", "jpeg", "gif", "bmp", "webp"}},
        {"ft-audio", "Типы файлов", "Музыка (mp3, wav)", Kind::FileType, {"mp3", "wav", "flac", "ogg", "m4a"}},
        {"ft-video", "Типы файлов", "Видео (mp4, mkv, avi)", Kind::FileType, {"mp4", "mkv", "avi", "mov", "wmv"}},
        {"ft-web", "Типы файлов", "Страница (html)", Kind::FileType, {"html", "htm"}},
        {"ft-code", "Типы файлов", "Код (js, json, xml, py)", Kind::FileType, {"js", "json", "xml", "py", "cs", "cpp", "h", "java"}},
        {"ft-script", "Типы файлов", "Сценарий (bat, cmd, ps1)", Kind::FileType, {"bat", "cmd", "ps1"}},
        {"ft-all", "Типы файлов", "Все остальные типы файлов (по списку Windows)", Kind::AllFileTypes, {}},
        {"lnk-desktop", "Ярлыки", "Ярлыки на рабочем столе (и ярлыки-ссылки Steam)", Kind::Shortcuts, {}},
        {"lnk-desktop-all", "Ярлыки", "Ярлыки на общем рабочем столе (для всех; спросит разрешение администратора)", Kind::Shortcuts, {}, nullptr, nullptr, true},
        {"lnk-start", "Ярлыки", "Ярлыки в меню «Пуск» (мои программы)", Kind::Shortcuts, {}},
        {"lnk-start-all", "Ярлыки", "Ярлыки в меню «Пуск» (общие; спросит разрешение администратора)", Kind::Shortcuts, {}, nullptr, nullptr, true},
        {"lnk-taskbar", "Ярлыки", "Закреплённые на панели задач", Kind::Shortcuts, {}},
    };
    return t;
}
const Target* targetById(const std::string& id) { for (auto& t : targets()) if (id == t.id) return &t; return nullptr; }

// ---------- настройки ----------
static bool hexOk(const std::string& s) { if (s.size() != 7 || s[0] != '#') return false; for (size_t i = 1; i < 7; i++) if (!isxdigit((unsigned char)s[i])) return false; return true; }
void Settings::normalize() {
    if (!hexOk(Color)) Color = "#00F0FF";
    for (auto& c : Color) c = (char)toupper((unsigned char)c);
    if (Style != "mono" && Style != "hue") Style = "mono";
    Strength = std::clamp(Strength, 0, 100);
    std::vector<std::string> ok; for (auto& id : Enabled) if (targetById(id) && std::find(ok.begin(), ok.end(), id) == ok.end()) ok.push_back(id);
    Enabled = ok;
}
Settings Settings::load() {
    Settings s; std::string t; Json j; bool have = false;
    if (gs::readFile(settingsFile(), t) && gs::jsonParse(t, j) && j.t == Json::Obj) {
        have = true;
        s.Color = j.str("Color", s.Color); s.Style = j.str("Style", s.Style); s.Strength = j.num("Strength", 100); s.AutoRestart = j.boolean("AutoRestart", true);
        if (auto* e = j.get("Enabled")) if (e->t == Json::Arr) for (auto& x : e->a) if (x.t == Json::Str) s.Enabled.push_back(x.s);
    }
    if (!have) for (auto& t2 : targets()) s.Enabled.push_back(t2.id);
    s.normalize();
    return s;
}
void Settings::save() const {
    Json j = Json::Object();
    j.set("Color", Json::String(Color)); j.set("Style", Json::String(Style)); j.set("Strength", Json::Number(Strength)); j.set("AutoRestart", Json::Boolean(AutoRestart)); j.set("Version", Json::Number(2));
    Json e = Json::Array(); for (auto& id : Enabled) e.a.push_back(Json::String(id)); j.set("Enabled", e);
    std::string out; gs::jsonDump(j, out); ensureDirs(); gs::writeFileAtomic(settingsFile(), out);
}

// ---------- перекраска ----------
static void toHsl(double r, double g, double b, double& h, double& s, double& l) {
    double mx = std::max({r, g, b}), mn = std::min({r, g, b}); l = (mx + mn) / 2; h = 0; s = 0; double d = mx - mn;
    if (d > 1e-9) { s = l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn); if (mx == r) h = (g - b) / d + (g < b ? 6 : 0); else if (mx == g) h = (b - r) / d + 2; else h = (r - g) / d + 4; h /= 6; }
}
static double hue2rgb(double p, double q, double t) { if (t < 0) t += 1; if (t > 1) t -= 1; if (t < 1.0 / 6) return p + (q - p) * 6 * t; if (t < 0.5) return q; if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6; return p; }
static void fromHsl(double h, double s, double l, double& r, double& g, double& b) {
    if (s < 1e-9) { r = g = b = l; return; }
    double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
    r = hue2rgb(p, q, h + 1.0 / 3); g = hue2rgb(p, q, h); b = hue2rgb(p, q, h - 1.0 / 3);
}

// Перекраска в один цвет, как «тема значков» на Android: форма и свет сохраняются, цвет — выбранный. Прозрачность не трогается.
void tintColor(Img& img, const std::string& hex, const std::string& style, int strength) {
    unsigned v = 0; std::sscanf(hex.c_str() + 1, "%x", &v);
    double tr = ((v >> 16) & 255) / 255.0, tg = ((v >> 8) & 255) / 255.0, tb = (v & 255) / 255.0, th, ts, tl;
    toHsl(tr, tg, tb, th, ts, tl);
    double k = std::clamp(strength, 0, 100) / 100.0;
    for (size_t i = 0; i + 3 < img.bgra.size(); i += 4) {
        double b = img.bgra[i] / 255.0, g = img.bgra[i + 1] / 255.0, r = img.bgra[i + 2] / 255.0, nr, ng, nb;
        if (style == "hue") { double h, s, l; toHsl(r, g, b, h, s, l); fromHsl(th, s, l, nr, ng, nb); }
        else {
            double lum = 0.299 * r + 0.587 * g + 0.114 * b, dr = tr * 0.16, dg = tg * 0.16, db = tb * 0.16, lr = tr + (1 - tr) * 0.78, lg = tg + (1 - tg) * 0.78, lb = tb + (1 - tb) * 0.78;
            if (lum < 0.5) { double t = lum * 2; nr = dr + (tr - dr) * t; ng = dg + (tg - dg) * t; nb = db + (tb - db) * t; }
            else { double t = (lum - 0.5) * 2; nr = tr + (lr - tr) * t; ng = tg + (lg - tg) * t; nb = tb + (lb - tb) * t; }
        }
        nr = r + (nr - r) * k; ng = g + (ng - g) * k; nb = b + (nb - b) * k;
        img.bgra[i] = (unsigned char)std::lround(std::clamp(nb, 0.0, 1.0) * 255); img.bgra[i + 1] = (unsigned char)std::lround(std::clamp(ng, 0.0, 1.0) * 255); img.bgra[i + 2] = (unsigned char)std::lround(std::clamp(nr, 0.0, 1.0) * 255);
    }
}

unsigned char* pixelsOf(Img& i) { return i.bgra.data(); }

Img resizeImg(const Img& s, int n) {
    Img o; o.w = o.h = n; o.bgra.assign((size_t)n * n * 4, 0);
    if (s.w <= 0 || s.h <= 0 || s.bgra.size() < (size_t)s.w * s.h * 4) return o;
    for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) {
        double x0 = (double)x * s.w / n, x1 = (double)(x + 1) * s.w / n, y0 = (double)y * s.h / n, y1 = (double)(y + 1) * s.h / n;
        double acc[4] = {0, 0, 0, 0}, wsum = 0, asum = 0;
        for (int yy = (int)y0; yy < std::min(s.h, (int)std::ceil(y1)); yy++) for (int xx = (int)x0; xx < std::min(s.w, (int)std::ceil(x1)); xx++) {
            double w = (std::min<double>(xx + 1, x1) - std::max<double>(xx, x0)) * (std::min<double>(yy + 1, y1) - std::max<double>(yy, y0));
            if (w <= 0) continue;
            const unsigned char* p = &s.bgra[((size_t)yy * s.w + xx) * 4];
            double a = p[3] / 255.0;
            acc[0] += p[0] * a * w; acc[1] += p[1] * a * w; acc[2] += p[2] * a * w; asum += a * w; wsum += w;
        }
        unsigned char* q = &o.bgra[((size_t)y * n + x) * 4];
        if (asum > 0) { q[0] = (unsigned char)(acc[0] / asum); q[1] = (unsigned char)(acc[1] / asum); q[2] = (unsigned char)(acc[2] / asum); q[3] = (unsigned char)std::clamp(asum / wsum * 255.0, 0.0, 255.0); }
    }
    return o;
}

// «Пустой» значок: сплошная плитка без рисунка (магазинные приложения) — красить там нечего, а записать такой значок — получить квадрат.
bool isBlank(const Img& img) {
    const int n = 64; Img p = resizeImg(img, n);
    long long count = 0, total = n * n; int minX = n, minY = n, maxX = -1, maxY = -1; double sum[3] = {}, sq[3] = {};
    for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) {
        const unsigned char* q = &p.bgra[((size_t)y * n + x) * 4];
        if (q[3] < 40) continue;
        count++; minX = std::min(minX, x); maxX = std::max(maxX, x); minY = std::min(minY, y); maxY = std::max(maxY, y);
        for (int c = 0; c < 3; c++) { sum[c] += q[c]; sq[c] += (double)q[c] * q[c]; }
    }
    if (count < total / 50) return true;
    double sd = 0; for (int c = 0; c < 3; c++) { double m = sum[c] / count; sd = std::max(sd, std::sqrt(std::max(0.0, sq[c] / count - m * m))); }
    long long box = (long long)(maxX - minX + 1) * (maxY - minY + 1); double cover = (double)count / (double)box;
    return sd < 12 && cover >= 0.9 && box >= total / 5;
}
bool isBlankSet(const std::vector<Img>& sizes) {
    const Img* big = nullptr; for (auto& i : sizes) if (!big || i.w > big->w) big = &i;
    return !big || isBlank(*big);
}

static void put16(std::string& s, unsigned v) { s += (char)(v & 255); s += (char)((v >> 8) & 255); }
static void put32(std::string& s, unsigned v) { put16(s, v & 0xFFFF); put16(s, v >> 16); }
// файл .ico: по картинке на размер, каждая как PNG (Windows читает с Vista)
std::string buildIco(const std::vector<Img>& sizes) {
    std::vector<std::string> png; std::vector<const Img*> src;
    for (auto& i : sizes) { std::string p; if (sp::encodePng(i, p)) { png.push_back(p); src.push_back(&i); } }
    std::string out; put16(out, 0); put16(out, 1); put16(out, (unsigned)png.size());
    unsigned off = 6 + 16 * (unsigned)png.size();
    for (size_t k = 0; k < png.size(); k++) {
        out += (char)(src[k]->w >= 256 ? 0 : src[k]->w); out += (char)(src[k]->h >= 256 ? 0 : src[k]->h); out += (char)0; out += (char)0;
        put16(out, 1); put16(out, 32); put32(out, (unsigned)png[k].size()); put32(out, off); off += (unsigned)png[k].size();
    }
    for (auto& p : png) out += p;
    return out;
}

// ---------- чтение значков ----------
static bool hiconToImg(HICON h, int px, Img& out) {
    HDC sdc = GetDC(nullptr); std::vector<unsigned char> onB, onW;
    for (int pass = 0; pass < 2; pass++) {
        BITMAPINFO bi{}; bi.bmiHeader.biSize = sizeof bi.bmiHeader; bi.bmiHeader.biWidth = px; bi.bmiHeader.biHeight = -px; bi.bmiHeader.biPlanes = 1; bi.bmiHeader.biBitCount = 32;
        void* bits = nullptr; HBITMAP dib = CreateDIBSection(sdc, &bi, DIB_RGB_COLORS, &bits, nullptr, 0);
        HDC mdc = CreateCompatibleDC(sdc); HGDIOBJ old = SelectObject(mdc, dib);
        memset(bits, pass == 0 ? 0x00 : 0xFF, (size_t)px * px * 4);
        DrawIconEx(mdc, 0, 0, h, px, px, 0, nullptr, DI_NORMAL); GdiFlush();
        (pass == 0 ? onB : onW).assign((unsigned char*)bits, (unsigned char*)bits + (size_t)px * px * 4);
        SelectObject(mdc, old); DeleteDC(mdc); DeleteObject(dib);
    }
    ReleaseDC(nullptr, sdc);
    out.w = out.h = px; out.bgra.assign((size_t)px * px * 4, 0); bool any = false;
    for (size_t i = 0; i < out.bgra.size(); i += 4) {
        int a = 0; for (int c = 0; c < 3; c++) a = std::max(a, 255 - (onW[i + c] - onB[i + c]));
        a = std::clamp(a, 0, 255);
        if (a > 0) { for (int c = 0; c < 3; c++) out.bgra[i + c] = (unsigned char)std::min(255, onB[i + c] * 255 / a); any = true; }
        out.bgra[i + 3] = (unsigned char)a;
    }
    return any;
}

static bool wicFrames(const std::wstring& file, std::vector<Img>& out) {
    ComPtr<IWICImagingFactory> wic;
    if (FAILED(CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&wic)))) return false;
    ComPtr<IWICBitmapDecoder> dec;
    if (FAILED(wic->CreateDecoderFromFilename(file.c_str(), nullptr, GENERIC_READ, WICDecodeMetadataCacheOnDemand, &dec))) return false;
    UINT n = 0; dec->GetFrameCount(&n);
    for (UINT i = 0; i < n && i < 16; i++) {
        ComPtr<IWICBitmapFrameDecode> fr; if (FAILED(dec->GetFrame(i, &fr))) continue;
        ComPtr<IWICFormatConverter> cv; if (FAILED(wic->CreateFormatConverter(&cv)) || FAILED(cv->Initialize(fr.Get(), GUID_WICPixelFormat32bppBGRA, WICBitmapDitherTypeNone, nullptr, 0, WICBitmapPaletteTypeCustom))) continue;
        UINT w = 0, h = 0; cv->GetSize(&w, &h); if (!w || !h || w > 512 || h > 512) continue;
        Img im; im.w = (int)w; im.h = (int)h; im.bgra.resize((size_t)w * h * 4);
        if (SUCCEEDED(cv->CopyPixels(nullptr, w * 4, (UINT)im.bgra.size(), im.bgra.data()))) out.push_back(im);
    }
    return !out.empty();
}

static bool shellImage(IShellItem* item, std::vector<Img>& out) {
    ComPtr<IShellItemImageFactory> f; if (FAILED(item->QueryInterface(IID_PPV_ARGS(&f)))) return false;
    HBITMAP hb = nullptr;
    if (FAILED(f->GetImage(SIZE{256, 256}, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, &hb)) || !hb) return false;
    BITMAP bm{}; GetObjectW(hb, sizeof bm, &bm);
    int w = bm.bmWidth, h = bm.bmHeight; if (w <= 0 || h <= 0) { DeleteObject(hb); return false; }
    BITMAPINFO bi{}; bi.bmiHeader.biSize = sizeof bi.bmiHeader; bi.bmiHeader.biWidth = w; bi.bmiHeader.biHeight = -h; bi.bmiHeader.biPlanes = 1; bi.bmiHeader.biBitCount = 32;
    Img im; im.w = w; im.h = h; im.bgra.resize((size_t)w * h * 4);
    HDC dc = GetDC(nullptr); GetDIBits(dc, hb, 0, h, im.bgra.data(), &bi, DIB_RGB_COLORS); ReleaseDC(nullptr, dc); DeleteObject(hb);
    for (size_t i = 0; i < im.bgra.size(); i += 4) {   // картинка с «предумноженной» прозрачностью → обычная
        unsigned a = im.bgra[i + 3];
        if (a > 0 && a < 255) for (int c = 0; c < 3; c++) im.bgra[i + c] = (unsigned char)std::min(255u, im.bgra[i + c] * 255u / a);
    }
    if (w != h) { int n = std::max(w, h); Img sq; sq.w = sq.h = n; sq.bgra.assign((size_t)n * n * 4, 0); for (int y = 0; y < h; y++) memcpy(&sq.bgra[((size_t)(y + (n - h) / 2) * n + (n - w) / 2) * 4], &im.bgra[(size_t)y * w * 4], (size_t)w * 4); im = sq; }
    out.push_back(im);
    for (int s : {64, 48, 32, 24, 16}) if (s < im.w) out.push_back(resizeImg(im, s));
    return true;
}

static std::wstring expandEnv(const std::wstring& s) { wchar_t b[MAX_PATH * 2]; DWORD n = ExpandEnvironmentStringsW(s.c_str(), b, (DWORD)std::size(b)); return n && n < std::size(b) ? std::wstring(b) : s; }

static bool lnkIdList(const std::wstring& lnk, PIDLIST_ABSOLUTE* pidl);

// «Как рисует Проводник»: shell:<путь> или lnktarget:<ярлык>
static bool loadShell(const std::wstring& path, bool viaLnk, std::vector<Img>& out) {
    ComPtr<IShellItem> item;
    if (viaLnk) { PIDLIST_ABSOLUTE pidl = nullptr; if (!lnkIdList(path, &pidl) || !pidl) return false; HRESULT hr = SHCreateItemFromIDList(pidl, IID_PPV_ARGS(&item)); CoTaskMemFree(pidl); if (FAILED(hr)) return false; }
    else if (FAILED(SHCreateItemFromParsingName(path.c_str(), nullptr, IID_PPV_ARGS(&item)))) return false;
    return shellImage(item.Get(), out);
}

// location: «путь,номер» (файл .ico, .exe, .dll), shell:<путь>, lnktarget:<ярлык>
static bool loadSizes(const std::string& loc, std::vector<Img>& out) {
    out.clear();
    if (loc.rfind("shell:", 0) == 0) return loadShell(widen(loc.substr(6)), false, out);
    if (loc.rfind("lnktarget:", 0) == 0) return loadShell(widen(loc.substr(10)), true, out);
    std::wstring w = widen(loc); int index = 0;
    size_t comma = w.find_last_of(L',');
    if (comma != std::wstring::npos && comma + 1 < w.size()) { wchar_t* e = nullptr; long v = wcstol(w.c_str() + comma + 1, &e, 10); if (e && !*e) { index = (int)v; w = w.substr(0, comma); } }
    while (!w.empty() && (w.front() == L'"')) w.erase(0, 1); while (!w.empty() && w.back() == L'"') w.pop_back();
    w = expandEnv(w);
    if (!fs::exists(w)) return false;
    std::wstring ext = fs::path(w).extension().wstring(); for (auto& c : ext) c = (wchar_t)towlower(c);
    if (ext == L".ico") { if (wicFrames(w, out)) return true; }
    for (int s : {256, 128, 64, 48, 32, 24, 16}) {
        HICON h = nullptr; UINT id = 0;
        if (PrivateExtractIconsW(w.c_str(), index, s, s, &h, &id, 1, LR_DEFAULTCOLOR) == 1 && h) { Img im; if (hiconToImg(h, s, im)) out.push_back(im); DestroyIcon(h); }
    }
    return !out.empty();
}

// ---------- реестр ----------
struct RegChange { std::string path, name; bool existed = false; std::string old, oldKind, createdFrom; bool hasCreated = false; };
struct LnkChange { std::string file, oldPath; int oldIndex = 0; };
struct IniChange { std::string file, old; bool hadOld = false; };

struct Engine::State {
    std::vector<RegChange> reg; std::vector<LnkChange> lnk; std::vector<IniChange> ini; std::vector<std::string> created;
    std::map<std::string, std::string> sources; bool machineLnk = false, folderMachine = false; std::string applied;
    std::mutex mu;
};

static std::string regReadStr(HKEY root, const std::string& path, const std::string& name, bool& exists, std::string* kind = nullptr, bool expand = false) {
    exists = false; HKEY k; if (RegOpenKeyExW(root, widen(path).c_str(), 0, KEY_QUERY_VALUE, &k) != ERROR_SUCCESS) return "";
    wchar_t buf[2048]; DWORD sz = sizeof buf, t = 0; std::string r;
    if (RegQueryValueExW(k, widen(name).c_str(), nullptr, &t, (BYTE*)buf, &sz) == ERROR_SUCCESS && (t == REG_SZ || t == REG_EXPAND_SZ)) {
        exists = true; r = narrow(std::wstring(buf, wcsnlen(buf, sz / 2)));
        if (kind) *kind = t == REG_EXPAND_SZ ? "ExpandString" : "String";
        if (expand) r = narrow(expandEnv(widen(r)));
    }
    RegCloseKey(k); return r;
}
static bool keyExists(const std::string& path) { HKEY k; if (RegOpenKeyExW(HKEY_CURRENT_USER, widen(path).c_str(), 0, KEY_READ, &k) == ERROR_SUCCESS) { RegCloseKey(k); return true; } return false; }
static std::string firstMissing(const std::string& path) {
    std::string cur; size_t i = 0;
    while (i <= path.size()) { size_t n = path.find('\\', i); if (n == std::string::npos) n = path.size(); cur = path.substr(0, n); if (!keyExists(cur)) return cur; i = n + 1; }
    return "";
}
static void regSetString(const std::string& path, const std::string& name, const std::string& value, DWORD type = REG_SZ) {
    HKEY k; if (RegCreateKeyExW(HKEY_CURRENT_USER, widen(path).c_str(), 0, nullptr, 0, KEY_SET_VALUE, nullptr, &k, nullptr) != ERROR_SUCCESS) throw std::runtime_error("реестр не открывается");
    std::wstring v = widen(value); LONG r = RegSetValueExW(k, widen(name).c_str(), 0, type, (const BYTE*)v.c_str(), (DWORD)((v.size() + 1) * 2)); RegCloseKey(k);
    if (r != ERROR_SUCCESS) throw std::runtime_error("реестр не записывается");
}
static void regWrite(Engine::State& st, const std::string& path, const std::string& name, const std::string& value) {
    bool have = false; for (auto& c : st.reg) if (c.path == path && c.name == name) have = true;
    if (!have) {
        RegChange c; c.path = path; c.name = name; std::string fm = firstMissing(path); c.hasCreated = !fm.empty(); c.createdFrom = fm;
        bool ex = false; std::string kind; std::string old = regReadStr(HKEY_CURRENT_USER, path, name, ex, &kind);
        if (ex) { c.existed = true; c.old = old; c.oldKind = kind; }
        st.reg.push_back(c);
    }
    regSetString(path, name, value);
}
static void regRevert(const RegChange& c) {
    try {
        if (c.existed) { regSetString(c.path, c.name, c.old, c.oldKind == "ExpandString" ? REG_EXPAND_SZ : REG_SZ); return; }
        HKEY k; if (RegOpenKeyExW(HKEY_CURRENT_USER, widen(c.path).c_str(), 0, KEY_SET_VALUE, &k) == ERROR_SUCCESS) { RegDeleteValueW(k, widen(c.name).c_str()); RegCloseKey(k); }
        if (!c.hasCreated) return;
        std::string cur = c.path;   // убрать пустые ветки, созданные нами (снизу вверх)
        while (cur.size() >= c.createdFrom.size()) {
            HKEY k2; bool empty = false;
            if (RegOpenKeyExW(HKEY_CURRENT_USER, widen(cur).c_str(), 0, KEY_READ, &k2) == ERROR_SUCCESS) { DWORD sub = 0, vals = 0; RegQueryInfoKeyW(k2, nullptr, nullptr, nullptr, &sub, nullptr, nullptr, &vals, nullptr, nullptr, nullptr, nullptr); empty = sub == 0 && vals == 0; RegCloseKey(k2); }
            if (!empty) break;
            size_t sl = cur.find_last_of('\\'); std::string parent = sl == std::string::npos ? "" : cur.substr(0, sl), leaf = sl == std::string::npos ? cur : cur.substr(sl + 1);
            HKEY pk = nullptr; if (parent.empty()) pk = HKEY_CURRENT_USER; else if (RegOpenKeyExW(HKEY_CURRENT_USER, widen(parent).c_str(), 0, KEY_WRITE, &pk) != ERROR_SUCCESS) break;
            LONG r = RegDeleteKeyW(pk, widen(leaf).c_str()); if (!parent.empty()) RegCloseKey(pk);
            if (r != ERROR_SUCCESS || cur == c.createdFrom) break;
            cur = parent;
        }
    } catch (...) {}
}

// ---------- ярлыки ----------
static bool isUrl(const std::wstring& f) { return f.size() > 4 && _wcsicmp(f.c_str() + f.size() - 4, L".url") == 0; }

static bool lnkIdList(const std::wstring& lnk, PIDLIST_ABSOLUTE* pidl) {
    ComPtr<IShellLinkW> sl; ComPtr<IPersistFile> pf;
    if (FAILED(CoCreateInstance(CLSID_ShellLink, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&sl))) || FAILED(sl.As(&pf)) || FAILED(pf->Load(lnk.c_str(), STGM_READ))) return false;
    return SUCCEEDED(sl->GetIDList(pidl)) && *pidl;
}
static bool lnkReadIcon(const std::wstring& lnk, std::string& path, int& index) {
    path.clear(); index = 0;
    if (isUrl(lnk)) {
        std::string t; if (!gs::readFile(lnk, t)) return false;
        size_t p = 0;
        while (p < t.size()) {
            size_t e = t.find('\n', p); if (e == std::string::npos) e = t.size(); std::string line = t.substr(p, e - p); p = e + 1;
            while (!line.empty() && isspace((unsigned char)line.back())) line.pop_back();
            size_t a = 0; while (a < line.size() && isspace((unsigned char)line[a])) a++; line = line.substr(a);
            if (_strnicmp(line.c_str(), "IconFile=", 9) == 0) path = line.substr(9); else if (_strnicmp(line.c_str(), "IconIndex=", 10) == 0) index = std::atoi(line.c_str() + 10);
        }
        return true;
    }
    ComPtr<IShellLinkW> sl; ComPtr<IPersistFile> pf;
    if (FAILED(CoCreateInstance(CLSID_ShellLink, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&sl))) || FAILED(sl.As(&pf)) || FAILED(pf->Load(lnk.c_str(), STGM_READ))) return false;
    wchar_t b[MAX_PATH * 2] = {}; int idx = 0; sl->GetIconLocation(b, (int)std::size(b), &idx); path = narrow(b); index = idx; return true;
}
static bool lnkWriteIcon(const std::wstring& lnk, const std::string& path, int index) {
    if (isUrl(lnk)) {
        std::string t; if (!gs::readFile(lnk, t)) return false;
        DWORD attrs = GetFileAttributesW(lnk.c_str()); if (attrs != INVALID_FILE_ATTRIBUTES && (attrs & FILE_ATTRIBUTE_READONLY)) SetFileAttributesW(lnk.c_str(), attrs & ~FILE_ATTRIBUTE_READONLY);
        std::string res; bool done = false; size_t p = 0;
        while (p < t.size()) {
            size_t e = t.find('\n', p); bool last = e == std::string::npos; if (last) e = t.size(); std::string line = t.substr(p, e - p); p = e + 1;
            std::string tl = line; while (!tl.empty() && isspace((unsigned char)tl.back())) tl.pop_back(); size_t a = 0; while (a < tl.size() && isspace((unsigned char)tl[a])) a++; tl = tl.substr(a);
            if (_strnicmp(tl.c_str(), "IconFile=", 9) == 0 || _strnicmp(tl.c_str(), "IconIndex=", 10) == 0) continue;
            res += line; if (!last || !line.empty()) res += "\n";
            if (!done && !path.empty() && _stricmp(tl.c_str(), "[InternetShortcut]") == 0) { res += "IconIndex=" + std::to_string(index) + "\nIconFile=" + path + "\n"; done = true; }
        }
        if (!done && !path.empty()) res += "[InternetShortcut]\nIconIndex=" + std::to_string(index) + "\nIconFile=" + path + "\n";
        bool ok = gs::writeFileAtomic(lnk, res);
        if (attrs != INVALID_FILE_ATTRIBUTES && (attrs & FILE_ATTRIBUTE_READONLY)) SetFileAttributesW(lnk.c_str(), attrs);
        return ok;
    }
    ComPtr<IShellLinkW> sl; ComPtr<IPersistFile> pf;
    if (FAILED(CoCreateInstance(CLSID_ShellLink, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&sl))) || FAILED(sl.As(&pf)) || FAILED(pf->Load(lnk.c_str(), STGM_READWRITE))) return false;
    sl->SetIconLocation(widen(path).c_str(), index);
    return SUCCEEDED(pf->Save(lnk.c_str(), TRUE));
}
static std::string lnkTarget(const std::wstring& lnk) {
    if (isUrl(lnk)) return "";
    ComPtr<IShellLinkW> sl; ComPtr<IPersistFile> pf;
    if (FAILED(CoCreateInstance(CLSID_ShellLink, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&sl))) || FAILED(sl.As(&pf)) || FAILED(pf->Load(lnk.c_str(), STGM_READ))) return "";
    wchar_t b[MAX_PATH * 2] = {}; sl->GetPath(b, (int)std::size(b), nullptr, SLGP_RAWPATH); return narrow(b);
}
static void collectShortcuts(const fs::path& dir, bool recursive, std::vector<std::wstring>& out) {
    std::error_code ec;
    if (!fs::exists(dir, ec)) return;
    auto take = [&](const fs::path& p) { std::wstring e = p.extension().wstring(); for (auto& c : e) c = (wchar_t)towlower(c); if ((e == L".lnk" || e == L".url") && out.size() < 4000) out.push_back(p.wstring()); };
    if (recursive) for (fs::recursive_directory_iterator it(dir, fs::directory_options::skip_permission_denied, ec), end; !ec && it != end; it.increment(ec)) { if (it->is_regular_file(ec)) take(it->path()); }
    else for (fs::directory_iterator it(dir, ec), end; !ec && it != end; it.increment(ec)) { if (it->is_regular_file(ec)) take(it->path()); }
}
static std::wstring knownFolder(REFKNOWNFOLDERID id) { PWSTR p = nullptr; std::wstring r; if (SUCCEEDED(SHGetKnownFolderPath(id, 0, nullptr, &p)) && p) { r = p; CoTaskMemFree(p); } return r; }
static std::vector<std::wstring> shortcutFiles(const std::string& id) {
    std::vector<std::wstring> v;
    if (id == "lnk-desktop") collectShortcuts(knownFolder(FOLDERID_Desktop), false, v);
    else if (id == "lnk-desktop-all") collectShortcuts(knownFolder(FOLDERID_PublicDesktop), false, v);
    else if (id == "lnk-start") collectShortcuts(knownFolder(FOLDERID_Programs), true, v);
    else if (id == "lnk-start-all") collectShortcuts(knownFolder(FOLDERID_CommonPrograms), true, v);
    else collectShortcuts(gs::appDataDir() + L"\\..\\..\\Microsoft\\Internet Explorer\\Quick Launch\\User Pinned\\TaskBar", false, v);
    return v;
}

// ---------- desktop.ini ----------
static const char* kSection = "[.ShellClassInfo]";
static std::vector<std::string> splitLines(const std::string& t) { std::vector<std::string> v; size_t p = 0; while (p < t.size()) { size_t e = t.find('\n', p); if (e == std::string::npos) e = t.size(); std::string l = t.substr(p, e - p); while (!l.empty() && l.back() == '\r') l.pop_back(); v.push_back(l); p = e + 1; } return v; }
static std::string trimS(const std::string& s) { size_t a = 0, b = s.size(); while (a < b && isspace((unsigned char)s[a])) a++; while (b > a && isspace((unsigned char)s[b - 1])) b--; return s.substr(a, b - a); }
static bool iniRead(const std::wstring& file, const char* key, std::string& value) {
    std::string t; if (!gs::readFile(file, t)) return false;
    bool in = false; std::string k = std::string(key) + "=";
    for (auto& l : splitLines(t)) { std::string s = trimS(l); if (!s.empty() && s[0] == '[') { in = _stricmp(s.c_str(), kSection) == 0; continue; } if (in && _strnicmp(s.c_str(), k.c_str(), k.size()) == 0) { value = trimS(s.substr(k.size())); return true; } }
    return false;
}
static bool iniWrite(const std::wstring& file, const char* key, const std::string* value) {   // value == nullptr — убрать строку
    bool existed = GetFileAttributesW(file.c_str()) != INVALID_FILE_ATTRIBUTES; DWORD attrs = existed ? GetFileAttributesW(file.c_str()) : (FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_SYSTEM);
    if (existed) SetFileAttributesW(file.c_str(), FILE_ATTRIBUTE_NORMAL);
    std::string t; if (existed) gs::readFile(file, t);
    std::vector<std::string> res; bool in = false, seen = false, wrote = false; std::string k = std::string(key) + "=";
    for (auto& l : splitLines(t)) {
        std::string s = trimS(l);
        if (!s.empty() && s[0] == '[') { if (in && !wrote && value) { res.push_back(k + *value); wrote = true; } in = _stricmp(s.c_str(), kSection) == 0; if (in) seen = true; res.push_back(l); continue; }
        if (in && _strnicmp(s.c_str(), k.c_str(), k.size()) == 0) { if (value && !wrote) { res.push_back(k + *value); wrote = true; } continue; }
        res.push_back(l);
    }
    if (value && !wrote) {
        if (seen && in) res.push_back(k + *value);
        else if (seen) { for (size_t i = 0; i < res.size(); i++) if (_stricmp(trimS(res[i]).c_str(), kSection) == 0) { res.insert(res.begin() + i + 1, k + *value); break; } }
        else { res.push_back(kSection); res.push_back(k + *value); }
    }
    std::string out; for (auto& l : res) out += l + "\r\n";
    bool ok = gs::writeFileAtomic(file, out);
    SetFileAttributesW(file.c_str(), attrs | FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_SYSTEM);
    return ok;
}

// ---------- Windows: значки типов и дисков ----------
static std::string assocDefaultIcon(const std::wstring& assoc) {
    wchar_t b[1040] = {}; DWORD n = (DWORD)std::size(b);
    return AssocQueryStringW(0, ASSOCSTR_DEFAULTICON, assoc.c_str(), nullptr, b, &n) == S_OK ? narrow(expandEnv(b)) : "";
}
static std::string iconLocation(const std::wstring& path, bool useAttrs, bool dir = false) {
    SHFILEINFOW fi{}; SHGetFileInfoW(path.c_str(), dir ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL, &fi, sizeof fi, SHGFI_ICONLOCATION | (useAttrs ? SHGFI_USEFILEATTRIBUTES : 0));
    return fi.szDisplayName[0] ? narrow(fi.szDisplayName) + "," + std::to_string(fi.iIcon) : "";
}
static std::string machineIcon(const std::string& path, const std::string& name) { bool ex; return regReadStr(HKEY_LOCAL_MACHINE, path, name, ex); }
static std::vector<std::string> progIds(const std::string& ext) {
    std::vector<std::string> v; bool ex = false;
    std::string uc = regReadStr(HKEY_CURRENT_USER, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\FileExts\\." + ext + "\\UserChoice", "ProgId", ex); if (ex && !uc.empty()) v.push_back(uc);
    std::string d = regReadStr(HKEY_CLASSES_ROOT, "." + ext, "", ex); if (ex && !d.empty() && std::find(v.begin(), v.end(), d) == v.end()) v.push_back(d);
    return v;
}
static std::vector<std::pair<std::string, std::string>> allProgIdIcons() {
    std::vector<std::pair<std::string, std::string>> list; wchar_t name[256];
    static const std::set<std::string> skip = {"*", "CLSID", "Folder", "Directory", "Drive", "Applications", "AllFilesystemObjects", "Unknown"};
    for (DWORD i = 0; list.size() < 2500; i++) {
        DWORD n = (DWORD)std::size(name); if (RegEnumKeyExW(HKEY_CLASSES_ROOT, i, name, &n, nullptr, nullptr, nullptr, nullptr) != ERROR_SUCCESS) break;
        std::string nm = narrow(std::wstring(name, n)); if (nm.empty() || nm[0] == '.' || skip.count(nm)) continue;
        bool ex = false; std::string v = regReadStr(HKEY_CLASSES_ROOT, nm + "\\DefaultIcon", "", ex);
        if (!ex || trimS(v).empty() || v.find("%1") != std::string::npos || v[0] == '@') continue;
        std::string ours = regReadStr(HKEY_CURRENT_USER, "Software\\Classes\\" + nm + "\\DefaultIcon", "", ex);
        if (ex && _strnicmp(ours.c_str(), narrow(generatedDir()).c_str(), narrow(generatedDir()).size()) == 0) continue;   // уже перекрашено нами
        list.push_back({nm, narrow(expandEnv(widen(v)))});
    }
    return list;
}

// ---------- движок ----------
Engine::Engine() : st_(new State) {
    ensureDirs();
    std::string t; Json j;
    if (gs::readFile(stateFile(), t) && gs::jsonParse(t, j) && j.t == Json::Obj) {
        if (auto* a = j.get("Reg")) if (a->t == Json::Arr) for (auto& x : a->a) { RegChange c; c.path = x.str("Path"); c.name = x.str("Name"); c.existed = x.boolean("Existed"); c.old = x.str("Old"); c.oldKind = x.str("OldKind"); c.createdFrom = x.str("CreatedFrom"); c.hasCreated = !c.createdFrom.empty(); if (!c.path.empty()) st_->reg.push_back(c); }
        if (auto* a = j.get("Lnk")) if (a->t == Json::Arr) for (auto& x : a->a) { LnkChange c; c.file = x.str("File"); c.oldPath = x.str("OldPath"); c.oldIndex = x.num("OldIndex"); if (!c.file.empty()) st_->lnk.push_back(c); }
        if (auto* a = j.get("Ini")) if (a->t == Json::Arr) for (auto& x : a->a) { IniChange c; c.file = x.str("File"); c.hadOld = x.get("Old") && x.get("Old")->t == Json::Str; c.old = x.str("Old"); if (!c.file.empty()) st_->ini.push_back(c); }
        if (auto* a = j.get("Created")) if (a->t == Json::Arr) for (auto& x : a->a) if (x.t == Json::Str) st_->created.push_back(x.s);
        if (auto* a = j.get("Sources")) if (a->t == Json::Obj) for (auto& kv : a->o) if (kv.second.t == Json::Str) st_->sources[kv.first] = kv.second.s;
        st_->machineLnk = j.boolean("MachineLnk"); st_->folderMachine = j.boolean("FolderMachine"); st_->applied = j.str("Applied");
    }
    // прошлые версии могли запомнить как «оригинал» пустую плитку — такие записи выбрасываем
    for (auto it = st_->sources.begin(); it != st_->sources.end();) {
        bool drop = false;
        if (_strnicmp(it->second.c_str(), narrow(originalsDir()).c_str(), narrow(originalsDir()).size()) == 0) { std::vector<Img> sz; if (!loadSizes(it->second, sz) || isBlankSet(sz)) { drop = true; DeleteFileW(widen(it->second).c_str()); } }
        if (drop) it = st_->sources.erase(it); else ++it;
    }
}
Engine::~Engine() = default;
bool Engine::applied() const { return !st_->applied.empty(); }
int Engine::changesCount() const { return (int)(st_->reg.size() + st_->lnk.size() + st_->ini.size() + st_->created.size()); }

static void saveState(Engine::State& s) {
    Json j = Json::Object();
    Json reg = Json::Array(); for (auto& c : s.reg) { Json x = Json::Object(); x.set("Path", Json::String(c.path)); x.set("Name", Json::String(c.name)); x.set("Existed", Json::Boolean(c.existed)); if (c.existed) { x.set("Old", Json::String(c.old)); x.set("OldKind", Json::String(c.oldKind)); } if (c.hasCreated) x.set("CreatedFrom", Json::String(c.createdFrom)); reg.a.push_back(x); }
    j.set("Reg", reg);
    Json lnk = Json::Array(); for (auto& c : s.lnk) { Json x = Json::Object(); x.set("File", Json::String(c.file)); x.set("OldPath", Json::String(c.oldPath)); x.set("OldIndex", Json::Number(c.oldIndex)); lnk.a.push_back(x); } j.set("Lnk", lnk);
    Json ini = Json::Array(); for (auto& c : s.ini) { Json x = Json::Object(); x.set("File", Json::String(c.file)); if (c.hadOld) x.set("Old", Json::String(c.old)); ini.a.push_back(x); } j.set("Ini", ini);
    Json cr = Json::Array(); for (auto& c : s.created) cr.a.push_back(Json::String(c)); j.set("Created", cr);
    Json src = Json::Object(); for (auto& kv : s.sources) src.set(kv.first, Json::String(kv.second)); j.set("Sources", src);
    j.set("MachineLnk", Json::Boolean(s.machineLnk)); j.set("FolderMachine", Json::Boolean(s.folderMachine)); j.set("Applied", Json::String(s.applied));
    std::string out; gs::jsonDump(j, out); ensureDirs(); gs::writeFileAtomic(stateFile(), out);
}

static std::string fnvHex(const std::string& s) { unsigned long long h = 1469598103934665603ULL; for (unsigned char c : s) { h ^= c; h *= 1099511628211ULL; } char b[24]; std::snprintf(b, sizeof b, "%016llx", h); return b; }
static std::wstring generatedPath(const std::string& loc, const Settings& s) { return generatedDir() + L"\\" + widen(fnvHex(loc + "|" + s.Color + "|" + s.Style + "|" + std::to_string(s.Strength) + "|" + kSalt)) + L".ico"; }
static bool isVirtual(const std::string& l) { return l.rfind("shell:", 0) == 0 || l.rfind("lnktarget:", 0) == 0; }

// Значок, который рисует Проводник (а не лежит в файле), запоминаем как файл: после замены Проводник покажет уже наш значок, а красить надо оригинал.
static std::string captureOriginal(const std::string& loc) {
    std::wstring path = originalsDir() + L"\\" + widen(fnvHex(loc)) + L".ico";
    if (fs::exists(path)) return narrow(path);
    std::vector<Img> sz; if (!loadSizes(loc, sz)) return "";
    gs::writeFileAtomic(path, buildIco(sz));
    return narrow(path);
}

struct Sink { std::string key; std::function<std::string()> source; std::function<void(const std::string&)> write; std::function<std::vector<std::string>()> fallbacks; };
struct Job { std::string folderIco; bool hasFolder = false; std::vector<std::pair<std::string, std::string>> lnks; bool restore = false; };
static std::string runMachine(Job& job, std::vector<std::pair<std::string, std::string>>& failures, bool& folderOk, int& lnkOk);

static std::string lnkSource(Engine::State& st, const std::wstring& file) {
    const LnkChange* orig = nullptr; std::string fn = narrow(file);
    for (auto& c : st.lnk) if (_stricmp(c.file.c_str(), fn.c_str()) == 0) orig = &c;
    std::string p; int i = 0;
    if (isUrl(file)) {
        if (orig) { p = orig->oldPath; i = orig->oldIndex; } else if (!lnkReadIcon(file, p, i)) return "shell:" + fn;
        std::wstring full = expandEnv(widen(p));
        if (!p.empty() && fs::exists(full)) return narrow(full) + "," + std::to_string(i);
        return "shell:" + fn;
    }
    if (orig) { p = orig->oldPath; i = orig->oldIndex; } else if (!lnkReadIcon(file, p, i)) { p.clear(); i = 0; }
    if (!p.empty() && _strnicmp(p.c_str(), narrow(generatedDir()).c_str(), narrow(generatedDir()).size()) != 0) { std::wstring full = expandEnv(widen(p)); if (fs::exists(full)) return narrow(full) + "," + std::to_string(i); }
    std::string target = lnkTarget(file);   // у ярлыка нет своего значка — берётся значок цели
    if (!target.empty() && fs::exists(widen(target))) {
        std::string ext = narrow(fs::path(widen(target)).extension().wstring()); for (auto& c : ext) c = (char)tolower((unsigned char)c);
        if (ext == ".exe" || ext == ".dll" || ext == ".ico" || ext == ".icl") return target + ",0";
        std::string a = assocDefaultIcon(widen(ext)); if (!a.empty()) return a;
    }
    return "";
}

static std::vector<Sink> sinksFor(Engine::State& st, const Target& t, Job& job) {
    std::vector<Sink> v; Engine::State* sp_ = &st;
    switch (t.kind) {
        case Kind::Folder:
            v.push_back({"folder", [] { std::string s = machineIcon("SOFTWARE\\Classes\\Folder\\DefaultIcon", ""); return s.empty() ? iconLocation(L"folder", true, true) : s; }, [&job](const std::string& p) { job.folderIco = p; job.hasFolder = true; }, nullptr});
            break;
        case Kind::Drive: {
            UINT type = std::string(t.id) == "drive-fixed" ? DRIVE_FIXED : std::string(t.id) == "drive-removable" ? DRIVE_REMOVABLE : std::string(t.id) == "drive-optical" ? DRIVE_CDROM : DRIVE_REMOTE;
            wchar_t buf[512]; DWORD n = GetLogicalDriveStringsW((DWORD)std::size(buf), buf);
            for (wchar_t* p = buf; n && *p; p += wcslen(p) + 1) {
                std::wstring root = p; if (GetDriveTypeW(root.c_str()) != type) continue;
                std::string letter(1, (char)toupper((int)root[0]));
                v.push_back({"drive:" + letter, [root] { return iconLocation(root, false); }, [sp_, letter](const std::string& pth) { regWrite(*sp_, "Software\\Classes\\Applications\\Explorer.exe\\Drives\\" + letter + "\\DefaultIcon", "", pth); }, [root] { return std::vector<std::string>{"shell:" + narrow(root)}; }});
            }
            break;
        }
        case Kind::Clsid: {
            std::string clsid = t.clsid, vn = t.valueName ? t.valueName : "", id = t.id;
            std::string path = "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\CLSID\\" + clsid + "\\DefaultIcon";
            v.push_back({"clsid:" + id, [clsid, vn] { std::string s = machineIcon("SOFTWARE\\Classes\\CLSID\\" + clsid + "\\DefaultIcon", vn); if (s.empty() && !vn.empty()) s = machineIcon("SOFTWARE\\Classes\\CLSID\\" + clsid + "\\DefaultIcon", ""); return s.empty() ? "shell:::" + clsid : s; },
                [sp_, path, vn, id](const std::string& p) { regWrite(*sp_, path, vn, p); if (id == "recycle-empty") regWrite(*sp_, path, "", p); }, nullptr});
            break;
        }
        case Kind::FileType:
            for (auto* e : t.exts) {
                std::string ext = e;
                v.push_back({"ext:" + ext, [ext] { std::string a = assocDefaultIcon(widen("." + ext)); return a.empty() ? iconLocation(widen("x." + ext), true) : a; },
                    [sp_, ext](const std::string& p) { for (auto& pid : progIds(ext)) regWrite(*sp_, "Software\\Classes\\" + pid + "\\DefaultIcon", "", p); },
                    [ext] { std::wstring dir = std::wstring(fs::temp_directory_path().wstring()) + L"\\iconplus-ext"; std::error_code ec; fs::create_directories(dir, ec); std::wstring f = dir + L"\\sample." + widen(ext); if (!fs::exists(f, ec)) gs::writeFileAtomic(f, ""); return std::vector<std::string>{"shell:" + narrow(f)}; }});
            }
            break;
        case Kind::Shortcuts:
            for (auto& f : shortcutFiles(t.id)) {
                std::string fn = narrow(f); if (std::find_if(st.created.begin(), st.created.end(), [&](auto& c) { return _stricmp(c.c_str(), fn.c_str()) == 0; }) != st.created.end()) continue;
                bool machine = t.needsAdmin; std::wstring file = f;
                v.push_back({"lnk:" + fn, [sp_, file] { return lnkSource(*sp_, file); },
                    [sp_, file, fn, machine, &job](const std::string& p) {
                        if (machine) { job.lnks.push_back({fn, p}); return; }
                        bool have = false; for (auto& c : sp_->lnk) if (c.file == fn) have = true;
                        if (!have) { LnkChange c; c.file = fn; if (!lnkReadIcon(file, c.oldPath, c.oldIndex)) throw std::runtime_error("ярлык не читается"); sp_->lnk.push_back(c); }
                        if (!lnkWriteIcon(file, p, 0)) throw std::runtime_error("ярлык не записывается");
                    },
                    [file, fn] { std::vector<std::string> r; r.push_back(isUrl(file) ? "shell:" + fn : "lnktarget:" + fn); return r; }});
            }
            break;
        case Kind::KnownFolders: {
            struct KF { const char* name; const KNOWNFOLDERID* id; };
            static const KF kfs[] = {{"Рабочий стол", &FOLDERID_Desktop}, {"Документы", &FOLDERID_Documents}, {"Изображения", &FOLDERID_Pictures}, {"Музыка", &FOLDERID_Music}, {"Видео", &FOLDERID_Videos}, {"Загрузки", &FOLDERID_Downloads}};
            for (auto& k : kfs) {
                std::wstring folder = knownFolder(*k.id); if (folder.empty() || !fs::exists(folder)) continue;
                std::wstring ini = folder + L"\\desktop.ini"; std::string nm = k.name;
                v.push_back({"kf:" + nm, [folder] { return "shell:" + narrow(folder); }, [sp_, ini](const std::string& p) {
                    std::string fn = narrow(ini); bool have = false; for (auto& c : sp_->ini) if (c.file == fn) have = true;
                    if (!have) { IniChange c; c.file = fn; c.hadOld = iniRead(ini, "IconResource", c.old); sp_->ini.push_back(c); }
                    std::string val = p + ",0"; if (!iniWrite(ini, "IconResource", &val)) throw std::runtime_error("desktop.ini не записывается");
                }, nullptr});
            }
            break;
        }
        case Kind::AllFileTypes:
            for (auto& pi : allProgIdIcons()) { std::string pid = pi.first, ic = pi.second; v.push_back({"prog:" + pid, [ic] { return ic; }, [sp_, pid](const std::string& p) { regWrite(*sp_, "Software\\Classes\\" + pid + "\\DefaultIcon", "", p); }, nullptr}); }
            break;
    }
    return v;
}

struct Pick { std::string location, how; std::vector<Img> sizes; };
static Pick* resolvePick(Engine::State& st, const Sink& sink, std::string& problem, Pick& store) {
    problem.clear(); int count = 0; bool sawBlank = false;
    std::vector<std::string> cand;
    auto sv = st.sources.find(sink.key); if (sv != st.sources.end() && !sv->second.empty()) cand.push_back(sv->second);
    std::string live = sink.source(); if (!live.empty()) cand.push_back(live);
    if (sink.fallbacks) for (auto& f : sink.fallbacks()) if (!f.empty()) cand.push_back(f);
    std::set<std::string> seen;
    for (auto& loc : cand) {
        if (!seen.insert(loc).second) continue;
        count++;
        std::string real = isVirtual(loc) ? captureOriginal(loc) : loc;
        if (real.empty()) continue;
        if (_strnicmp(real.c_str(), narrow(generatedDir()).c_str(), narrow(generatedDir()).size()) == 0) continue;   // это уже наш перекрашенный значок
        std::vector<Img> sz; if (!loadSizes(real, sz)) continue;
        if (isBlankSet(sz)) { sawBlank = true; if (_strnicmp(real.c_str(), narrow(originalsDir()).c_str(), narrow(originalsDir()).size()) == 0) DeleteFileW(widen(real).c_str()); continue; }
        st.sources[sink.key] = real;
        store.location = real; store.how = isVirtual(loc) ? "как рисует Проводник" : "файл значка"; store.sizes = sz;
        return &store;
    }
    st.sources.erase(sink.key);
    problem = sawBlank ? "значок пустой (плитка без рисунка) — красить нечего" : count == 0 ? "значок не найден" : "значок не читается";
    return nullptr;
}

// null — после перекраски получилось сплошное пятно («серый квадратик»): такой значок ставить нельзя
static std::string generatedIcon(const Pick& pick, const Settings& s) {
    std::wstring path = generatedPath(pick.location, s);
    if (fs::exists(path)) return narrow(path);
    std::vector<Img> out; for (auto& im : pick.sizes) { Img c = im; tintColor(c, s.Color, s.Style, s.Strength); out.push_back(c); }
    if (isBlankSet(out)) return "";
    gs::writeFileAtomic(path, buildIco(out));
    return narrow(path);
}

static void revertLnk(Engine::State& st, const std::string& fileUtf8) {
    for (size_t i = 0; i < st.lnk.size(); i++) if (_stricmp(st.lnk[i].file.c_str(), fileUtf8.c_str()) == 0) {
        std::wstring f = widen(fileUtf8); if (fs::exists(f) && lnkWriteIcon(f, st.lnk[i].oldPath, st.lnk[i].oldIndex)) st.lnk.erase(st.lnk.begin() + i);
        return;
    }
}

static std::string itemName(const std::string& key) { if (key.rfind("lnk:", 0) == 0) return narrow(fs::path(widen(key.substr(4))).stem().wstring()); return key; }

ApplyResult Engine::applyColor(const Settings& sIn, const std::function<void(const std::string&)>& progress, bool refresh) {
    Settings s = sIn; s.normalize();
    ApplyResult res; Job job;
    std::vector<std::tuple<std::string, std::string, std::string>> pending;   // ключ, группа, имя — ждут ответа администратора
    for (auto& id : s.Enabled) {
        const Target* t = targetById(id); if (!t) continue;
        if (progress) progress(t->title);
        std::vector<Sink> sinks; try { sinks = sinksFor(*st_, *t, job); } catch (...) { continue; }
        for (auto& sink : sinks) {
            std::string item = itemName(sink.key);
            try {
                Pick store; std::string problem; Pick* pick = resolvePick(*st_, sink, problem, store);
                bool lnk = sink.key.rfind("lnk:", 0) == 0;
                if (!pick) { res.failed++; res.outcomes.push_back({t->title, item, problem}); if (lnk && !t->needsAdmin) revertLnk(*st_, sink.key.substr(4)); continue; }
                std::string ico = generatedIcon(*pick, s);
                if (ico.empty()) { res.failed++; res.outcomes.push_back({t->title, item, "после перекраски значок стал сплошным пятном — оставлен как был"}); if (lnk && !t->needsAdmin) revertLnk(*st_, sink.key.substr(4)); continue; }
                sink.write(ico); res.done++;
                if (t->needsAdmin && (t->kind == Kind::Shortcuts || t->kind == Kind::Folder)) pending.push_back({sink.key, t->title, item}); else res.outcomes.push_back({t->title, item, "ok"});
            } catch (const std::exception& e) { res.failed++; res.outcomes.push_back({t->title, item, e.what()}); res.messages.push_back(sink.key + ": " + e.what()); }
        }
    }
    if (job.hasFolder || !job.lnks.empty()) {   // действия с правами администратора — одним заданием (одно окно «Разрешить?»)
        std::vector<std::pair<std::string, std::string>> failures; bool folderOk = false; int lnkOk = 0;
        std::string err = runMachine(job, failures, folderOk, lnkOk);
        if (!err.empty()) res.messages.push_back("для администратора: " + err);
        for (auto& p : pending) {
            std::string key = std::get<0>(p), result = "ok";
            if (key == "folder") result = folderOk ? "ok" : (err.empty() ? "значок папок не изменён" : err);
            else if (!err.empty() && lnkOk == 0) result = err;
            else for (auto& f : failures) if (_stricmp(f.first.c_str(), key.substr(4).c_str()) == 0) result = f.second;
            res.outcomes.push_back({std::get<1>(p), std::get<2>(p), result});
            if (result != "ok") { res.done--; res.failed++; }
        }
        if (job.hasFolder && folderOk) st_->folderMachine = true;
        if (lnkOk > 0) st_->machineLnk = true;
    }
    st_->applied = "color"; saveState(*st_);
    notifyChanged();
    if (refresh && s.AutoRestart) fullRefresh();
    return res;
}

// ---------- вернуть как было ----------
int Engine::restoreAll(bool refresh) {
    int n = 0;
    for (size_t i = st_->reg.size(); i-- > 0;) { regRevert(st_->reg[i]); n++; }
    if (st_->folderMachine || st_->machineLnk) { Job j; j.restore = true; std::vector<std::pair<std::string, std::string>> f; bool fo = false; int lo = 0; runMachine(j, f, fo, lo); n += lo + (fo ? 1 : 0); st_->folderMachine = st_->machineLnk = false; }
    for (auto& l : st_->lnk) { std::wstring f = widen(l.file); if (fs::exists(f) && lnkWriteIcon(f, l.oldPath, l.oldIndex)) n++; }
    for (auto& f : st_->created) { std::wstring w = widen(f); if (fs::exists(w)) { DeleteFileW(w.c_str()); n++; } }
    for (auto& c : st_->ini) { std::wstring f = widen(c.file); if (fs::exists(f)) { iniWrite(f, "IconResource", c.hadOld ? &c.old : nullptr); n++; } }
    st_->reg.clear(); st_->lnk.clear(); st_->ini.clear(); st_->created.clear(); st_->sources.clear(); st_->applied.clear();
    saveState(*st_);
    notifyChanged();
    if (refresh) fullRefresh();
    return n;
}

// ---------- превью ----------
std::vector<Preview> Engine::previews(const Settings& sIn) {
    Settings s = sIn; s.normalize(); std::vector<Preview> out; Job job;
    auto one = [&](const Sink& sink, const std::string& name) {
        Preview p; p.name = name; Pick store; std::string problem; Pick* pick = resolvePick(*st_, sink, problem, store);
        if (!pick) { p.problem = problem; out.push_back(p); return; }
        const Img* best = nullptr; for (auto& i : pick->sizes) if (!best || std::abs(i.w - 48) < std::abs(best->w - 48)) best = &i;
        p.before = resizeImg(*best, 48); p.hasBefore = true; p.after = p.before; tintColor(p.after, s.Color, s.Style, s.Strength);
        if (isBlank(p.after)) p.problem = "после перекраски стал бы сплошным пятном — оставим как есть"; else p.hasAfter = true;
        out.push_back(p);
    };
    for (const char* id : {"thispc", "recycle-empty", "drive-fixed", "ft-text", "ft-pdf", "kf"}) {
        const Target* t = targetById(id); if (!t) continue;
        auto sinks = sinksFor(*st_, *t, job);
        if (!sinks.empty()) one(sinks[0], t->title);
    }
    if (const Target* t = targetById("lnk-taskbar")) { auto sinks = sinksFor(*st_, *t, job); int n = 0; for (auto& sk : sinks) { if (n++ >= 6) break; one(sk, itemName(sk.key)); } }
    return out;
}

// ---------- обновление значков в системе ----------
void Engine::notifyChanged() {
    SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, nullptr, nullptr);
    DWORD_PTR r; SendMessageTimeoutW(HWND_BROADCAST, WM_SETTINGCHANGE, 0, (LPARAM)L"Environment", SMTO_ABORTIFHUNG, 1500, &r);
}
static void runHidden(const std::wstring& cmd, DWORD wait) {
    STARTUPINFOW si{sizeof si}; si.dwFlags = STARTF_USESHOWWINDOW; si.wShowWindow = SW_HIDE; PROCESS_INFORMATION pi{}; std::wstring c = cmd;
    if (CreateProcessW(nullptr, c.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi)) { WaitForSingleObject(pi.hProcess, wait); CloseHandle(pi.hProcess); CloseHandle(pi.hThread); }
}
static void killByName(const wchar_t* exe) {
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0); if (snap == INVALID_HANDLE_VALUE) return;
    PROCESSENTRY32W pe{sizeof pe};
    for (BOOL ok = Process32FirstW(snap, &pe); ok; ok = Process32NextW(snap, &pe)) if (_wcsicmp(pe.szExeFile, exe) == 0) { HANDLE h = OpenProcess(PROCESS_TERMINATE, FALSE, pe.th32ProcessID); if (h) { TerminateProcess(h, 1); CloseHandle(h); } }
    CloseHandle(snap);
}
static bool processRunning(const wchar_t* exe) {
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0); if (snap == INVALID_HANDLE_VALUE) return false; bool f = false;
    PROCESSENTRY32W pe{sizeof pe}; for (BOOL ok = Process32FirstW(snap, &pe); ok && !f; ok = Process32NextW(snap, &pe)) if (_wcsicmp(pe.szExeFile, exe) == 0) f = true;
    CloseHandle(snap); return f;
}
// Полное обновление: Проводник и оболочка «Пуска» перезапускаются, файлы кэша значков удаляются (пока Проводник жив, он перезаписывает их старыми значками).
void Engine::fullRefresh() {
    wchar_t sys[MAX_PATH]; GetSystemDirectoryW(sys, MAX_PATH);
    gs::writeFileAtomic(gs::appDataDir() + L"\\explorer-restart.txt", "restart");   // метка для модуля «Панель»: перезапуск по нашей просьбе, а не падение движка
    killByName(L"explorer.exe"); killByName(L"StartMenuExperienceHost.exe"); killByName(L"SearchHost.exe");
    Sleep(700);
    wchar_t local[MAX_PATH]; GetEnvironmentVariableW(L"LOCALAPPDATA", local, MAX_PATH);
    std::error_code ec; fs::remove(std::wstring(local) + L"\\IconCache.db", ec);
    for (fs::directory_iterator it(std::wstring(local) + L"\\Microsoft\\Windows\\Explorer", ec), end; !ec && it != end; it.increment(ec)) { std::wstring n = it->path().filename().wstring(); if (n.rfind(L"iconcache_", 0) == 0) fs::remove(it->path(), ec); }
    Sleep(500);
    if (!processRunning(L"explorer.exe")) ShellExecuteW(nullptr, L"open", L"explorer.exe", nullptr, nullptr, SW_SHOWNORMAL);
    for (int i = 0; i < 40 && !FindWindowW(L"Shell_TrayWnd", nullptr); i++) Sleep(500);
    Sleep(2000);
    for (int pass = 0; pass < 2; pass++) { SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, nullptr, nullptr); runHidden(std::wstring(sys) + L"\\ie4uinit.exe -ClearIconCache", 8000); runHidden(std::wstring(sys) + L"\\ie4uinit.exe -show", 8000); Sleep(2500); }
}

// ---------- действия с правами администратора ----------
bool isAdmin() {
    BOOL is = FALSE; PSID g = nullptr; SID_IDENTIFIER_AUTHORITY nt = SECURITY_NT_AUTHORITY;
    if (AllocateAndInitializeSid(&nt, 2, SECURITY_BUILTIN_DOMAIN_RID, DOMAIN_ALIAS_RID_ADMINS, 0, 0, 0, 0, 0, 0, &g)) { CheckTokenMembership(nullptr, g, &is); FreeSid(g); }
    return is != FALSE;
}
static const wchar_t* kShellIcons = L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Shell Icons";
static int folderSetMachine(const std::string& ico) {
    if (!isAdmin()) return 5;
    HKEY probe; bool existed = RegOpenKeyExW(HKEY_LOCAL_MACHINE, kShellIcons, 0, KEY_READ, &probe) == ERROR_SUCCESS; if (existed) RegCloseKey(probe);
    HKEY k; if (RegCreateKeyExW(HKEY_LOCAL_MACHINE, kShellIcons, 0, nullptr, 0, KEY_READ | KEY_WRITE, nullptr, &k, nullptr) != ERROR_SUCCESS) return 5;
    auto setS = [&](const std::wstring& n, const std::wstring& v) { RegSetValueExW(k, n.c_str(), 0, REG_SZ, (const BYTE*)v.c_str(), (DWORD)((v.size() + 1) * 2)); };
    if (!existed) setS(L"IconPlus.Created", L"1");
    for (const wchar_t* n : {L"3", L"4"}) {
        wchar_t buf[1024]; DWORD sz = sizeof buf, t = 0; bool has = RegQueryValueExW(k, n, nullptr, &t, (BYTE*)buf, &sz) == ERROR_SUCCESS;
        DWORD t2 = 0, s2 = 0; bool hasBackup = RegQueryValueExW(k, (std::wstring(L"IconPlus.Backup.") + n).c_str(), nullptr, &t2, nullptr, &s2) == ERROR_SUCCESS;
        if (has && !hasBackup) setS(std::wstring(L"IconPlus.Backup.") + n, buf);
        setS(n, widen(ico) + L",0");
    }
    RegCloseKey(k); return 0;
}
static int folderRestoreMachine() {
    if (!isAdmin()) return 5;
    HKEY k; if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, kShellIcons, 0, KEY_READ | KEY_WRITE, &k) != ERROR_SUCCESS) return 0;
    for (const wchar_t* n : {L"3", L"4"}) {
        std::wstring bk = std::wstring(L"IconPlus.Backup.") + n; wchar_t buf[1024]; DWORD sz = sizeof buf, t = 0;
        if (RegQueryValueExW(k, bk.c_str(), nullptr, &t, (BYTE*)buf, &sz) == ERROR_SUCCESS) { RegSetValueExW(k, n, 0, REG_SZ, (const BYTE*)buf, sz); RegDeleteValueW(k, bk.c_str()); }
        else RegDeleteValueW(k, n);
    }
    DWORD t3 = 0, s3 = 0; bool created = RegQueryValueExW(k, L"IconPlus.Created", nullptr, &t3, nullptr, &s3) == ERROR_SUCCESS; if (created) RegDeleteValueW(k, L"IconPlus.Created");
    DWORD sub = 0, vals = 0; RegQueryInfoKeyW(k, nullptr, nullptr, nullptr, &sub, nullptr, nullptr, &vals, nullptr, nullptr, nullptr, nullptr); RegCloseKey(k);
    if (created && sub == 0 && vals == 0) { HKEY p; if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer", 0, KEY_WRITE, &p) == ERROR_SUCCESS) { RegDeleteKeyW(p, L"Shell Icons"); RegCloseKey(p); } }
    return 0;
}

// Задание — json: {FolderIco, Lnks:[{File,Ico}], Restore, BackupFile, ResultFile}; ответ — строки ключ=значение.
int runMachineJob(const std::wstring& jobFile) {
    std::string out, resultFile; int code = 0;
    try {
        std::string t; Json j; if (!gs::readFile(jobFile, t) || !gs::jsonParse(t, j)) throw std::runtime_error("задание не читается");
        resultFile = j.str("ResultFile"); std::wstring backupFile = widen(j.str("BackupFile"));
        if (!isAdmin()) { out += "error=нет прав администратора\n"; code = 5; }
        else {
            std::vector<LnkChange> backup; std::string bt; Json bj;
            if (gs::readFile(backupFile, bt) && gs::jsonParse(bt, bj)) if (auto* a = bj.get("Lnk")) if (a->t == Json::Arr) for (auto& x : a->a) { LnkChange c; c.file = x.str("File"); c.oldPath = x.str("OldPath"); c.oldIndex = x.num("OldIndex"); backup.push_back(c); }
            int ok = 0, fail = 0; bool folder = false; std::string fails;
            auto failIt = [&](const std::string& f, const std::string& why) { fail++; if (fail <= 400) fails += "failitem=" + f + "|" + why + "\n"; };
            if (j.boolean("Restore")) {
                for (auto& b : backup) { std::wstring f = widen(b.file); if (fs::exists(f) && lnkWriteIcon(f, b.oldPath, b.oldIndex)) ok++; else failIt(b.file, "не вернулся"); }
                backup.clear(); folderRestoreMachine(); folder = true;
            } else {
                std::string fi = j.str("FolderIco"); if (!fi.empty()) folder = folderSetMachine(fi) == 0;
                if (auto* l = j.get("Lnks")) if (l->t == Json::Arr) for (auto& x : l->a) {
                    std::string file = x.str("File"), ico = x.str("Ico"); std::wstring wf = widen(file);
                    bool have = false; for (auto& b : backup) if (b.file == file) have = true;
                    if (!have) { LnkChange c; c.file = file; if (!lnkReadIcon(wf, c.oldPath, c.oldIndex)) { failIt(file, "ярлык не читается"); continue; } backup.push_back(c); }
                    if (lnkWriteIcon(wf, ico, 0)) ok++; else failIt(file, "ярлык не записывается (нет доступа или файл занят)");
                }
            }
            Json bo = Json::Object(); Json arr = Json::Array(); for (auto& b : backup) { Json x = Json::Object(); x.set("File", Json::String(b.file)); x.set("OldPath", Json::String(b.oldPath)); x.set("OldIndex", Json::Number(b.oldIndex)); arr.a.push_back(x); }
            bo.set("Lnk", arr); std::string bs; gs::jsonDump(bo, bs); gs::writeFileAtomic(backupFile, bs);
            out += std::string("folder=") + (folder ? "1" : "0") + "\nok=" + std::to_string(ok) + "\nfail=" + std::to_string(fail) + "\n" + fails;
        }
    } catch (const std::exception& e) { out += std::string("error=") + e.what() + "\n"; code = 1; }
    if (!resultFile.empty()) gs::writeFileAtomic(widen(resultFile), out);
    return code;
}

static std::string runMachine(Job& job, std::vector<std::pair<std::string, std::string>>& failures, bool& folderOk, int& lnkOk) {
    ensureDirs();
    std::wstring jobFile = rootDir() + L"\\machine-job.json", resultFile = rootDir() + L"\\machine-result.txt", backupFile = rootDir() + L"\\machine-lnk-backup.json";
    Json j = Json::Object();
    if (job.hasFolder) j.set("FolderIco", Json::String(job.folderIco));
    Json ls = Json::Array(); for (auto& l : job.lnks) { Json x = Json::Object(); x.set("File", Json::String(l.first)); x.set("Ico", Json::String(l.second)); ls.a.push_back(x); } j.set("Lnks", ls);
    j.set("BackupFile", Json::String(narrow(backupFile))); j.set("ResultFile", Json::String(narrow(resultFile))); j.set("Restore", Json::Boolean(job.restore));
    std::string js; gs::jsonDump(j, js); gs::writeFileAtomic(jobFile, js); DeleteFileW(resultFile.c_str());
    if (isAdmin()) runMachineJob(jobFile);
    else {
        std::wstring args = L"--icons-machine=\"" + jobFile + L"\"";
        SHELLEXECUTEINFOW sei{sizeof sei}; sei.fMask = SEE_MASK_NOCLOSEPROCESS; sei.lpVerb = L"runas"; sei.lpFile = gs::exePath().c_str(); sei.lpParameters = args.c_str(); sei.nShow = SW_HIDE;
        std::wstring exe = gs::exePath(); sei.lpFile = exe.c_str();
        if (!ShellExecuteExW(&sei)) return GetLastError() == ERROR_CANCELLED ? "разрешение администратора не дано" : "не удалось запросить права администратора";
        if (sei.hProcess) { WaitForSingleObject(sei.hProcess, 120000); CloseHandle(sei.hProcess); }
    }
    std::string t; if (!gs::readFile(resultFile, t)) return "нет ответа от действия администратора";
    std::string err; folderOk = false; lnkOk = 0;
    for (auto& l : splitLines(t)) {
        size_t i = l.find('='); if (i == std::string::npos) continue; std::string k = l.substr(0, i), v = l.substr(i + 1);
        if (k == "folder") folderOk = v == "1"; else if (k == "ok") lnkOk = std::atoi(v.c_str()); else if (k == "error") err = v;
        else if (k == "failitem") { size_t b = v.rfind('|'); if (b != std::string::npos) failures.push_back({v.substr(0, b), v.substr(b + 1)}); }
    }
    return err;
}

}  // namespace ic

namespace ic { bool icLoadForTest(const std::string& loc, std::vector<Img>& out) { return loadSizes(loc, out); } }
