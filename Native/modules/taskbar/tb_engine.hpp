// Движок для Windows 11: маленькая DLL (C++, GhostShellTap) подключается к панели задач внутри Проводника через публичный интерфейс XAML Diagnostics.
// Она делает две вещи: делает родной фон панели прозрачным (пока работает приложение) и измеряет, где лежат значки, часы с треем и виджеты.
// Плашки рисует само приложение в окне под панелью (см. tb_overlay). Здесь — «хозяин» DLL: достаёт её, подключает к Проводнику, пишет ей
// настройки и читает её статус. Если Проводник падает сразу после подключения (два раза подряд), движок сам отключается.
#pragma once
#include <windows.h>
#include <atomic>
#include <functional>
#include <mutex>
#include <string>
#include <thread>
#include <vector>
#include "tb_logic.hpp"

namespace tb {

class Engine {
public:
    Engine();
    ~Engine();
    static bool osSupported();                       // Windows 11, 64-бит
    static bool dllIncluded();                       // в exe вложен GhostShellTap.dll
    static std::string unavailableReason();          // пусто — можно
    void update(const Appearance& s);                // включить/выключить движок под настройки
    void shutdown();                                 // вернуть панели родной фон и дождаться этого
    bool ready() const { std::lock_guard<std::mutex> g(mu_); return ready_; }
    bool needsRestart() const { std::lock_guard<std::mutex> g(mu_); return needsRestart_; }
    std::string status() const { std::lock_guard<std::mutex> g(mu_); return status_; }
    bool blocked();
    void clearBlock();
    bool barTransparent() const { return barTransparent_; }
    void noteExplorerRestart();
    std::function<std::string()> debugText;          // подробности от окна с плашками для отчёта
    std::function<void()> onChanged;
    std::string buildReport();                       // ждёт ~1,5 с (движок записывает дерево элементов) — вызывать не из потока окна
    bool saveReport(std::wstring& path);
    std::string uploadReport();                      // пусто — отправлено, иначе текст ошибки
    static std::string engineLogTail();
    std::string engineVersion() const { return "7"; }

private:
    mutable std::mutex mu_;
    Appearance settings_;
    std::string status_;
    bool ready_ = false, needsRestart_ = false;
    std::thread th_;
    std::atomic<bool> run_{false};
    std::atomic<bool> barTransparent_{false};
    HMODULE hookModule_ = nullptr; HHOOK hook_ = nullptr;
    DWORD lastPid_ = 0, injectedPid_ = 0;
    ULONGLONG injectAt_ = 0, connectedSince_ = 0, lastCfgWrite_ = 0;
    std::atomic<ULONGLONG> expectedRestartUntil_{0};
    int attempts_ = 0, dumpCounter_ = 0;
    bool crashCounted_ = false, autoReported_ = false;
    struct Health { int crashes = 0; bool blocked = false; long long blockedAtMs = 0; } health_;
    std::vector<std::string> rules_;
    std::thread rulesTh_;

    void setState(const std::string& text, bool isReady);
    void loop();
    void tick();
    void onExplorerChanged(DWORD newPid);
    void inject(HWND hwnd, DWORD pid);
    void unhook();
    void writeCfg(bool enabled);
    void loadHealth(); void saveHealth();
    void refreshRules();
    void start(); void stop();
};

// Цвет плашек: Color (в Ghost Style — модуль «Цвет») кладёт пожелание в color-plus-taskbar.json
struct ColorLink { bool active = false; std::string mode = "default", color = "#101018"; int opacity = 60; bool hideStroke = false; };
ColorLink colorLinkCurrent();
bool colorLinkRefresh();                              // true — файл изменился с прошлого чтения

}  // namespace tb
