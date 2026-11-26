// «Слой» поверх экрана: сезоны, шлейф курсора и т. п. Все слои рисуются в одном прозрачном окне одной видеокартой.
#pragma once
#include <vector>
#include <windows.h>
#include "gfx.hpp"
#include "surface.hpp"

namespace sp {

// Что известно о происходящем на экране в этом кадре.
struct Env {
    double dt = 0;                                   // секунд с прошлого кадра
    double time = 0;                                 // секунд с запуска
    int vx = 0, vy = 0, vw = 1920, vh = 1080;        // виртуальный рабочий стол (все мониторы)
    const std::vector<Surf>* surfaces = nullptr;     // края окон (только если кому-то нужны)
    bool surfacesChanged = false;                    // список обновился в этом кадре
    POINT cursor{};                                  // положение мыши на экране
    bool lbtn = false, rbtn = false;                 // кнопки мыши сейчас нажаты
    bool lclick = false, rclick = false;             // нажатие произошло только что
};

class Layer {
public:
    virtual ~Layer() = default;
    virtual bool wantsFrame() const = 0;             // нужно ли сейчас рисовать (выключенные слои ничего не стоят)
    virtual bool needsSurfaces() const { return false; }
    virtual void onResize(int vx, int vy, int w, int h) {}
    virtual void update(const Env& env) = 0;
    virtual void draw(Gfx& g, const Env& env) = 0;
};

}  // namespace sp
