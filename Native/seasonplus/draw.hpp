#pragma once
#include "engine.hpp"
#include "gfx.hpp"
namespace sp {
// Рисует всё, что есть в движке: сугробы, траву, гирлянды, украшения, частицы, отсчёт. Координаты — экранные, вычитается начало экрана.
void drawScene(Gfx& g, const Engine& e);
}
