#include "tb_registry.hpp"
#include <windows.h>
#include <shellapi.h>
#include <thread>
#include "json.hpp"
#include "tb_settings.hpp"
#include "win.hpp"

namespace tb {

using gs::Json;

static const wchar_t* kAdv = L"Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced";
static const wchar_t* kSearch = L"Software\\Microsoft\\Windows\\CurrentVersion\\Search";

static bool win11() {
    typedef LONG (WINAPI* Fn)(OSVERSIONINFOEXW*); OSVERSIONINFOEXW v{}; v.dwOSVersionInfoSize = sizeof v;
    auto fn = (Fn)GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "RtlGetVersion");
    return fn && fn(&v) == 0 && v.dwBuildNumber >= 22000;
}

const std::vector<RegOption>& regOptions() {
    static const std::vector<RegOption> o = {
        {"align", "Значки", "Где стоят значки: слева, как в Windows 10, или по центру. Справа Windows не умеет.", kAdv, L"TaskbarAl", {0, 1}, {"Слева", "По центру"}, 1, true, false, false},
        {"search", "Поиск", "Что показывать вместо строки поиска", kSearch, L"SearchboxTaskbarMode", {0, 1, 2}, {"Скрыть", "Значок", "Строка"}, 2, false, false, false},
        {"taskview", "Представление задач", "Кнопка обзора всех открытых окон", kAdv, L"ShowTaskViewButton", {1, 0}, {"Показать", "Скрыть"}, 1, false, false, false},
        {"widgets", "Виджеты", "Кнопка с погодой и новостями слева на панели. Новые сборки Windows 11 защищают её от программ — тогда её можно скрыть только вручную.", kAdv, L"TaskbarDa", {1, 0}, {"Показать", "Скрыть"}, 1, true, false, false},
        {"chat", "Чат", "Кнопка «Чат» (Teams); в новых сборках Windows 11 её уже нет", kAdv, L"TaskbarMn", {1, 0}, {"Показать", "Скрыть"}, 1, true, false, false},
        {"size", "Размер панели", "Высота панели и значков. Появляется после перезапуска панели; в самых новых сборках Windows 11 может не работать.", kAdv, L"TaskbarSi", {0, 1, 2}, {"Малая", "Средняя", "Большая"}, 1, true, false, true},
        {"small", "Размер значков", "Мелкие значки на панели", kAdv, L"TaskbarSmallIcons", {0, 1}, {"Обычные", "Мелкие"}, 0, false, true, true},
        {"combine", "Группировка кнопок", "Когда объединять окна одной программы в одну кнопку", kAdv, L"TaskbarGlomLevel", {0, 1, 2}, {"Всегда", "При нехватке места", "Никогда"}, 0, false, false, true},
        {"seconds", "Секунды в часах", "Показывать секунды в часах на панели", kAdv, L"ShowSecondsInSystemClock", {0, 1}, {"Нет", "Да"}, 0, false, false, true},
        {"animations", "Анимации панели", "Движение панели, когда она появляется и прячется. Нужны ещё анимации самой Windows (ниже).", kAdv, L"TaskbarAnimations", {1, 0}, {"Включены", "Выключены"}, 1, false, false, false},
    };
    return o;
}
bool regAvailable(const RegOption& o) { bool w = win11(); return !(o.win11Only && !w) && !(o.win10Only && w); }

static bool readVal(const RegOption& o, DWORD& v) { DWORD sz = sizeof v; return RegGetValueW(HKEY_CURRENT_USER, o.key, o.name, RRF_RT_REG_DWORD, nullptr, &v, &sz) == ERROR_SUCCESS; }
int regCurrent(const RegOption& o) { DWORD v; return readVal(o, v) ? (int)v : o.def; }
static void writeVal(const RegOption& o, bool has, DWORD v) {
    HKEY k; if (RegCreateKeyExW(HKEY_CURRENT_USER, o.key, 0, nullptr, 0, KEY_SET_VALUE, nullptr, &k, nullptr) != ERROR_SUCCESS) return;
    auto put = [&](const wchar_t* n) { if (has) RegSetValueExW(k, n, 0, REG_DWORD, (const BYTE*)&v, sizeof v); else RegDeleteValueW(k, n); };
    put(o.name);
    if (!wcscmp(o.name, L"TaskbarGlomLevel")) put(L"MMTaskbarGlomLevel");   // вторые мониторы — тот же выбор
    RegCloseKey(k);
}

