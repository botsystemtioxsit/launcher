// Предпросмотр Season: тот же движок и то же рисование, но в текстуру, которую ImGui показывает как картинку.
#pragma once
#include <d3d11.h>
#include <d2d1_1.h>
#include <dwrite.h>
#include <wrl/client.h>
#include "draw.hpp"
#include "model.hpp"

namespace sp {

class SeasonPreview {
public:
    static constexpr int kW = 680, kH = 320;
    bool init(ID3D11Device* dev);
    void release();
    void sync(const Settings& s);
    void tick(double dt);
    ID3D11ShaderResourceView* srv() const { return srv_.Get(); }
    Engine& engine() { return eng_; }
    bool ready() const { return ctx_ != nullptr; }
private:
    Microsoft::WRL::ComPtr<ID2D1Factory1> fac_;
    Microsoft::WRL::ComPtr<IDWriteFactory> dw_;
    Microsoft::WRL::ComPtr<ID2D1Device> dev_;
    Microsoft::WRL::ComPtr<ID2D1DeviceContext> ctx_;
    Microsoft::WRL::ComPtr<ID2D1Bitmap1> bmp_;
    Microsoft::WRL::ComPtr<ID3D11Texture2D> tex_;
    Microsoft::WRL::ComPtr<ID3D11ShaderResourceView> srv_;
    Gfx gfx_;
    Engine eng_{7};
    bool warmed_ = false;
};

}  // namespace sp
