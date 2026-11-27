#pragma once
#include <string>
#include "module.hpp"
#include "preview.hpp"
#include "season_layer.hpp"

namespace sp {

class SeasonModule : public Module {
public:
    SeasonModule();
    const char* id() const override { return "season"; }
    const char* title() const override { return "Сезоны"; }
    void init(ID3D11Device* dev) override;
    void shutdownUi() override { preview_.release(); }
    void tick(double dt) override { if (preview_.ready()) preview_.tick(dt); }
    void drawPanel() override;
    bool hasPreview() const override { return true; }
    void drawPreview() override;
    bool active() const override { return saved_.Active; }
    const char* skinPart() const override { return "season"; }
    bool skinCapture(gs::Json& out) override { if (!saved_.Active) return false; out = saved_.toJson(); return true; }
    std::string skinApply(const gs::Json& part) override { Settings s = Settings::fromJson(part); s.Active = true; applyNow(s); return ""; }
    void revert() override;
    int snapshots(const std::wstring& dir, std::string& log) override;
    void prepareOverlayTest() override;
    Layer* layer() override { return &layer_; }
    SeasonLayer& seasonLayer() { return layer_; }
    // для скинов и проверок
    Settings& settings() { return draft_; }
    void applyNow(const Settings& s);

private:
    Settings draft_, saved_;
    SeasonLayer layer_;
    SeasonPreview preview_;
    std::string status_;
    void changed();
};

}  // namespace sp
