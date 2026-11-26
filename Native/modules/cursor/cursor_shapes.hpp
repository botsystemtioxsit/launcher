// Формы курсора, нарисованные кодом (векторно, на любой размер). Клетка 32×32.
#pragma once
#include <string>
#include <vector>
#include "gfx.hpp"

namespace sp {

struct ShapeInfo { const char* id; const char* title; };
extern const std::vector<ShapeInfo> kShapes;

bool isProcedural(const std::string& shape);          // classic, round, neon, dot, ring, cross, ghost, star
bool isOwnRole(const std::string& shape);              // ibeam, crosshair, no — их тоже рисуем сами
void shapeHot(const std::string& shape, float& hx, float& hy);   // «горячая точка» в долях картинки
// Нарисовать форму в клетке 32×32 (перед вызовом можно задать масштаб через Gfx::setTransform).
void drawShape(Gfx& g, const std::string& shape, Rgb fill, Rgb line);
// Стрелка из формы classic — для призрачного шлейфа
const std::vector<Gfx::Pt>& arrowPoints();

}  // namespace sp