static std::wstring savedPath() { return dataDir() + L"\\taskbar-plus.json"; }
static Json loadSaved() { std::string t; Json j; if (gs::readFile(savedPath(), t) && gs::jsonParse(t, j) && j.t == Json::Obj) return j; return Json::Object(); }
static void saveSaved(const Json& j) { std::string o; gs::jsonDump(j, o); gs::makeDirs(dataDir()); gs::writeFileAtomic(savedPath(), o); }

void notifySettingsChanged() {
    std::thread([] {   // в фоне: окна, которые долго отвечают, не должны вешать окно программы
        DWORD_PTR r;
        SendMessageTimeoutW(HWND_BROADCAST, WM_SETTINGCHANGE, 0, (LPARAM)L"TraySettings", SMTO_ABORTIFHUNG, 1500, &r);
        SendMessageTimeoutW(HWND_BROADCAST, WM_SETTINGCHANGE, 0, (LPARAM)L"ImmersiveColorSet", SMTO_ABORTIFHUNG, 1500, &r);
    }).detach();
}

std::string regSet(const RegOption& o, int value) {
    bool okv = false; for (int v : o.values) if (v == value) okv = true;
    if (!okv) return "Недопустимое значение";
    Json saved = loadSaved();
    Json* orig = nullptr; for (auto& kv : saved.o) if (kv.first == "Original") orig = &kv.second;
    if (!orig) { saved.set("Original", Json::Object()); for (auto& kv : saved.o) if (kv.first == "Original") orig = &kv.second; }
    if (!orig->get(o.id)) { DWORD v; orig->set(o.id, readVal(o, v) ? Json::Number(v) : Json()); saveSaved(saved); }
    writeVal(o, true, (DWORD)value);
    DWORD back; if (!readVal(o, back) || (int)back != value) return "Windows защищает этот параметр и не даёт его менять программам";   // Windows 11 тихо защищает часть параметров
    notifySettingsChanged();
    return "";
}

bool autoHideGet() { APPBARDATA d{sizeof d}; d.hWnd = FindWindowW(L"Shell_TrayWnd", nullptr); return ((UINT)SHAppBarMessage(ABM_GETSTATE, &d) & ABS_AUTOHIDE) != 0; }
std::string autoHideSet(bool on) {
    Json saved = loadSaved();
    if (!saved.get("OriginalAutoHide")) { saved.set("OriginalAutoHide", Json::Boolean(autoHideGet())); saveSaved(saved); }
    APPBARDATA d{sizeof d}; d.hWnd = FindWindowW(L"Shell_TrayWnd", nullptr); d.lParam = on ? (ABS_AUTOHIDE | ABS_ALWAYSONTOP) : ABS_ALWAYSONTOP;
    SHAppBarMessage(ABM_SETSTATE, &d);
    return "";
}
bool animationsOn() { BOOL v = TRUE; return !SystemParametersInfoW(0x1042 /* SPI_GETCLIENTAREAANIMATION */, 0, &v, 0) || v; }
void animationsEnable() { SystemParametersInfoW(0x1043 /* SPI_SETCLIENTAREAANIMATION */, 0, (PVOID)1, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE); }

// Перезапуск Проводника: закрывает его окна и панель на пару секунд; нужен для параметров, которые Windows читает только при запуске.
std::string explorerRestart() {
    STARTUPINFOW si{sizeof si}; si.dwFlags = STARTF_USESHOWWINDOW; si.wShowWindow = SW_HIDE; PROCESS_INFORMATION pi{};
    std::wstring cmd = L"taskkill /f /im explorer.exe";
    if (CreateProcessW(nullptr, cmd.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi)) { WaitForSingleObject(pi.hProcess, 5000); CloseHandle(pi.hProcess); CloseHandle(pi.hThread); }
    ShellExecuteW(nullptr, L"open", L"explorer.exe", nullptr, nullptr, SW_SHOWNORMAL);
    return "";
}

int regReset(std::string& error) {
    Json saved = loadSaved(); int restored = 0;
    if (auto* orig = saved.get("Original")) for (auto& o : regOptions()) {
        auto* v = orig->get(o.id); if (!v) continue;
        writeVal(o, v->t == Json::Num, v->t == Json::Num ? (DWORD)v->n : 0); restored++;
    }
    if (auto* a = saved.get("OriginalAutoHide")) { if (a->t == Json::Bool) { APPBARDATA d{sizeof d}; d.hWnd = FindWindowW(L"Shell_TrayWnd", nullptr); d.lParam = a->b ? (ABS_AUTOHIDE | ABS_ALWAYSONTOP) : ABS_ALWAYSONTOP; SHAppBarMessage(ABM_SETSTATE, &d); restored++; } }
    DeleteFileW(savedPath().c_str());
    notifySettingsChanged();
    error.clear();
    return restored;
}

}  // namespace tb
