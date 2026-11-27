#include "scanner.hpp"
#include <dwmapi.h>
#include <mutex>
#include <set>
#include <string>

namespace sp {

static std::mutex gPlatesMu;
static std::vector<Surf> gPlates;
static ULONGLONG gPlatesAt = 0;
void setTaskbarPlates(const std::vector<Surf>& plates) { std::lock_guard<std::mutex> g(gPlatesMu); gPlates = plates; gPlatesAt = GetTickCount64(); }
std::vector<Surf> taskbarPlates() { std::lock_guard<std::mutex> g(gPlatesMu); if (GetTickCount64() - gPlatesAt > 2000) return {}; return gPlates; }

namespace {

bool isWin11() {
    typedef LONG (WINAPI* RtlGetVersionFn)(OSVERSIONINFOW*);
    auto fn = (RtlGetVersionFn)(void*)GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "RtlGetVersion");
    OSVERSIONINFOW v{}; v.dwOSVersionInfoSize = sizeof v;
    if (fn && fn(&v) == 0) return v.dwBuildNumber >= 22000;
    return false;
}

RECT frameRect(HWND h) {
    RECT r{};
    if (SUCCEEDED(DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, &r, sizeof r)) && r.right > r.left) return r;
    GetWindowRect(h, &r);
    return r;
}

std::vector<Seg> coveredAt(const std::vector<RECT>& covers, double y) {
    std::vector<Seg> res;
    for (auto& c : covers) if (c.top <= y && y < c.bottom) res.push_back({(double)c.left, (double)c.right});
    return res;
}

struct Ctx { DWORD pid; int vy; double radius; std::vector<Surf>* out; std::vector<RECT> covers; };

BOOL CALLBACK enumProc(HWND h, LPARAM lp) {
    static const std::set<std::wstring> skip = {L"Progman", L"WorkerW", L"Windows.UI.Core.CoreWindow", L"ApplicationFrameInputSinkWindow", L"XamlExplorerHostIslandWindow", L"TopLevelWindowForOverflowXamlIsland"};
    auto* c = (Ctx*)lp;
    if (!IsWindowVisible(h) || IsIconic(h)) return TRUE;
    int cloaked = 0;
    if (SUCCEEDED(DwmGetWindowAttribute(h, DWMWA_CLOAKED, &cloaked, sizeof cloaked)) && cloaked) return TRUE;
    DWORD pid = 0; GetWindowThreadProcessId(h, &pid);
    wchar_t cls[64] = {}; GetClassNameW(h, cls, 64);
    std::wstring cn = cls;
    bool tray = cn == L"Shell_TrayWnd" || cn == L"Shell_SecondaryTrayWnd";
    if (!tray && skip.count(cn)) return TRUE;
    LONG_PTR ex = GetWindowLongPtrW(h, GWL_EXSTYLE);
    if (!tray) {
        if ((ex & WS_EX_TRANSPARENT) || (ex & WS_EX_NOACTIVATE)) return TRUE;
        if ((ex & WS_EX_TOOLWINDOW) && !(ex & WS_EX_APPWINDOW)) return TRUE;
    }
    RECT r = frameRect(h);
    int w = r.right - r.left, hg = r.bottom - r.top;
    if (w < 10 || hg < 10) return TRUE;

    if (tray) {                                   // панель задач: снег ложится на верхний край, если она внизу и не спрятана
        HMONITOR mon = MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST);
        MONITORINFO mi{}; mi.cbSize = sizeof mi;
        if (mon && GetMonitorInfoW(mon, &mi)) {
            bool atBottom = w > hg && r.top > (mi.rcMonitor.top + mi.rcMonitor.bottom) / 2;
            bool hidden = r.top >= mi.rcMonitor.bottom - 3;
            auto plates = taskbarPlates();
            if (atBottom && !hidden && !plates.empty()) {            // «Панель» рисует плашки — снег ложится на них, а не на невидимый край панели
                for (auto& pl : plates) {
                    if (pl.top < r.top - 4 || pl.top > r.bottom) continue;   // плашка другой панели (другой монитор)
                    Surf s = pl; s.taskbar = true;
                    s.vis = subtract(s.left, s.right, coveredAt(c->covers, s.top));
                    if (!s.vis.empty()) c->out->push_back(std::move(s));
                }
            } else if (atBottom && !hidden) {
                Surf s; s.id = (long long)(intptr_t)h; s.left = r.left; s.right = r.right; s.top = r.top; s.radius = 0; s.taskbar = true;
                s.vis = subtract(s.left, s.right, coveredAt(c->covers, s.top));
                if (!s.vis.empty()) c->out->push_back(std::move(s));
            }
        }
        c->covers.push_back(r);
        return TRUE;
    }
    bool maximized = IsZoomed(h);
    if (!maximized && w >= 140 && hg >= 80 && r.top > c->vy + 24) {
        Surf s; s.id = (long long)(intptr_t)h; s.left = r.left; s.right = r.right; s.top = r.top; s.radius = c->radius;
        s.vis = subtract(s.left, s.right, coveredAt(c->covers, s.top));
        if (!s.vis.empty()) c->out->push_back(std::move(s));
    }
    c->covers.push_back(r);
    return TRUE;
}

}  // namespace

std::vector<Surf> scanWindows(DWORD ownPid, int vy) {
    std::vector<Surf> res;
    static const double radius = isWin11() ? 8.0 : 0.0;
    Ctx c{ownPid, vy, radius, &res, {}};
    EnumWindows(enumProc, (LPARAM)&c);
    return res;
}

}  // namespace sp
