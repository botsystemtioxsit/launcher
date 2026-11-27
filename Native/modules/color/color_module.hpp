// Модуль «Цвет»: один цвет для акцента Windows, выделения и заголовков окон. Заголовки красит фоновый поток (DwmSetWindowAttribute), остальное — реестр.
#pragma once
#include <atomic>
#include <mutex>
#include <string>
#include <thread>
#include "gpu_canvas.hpp"
#include "module.hpp"

namespace sp {

struct ColorSettings {
    std::string Color = "#FF2A9D";
    bool Windows = true;        // окна (заголовки и рамки), выделение, акцент Windows
    bool Taskbar = true;        // острова панели задач (модуль «Панель»)
    bool Applied = false;
    static ColorSettings load();
    void save() const;
    void normalize();
};

class TitleEngine {
public:
    ~TitleEngine() { stop(); }
    static bool supported();
    void set(const std::string& hex);      // включить/сменить цвет заголовков
    void stop();                            // вернуть окнам цвета Windows
    bool running() const { return run_; }
    int painted() const { return painted_; }
    static void recoverLeftovers();         // убрать следы аварийно закрытого прошлого запуска
private:
    std::thread th_;
    std::atomic<bool> run_{false};
    std::mutex mu_;
    std::string color_;
    std::atomic<int> painted_{0};
    void loop();
};

class ColorModule : public Module {
public:
    ColorModule();
    const char* id() const override { return "color"; }
    const char* title() const override { return "Цвет"; }
    void init(ID3D11Device* dev) override;
    void shutdownUi() override { canvas_.release(); }
    void tick(double dt) override;
    void drawPanel() override;
    bool hasPreview() const override { return true; }
    void drawPreview() override;
    bool active() const override { return saved_.Applied; }
    const char* skinPart() const override { return "color"; }
    bool skinCapture(gs::Json& out) override { if (!saved_.Applied) return false; out = gs::Json::Object(); out.set("color", gs::Json::String(saved_.Color)); out.set("windows", gs::Json::Boolean(saved_.Windows)); out.set("taskbar", gs::Json::Boolean(saved_.Taskbar)); return true; }
    std::string skinApply(const gs::Json& p) override { ColorSettings s; s.Color = p.str("color", s.Color); s.Windows = p.boolean("windows", true); s.Taskbar = p.boolean("taskbar", true); return applyNow(s); }
    bool needsBackground() const override { return saved_.Applied && saved_.Windows && TitleEngine::supported(); }
    void revert() override;
    int selfTest(const std::wstring& dir, std::string& log) override;
    ColorSettings& settings() { return draft_; }
    std::string applyNow(const ColorSettings& s);

private:
    ColorSettings draft_, saved_;
    TitleEngine titles_;
    GpuCanvas canvas_;
    std::string status_;
    void drawPreview(Gfx& g);
};

// для проверок: цвет «читаемого» текста на фоне, палитра акцента
std::string readableOn(const std::string& hex);

}  // namespace sp
