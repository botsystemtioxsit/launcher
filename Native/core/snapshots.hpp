#pragma once
#include <string>
namespace sp {
// Записать картинку (B8G8R8A8) в PNG средствами Windows (WIC).
bool savePngBgra(const std::wstring& path, int w, int h, const unsigned char* px, int stride);
}
