#include "preview.hpp"
#include "scenes.hpp"

namespace sp {

bool SeasonPreview::init(ID3D11Device* dev) {
    D2D1_FACTORY_OPTIONS fo{};
    if (FAILED(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, __uuidof(ID2D1Factory1), &fo, (void**)fac_.GetAddressOf()))) return false;
    if (FAILED(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory), (IUnknown**)dw_.GetAddressOf()))) return false;
    if (!gfx_.init(fac_.Get(), dw_.Get())) return false;
    ComPtr<IDXGIDevice> dxgi;
    if (FAILED(dev->QueryInterface(IID_PPV_ARGS(&dxgi)))) return false;
    if (FAILED(fac_->CreateDevice(dxgi.Get(), &dev_)) || FAILED(dev_->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE, &ctx_))) return false;
    D3D11_TEXTURE2D_DESC td{};
    td.Width = kW; td.Height = kH; td.MipLevels = 1; td.ArraySize = 1; td.Format = DXGI_FORMAT_B8G8R8A8_UNORM; td.SampleDesc.Count = 1;
    td.Usage = D3D11_USAGE_DEFAULT; td.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
    if (FAILED(dev->CreateTexture2D(&td, nullptr, &tex_)) || FAILED(dev->CreateShaderResourceView(tex_.Get(), nullptr, &srv_))) return false;
    ComPtr<IDXGISurface> surf;
    if (FAILED(tex_.As(&surf))) return false;
    D2D1_BITMAP_PROPERTIES1 bp = D2D1::BitmapProperties1(D2D1_BITMAP_OPTIONS_TARGET | D2D1_BITMAP_OPTIONS_CANNOT_DRAW, D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED));
    if (FAILED(ctx_->CreateBitmapFromDxgiSurface(surf.Get(), &bp, &bmp_))) return false;
    ctx_->SetTarget(bmp_.Get());
    eng_.W = kW; eng_.H = kH; eng_.OX = 0; eng_.OY = 0;
    return true;
}

void SeasonPreview::sync(const Settings& s) {
    eng_.S = s;
    eng_.setSurfaces(demoSurfaces(kW, kH));
    eng_.step(0.001);
    if (!warmed_) { warmed_ = true; eng_.warm(14); }
}

void SeasonPreview::tick(double dt) {
    if (!ctx_) return;
    eng_.step(dt);
    ctx_->BeginDraw();
    gfx_.begin(ctx_.Get());
    drawBackdrop(gfx_, kW, kH, eng_.season, eng_.holiday);
    drawScene(gfx_, eng_);
    ctx_->EndDraw();
}

void SeasonPreview::release() {
    gfx_.begin(nullptr);
    bmp_.Reset(); ctx_.Reset(); dev_.Reset(); srv_.Reset(); tex_.Reset();
}

}  // namespace sp
