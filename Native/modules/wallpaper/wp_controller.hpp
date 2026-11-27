// Держит обои на рабочем столе: окно на каждый монитор (за значками), страница WebView2 внутри, паузы, перезапуск Проводника.
#pragma once
#include <windows.h>
#include <memory>
#include <set>
#include <string>
#include <vector>
#include "webview.hpp"
#include "wp_model.hpp"
#include "wp_telemetry.hpp"

namespace wp {

struct MonitorInfoLite { RECT rect; bool primary; };
std::vector<MonitorInfoLite> monitors();

// Адрес страницы обоев (для рабочего стола, предпросмотра и редактора).
struct LoadOpts { bool edit = false; std::string background; bool useBackground = false; };
std::wstring entryUrl(const Wallpaper& w, const Settings& s, const std::map<std::string, std::string>* valuesOverride, const std::wstring& host, const std::string& extra);

// Окно с браузером: общий код для рабочего стола и окна редактора. Настраивает безопасность и открывает обои.
class PageHost {
public:
    PageHost() = default;
    // Открыть обои в браузере окна hwnd. ready вызывается, когда страница пошла грузиться.
    void open(HWND hwnd, const Wallpaper& w, const Settings& s, const LoadOpts& o, Telemetry* tel, std::function<void(const std::string&)> onMessage, std::function<void(bool)> done);
    void navigateTo(const Wallpaper& w, const Settings& s, const LoadOpts& o, const std::map<std::string, std::string>* values);
    sp::WebView web;
    std::set<std::string> kinds;
    bool ready = false;
    bool failed = false;
    std::wstring error;
    std::shared_ptr<bool> alive = std::make_shared<bool>(true);
    ~PageHost() { *alive = false; }
private:
    Telemetry* tel_ = nullptr;
};

class Controller {
public:
    Controller();
    ~Controller();
    void apply(const Settings& s);               // включить, сменить или убрать обои (повтор с теми же настройками ничего не пересоздаёт)
    void stopAll();
    bool running() const { return !hosts_.empty(); }
    const std::string& status() const { return status_; }
    Telemetry& telemetry() { return tel_; }
    // для проверок
    int hostCount() const { return (int)hosts_.size(); }
    bool allReady() const;
    bool anyFailed() const;
    std::string describe() const;                 // что видно: окна, родитель, размеры
    bool pausedAll() const;
    std::wstring lastErrors() const;
    void executeOnFirst(const std::wstring& js, std::function<void(const std::string&)> done);
private:
    struct Host;
    std::vector<std::unique_ptr<Host>> hosts_;
    Settings settings_;
    std::string signature_, status_, monSig_;
    HWND timerWin_ = nullptr;
    bool busy_ = false;
    Telemetry tel_;
    int tickN_ = 0;
    static LRESULT CALLBACK timerProc(HWND, UINT, WPARAM, LPARAM);
    void onTimer();
    void slowTick();
    void createHosts(const Wallpaper& w);
    void updateWanted();
    void setStatus(const std::string& s) { status_ = s; }
};

// рабочий стол
struct DesktopTarget { HWND parent = nullptr, insertAfter = nullptr; std::string mode; };
DesktopTarget findDesktop();
bool attachToDesktop(HWND hwnd, const RECT& monitor, std::string* mode = nullptr);
bool stillAttached(HWND hwnd);
std::string dumpDesktop();
void repaintDesktop();
bool desktopCovered(const RECT& monitor);

}  // namespace wp
