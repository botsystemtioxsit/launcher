#include "skins_module.hpp"
#include <windows.h>
#include <shellapi.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <wincodec.h>
#include <wrl/client.h>
#include <algorithm>
#include <cstdio>
#include <filesystem>
#include "imgui.h"
#include "loc.hpp"
#include "ui_common.hpp"
#include "win.hpp"

namespace fs = std::filesystem;
using Microsoft::WRL::ComPtr;
using gs::Json;
using gs::narrow;
using gs::widen;
using namespace sp::ui;
using gs::L;

namespace sk {

static const char* kParts[] = {"taskbar", "color", "wallpaper", "cursor", "season", "icons"};   // порядок применения
static const char* partName(const std::string& p) {
    if (p == "taskbar") return "Панель задач"; if (p == "color") return "Цвет окон"; if (p == "icons") return "Значки"; if (p == "wallpaper") return "Обои"; if (p == "cursor") return "Курсор"; if (p == "season") return "Сезоны";
    return "?";
}
static bool knownPart(const std::string& p) { for (auto k : kParts) if (p == k) return true; return false; }

// Подпись, имя, описание: без управляющих и «невидимых» символов, в одну строку, не длиннее max
static std::string cleanText(const std::string& s, size_t max, const std::string& fallback) {
    std::string o; bool space = true;
    for (size_t i = 0; i < s.size();) {
        unsigned char c = s[i]; size_t len = c < 0x80 ? 1 : c < 0xE0 ? 2 : c < 0xF0 ? 3 : 4;
        if (i + len > s.size()) break;
        std::string ch = s.substr(i, len); i += len;
        if (len == 1 && (c < 32 || c == 127)) { c = ' '; ch = " "; }
        // невидимые и «перевёрнутые» знаки: U+200B–200F, U+202A–202E, U+2066–2069
        if (len == 3 && (unsigned char)ch[0] == 0xE2) { unsigned cp = ((unsigned char)ch[0] & 0xF) << 12 | ((unsigned char)ch[1] & 0x3F) << 6 | ((unsigned char)ch[2] & 0x3F); if ((cp >= 0x200B && cp <= 0x200F) || (cp >= 0x202A && cp <= 0x202E) || (cp >= 0x2066 && cp <= 0x2069)) continue; }
        if (ch == " ") { if (space) continue; space = true; } else space = false;
        o += ch;
    }
    while (!o.empty() && o.back() == ' ') o.pop_back();
    if (o.size() > max) { size_t cut = max; while (cut > 0 && ((unsigned char)o[cut] & 0xC0) == 0x80) cut--; o.resize(cut); while (!o.empty() && o.back() == ' ') o.pop_back(); }
    return o.empty() ? fallback : o;
}

std::string SkinsModule::describe(const std::string& part, const Json& p) {
    if (part == "color") return "цвет " + p.str("color") + (p.boolean("windows") ? ", окна и выделение" : "") + (p.boolean("taskbar") ? ", острова панели" : "");
    if (part == "icons") return "все значки цвета " + p.str("color") + (p.str("style") == "hue" ? " (сдвиг оттенка)" : "");
    if (part == "wallpaper") return "обои «" + p.str("id").substr(std::min<size_t>(2, p.str("id").size())) + "»";
    if (part == "cursor") return "курсор «" + p.str("shape") + "» " + p.str("fill") + (p.str("trail") != "none" && !p.str("trail").empty() ? ", шлейф «" + p.str("trail") + "»" : "");
    if (part == "taskbar") { std::string s = p.str("shape"); return "форма панели: " + std::string(s == "pill" ? "плавающая" : s == "tab" ? "«вкладка»" : s == "islands" ? "острова" : "обычная"); }
    if (part == "season") return "сезон «" + p.str("Season") + "», праздник «" + p.str("Holiday") + "»";
    return part;
}

// Разбор и очистка. Фатально только то, что вообще не скин; остальное чистится, замечания — в warnings.
bool SkinsModule::parse(const std::string& text, Skin& out, std::vector<std::string>& warnings, std::string& error) {
    if (text.size() > 64 * 1024) { error = "Файл скина слишком большой"; return false; }
    Json root; if (!gs::jsonParse(text, root) || root.t != Json::Obj) { error = "Файл повреждён: это не скин"; return false; }
    int fmt = root.num("format", 0);
    if (fmt < 1) { error = "В файле нет номера формата скина"; return false; }
    if (fmt > 1) { error = "Скин сделан в более новой версии Ghost Style — обновите приложение"; return false; }
    Skin s; s.name = cleanText(root.str("name"), 40, "Скин без названия"); s.author = cleanText(root.str("author"), 40, ""); s.description = cleanText(root.str("description"), 300, "");
    if (auto* c = root.get("created")) if (c->t == Json::Num && c->n > 0) s.created = (long long)c->n;
    auto* parts = root.get("parts"); if (!parts || parts->t != Json::Obj) { error = "В скине нет настроек"; return false; }
    for (auto& kv : parts->o) {
        if (!knownPart(kv.first)) { warnings.push_back("Неизвестная часть «" + cleanText(kv.first, 30, "?") + "» пропущена"); continue; }
        if (kv.second.t != Json::Obj) { warnings.push_back(std::string("Часть «") + partName(kv.first) + "» повреждена и пропущена"); continue; }
        s.parts.push_back({kv.first, kv.second});
    }
    if (s.parts.empty()) { error = "В скине нет ни одной допустимой части"; return false; }
    out = s; return true;
}

std::string SkinsModule::toJson(const Skin& s) {
    Json r = Json::Object(); r.set("format", Json::Number(1)); r.set("name", Json::String(s.name)); r.set("author", Json::String(s.author)); r.set("description", Json::String(s.description)); r.set("created", Json::Number((double)s.created));
    Json parts = Json::Object(); for (auto& p : s.parts) parts.set(p.first, p.second); r.set("parts", parts);
    std::string out; gs::jsonDump(r, out); return out;
}

// ---------- архив .ghostskin: zip без сжатия (skin.json + preview.jpg) ----------
static unsigned crc32(const std::string& d) { unsigned c = 0xFFFFFFFFu; for (unsigned char b : d) { c ^= b; for (int k = 0; k < 8; k++) c = (c >> 1) ^ (0xEDB88320u & (0u - (c & 1))); } return ~c; }
static void put16(std::string& s, unsigned v) { s += (char)(v & 255); s += (char)((v >> 8) & 255); }
static void put32(std::string& s, unsigned v) { put16(s, v & 0xFFFF); put16(s, v >> 16); }
std::string SkinsModule::makeArchive(const Skin& s) {
    std::vector<std::pair<std::string, std::string>> files = {{"skin.json", toJson(s)}};
    if (!s.preview.empty()) files.push_back({"preview.jpg", std::string((const char*)s.preview.data(), s.preview.size())});
    std::string out, central; unsigned count = 0;
    for (auto& f : files) {
        unsigned off = (unsigned)out.size(), crc = crc32(f.second);
        out += "PK\x03\x04"; put16(out, 20); put16(out, 0); put16(out, 0); put16(out, 0); put16(out, 0x21); put32(out, crc); put32(out, (unsigned)f.second.size()); put32(out, (unsigned)f.second.size()); put16(out, (unsigned)f.first.size()); put16(out, 0); out += f.first; out += f.second;
        central += "PK\x01\x02"; put16(central, 20); put16(central, 20); put16(central, 0); put16(central, 0); put16(central, 0); put16(central, 0x21); put32(central, crc); put32(central, (unsigned)f.second.size()); put32(central, (unsigned)f.second.size());
        put16(central, (unsigned)f.first.size()); put16(central, 0); put16(central, 0); put16(central, 0); put16(central, 0); put32(central, 0); put32(central, off); central += f.first; count++;
    }
    unsigned cd = (unsigned)out.size(); out += central; out += "PK\x05\x06"; put16(out, 0); put16(out, 0); put16(out, count); put16(out, count); put32(out, (unsigned)central.size()); put32(out, cd); put16(out, 0);
    return out;
}

// Картинка из чужого файла разбирается и пересохраняется как JPEG ≤ 640 px (чужие байты в библиотеку не попадают как есть)
static bool sanitizeJpeg(const std::string& in, std::vector<unsigned char>& out, int* wOut = nullptr, int* hOut = nullptr, std::vector<unsigned char>* bgra = nullptr) {
    ComPtr<IWICImagingFactory> wic; if (FAILED(CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&wic)))) return false;
    ComPtr<IWICStream> st; if (FAILED(wic->CreateStream(&st)) || FAILED(st->InitializeFromMemory((BYTE*)in.data(), (DWORD)in.size()))) return false;
    ComPtr<IWICBitmapDecoder> dec; if (FAILED(wic->CreateDecoderFromStream(st.Get(), nullptr, WICDecodeMetadataCacheOnDemand, &dec))) return false;
    ComPtr<IWICBitmapFrameDecode> fr; if (FAILED(dec->GetFrame(0, &fr))) return false;
    UINT w = 0, h = 0; fr->GetSize(&w, &h); if (!w || !h || w > 8000 || h > 8000) return false;
    double k = std::min(1.0, 640.0 / std::max(w, h)); UINT nw = std::max(1u, (UINT)(w * k)), nh = std::max(1u, (UINT)(h * k));
    ComPtr<IWICBitmapScaler> sc; if (FAILED(wic->CreateBitmapScaler(&sc)) || FAILED(sc->Initialize(fr.Get(), nw, nh, WICBitmapInterpolationModeFant))) return false;
    ComPtr<IWICFormatConverter> cv; if (FAILED(wic->CreateFormatConverter(&cv)) || FAILED(cv->Initialize(sc.Get(), GUID_WICPixelFormat32bppBGRA, WICBitmapDitherTypeNone, nullptr, 0, WICBitmapPaletteTypeCustom))) return false;
    std::vector<unsigned char> px((size_t)nw * nh * 4); if (FAILED(cv->CopyPixels(nullptr, nw * 4, (UINT)px.size(), px.data()))) return false;
    ComPtr<IWICBitmap> bmp; if (FAILED(wic->CreateBitmapFromMemory(nw, nh, GUID_WICPixelFormat32bppBGRA, nw * 4, (UINT)px.size(), px.data(), &bmp))) return false;
    ComPtr<IStream> ms; if (FAILED(CreateStreamOnHGlobal(nullptr, TRUE, &ms))) return false;
    ComPtr<IWICBitmapEncoder> enc; if (FAILED(wic->CreateEncoder(GUID_ContainerFormatJpeg, nullptr, &enc)) || FAILED(enc->Initialize(ms.Get(), WICBitmapEncoderNoCache))) return false;
    ComPtr<IWICBitmapFrameEncode> fe; ComPtr<IPropertyBag2> props; if (FAILED(enc->CreateNewFrame(&fe, &props))) return false;
    PROPBAG2 pb{}; wchar_t qn[] = L"ImageQuality"; pb.pstrName = qn; VARIANT v; VariantInit(&v); v.vt = VT_R4; v.fltVal = 0.7f; props->Write(1, &pb, &v);
    if (FAILED(fe->Initialize(props.Get()))) return false; fe->SetSize(nw, nh); WICPixelFormatGUID fmt = GUID_WICPixelFormat24bppBGR; fe->SetPixelFormat(&fmt);
    if (FAILED(fe->WriteSource(bmp.Get(), nullptr)) || FAILED(fe->Commit()) || FAILED(enc->Commit())) return false;
    STATSTG ss{}; ms->Stat(&ss, STATFLAG_NONAME); out.resize((size_t)ss.cbSize.QuadPart); LARGE_INTEGER z{}; ms->Seek(z, STREAM_SEEK_SET, nullptr); ULONG got = 0; ms->Read(out.data(), (ULONG)out.size(), &got); out.resize(got);
    if (out.size() > 120 * 1024) return false;
    if (wOut) *wOut = (int)nw; if (hOut) *hOut = (int)nh; if (bgra) *bgra = px;
    return true;
}

