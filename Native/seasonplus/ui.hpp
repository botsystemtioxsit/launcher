// Окно настроек Season Plus (Dear ImGui на Direct3D 11): предпросмотр на выдуманном рабочем столе, сезон, праздник, гирлянда, «Включить».
#pragma once
#include <d3d11.h>
#include <d2d1_1.h>
#include <dwrite.h>
#include <wrl/client.h>
#include <functional>
#include "draw.hpp"
#include "model.hpp"

namespace sp {

using Microsoft::WRL::ComPtr;

// Предпросмотр: тот же движок и то же рисование, но в текстуру, которую ImGui показывает как картинку.
class Preview {
public:
    static constexpr int kW = 680, kH = 320;
    bool init(ID3D11Device* dev);
    void sync(const Settings& s);
    void tick(double dt);
    ID3D11ShaderResourceView* srv() const { return srv_.Get(); }
    Engine& engine() { return eng_; }
private:
    ComPtr<ID2D1Factory1> fac_;
    ComPtr<IDWriteFactory> dw_;
    ComPtr<ID2D1Device> dev_;
    ComPtr<ID2D1DeviceContext> ctx_;
    ComPtr<ID2D1Bitmap1> bmp_;
    ComPtr<ID3D11Texture2D> tex_;
    ComPtr<ID3D11ShaderResourceView> srv_;
    Gfx gfx_;
    Engine eng_{7};
    bool warmed_ = false;
};

struct UiHooks {
    std::function<void(const Settings&)> apply;     // «Включить» / «Применить»
    std::function<void()> revert;                   // «Выключить»
    std::function<void()> fireworks;                // салют на настоящем экране
};

class UiApp {
public:
    UiApp(const Settings& saved, UiHooks hooks) : draft_(saved), saved_(saved), hooks_(std::move(hooks)) {}
    bool init(ID3D11Device* dev) { return preview_.init(dev); }
    void tick(double dt) { preview_.tick(dt); }
    void frame();                                   // между ImGui::NewFrame() и ImGui::Render()
    void setStatusActive(bool on) { draft_.Active = on; }
    static void setupStyle(float scale);
    static bool loadFonts(float scale);
private:
    Settings draft_, saved_;
    UiHooks hooks_;
    Preview preview_;
    std::string status_;
    bool changed_ = false;
    void changed();
    bool seg(const char* id, const char* const* labels, int n, int& sel);
    bool slider(const char* title, const char* hint, int& v, int lo, int hi);
    bool toggle(const char* title, const char* hint, bool& v);
    void rowTitle(const char* title, const char* hint);
};

}  // namespace sp
