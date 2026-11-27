// Модуль «Значки»: один цвет для значков Windows (диски, «Этот компьютер», типы файлов, ярлыки, папки пользователя).
#pragma once
#include <atomic>
#include <mutex>
#include <thread>
#include <wrl/client.h>
#include "ic_core.hpp"
#include "module.hpp"

namespace ic {

class IconsModule : public sp::Module {
public:
    IconsModule();
    ~IconsModule() override;
    const char* id() const override { return "icons"; }
    const char* title() const override { return "Значки"; }
    void init(ID3D11Device* dev) override { dev_ = dev; }
    void shutdownUi() override { texBefore_.clear(); texAfter_.clear(); dev_ = nullptr; }
    void drawPanel() override;
    bool hasPreview() const override { return true; }
    void drawPreview() override;
    bool active() const override { return engine_.applied(); }
    const char* skinPart() const override { return "icons"; }
    bool skinCapture(gs::Json& out) override;
    std::string skinApply(const gs::Json& part) override;
    void revert() override;
    int selfTest(const std::wstring& dir, std::string& log) override;
private:
    Settings draft_;
    Engine engine_;
    ID3D11Device* dev_ = nullptr;
    std::mutex mu_;
    std::thread job_;
    std::atomic<bool> busy_{false};
    std::string status_, progress_;
    ApplyResult last_; bool haveLast_ = false;
    std::vector<Preview> previews_; bool previewsFresh_ = false, previewBusy_ = false; double previewAt_ = 0; std::string previewKey_;
    std::vector<Microsoft::WRL::ComPtr<ID3D11ShaderResourceView>> texBefore_, texAfter_;
    std::thread previewJob_;
    void startApply();
    void startRevert();
    void startPreview();
    Microsoft::WRL::ComPtr<ID3D11ShaderResourceView> makeTex(const Img& im);
};

}  // namespace ic
