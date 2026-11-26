// Маленькая картинка, нарисованная на видеокарте (Direct2D) в текстуру Direct3D 11: ImGui показывает её как обычное изображение.
// Нужна для предпросмотров и миниатюр в окне настроек.
#pragma once
#include <d3d11.h>
#include <d2d1_1.h>
#include <dwrite.h>
#include <wrl/client.h>
#include <functional>
#include "gfx.hpp"

namespace sp {

class GpuCanvas {
public:
    bool init(ID3D11Device* dev, int w, int h);
    void release();
    bool ready() const { return ctx_ != nullptr; }
    // Очистить прозрачным и вызвать fn: внутри рисуем как обычно через Gfx.
    void draw(const std::function<void(Gfx&)>& fn, bool clear = true);
    ID3D11ShaderResourceView* srv() const { return srv_.Get(); }
    int width() const { return w_; }
    int height() const { return h_; }
private:
    Microsoft::WRL::ComPtr<ID2D1Factory1> fac_;
    Microsoft::WRL::ComPtr<IDWriteFactory> dw_;
    Microsoft::WRL::ComPtr<ID2D1Device> dev_;
    Microsoft::WRL::ComPtr<ID2D1DeviceContext> ctx_;
    Microsoft::WRL::ComPtr<ID2D1Bitmap1> bmp_;
    Microsoft::WRL::ComPtr<ID3D11Texture2D> tex_;
    Microsoft::WRL::ComPtr<ID3D11ShaderResourceView> srv_;
    Gfx gfx_;
    int w_ = 0, h_ = 0;
};

}  // namespace sp
