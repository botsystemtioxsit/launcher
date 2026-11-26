// Модуль «Панель»: панель задач Windows 11 по-своему — плавающая, вкладка, острова под значками. Движок в Проводнике делает родной фон прозрачным
// и измеряет значки; плашки рисует окно под панелью (tb_overlay). Предпросмотр и настоящая панель считаются одним кодом (buildShapes).
#pragma once
#include <memory>
#include <string>
#include <thread>
#include "gpu_canvas.hpp"
#include "module.hpp"
#include "tb_engine.hpp"
#include "tb_logic.hpp"
#include "tb_overlay.hpp"

namespace tb {

class TaskbarModule : public sp::Module {
public:
    TaskbarModule();
    ~TaskbarModule() override;
    const char* id() const override { return "taskbar"; }
    const char* title() const override { return "Панель"; }
    void init(ID3D11Device* dev) override;
    void shutdownUi() override { canvas_.release(); }
    void tick(double dt) override;
    void drawPanel() override;
    bool active() const override { return saved_.Shape != "none"; }
    void start() override;
    void stop() override;
    void revert() override;
    int selfTest(const std::wstring& dir, std::string& log) override;
private:
    Appearance draft_, saved_;
    Engine engine_;
    OverlayManager overlay_;
    sp::GpuCanvas canvas_;
    HWND timerWin_ = nullptr;
    std::string status_, reportMsg_;
    bool started_ = false;
    std::thread job_; std::atomic<bool> jobBusy_{false}; std::string jobResult_; std::mutex jobMu_;
    int shapeSel_ = 0;
    void apply();
    void drawPreview(sp::Gfx& g);
    void applyEngine();
    static LRESULT CALLBACK timerProc(HWND, UINT, WPARAM, LPARAM);
    void presetFrom(int sel);
    int selFrom(const Appearance& a) const;
};

}  // namespace tb