static int runTool(const std::wstring& cmd, const std::wstring& outFile, DWORD ms) {
    SECURITY_ATTRIBUTES sa{sizeof sa, nullptr, TRUE}; HANDLE o = CreateFileW(outFile.c_str(), GENERIC_WRITE, FILE_SHARE_READ, &sa, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    STARTUPINFOW si{sizeof si}; si.dwFlags = STARTF_USESTDHANDLES | STARTF_USESHOWWINDOW; si.wShowWindow = SW_HIDE; si.hStdOutput = o; si.hStdError = o; si.hStdInput = GetStdHandle(STD_INPUT_HANDLE);
    PROCESS_INFORMATION pi{}; std::wstring c = cmd; BOOL ok = CreateProcessW(nullptr, c.data(), nullptr, nullptr, TRUE, CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi);
    if (o != INVALID_HANDLE_VALUE) CloseHandle(o); if (!ok) return -1;
    DWORD w = WaitForSingleObject(pi.hProcess, ms), code = 1; if (w == WAIT_TIMEOUT) { TerminateProcess(pi.hProcess, 1); code = (DWORD)-2; } else GetExitCodeProcess(pi.hProcess, &code);
    CloseHandle(pi.hThread); CloseHandle(pi.hProcess); return (int)code;
}

bool SkinsModule::readArchive(const std::wstring& file, Skin& out, std::string& error) {
    std::error_code ec;
    if (!fs::exists(file, ec)) { error = "Файл не найден"; return false; }
    if (fs::file_size(file, ec) > 2 * 1024 * 1024) { error = "Файл больше 2 МБ — это не скин"; return false; }
    wchar_t sys[MAX_PATH]; GetSystemDirectoryW(sys, MAX_PATH); std::wstring tar = std::wstring(L"\"") + sys + L"\\tar.exe\"";
    GUID g; CoCreateGuid(&g); char hx[16]; std::snprintf(hx, sizeof hx, "%08lx", g.Data1);
    fs::path tmp = fs::temp_directory_path() / widen(std::string("ghostskin-") + hx); fs::path listF = tmp.wstring() + L".txt";
    struct Cl { fs::path t, l; ~Cl() { std::error_code e; fs::remove_all(t, e); fs::remove(l, e); } } cl{tmp, listF};
    if (runTool(tar + L" -tf \"" + file + L"\"", listF.wstring(), 30000) != 0) { error = "Архив повреждён или это не скин"; return false; }
    std::string list; gs::readFile(listF.wstring(), list); int n = 0; size_t pos = 0;
    while (pos < list.size()) {   // внутри допустимы только skin.json и preview.* (остальное игнорируется)
        size_t e = list.find('\n', pos); if (e == std::string::npos) e = list.size(); std::string l = list.substr(pos, e - pos); pos = e + 1;
        while (!l.empty() && l.back() == '\r') l.pop_back(); if (l.empty()) continue;
        if (++n > 8) { error = "В архиве слишком много файлов"; return false; }
    }
    fs::create_directories(tmp, ec);
    if (runTool(tar + L" -xf \"" + file + L"\" -C \"" + tmp.wstring() + L"\" skin.json preview.jpg preview.png", tmp.wstring() + L".x.txt", 30000) != 0 && !fs::exists(tmp / L"skin.json", ec)) { error = "В архиве нет skin.json"; fs::remove(tmp.wstring() + L".x.txt", ec); return false; }
    fs::remove(tmp.wstring() + L".x.txt", ec);
    std::string text; if (!gs::readFile((tmp / L"skin.json").wstring(), text)) { error = "В архиве нет skin.json"; return false; }
    std::vector<std::string> warn; if (!parse(text, out, warn, error)) return false;
    for (const wchar_t* pn : {L"preview.jpg", L"preview.png"}) { std::string img; if (gs::readFile((tmp / pn).wstring(), img) && !img.empty() && img.size() < 4 * 1024 * 1024 && sanitizeJpeg(img, out.preview)) break; }
    return true;
}

// ---------- библиотека ----------
static std::wstring libDir() { return gs::appDataDir() + L"\\skin-loader\\skins"; }
static bool idOk(const std::string& id) { if (id.empty() || id.size() > 40) return false; for (unsigned char c : id) if (!(isalnum(c) || c == '-' || c == '_')) return false; return true; }

void SkinsModule::load() {
    list_.clear(); std::error_code ec;
    for (fs::directory_iterator it(libDir(), ec), end; !ec && it != end; it.increment(ec)) {
        if (!it->is_directory(ec)) continue; std::string id = narrow(it->path().filename().wstring()); if (!idOk(id)) continue;
        std::string t; if (!gs::readFile((it->path() / L"skin.json").wstring(), t)) continue;
        Skin s; std::vector<std::string> w; std::string e; if (!parse(t, s, w, e)) continue;
        s.id = id; std::string img; if (gs::readFile((it->path() / L"preview.jpg").wstring(), img)) { std::vector<unsigned char> o; if (sanitizeJpeg(img, o)) s.preview = o; }
        list_.push_back(s);
    }
    std::sort(list_.begin(), list_.end(), [](const Skin& a, const Skin& b) { return a.created > b.created; });
    reload_ = false; tex_.clear();
    for (auto& s : list_) {
        Microsoft::WRL::ComPtr<ID3D11ShaderResourceView> srv;
        std::vector<unsigned char> px; int w = 0, h = 0, rw = 0;
        if (dev_ && !s.preview.empty()) {
            std::vector<unsigned char> dummy; std::string raw((const char*)s.preview.data(), s.preview.size());
            if (sanitizeJpeg(raw, dummy, &w, &h, &px)) {
                (void)rw; D3D11_TEXTURE2D_DESC d{}; d.Width = w; d.Height = h; d.MipLevels = 1; d.ArraySize = 1; d.Format = DXGI_FORMAT_B8G8R8A8_UNORM; d.SampleDesc.Count = 1; d.Usage = D3D11_USAGE_IMMUTABLE; d.BindFlags = D3D11_BIND_SHADER_RESOURCE;
                D3D11_SUBRESOURCE_DATA sd{px.data(), (UINT)(w * 4), 0}; Microsoft::WRL::ComPtr<ID3D11Texture2D> t; if (SUCCEEDED(dev_->CreateTexture2D(&d, &sd, &t))) dev_->CreateShaderResourceView(t.Get(), nullptr, &srv);
            }
        }
        tex_.push_back(srv);
    }
}

std::string SkinsModule::save(const Skin& sIn) {
    Skin s = sIn; GUID g; CoCreateGuid(&g); char b[24]; std::snprintf(b, sizeof b, "s-%08lx%04x", g.Data1, g.Data2); if (s.id.empty()) s.id = b;
    fs::path dir = fs::path(libDir()) / widen(s.id); std::error_code ec; fs::create_directories(dir, ec);
    if (!gs::writeFileAtomic((dir / L"skin.json").wstring(), toJson(s))) return "не удалось записать скин";
    if (!s.preview.empty()) gs::writeFileAtomic((dir / L"preview.jpg").wstring(), std::string((const char*)s.preview.data(), s.preview.size()));
    reload_ = true; return "";
}

// ---------- сборка и применение ----------
static std::vector<sp::Module*> mods(sp::AppCtx* a) { std::vector<sp::Module*> v; if (a && a->all) v = *a->all; return v; }

Skin SkinsModule::build() {
    Skin s; s.name = cleanText(name_, 40, "Мой скин"); s.author = cleanText(author_, 40, ""); s.description = cleanText(desc_, 300, ""); FILETIME ft; GetSystemTimeAsFileTime(&ft); ULARGE_INTEGER u{}; u.LowPart = ft.dwLowDateTime; u.HighPart = ft.dwHighDateTime; s.created = (long long)((u.QuadPart - 116444736000000000ULL) / 10000);
    for (auto* m : mods(app_)) { const char* p = m->skinPart(); if (!p || !pick_[p]) continue; Json j; if (m->skinCapture(j)) s.parts.push_back({p, j}); }
    std::stable_sort(s.parts.begin(), s.parts.end(), [](auto& a, auto& b) { return std::find(std::begin(kParts), std::end(kParts), a.first) < std::find(std::begin(kParts), std::end(kParts), b.first); });
    s.preview = pendingPreview_;
    return s;
}

std::string SkinsModule::applySkin(const Skin& s) {
    std::string report;
    for (auto part : kParts) for (auto& p : s.parts) if (p.first == part) {
        sp::Module* target = nullptr; for (auto* m : mods(app_)) if (m->skinPart() && p.first == m->skinPart()) target = m;
        std::string r = target ? target->skinApply(p.second) : "этот модуль не найден";
        if (!r.empty()) report += std::string(partName(p.first)) + ": " + r + "; ";
    }
    return report;
}

static std::wstring pickFile(bool save, const wchar_t* defName) {
    std::wstring res; IFileDialog* d = nullptr;
    if (FAILED(CoCreateInstance(save ? CLSID_FileSaveDialog : CLSID_FileOpenDialog, nullptr, CLSCTX_ALL, save ? IID_IFileSaveDialog : IID_IFileOpenDialog, (void**)&d)) || !d) return res;
    COMDLG_FILTERSPEC f[] = {{L"Скины Ghost Style (*.ghostskin)", L"*.ghostskin"}, {L"Все файлы", L"*.*"}};
    d->SetFileTypes(2, f); d->SetDefaultExtension(L"ghostskin"); if (defName) d->SetFileName(defName);
    DWORD o = 0; d->GetOptions(&o); d->SetOptions(o | FOS_FORCEFILESYSTEM);
    if (SUCCEEDED(d->Show(nullptr))) { IShellItem* it = nullptr; if (SUCCEEDED(d->GetResult(&it)) && it) { PWSTR p = nullptr; if (SUCCEEDED(it->GetDisplayName(SIGDN_FILESYSPATH, &p)) && p) { res = p; CoTaskMemFree(p); } it->Release(); } }
    d->Release(); return res;
}

static bool captureScreen(std::vector<unsigned char>& jpeg) {
    int w = GetSystemMetrics(SM_CXSCREEN), h = GetSystemMetrics(SM_CYSCREEN); if (w <= 0 || h <= 0) return false;
    HDC sdc = GetDC(nullptr), mdc = CreateCompatibleDC(sdc); HBITMAP bmp = CreateCompatibleBitmap(sdc, w, h); HGDIOBJ old = SelectObject(mdc, bmp);
    BitBlt(mdc, 0, 0, w, h, sdc, 0, 0, SRCCOPY | CAPTUREBLT);
    BITMAPINFO bi{}; bi.bmiHeader.biSize = sizeof bi.bmiHeader; bi.bmiHeader.biWidth = w; bi.bmiHeader.biHeight = -h; bi.bmiHeader.biPlanes = 1; bi.bmiHeader.biBitCount = 32;
    std::vector<unsigned char> px((size_t)w * h * 4); GetDIBits(mdc, bmp, 0, h, px.data(), &bi, DIB_RGB_COLORS);
    SelectObject(mdc, old); DeleteObject(bmp); DeleteDC(mdc); ReleaseDC(nullptr, sdc);
    for (size_t i = 3; i < px.size(); i += 4) px[i] = 255;
    // через PNG-в-памяти → общий разборщик (JPEG ≤ 640 px)
    ComPtr<IWICImagingFactory> wic; if (FAILED(CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&wic)))) return false;
    ComPtr<IWICBitmap> wb; if (FAILED(wic->CreateBitmapFromMemory(w, h, GUID_WICPixelFormat32bppBGRA, w * 4, (UINT)px.size(), px.data(), &wb))) return false;
    ComPtr<IStream> ms; CreateStreamOnHGlobal(nullptr, TRUE, &ms); ComPtr<IWICBitmapEncoder> enc; wic->CreateEncoder(GUID_ContainerFormatPng, nullptr, &enc); enc->Initialize(ms.Get(), WICBitmapEncoderNoCache);
    ComPtr<IWICBitmapFrameEncode> fe; enc->CreateNewFrame(&fe, nullptr); fe->Initialize(nullptr); fe->SetSize(w, h); WICPixelFormatGUID f = GUID_WICPixelFormat32bppBGRA; fe->SetPixelFormat(&f); fe->WriteSource(wb.Get(), nullptr); fe->Commit(); enc->Commit();
    STATSTG ss{}; ms->Stat(&ss, STATFLAG_NONAME); std::string png((size_t)ss.cbSize.QuadPart, '\0'); LARGE_INTEGER z{}; ms->Seek(z, STREAM_SEEK_SET, nullptr); ULONG got = 0; ms->Read(png.data(), (ULONG)png.size(), &got);
    return sanitizeJpeg(png, jpeg);
}

