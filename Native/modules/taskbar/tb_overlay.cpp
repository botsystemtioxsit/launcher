#include "tb_overlay.hpp"
#include <d2d1.h>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include "json.hpp"
#include "tb_settings.hpp"
#include "win.hpp"

namespace tb {

using gs::narrow;

// ---------- цвет плашек ----------
static bool lightTheme() {
    static ULONGLONG at = 0; static bool light = false;
    ULONGLONG now = GetTickCount64();
    if (at && now - at < 5000) return light;
    at = now;
    DWORD v = 0, sz = sizeof v;
    light = RegGetValueW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", L"SystemUsesLightTheme", RRF_RT_REG_DWORD, nullptr, &v, &sz) == ERROR_SUCCESS && v != 0;
    return light;
}
static void parseHex(const std::string& hex, double& r, double& g, double& b) {
    unsigned v = 0; std::sscanf(hex.c_str() + (hex.size() && hex[0] == '#' ? 1 : 0), "%x", &v);
    r = ((v >> 16) & 255) / 255.0; g = ((v >> 8) & 255) / 255.0; b = (v & 255) / 255.0;
}
Fill resolveFill() {
    ColorLink l = colorLinkCurrent();
    Fill f; f.has = true;
    if (l.active) {
        if (l.mode == "clear") { f.has = false; return f; }
        if (l.mode == "solid" || l.mode == "acrylic") { parseHex(l.color, f.r, f.g, f.b); f.a = std::clamp(l.opacity / 100.0, 0.05, 1.0); return f; }
    }
    if (lightTheme()) { parseHex("#F3F3F3", f.r, f.g, f.b); } else parseHex("#1F1F1F", f.r, f.g, f.b);
    f.a = 0.88;
    return f;
}

// ---------- окно ----------
struct OverlayWindow::D2D {
    ID2D1Factory* factory = nullptr;
    ID2D1DCRenderTarget* rt = nullptr;
    ~D2D() { if (rt) rt->Release(); if (factory) factory->Release(); }
};

static const wchar_t* kOverlayClass = L"GhostShellTaskbarPlusOverlay";
static LRESULT CALLBACK overlayProc(HWND h, UINT m, WPARAM w, LPARAM l) {
    if (m == WM_NCHITTEST) return HTTRANSPARENT;
    if (m == WM_MOUSEACTIVATE) return MA_NOACTIVATE;
    return DefWindowProcW(h, m, w, l);
}

OverlayWindow::OverlayWindow(const RECT& rc) {
    static bool reg = false;
    if (!reg) { reg = true; WNDCLASSEXW wc{}; wc.cbSize = sizeof wc; wc.lpfnWndProc = overlayProc; wc.hInstance = GetModuleHandleW(nullptr); wc.lpszClassName = kOverlayClass; RegisterClassExW(&wc); }
    hwnd_ = CreateWindowExW(WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST, kOverlayClass, kOverlayClass, WS_POPUP, rc.left, rc.top,
                            std::max<LONG>(1, rc.right - rc.left), std::max<LONG>(1, rc.bottom - rc.top), nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
    d2d_ = std::make_unique<D2D>();
    D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, &d2d_->factory);
}
OverlayWindow::~OverlayWindow() {
    if (mem_) { SelectObject(mem_, old_); DeleteObject(bmp_); DeleteDC(mem_); }
    if (hwnd_) DestroyWindow(hwnd_);
}

void OverlayWindow::ensureBuffer(int w, int h) {
    if (mem_ && w == w_ && h == h_) return;
    if (mem_) { SelectObject(mem_, old_); DeleteObject(bmp_); DeleteDC(mem_); mem_ = nullptr; }
    HDC scr = GetDC(nullptr); mem_ = CreateCompatibleDC(scr); ReleaseDC(nullptr, scr);
    BITMAPINFO bi{}; bi.bmiHeader.biSize = sizeof bi.bmiHeader; bi.bmiHeader.biWidth = w; bi.bmiHeader.biHeight = -h; bi.bmiHeader.biPlanes = 1; bi.bmiHeader.biBitCount = 32; bi.bmiHeader.biCompression = BI_RGB;
    bmp_ = CreateDIBSection(mem_, &bi, DIB_RGB_COLORS, &bits_, nullptr, 0);
    old_ = (HBITMAP)SelectObject(mem_, bmp_);
    w_ = w; h_ = h; dirty_ = true;
    if (d2d_->rt) { d2d_->rt->Release(); d2d_->rt = nullptr; }
}

void OverlayWindow::redraw() {
    RECT rc{}; GetWindowRect(hwnd_, &rc);
    int w = std::max<LONG>(1, rc.right - rc.left), h = std::max<LONG>(1, rc.bottom - rc.top);
    ensureBuffer(w, h);
    if (!d2d_->factory) return;
    if (!d2d_->rt) {
        D2D1_RENDER_TARGET_PROPERTIES p = D2D1::RenderTargetProperties(D2D1_RENDER_TARGET_TYPE_DEFAULT, D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED), 0, 0, D2D1_RENDER_TARGET_USAGE_NONE, D2D1_FEATURE_LEVEL_DEFAULT);
        if (FAILED(d2d_->factory->CreateDCRenderTarget(&p, &d2d_->rt))) return;
    }
    RECT bind{0, 0, w, h};
    memset(bits_, 0, (size_t)w * h * 4);
    d2d_->rt->BindDC(mem_, &bind);
    d2d_->rt->BeginDraw();
    d2d_->rt->Clear(D2D1::ColorF(0, 0, 0, 0));
    if (fill_.has && !rects_.empty()) {
        ID2D1SolidColorBrush* br = nullptr;
        d2d_->rt->CreateSolidColorBrush(D2D1::ColorF((float)fill_.r, (float)fill_.g, (float)fill_.b, (float)fill_.a), &br);
        std::vector<ID2D1Geometry*> geoms;
        for (auto& q : rects_) {
            ID2D1RoundedRectangleGeometry* g = nullptr;
            if (SUCCEEDED(d2d_->factory->CreateRoundedRectangleGeometry(D2D1::RoundedRect(D2D1::RectF((float)q.x, (float)q.y, (float)(q.x + q.w), (float)(q.y + q.h)), (float)q.r, (float)q.r), &g))) geoms.push_back(g);
        }
        ID2D1GeometryGroup* group = nullptr;
        if (br && !geoms.empty() && SUCCEEDED(d2d_->factory->CreateGeometryGroup(D2D1_FILL_MODE_WINDING, geoms.data(), (UINT32)geoms.size(), &group))) {
            d2d_->rt->FillGeometry(group, br);   // одна заливка на все плашки: пересечения не темнеют дважды
            group->Release();
        }
        for (auto* g : geoms) g->Release();
        if (br) br->Release();
    }
    HRESULT hr = d2d_->rt->EndDraw();
    if (hr == D2DERR_RECREATE_TARGET) { d2d_->rt->Release(); d2d_->rt = nullptr; return; }
    POINT dst{rc.left, rc.top}, src{0, 0}; SIZE sz{w, h};
    BLENDFUNCTION bf{AC_SRC_OVER, 0, 255, AC_SRC_ALPHA};
    HDC scr = GetDC(nullptr);
    UpdateLayeredWindow(hwnd_, scr, &dst, &sz, mem_, &src, 0, &bf, ULW_ALPHA);
    ReleaseDC(nullptr, scr);
    dirty_ = false;
}

