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
    bool active() const override { return saved_.Active; }
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
