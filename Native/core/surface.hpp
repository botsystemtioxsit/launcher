// Края окон: куда ложится снег и где висят гирлянды. Общее для всех слоёв поверх экрана.
#pragma once
#include <vector>

namespace sp {

struct Seg { double a, b; };

// Верхний край окна или панели задач: пиксели экрана.
struct Surf {
    long long id = 0;
    double left = 0, right = 0, top = 0, radius = 0;
    bool taskbar = false;
    std::vector<Seg> vis;            // видимые куски края (не закрытые окнами выше)
};

// Куски отрезка [a, b], не попавшие в закрытые промежутки.
std::vector<Seg> subtract(double a, double b, const std::vector<Seg>& cut);

}  // namespace sp
