// Прозрачное окно поверх всего рабочего стола (DirectComposition): клики проходят сквозь него, фокус оно не берёт.
// Рисуется на видеокарте; если её нет — Windows сама использует программный драйвер (WARP).
#pragma once
#include <atomic>
#include <mutex>
#include <string>
#include <thread>
#include "model.hpp"

namespace sp {

class Overlay {
public:
    ~Overlay() { stop(); }
    void start(const Settings& s);
    void stop();
    void setSettings(const Settings& s);
    void fireworks() { fw_ = true; }
    bool running() const { return run_ && th_.joinable(); }
    // сведения для проверки
    std::atomic<int> particles{0};
    std::atomic<int> framesDrawn{0};
    std::atomic<bool> healthy{true};
    std::atomic<double> avgFrameMs{0};
    std::string lastError() { std::lock_guard<std::mutex> l(mu_); return err_; }

private:
    std::thread th_;
    std::atomic<bool> run_{false}, fw_{false};
    std::mutex mu_;
    Settings settings_;
    std::string err_;
    void loop();
    void fail(const std::string& m) { std::lock_guard<std::mutex> l(mu_); err_ = m; healthy = false; }
};

}  // namespace sp
