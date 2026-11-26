#include "gpu_canvas.hpp"
#include <dxgi.h>

namespace sp {

using Microsoft::WRL::ComPtr;

bool GpuCanvas::init(ID3D11Device* dev, int w, int h) {
    release();
    w_ = w; h_ = h;
    D2D1_FACTORY_OPTIONS fo{};
    if (FAILED(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, __uuidof(ID2D1Factory1), &fo, (void**)fac_.GetAddressOf()))) return false;
    if (FAILED(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory), (IUnknown**)dw_.GetAddressOf()))) return false;
    if (!gfx_.init(fac_.Get(), dw_.Get())) return false;
    ComPtr<IDXGIDevice> dxgi;
    if (FAILED(dev->QueryInterface(IID_PPV_ARGS(&dxgi)))) return false;
    if (FAILED(fac_->CreateDevice(dxgi.Get(), &dev_)) || FAILED(dev_->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE, &ctx_))) return false;
    D3D11_TEXTURE2D_DESC td{};
    td.Width = w; td.Height = h; td.MipLevels = 1; td.ArraySize = 1; td.Format = DXGI_FORMAT_B8G8R8A8_UNORM; td.SampleDesc.Count = 1;
    td.Usage = D3D11_USAGE_DEFAULT; td.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
    if (FAILED(dev->CreateTexture2D(&td, nullptr, &tex_)) || FAILED(dev->CreateShaderResourceView(tex_.Get(), nullptr, &srv_))) return false;
    ComPtr<IDXGISurface> surf;
    if (FAILED(tex_.As(&surf))) return false;
    D2D1_BITMAP_PROPERTIES1 bp = D2D1::BitmapProperties1(D2D1_BITMAP_OPTIONS_TARGET | D2D1_BITMAP_OPTIONS_CANNOT_DRAW, D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED));
    if (FAILED(ctx_->CreateBitmapFromDxgiSurface(surf.Get(), &bp, &bmp_))) return false;
    ctx_->SetTarget(bmp_.Get());
    return true;
}

void GpuCanvas::release() {
    gfx_.begin(nullptr);
    bmp_.Reset(); ctx_.Reset(); dev_.Reset(); srv_.Reset(); tex_.Reset(); dw_.Reset(); fac_.Reset();
}

void GpuCanvas::draw(const std::function<void(Gfx&)>& fn, bool clear) {
    if (!ctx_) return;
    ctx_->BeginDraw();
    if (clear) ctx_->Clear(D2D1::ColorF(0, 0, 0, 0));
    gfx_.begin(ctx_.Get());
    fn(gfx_);
    ctx_->EndDraw();
}

}  // namespace sp
