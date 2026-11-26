#pragma once
#include <string>
#include <vector>
#include "engine.hpp"
#include "gfx.hpp"

namespace sp {
// Выдуманный рабочий стол (окна, панель задач) — для предпросмотра и снимков.
std::vector<Surf> demoSurfaces(int w, int h);
void drawBackdrop(Gfx& g, int w, int h, int season, const std::string& holiday);
// Снимки сцен всех сезонов и праздников в PNG (программная отрисовка WIC: работает без видеокарты). Возвращает число записанных файлов.
int renderSeasonScenes(const std::wstring& dir, std::string& log);
}