void OverlayWindow::setShapes(const std::vector<RoundRect>& rects, const Fill& fill) {
    bool same = rects == rects_ && fill.has == fill_.has && std::fabs(fill.a - fill_.a) < 0.001 && fill.r == fill_.r && fill.g == fill_.g && fill.b == fill_.b;
    if (same && !dirty_) return;
    rects_ = rects; fill_ = fill; dirty_ = true;
}

// Ставит окно на место панели — СРАЗУ ПОД ней в порядке окон (она выше, мы сразу за ней).
void OverlayWindow::place(HWND taskbar, const RECT& rc) {
    // окно плашек должно лежать под панелью; между ними могут оказаться другие наши прозрачные слои — это нормально (иначе окна дерутся за порядок и панель мигает)
    bool below = false; { HWND w = GetWindow(taskbar, GW_HWNDNEXT); for (int i = 0; i < 4 && w; i++, w = GetWindow(w, GW_HWNDNEXT)) if (w == hwnd_) { below = true; break; } }
    ULONGLONG nowT = GetTickCount64();
    bool moved = rc.left != lastRect_.left || rc.top != lastRect_.top || rc.right - rc.left != lastRect_.right - lastRect_.left || rc.bottom - rc.top != lastRect_.bottom - lastRect_.top;
    if (!shown_ || moved || (!below && nowT - lastPlaceAt_ > 1000)) {
        lastPlaceAt_ = nowT;
        SetWindowPos(hwnd_, taskbar, rc.left, rc.top, std::max<LONG>(1, rc.right - rc.left), std::max<LONG>(1, rc.bottom - rc.top), SWP_NOACTIVATE | (shown_ ? 0 : SWP_SHOWWINDOW));
        lastRect_ = rc; shown_ = true; dirty_ = true;
    }
    if (dirty_) redraw();
}
void OverlayWindow::hide() { if (!shown_) return; ShowWindow(hwnd_, SW_HIDE); shown_ = false; }

