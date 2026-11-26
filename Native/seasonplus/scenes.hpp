#pragma once
#include <string>
#include "engine.hpp"
#include "gfx.hpp"
namespace sp {
// Выдуманный рабочий стол (окна, панель задач) — для предпросмотра и снимков.
std::vector<Surf> demoSurfaces(int w, int h);
void drawBackdrop(Gfx& g, int w, int h, int season, const std::string& holiday);
// Снимки сцен всех сезонов и праздников в PNG (программная отрисовка WIC: работает без видеокарты). Возвращает число записанных файлов.
int renderScenes(const std::wstring& dir, std::string& log);
// Записать картинку (B8G8R8A8, без прозрачности) в PNG.
bool savePngBgra(const std::wstring& path, int w, int h, const unsigned char* px, int stride);
}