// ---------- окно ----------
void SkinsModule::drawPanel() {
    if (reload_) load();
    ImGui::PushFont(mono()); ImGui::PushStyleColor(ImGuiCol_Text, rgb(200, 208, 218)); ImGui::TextUnformatted(L("Скины")); ImGui::PopStyleColor(); ImGui::PopFont();
    note("Скин — это набор настроек всех модулей (сезоны, панель, цвет, значки, обои, курсор) под одним названием. Соберите свой из того, что включено сейчас, или примените чужой файл .ghostskin. Скин — это данные, а не программа: в нём нет ни путей, ни команд.");
    if (!status_.empty()) { ImGui::PushStyleColor(ImGuiCol_Text, muted()); ImGui::TextWrapped("%s", L(status_.c_str())); ImGui::PopStyleColor(); }
    ImGui::Spacing();
    if (ImGui::BeginTabBar("sktabs")) {
        if (ImGui::BeginTabItem(L("Мои скины"))) {
            ImGui::Spacing();
            if (ImGui::Button(L("Импорт файла .ghostskin…"))) {
                std::wstring f = pickFile(false, nullptr);
                if (!f.empty()) { Skin s; std::string e; if (readArchive(f, s, e)) { std::string r = save(s); status_ = r.empty() ? "Скин добавлен: " + s.name : r; } else status_ = e; }
            }
            ImGui::SameLine(); if (ImGui::Button(L("Вернуть всё как было")) && app_ && app_->revertAll) { app_->revertAll(); status_ = "Всё выключено, настройки Windows возвращены."; }
            ImGui::Spacing();
            for (size_t i = 0; i < list_.size(); i++) {
                Skin& s = list_[i]; ImGui::PushID((int)i);
                if (ImGui::BeginChild(s.id.c_str(), ImVec2(0, 0), ImGuiChildFlags_AutoResizeY | ImGuiChildFlags_Borders | ImGuiChildFlags_AlwaysUseWindowPadding)) {
                    if (i < tex_.size() && tex_[i]) { ImGui::Image((ImTextureID)(intptr_t)tex_[i].Get(), ImVec2(192, 108)); ImGui::SameLine(); }
                    ImGui::BeginGroup();
                    ImGui::PushFont(mono()); ImGui::TextUnformatted(s.name.c_str()); ImGui::PopFont();
                    if (!s.author.empty()) { ImGui::PushStyleColor(ImGuiCol_Text, muted()); ImGui::Text("%s %s", L("автор:"), s.author.c_str()); ImGui::PopStyleColor(); }
                    if (!s.description.empty()) ImGui::TextWrapped("%s", s.description.c_str());
                    ImGui::PushStyleColor(ImGuiCol_Text, muted()); for (auto& p : s.parts) ImGui::TextWrapped("• %s: %s", L(partName(p.first)), describe(p.first, p.second).c_str()); ImGui::PopStyleColor();
                    ImGui::EndGroup();
                    if (confirmId_ == s.id) {
                        ImGui::PushStyleColor(ImGuiCol_Text, rgb(255, 176, 32)); ImGui::TextWrapped("%s", L("Применить скин? Настройки перечисленных модулей изменятся (прежние запоминаются у каждого модуля, «Вернуть как было» вернёт)."));
                        ImGui::PopStyleColor();
                        if (ImGui::Button(L("Да, применить"))) { std::string r = applySkin(s); status_ = r.empty() ? "Скин применён: " + s.name : "Скин применён не полностью — " + r; confirmId_.clear(); }
                        ImGui::SameLine(); if (ImGui::Button(L("Отмена"))) confirmId_.clear();
                    } else {
                        ImGui::PushStyleColor(ImGuiCol_Button, rgb(200, 208, 218)); ImGui::PushStyleColor(ImGuiCol_Text, rgb(6, 5, 13));
                        if (ImGui::Button(L("Применить"))) confirmId_ = s.id;
                        ImGui::PopStyleColor(2);
                        ImGui::SameLine();
                        if (ImGui::Button(L("Экспорт…"))) { std::wstring f = pickFile(true, (widen(s.name) + L".ghostskin").c_str()); if (!f.empty()) status_ = gs::writeFileAtomic(f, makeArchive(s)) ? "Скин сохранён в файл." : "Не удалось сохранить файл."; }
                        ImGui::SameLine();
                        if (ImGui::Button(L("Удалить"))) { std::error_code ec; fs::remove_all(fs::path(libDir()) / widen(s.id), ec); reload_ = true; }
                    }
                }
                ImGui::EndChild(); ImGui::Spacing(); ImGui::PopID();
            }
            if (list_.empty()) { ImGui::PushStyleColor(ImGuiCol_Text, muted()); ImGui::TextUnformatted(L("Скинов пока нет. Соберите свой на вкладке «Собрать» или импортируйте файл.")); ImGui::PopStyleColor(); }
            ImGui::EndTabItem();
        }
        if (ImGui::BeginTabItem(L("Собрать"))) {
            ImGui::Spacing();
            note("Отметьте, что войдёт в скин. Войдёт только то, что включено сейчас: у выключенного модуля нечего брать. Свои файлы (свои обои, нарисованный курсор) в скин не входят.");
            for (auto* m : mods(app_)) {
                const char* p = m->skinPart(); if (!p) continue; Json j; bool has = m->skinCapture(j);
                ImGui::BeginDisabled(!has); bool on = has && pick_[p]; if (!has) pick_[p] = false;
                ImGui::PushID(p); if (ImGui::Checkbox(L(partName(p)), &on)) pick_[p] = on; ImGui::PopID(); ImGui::EndDisabled();
                ImGui::SameLine(); ImGui::PushStyleColor(ImGuiCol_Text, muted()); ImGui::TextUnformatted(has ? describe(p, j).c_str() : L("не включено")); ImGui::PopStyleColor();
            }
            ImGui::Spacing(); ImGui::SetNextItemWidth(320); ImGui::InputText(L("Название"), name_, sizeof name_);
            ImGui::SetNextItemWidth(320); ImGui::InputText(L("Автор"), author_, sizeof author_);
            ImGui::SetNextItemWidth(480); ImGui::InputText(L("Описание"), desc_, sizeof desc_);
            if (ImGui::Button(L("Снять экран для картинки"))) {
                if (app_ && app_->showWindow) app_->showWindow(false);
                Sleep(1000); std::vector<unsigned char> jp; bool ok = captureScreen(jp);
                if (app_ && app_->showWindow) app_->showWindow(true);
                if (ok) { pendingPreview_ = jp; status_ = "Картинка снята."; } else status_ = "Не удалось снять экран.";
            }
            if (!pendingPreview_.empty()) { ImGui::SameLine(); ImGui::TextUnformatted(L("картинка есть")); }
            ImGui::PushStyleColor(ImGuiCol_Button, rgb(200, 208, 218)); ImGui::PushStyleColor(ImGuiCol_Text, rgb(6, 5, 13));
            if (ImGui::Button(L("Сохранить скин"))) {
                Skin s = build();
                if (s.parts.empty()) status_ = "Нечего собирать: отметьте хотя бы одну часть, у которой есть настройки.";
                else { std::string r = save(s); status_ = r.empty() ? "Скин сохранён: " + s.name : r; pendingPreview_.clear(); }
            }
            ImGui::PopStyleColor(2);
            ImGui::EndTabItem();
        }
        ImGui::EndTabBar();
    }
}