// ---------- менеджер ----------
OverlayManager::OverlayManager(Engine* e) : engine_(e) {}
OverlayManager::~OverlayManager() { windows_.clear(); }

void OverlayManager::update(const Appearance& s) {
    s_ = s; settingsVersion_++;
    active_ = s.Shape != "none" && Engine::unavailableReason().empty();
    if (!active_) { hideAll(); note_.clear(); }
}

static std::vector<HWND> taskbarWindows() {
    std::vector<HWND> v;
    HWND m = FindWindowW(L"Shell_TrayWnd", nullptr); if (m) v.push_back(m);
    HWND h = nullptr;
    while ((h = FindWindowExW(nullptr, h, L"Shell_SecondaryTrayWnd", nullptr))) v.push_back(h);
    return v;
}

void OverlayManager::refreshGeometry() {
    ULONGLONG now = GetTickCount64();
    if (now - geoCheckedAt_ < 40) return;
    geoCheckedAt_ = now;
    std::wstring p = gs::appDataDir() + L"\\taskbar-plus-geometry.txt";
    WIN32_FILE_ATTRIBUTE_DATA fa{};
    if (!GetFileAttributesExW(p.c_str(), GetFileExInfoStandard, &fa)) { haveGeo_ = false; geoStamp_ = FILETIME{}; return; }
    if (CompareFileTime(&fa.ftLastWriteTime, &geoStamp_) == 0) return;
    HANDLE h = CreateFileW(p.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) return;
    std::string t; char buf[8192]; DWORD n = 0;
    while (ReadFile(h, buf, sizeof buf, &n, nullptr) && n) t.append(buf, n);
    CloseHandle(h);
    GeometryFile g;
    if (!GeometryFile::parse(t, g)) return;
    geo_ = g; haveGeo_ = true; geoStamp_ = fa.ftLastWriteTime;
}

// Какой панели принадлежат измерения: по размеру окна XAML (он совпадает с размером окна панели).
const BarGeometry* OverlayManager::match(const RECT& rc, bool primary) const {
    if (!haveGeo_ || geo_.bars.empty()) return nullptr;
    for (auto& b : geo_.bars) if (std::abs(b.rootW - (rc.right - rc.left)) <= 3 && std::abs(b.rootH - (rc.bottom - rc.top)) <= 3) return &b;
    return primary && geo_.bars.size() == 1 ? &geo_.bars[0] : nullptr;   // размеры не сошлись (редкий масштаб): единственная панель — основная
}

