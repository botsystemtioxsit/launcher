#pragma once
#include <array>
#include <string>
#include "cursor_settings.hpp"
#include "gpu_canvas.hpp"
#include "module.hpp"
#include "trail_layer.hpp"

namespace sp {

class CursorModule : public Module {
public:
    CursorModule();
    const char* id() const override { return "cursor"; }
    const char* title() const override { return "Курсор"; }
    void init(ID3D11Device* dev) override;
    void shutdownUi() override;
    void tick(double dt) override;
    void drawPanel() override;
    bool active() const override { return saved_.Applied; }
    bool needsBackground() const override { return saved_.OverlayOn && saved_.wantsOverlay(); }
    void revert() override;
    Layer* layer() override { return &trail_; }
    int snapshots(const std::wstring& dir, std::string& log) override;
    int selfTest(const std::wstring& dir, std::string& log) override;
    void prepareOverlayTest() override;
    CursorSettings& settings() { return draft_; }
    std::string applyNow(const CursorSettings& s);

private:
    CursorSettings draft_, saved_;
    TrailLayer trail_{5};          // настоящий (на экране)
    TrailLayer preview_{9};        // для предпросмотра в окне
    GpuCanvas canvas_;
    std::array<GpuCanvas, 9> thumbs_;
    bool thumbsDirty_ = true;
    double simT_ = 0, clickT_ = 0;
    std::string status_;
    void changed();
    void syncPreview();
    void drawPreview(Gfx& g);
    void drawThumbs();
};

}  // namespace sp