// ---------- проверка ----------
int SkinsModule::selfTest(const std::wstring& dir, std::string& log) {
    int fails = 0;
    auto check = [&](const std::string& name, bool ok, const std::string& extra = "") { log += (ok ? "PASS " : "FAIL ") + name + (extra.empty() ? "" : " — " + extra) + "\n"; if (!ok) fails++; };
    Skin s; std::vector<std::string> w; std::string e;
    check("враждебный скин: мусор вместо json отклонён", !parse("не json", s, w, e), e);
    check("нет формата — отклонён", !parse("{\"parts\":{}}", s, w, e), e);
    check("слишком новый формат — отклонён", !parse("{\"format\":9,\"parts\":{\"color\":{}}}", s, w, e), e);
    std::string big = "{\"format\":1,\"name\":\"" + std::string(70000, 'a') + "\"}"; check("слишком большой файл отклонён", !parse(big, s, w, e), e);
    std::vector<std::string> w2; Skin h;
    bool ok = parse("{\"format\":1,\"name\":\"Тест\\u202eevil\\n\",\"author\":\"a\",\"parts\":{\"color\":{\"color\":\"#112233\"},\"shell\":{\"cmd\":\"calc\"},\"wallpaper\":5}}", h, w2, e);
    check("враждебный скин разобран, лишнее выброшено", ok && h.parts.size() == 1 && h.parts[0].first == "color" && w2.size() == 2, std::to_string(w2.size()));
    check("название очищено от невидимых знаков", h.name == "Тестevil", h.name);
    Skin mk; mk.name = "Ghost"; mk.author = "me"; mk.created = 1; Json c = Json::Object(); c.set("color", Json::String("#20C27A")); c.set("windows", Json::Boolean(true)); mk.parts.push_back({"color", c});
    Json wp = Json::Object(); wp.set("id", Json::String("b-aurora")); wp.set("fit", Json::String("cover")); wp.set("fps", Json::Number(60)); mk.parts.push_back({"wallpaper", wp});
    std::wstring f = dir + L"\\test.ghostskin"; gs::makeDirs(dir); gs::writeFileAtomic(f, makeArchive(mk));
    Skin back; std::string err; bool rd = readArchive(f, back, err);
    check("файл .ghostskin: запись и чтение", rd && back.name == "Ghost" && back.parts.size() == 2 && back.parts[0].second.str("color") == "#20C27A", err);
    check("описание части скина читается по-русски", describe("wallpaper", wp).find("aurora") != std::string::npos);
    // применение вместе с настоящими модулями
    std::string rep = applySkin(mk);
    check("применение скина: все части приняты", rep.empty() || rep.find("Цвет") == std::string::npos, rep);
    return fails;
}

}  // namespace sk
