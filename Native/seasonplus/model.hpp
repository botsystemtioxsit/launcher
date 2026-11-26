// Season Plus (нативная версия): настройки, календарь, края окон. Файл настроек тот же, что у версии на C#: %APPDATA%\BattleLauncher\season-plus-settings.json
#pragma once
#include <string>
#include <vector>
#include <algorithm>
#include <ctime>
#include "json.hpp"

namespace sp {

struct Settings {
    std::string Season = "auto";     // auto | winter | spring | summer | autumn | off
    std::string Holiday = "auto";    // off | auto | newyear | xmas | tet | halloween | valentine | women | maslenitsa | easter | custom
    int Intensity = 45, Wind = 10, Speed = 100, Size = 100;
    bool Piles = true;
    int PileHeight = 50;
    bool TaskbarPile = true, Props = true;
    int Garland = 1, GarlandWhere = 0, GarlandMode = 0, GarlandColor = 0, GarlandBulb = 0;
    bool Countdown = true, NyFireworks = true;
    int CustomDay = 0, CustomMonth = 0, CustomFx = 0;
    std::string CustomText;
    bool Active = false;
    int Gpu = 0;                     // 0 как решит Windows, 1 мощная видеокарта (новое поле: старые копии его не читают)

    void normalize();
    static Settings fromJson(const gs::Json& j);
    gs::Json toJson() const;
    static Settings load();
    void save() const;
};

struct Date { int y, m, d; };

struct Cal {
    static int seasonIndex(const Settings& s, Date now);     // 0 зима, 1 весна, 2 лето, 3 осень, −1 выключено
    static Date easter(int year);                            // православная Пасха (по григорианскому календарю)
    static std::string holiday(const Settings& s, Date now);
    static int daysFrom(Date a);                              // номер дня (для сравнения дат)
    static Date addDays(Date a, int n);
    static Date today();
};

struct Seg { double a, b; };

// Верхний край окна или панели задач: пиксели экрана.
struct Surf {
    long long id = 0;
    double left = 0, right = 0, top = 0, radius = 0;
    bool taskbar = false;
    std::vector<Seg> vis;            // видимые куски края
};

// Куски отрезка [a, b], не попавшие в закрытые промежутки.
std::vector<Seg> subtract(double a, double b, const std::vector<Seg>& cut);

}  // namespace sp