static double dpiScale(HWND h) { UINT d = GetDpiForWindow(h); return d ? d / 96.0 : 1.0; }
static bool topmost(HWND h) { return (GetWindowLongPtrW(h, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0; }
static bool hiddenAtBottom(HWND h, const RECT& rc, double scale) {
    HMONITOR mon = MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST); MONITORINFO mi{sizeof mi};
    if (!mon || !GetMonitorInfoW(mon, &mi)) return false;
    return rc.top >= mi.rcMonitor.bottom - (int)std::lround(6 * scale);
}

void OverlayManager::tick() {
    if (!active_) return;
    try {
        if (!engine_->barTransparent()) { hideAll(); note_ = engine_->ready() ? "Панель ещё не стала прозрачной — плашки появятся, когда движок её подготовит." : ""; return; }
        refreshGeometry();
        Fill fill = resolveFill();
        std::vector<HWND> seen; auto wins = taskbarWindows();
        std::vector<std::string> issues;
        for (size_t n = 0; n < wins.size(); n++) {
            HWND hwnd = wins[n]; RECT rc{};
            if (!GetWindowRect(hwnd, &rc)) continue;
            double scale = dpiScale(hwnd);
            std::string hide;
            if (!IsWindowVisible(hwnd)) hide = "панель не видна";
            else if (!topmost(hwnd)) hide = "панель опущена под окно (полноэкранное приложение)";
            else if (hiddenAtBottom(hwnd, rc, scale)) hide = "панель спрятана (автоскрытие)";
            else if (gs::fullscreenBusy()) hide = "на экране полноэкранное приложение";
            const BarGeometry* bar = hide.empty() ? match(rc, n == 0) : nullptr;
            if (hide.empty() && !bar) hide = haveGeo_ ? "измерения не относятся к этой панели" : "нет данных от движка о положении значков";
            if (!hide.empty()) {
                auto it = windows_.find(hwnd); if (it != windows_.end()) it->second->hide();
                notes_[hwnd] = hide;
                if ((n == 0 && hide.rfind("нет данных", 0) == 0) || hide.rfind("измерения", 0) == 0) issues.push_back(hide);
                seen.push_back(hwnd); continue;
            }
            // план плашек пересчитываем, только когда поменялись настройки, измерения или размер окна
            char kb[160]; std::snprintf(kb, sizeof kb, "%lld|%lu%lu|%ldx%ld|%d", settingsVersion_, (unsigned long)geoStamp_.dwHighDateTime, (unsigned long)geoStamp_.dwLowDateTime, rc.right - rc.left, rc.bottom - rc.top, bar->scale100);
            std::string key = kb;
            if (planKeys_[hwnd] != key) { plans_[hwnd] = buildShapes(s_, rc.right - rc.left, rc.bottom - rc.top, bar->scale(), *bar); planKeys_[hwnd] = key; }
            auto& ow = windows_[hwnd]; if (!ow) ow = std::make_unique<OverlayWindow>(rc);
            auto& plan = plans_[hwnd];
            ow->setShapes(plan, fill);
            ow->place(hwnd, rc);
            notes_[hwnd] = plan.empty() ? "плашек нет: значки не найдены" : "плашек: " + std::to_string(plan.size());
            if (plan.empty()) issues.push_back("значки панели не найдены");
            seen.push_back(hwnd);
        }
        for (auto it = windows_.begin(); it != windows_.end();) {   // панели, которых больше нет (перезапуск Проводника, отключён монитор)
            if (std::find(seen.begin(), seen.end(), it->first) == seen.end()) { notes_.erase(it->first); plans_.erase(it->first); planKeys_.erase(it->first); it = windows_.erase(it); } else ++it;
        }
        std::string note;
        for (auto& i : issues) if (note.find(i) == std::string::npos) note += (note.empty() ? "" : "; ") + i;
        note_ = note;
    } catch (...) { note_ = "плашки: ошибка"; }
}

void OverlayManager::hideAll() { for (auto& kv : windows_) kv.second->hide(); }

std::string OverlayManager::describeWindows() const {
    std::string o;
    for (auto& kv : windows_) {
        RECT tb{}, ov{}; GetWindowRect(kv.first, &tb); GetWindowRect(kv.second->hwnd(), &ov);
        char b[400]; std::snprintf(b, sizeof b, "панель %p (%ld,%ld,%ld,%ld); окно плашек %s (%ld,%ld,%ld,%ld); сразу под панелью: %s\n", (void*)kv.first, tb.left, tb.top, tb.right, tb.bottom,
            kv.second->shown() ? "показано" : "скрыто", ov.left, ov.top, ov.right, ov.bottom, GetWindow(kv.first, GW_HWNDNEXT) == kv.second->hwnd() ? "да" : "нет");
        o += b;
    }
    return o;
}

std::string OverlayManager::debugText() const {
    std::string o = "Плашки: " + (note_.empty() ? std::string("работают") : note_) + "\n" + describeWindows();
    for (auto& kv : plans_) for (auto& r : kv.second) { char b[160]; std::snprintf(b, sizeof b, "    плашка x=%.0f y=%.0f ш=%.0f в=%.0f скругление=%.0f\n", r.x, r.y, r.w, r.h, r.r); o += b; }
    for (auto& b : geo_.bars) {
        char t[400]; std::snprintf(t, sizeof t, "  измерения: окно %dx%d масштаб %d%%, значки %d..%d, виджеты %d..%d, трей %d..%d, отдельных значков %d\n", b.rootW, b.rootH, b.scale100, b.apps ? b.apps->L : 0, b.apps ? b.apps->R : 0,
            b.widgets ? b.widgets->L : 0, b.widgets ? b.widgets->R : 0, b.system ? b.system->L : 0, b.system ? b.system->R : 0, (int)b.icons.size());
        o += t;
    }
    return o;
}

}  // namespace tb
