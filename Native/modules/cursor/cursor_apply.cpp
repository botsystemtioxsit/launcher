#include "cursor_apply.hpp"
#include <windows.h>
#include <algorithm>
#include <cstdio>
#include <map>
#include <set>
#include "cursor_files.hpp"
#include "cursor_shapes.hpp"
#include "win.hpp"

namespace sp {
namespace {

struct Role { const char* name; const char* defFile; const char* kind; };   // kind: own — рисуем сами; shape — форма из настройки; sys — системный указатель, перекрашенный
const Role kRoles[] = {
    {"Arrow", "aero_arrow.cur", "shape"}, {"Hand", "aero_link.cur", "shape"}, {"UpArrow", "aero_up.cur", "shape"},
    {"IBeam", "", "own:ibeam"}, {"Crosshair", "", "own:crosshair"}, {"No", "aero_unavail.cur", "own:no"},
    {"Help", "aero_helpsel.cur", "sys"}, {"SizeNS", "aero_ns.cur", "sys"}, {"SizeWE", "aero_ew.cur", "sys"},
    {"SizeNWSE", "aero_nwse.cur", "sys"}, {"SizeNESW", "aero_nesw.cur", "sys"}, {"SizeAll", "aero_move.cur", "sys"},
};

std::wstring curKey() { return L"Control Panel\\Cursors"; }

std::wstring expandEnv(const std::wstring& s) {
    wchar_t buf[MAX_PATH * 2] = {};
    DWORD n = ExpandEnvironmentStringsW(s.c_str(), buf, (DWORD)std::size(buf));
    return n ? std::wstring(buf) : s;
}

bool readReg(const wchar_t* name, std::wstring& v) {
    HKEY k; if (RegOpenKeyExW(HKEY_CURRENT_USER, curKey().c_str(), 0, KEY_QUERY_VALUE, &k) != ERROR_SUCCESS) return false;
    wchar_t buf[1024]; DWORD sz = sizeof buf, type = 0;
    bool ok = RegQueryValueExW(k, name, nullptr, &type, (BYTE*)buf, &sz) == ERROR_SUCCESS && (type == REG_SZ || type == REG_EXPAND_SZ);
    RegCloseKey(k);
    if (ok) v = buf;
    return ok;
}
bool writeRegStr(const wchar_t* name, const std::wstring& v) {
    HKEY k; if (RegCreateKeyExW(HKEY_CURRENT_USER, curKey().c_str(), 0, nullptr, 0, KEY_SET_VALUE, nullptr, &k, nullptr) != ERROR_SUCCESS) return false;
    bool ok = RegSetValueExW(k, name, 0, REG_EXPAND_SZ, (const BYTE*)v.c_str(), (DWORD)((v.size() + 1) * sizeof(wchar_t))) == ERROR_SUCCESS;
    RegCloseKey(k); return ok;
}
void deleteReg(const wchar_t* name) { HKEY k; if (RegOpenKeyExW(HKEY_CURRENT_USER, curKey().c_str(), 0, KEY_SET_VALUE, &k) == ERROR_SUCCESS) { RegDeleteValueW(k, name); RegCloseKey(k); } }
bool readDword(const wchar_t* key, const wchar_t* name, DWORD& v) {
    HKEY k; if (RegOpenKeyExW(HKEY_CURRENT_USER, key, 0, KEY_QUERY_VALUE, &k) != ERROR_SUCCESS) return false;
    DWORD sz = sizeof v, type = 0; bool ok = RegQueryValueExW(k, name, nullptr, &type, (BYTE*)&v, &sz) == ERROR_SUCCESS && type == REG_DWORD; RegCloseKey(k); return ok;
}
void writeDword(const wchar_t* key, const wchar_t* name, DWORD v) {
    HKEY k; if (RegCreateKeyExW(HKEY_CURRENT_USER, key, 0, nullptr, 0, KEY_SET_VALUE, nullptr, &k, nullptr) != ERROR_SUCCESS) return;
    RegSetValueExW(k, name, 0, REG_DWORD, (const BYTE*)&v, sizeof v); RegCloseKey(k);
}
void deleteDword(const wchar_t* key, const wchar_t* name) { HKEY k; if (RegOpenKeyExW(HKEY_CURRENT_USER, key, 0, KEY_SET_VALUE, &k) == ERROR_SUCCESS) { RegDeleteValueW(k, name); RegCloseKey(k); } }

const wchar_t* kAccKey = L"Software\\Microsoft\\Accessibility";

std::wstring originalsPath() { return CursorSettings::dir() + L"\\originals.json"; }

// Что было в Windows до нас: запоминаем один раз.
gs::Json loadOrCaptureOriginals() {
    std::string t; gs::Json j;
    if (gs::readFile(originalsPath(), t) && gs::jsonParse(t, j) && j.t == gs::Json::Obj) return j;
    j = gs::Json::Object();
    gs::Json roles = gs::Json::Object();
    for (auto& r : kRoles) {
        std::wstring v; bool has = readReg(gs::widen(r.name).c_str(), v);
        gs::Json e = gs::Json::Object(); e.set("has", gs::Json::Boolean(has)); e.set("value", gs::Json::String(gs::narrow(v)));
        roles.set(r.name, e);
    }
    j.set("roles", roles);
    DWORD v = 0;
    if (readDword(curKey().c_str(), L"CursorBaseSize", v)) j.set("baseSize", gs::Json::Number(v));
    if (readDword(kAccKey, L"CursorSize", v)) j.set("accSize", gs::Json::Number(v));
    gs::makeDirs(CursorSettings::dir());
    std::string out; gs::jsonDump(j, out); gs::writeFileAtomic(originalsPath(), out);
    return j;
}

std::wstring originalFile(const gs::Json& orig, const Role& r) {
    std::wstring file;
    if (auto* roles = orig.get("roles")) if (auto* e = roles->get(r.name)) { if (e->boolean("has")) file = expandEnv(gs::widen(e->str("value"))); }
    if (file.empty() && r.defFile[0]) { wchar_t win[MAX_PATH] = {}; GetWindowsDirectoryW(win, MAX_PATH); file = std::wstring(win) + L"\\Cursors\\" + gs::widen(r.defFile); }
    return file;
}

}  // namespace

int baseSizePx(const CursorSettings& s) { return std::clamp((int)std::lround(32.0 * s.Size / 100.0 / 16.0) * 16, 32, 256); }

std::string applyCursors(const CursorSettings& s) {
    gs::Json orig = loadOrCaptureOriginals();
    int base = baseSizePx(s);
    std::wstring root = CursorSettings::dir() + L"\\cursors";
    gs::makeDirs(root);
    char lab[32]; std::snprintf(lab, sizeof lab, "c%08lX", (unsigned long)GetTickCount());
    std::wstring dir = root + L"\\" + gs::widen(lab);
    gs::makeDirs(dir);
    Rgb fill = s.fillRgb(), line = s.lineRgb();
    const int sizes[] = {32, 48, 64, 96, 128, 192, 256};
    std::string errors;
    for (auto& r : kRoles) {
        std::string kind = r.kind;
        std::string shape;
        bool doIt = true;
        if (kind == "shape") shape = s.Shape == "windows" || s.Shape.rfind("mine:", 0) == 0 ? "" : s.Shape;
        else if (kind.rfind("own:", 0) == 0) { shape = kind.substr(4); doIt = s.All; }
        else doIt = s.All;
        std::vector<CurImg> imgs;
        for (int px : sizes) {
            CurImg im;
            bool ok = false;
            if (!shape.empty()) ok = renderShapeImg(shape, px, fill, line, im);
            else {
                std::wstring src = originalFile(orig, r);
                ok = !src.empty() && loadSystemCursorImg(src, px, im);
                if (ok) tintImg(im, fill, line);
            }
            if (ok) imgs.push_back(std::move(im));
        }
        if (!doIt || imgs.empty()) {
            // не меняем этот указатель: возвращаем прежний (после прошлого применения мог остаться наш файл)
            std::wstring nm = gs::widen(r.name);
            if (auto* roles = orig.get("roles")) if (auto* e = roles->get(r.name)) { if (e->boolean("has")) writeRegStr(nm.c_str(), gs::widen(e->str("value"))); else deleteReg(nm.c_str()); }
            continue;
        }
        std::string data = buildCur(imgs);
        std::wstring path = dir + L"\\" + gs::widen(r.name) + L".cur";
        if (data.empty() || !gs::writeFileAtomic(path, data)) { errors += std::string(r.name) + ": файл не записался; "; continue; }
        if (!writeRegStr(gs::widen(r.name).c_str(), path)) errors += std::string(r.name) + ": реестр не принял; ";
    }
    writeDword(curKey().c_str(), L"CursorBaseSize", (DWORD)base);
    writeDword(kAccKey, L"CursorSize", (DWORD)((base - 32) / 16 + 1));
    SystemParametersInfoW(SPI_SETCURSORS, 0, nullptr, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
    // прежние папки с файлами убираем (Windows уже перечитала указатели)
    WIN32_FIND_DATAW fd; HANDLE h = FindFirstFileW((root + L"\\c*").c_str(), &fd);
    if (h != INVALID_HANDLE_VALUE) {
        do {
            if (!(fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) || gs::widen(lab) == fd.cFileName) continue;
            std::wstring old = root + L"\\" + fd.cFileName;
            WIN32_FIND_DATAW f2; HANDLE h2 = FindFirstFileW((old + L"\\*.cur").c_str(), &f2);
            if (h2 != INVALID_HANDLE_VALUE) { do DeleteFileW((old + L"\\" + f2.cFileName).c_str()); while (FindNextFileW(h2, &f2)); FindClose(h2); }
            RemoveDirectoryW(old.c_str());
        } while (FindNextFileW(h, &fd));
        FindClose(h);
    }
    return errors;
}

static void removeTree(const std::wstring& dir) {
    WIN32_FIND_DATAW fd; HANDLE h = FindFirstFileW((dir + L"\\*").c_str(), &fd);
    if (h != INVALID_HANDLE_VALUE) {
        do {
            std::wstring n = fd.cFileName;
            if (n == L"." || n == L"..") continue;
            std::wstring full = dir + L"\\" + n;
            if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) removeTree(full); else DeleteFileW(full.c_str());
        } while (FindNextFileW(h, &fd));
        FindClose(h);
    }
    RemoveDirectoryW(dir.c_str());
}

std::string revertCursors() {
    std::string t; gs::Json orig;
    if (!gs::readFile(originalsPath(), t) || !gs::jsonParse(t, orig) || orig.t != gs::Json::Obj) {
        SystemParametersInfoW(SPI_SETCURSORS, 0, nullptr, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        return "";
    }
    if (auto* roles = orig.get("roles")) {
        for (auto& r : kRoles) {
            auto* e = roles->get(r.name);
            if (!e) continue;
            std::wstring nm = gs::widen(r.name);
            if (e->boolean("has")) writeRegStr(nm.c_str(), gs::widen(e->str("value"))); else deleteReg(nm.c_str());
        }
    }
    if (auto* b = orig.get("baseSize")) writeDword(curKey().c_str(), L"CursorBaseSize", (DWORD)b->n); else deleteDword(curKey().c_str(), L"CursorBaseSize");
    if (auto* a = orig.get("accSize")) writeDword(kAccKey, L"CursorSize", (DWORD)a->n); else deleteDword(kAccKey, L"CursorSize");
    SystemParametersInfoW(SPI_SETCURSORS, 0, nullptr, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
    DeleteFileW(originalsPath().c_str());
    removeTree(CursorSettings::dir() + L"\\cursors");
    return "";
}

}  // namespace sp
