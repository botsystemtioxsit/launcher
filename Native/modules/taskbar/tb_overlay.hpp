// Окно с плашками: тонкое окно без рамки, которое лежит прямо ПОД панелью задач (в порядке окон — сразу за ней), пропускает все клики
// и не берёт фокус. Родной фон панели прозрачный (его делает движок), поэтому плашки видны сквозь неё, а значки и часы рисуются поверх.
// Рисует Direct2D в буфер с попиксельной прозрачностью: края скруглений гладкие.
#pragma once
#include <windows.h>
#include <map>
#include <memory>
#include <string>
#include <vector>
#include "tb_engine.hpp"
#include "tb_logic.hpp"

namespace tb {

struct Fill { bool has = false; double r = 0, g = 0, b = 0, a = 0; };
Fill resolveFill();                 // цвет плашек: у цвета (если применён), иначе тёмный/светлый, как у панели Windows

class OverlayWindow {
public:
    explicit OverlayWindow(const RECT& rc);
    ~OverlayWindow();
    HWND hwnd() const { return hwnd_; }
    void setShapes(const std::vector<RoundRect>& rects, const Fill& fill);
    void place(HWND taskbar, const RECT& rc);
    void hide();
    bool shown() const { return shown_; }
private:
    HWND hwnd_ = nullptr;
    std::vector<RoundRect> rects_; Fill fill_{};
    RECT lastRect_{};
    bool shown_ = false, dirty_ = true;
    int w_ = 0, h_ = 0;
    void redraw();
    void ensureBuffer(int w, int h);
    HDC mem_ = nullptr; HBITMAP bmp_ = nullptr, old_ = nullptr; void* bits_ = nullptr;
    struct D2D; std::unique_ptr<D2D> d2d_;
};

class OverlayManager {
public:
    explicit OverlayManager(Engine* engine);
    ~OverlayManager();
    void update(const Appearance& s);
    void tick();                    // вызывать ~25 раз в секунду из потока окна
    const std::string& note() const { return note_; }
    std::string debugText() const;
    bool lastPlan(std::vector<RoundRect>& out) const { if (plans_.empty()) return false; out = plans_.begin()->second; return true; }
    int windowCount() const { return (int)windows_.size(); }
    bool anyShown() const { for (auto& kv : windows_) if (kv.second->shown()) return true; return false; }
    // для проверок: что видит менеджер
    std::string describeWindows() const;
private:
    Engine* engine_;
    Appearance s_;
    long long settingsVersion_ = 0;
    std::map<HWND, std::unique_ptr<OverlayWindow>> windows_;
    std::map<HWND, std::string> notes_, planKeys_;
    std::map<HWND, std::vector<RoundRect>> plans_;
    GeometryFile geo_; bool haveGeo_ = false; FILETIME geoStamp_{}; ULONGLONG geoCheckedAt_ = 0;
    std::string note_;
    bool active_ = false;
    void refreshGeometry();
    const BarGeometry* match(const RECT& rc, bool primary) const;
    void hideAll();
};

}  // namespace tb
