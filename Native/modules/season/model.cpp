#include "model.hpp"
#include <cstdio>
#include <ctime>
#ifndef _WIN32
#define localtime_s(tmp, tt) localtime_r((tt), (tmp))
#endif

namespace sp {

static int clampi(int v, int a, int b) { return v < a ? a : v > b ? b : v; }
static bool in(const std::string& v, std::initializer_list<const char*> l) { for (auto* x : l) if (v == x) return true; return false; }

void Settings::normalize() {
    if (!in(Season, {"auto", "winter", "spring", "summer", "autumn", "off"})) Season = "auto";
    if (!in(Holiday, {"off", "auto", "newyear", "xmas", "tet", "halloween", "valentine", "women", "maslenitsa", "easter", "custom"})) Holiday = "auto";
    Intensity = clampi(Intensity, 5, 100); Wind = clampi(Wind, -100, 100); Speed = clampi(Speed, 50, 200); Size = clampi(Size, 50, 200);
    PileHeight = clampi(PileHeight, 10, 100);
    Garland = clampi(Garland, 0, 2); GarlandWhere = clampi(GarlandWhere, 0, 1); GarlandMode = clampi(GarlandMode, 0, 5);
    GarlandColor = clampi(GarlandColor, 0, 5); GarlandBulb = clampi(GarlandBulb, 0, 3);
    CustomDay = clampi(CustomDay, 0, 31); CustomMonth = clampi(CustomMonth, 0, 12); CustomFx = clampi(CustomFx, 0, 3);
    Gpu = clampi(Gpu, 0, 1);
    std::string t;
    for (unsigned char c : CustomText) if (c >= 0x20 && t.size() < 120) t += (char)c;
    CustomText = t;
}

Settings Settings::fromJson(const gs::Json& j) {
    Settings s;
    s.Season = j.str("Season", s.Season); s.Holiday = j.str("Holiday", s.Holiday);
    s.Intensity = j.num("Intensity", s.Intensity); s.Wind = j.num("Wind", s.Wind); s.Speed = j.num("Speed", s.Speed); s.Size = j.num("Size", s.Size);
    s.Piles = j.boolean("Piles", s.Piles); s.PileHeight = j.num("PileHeight", s.PileHeight);
    s.TaskbarPile = j.boolean("TaskbarPile", s.TaskbarPile); s.Props = j.boolean("Props", s.Props);
    s.Garland = j.num("Garland", s.Garland); s.GarlandWhere = j.num("GarlandWhere", s.GarlandWhere);
    s.GarlandMode = j.num("GarlandMode", s.GarlandMode); s.GarlandColor = j.num("GarlandColor", s.GarlandColor); s.GarlandBulb = j.num("GarlandBulb", s.GarlandBulb);
    s.Countdown = j.boolean("Countdown", s.Countdown); s.NyFireworks = j.boolean("NyFireworks", s.NyFireworks);
    s.CustomDay = j.num("CustomDay", s.CustomDay); s.CustomMonth = j.num("CustomMonth", s.CustomMonth); s.CustomFx = j.num("CustomFx", s.CustomFx);
    s.CustomText = j.str("CustomText", s.CustomText);
    s.Active = j.boolean("Active", s.Active);
    s.Gpu = j.num("Gpu", s.Gpu);
    s.normalize();
    return s;
}

gs::Json Settings::toJson() const {
    using gs::Json;
    Json j = Json::Object();
    j.set("Season", Json::String(Season)); j.set("Holiday", Json::String(Holiday));
    j.set("Intensity", Json::Number(Intensity)); j.set("Wind", Json::Number(Wind)); j.set("Speed", Json::Number(Speed)); j.set("Size", Json::Number(Size));
    j.set("Piles", Json::Boolean(Piles)); j.set("PileHeight", Json::Number(PileHeight));
    j.set("TaskbarPile", Json::Boolean(TaskbarPile)); j.set("Props", Json::Boolean(Props));
    j.set("Garland", Json::Number(Garland)); j.set("GarlandWhere", Json::Number(GarlandWhere));
    j.set("GarlandMode", Json::Number(GarlandMode)); j.set("GarlandColor", Json::Number(GarlandColor)); j.set("GarlandBulb", Json::Number(GarlandBulb));
    j.set("Countdown", Json::Boolean(Countdown)); j.set("NyFireworks", Json::Boolean(NyFireworks));
    j.set("CustomDay", Json::Number(CustomDay)); j.set("CustomMonth", Json::Number(CustomMonth)); j.set("CustomFx", Json::Number(CustomFx));
    j.set("CustomText", Json::String(CustomText));
    j.set("Active", Json::Boolean(Active));
    j.set("Gpu", Json::Number(Gpu));
    return j;
}

// ---------- календарь ----------
static long long civil(int y, int m, int d) {    // дни от 1970-01-01 (алгоритм Говарда Хиннанта)
    y -= m <= 2;
    long long era = (y >= 0 ? y : y - 399) / 400;
    unsigned yoe = (unsigned)(y - era * 400);
    unsigned doy = (153 * (m + (m > 2 ? -3 : 9)) + 2) / 5 + d - 1;
    unsigned doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    return era * 146097 + (long long)doe - 719468;
}
static Date fromCivil(long long z) {
    z += 719468;
    long long era = (z >= 0 ? z : z - 146096) / 146097;
    unsigned doe = (unsigned)(z - era * 146097);
    unsigned yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
    long long y = (long long)yoe + era * 400;
    unsigned doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    unsigned mp = (5 * doy + 2) / 153;
    unsigned d = doy - (153 * mp + 2) / 5 + 1;
    unsigned m = mp < 10 ? mp + 3 : mp - 9;
    return Date{(int)(y + (m <= 2)), (int)m, (int)d};
}

int Cal::daysFrom(Date a) { return (int)civil(a.y, a.m, a.d); }
Date Cal::addDays(Date a, int n) { return fromCivil(civil(a.y, a.m, a.d) + n); }
Date Cal::today() { time_t t = time(nullptr); tm lt{}; localtime_s(&lt, &t); return Date{lt.tm_year + 1900, lt.tm_mon + 1, lt.tm_mday}; }

int Cal::seasonIndex(const Settings& s, Date now) {
    static const char* ids[] = {"winter", "spring", "summer", "autumn"};
    for (int i = 0; i < 4; i++) if (s.Season == ids[i]) return i;
    if (s.Season == "off") return -1;
    int m = now.m;
    return (m == 12 || m <= 2) ? 0 : m <= 5 ? 1 : m <= 8 ? 2 : 3;
}

Date Cal::easter(int year) {
    int a = year % 4, b = year % 7, c = year % 19, d = (19 * c + 15) % 30, e = (2 * a + 4 * b - d + 34) % 7;
    int month = (d + e + 114) / 31, day = (d + e + 114) % 31 + 1;
    return addDays(Date{year, month, day}, 13);
}

std::string Cal::holiday(const Settings& s, Date now) {
    if (s.Holiday == "off") return "";
    if (s.Holiday != "auto") return s.Holiday;
    int m = now.m, d = now.d;
    if ((m == 12 && d >= 15) || (m == 1 && d <= 10)) return "newyear";
    if ((m == 10 && d >= 25) || (m == 11 && d == 1)) return "halloween";
    if (m == 2 && d >= 12 && d <= 14) return "valentine";
    if (m == 3 && d >= 6 && d <= 8) return "women";
    int e = daysFrom(easter(now.y)), t = daysFrom(now);
    if (t >= e - 55 && t <= e - 49) return "maslenitsa";
    if (t >= e - 3 && t <= e + 1) return "easter";
    if (s.CustomDay > 0 && s.CustomMonth > 0 && m == s.CustomMonth && d == s.CustomDay) return "custom";
    return "";
}

}  // namespace sp
