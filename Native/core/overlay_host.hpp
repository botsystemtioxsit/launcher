// Прозрачное окно поверх всего рабочего стола (DirectComposition): клики проходят сквозь него, фокус оно не берёт.
// Рисует все включённые слои на видеокарте; если её нет — Windows сама использует программный драйвер (WARP).
#pragma once
#include <atomic>
#include <mutex>
#include <string>
#include <thread>
#include <vector>
#include "layer.hpp"

namespace sp {

class OverlayHost {
public:
    ~OverlayHost() { stop(); }
    void addLayer(Layer* l);               // до start()
    void start();
    void stop();
    bool running() const { return run_ && th_.joinable(); }
    void wake() { wake_ = true; }          // слои изменились — проснуться, если спали

    std::atomic<int> framesDrawn{0};
    std::atomic<bool> healthy{true};
    std::atomic<double> avgFrameMs{0};
    std::string lastError() { std::lock_guard<std::mutex> l(mu_); return err_; }

private:
    std::thread th_;
    std::atomic<bool> run_{false}, wake_{false};
    std::mutex mu_;
    std::vector<Layer*> layers_;
    std::string err_;
    void loop();
    void fail(const std::string& m) { std::lock_guard<std::mutex> l(mu_); err_ = m; healthy = false; }
};

}  // namespace sp
