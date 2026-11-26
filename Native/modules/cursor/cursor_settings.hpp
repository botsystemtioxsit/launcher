// Cursor: настройки. Файл тот же, что у версии на C#: %APPDATA%\BattleLauncher\cursor-plus\settings.json
#pragma once
#include <string>
#include "gfx.hpp"
#include "json.hpp"

namespace sp {

struct CursorSettings {
    std::string Shape = "classic";            // windows | classic | round | neon | dot | ring | cross | ghost | star | mine:<id>
    std::string Fill = "#FF2A9D";
    std::string Outline = "auto";             // auto | #RRGGBB
    int Size = 100;                           // 100–300 %
    bool All = true;                          // перекрасить и остальные указатели
    std::string Trail = "comet";              // none | comet | sparks | glow | stars | bubbles | hearts | ghost
    std::string TrailColorMode = "cursor";    // cursor | rainbow | two | custom
    std::string TrailColor = "#00F0FF";
    std::string TrailColor2 = "#FF2A9D";
    int TrailLength = 50;                     // 0–100
    int TrailSize = 40;                       // 0–100
    std::string Click = "none";               // none | ring | burst
    bool Applied = false;                     // курсор записан в Windows
    bool OverlayOn = false;                   // шлейф и клики включены

    void normalize();
    static CursorSettings fromJson(const gs::Json& j);
    gs::Json toJson() const;
    static CursorSettings load();
    void save() const;
    bool wantsOverlay() const { return Trail != "none" || Click != "none"; }
    Rgb fillRgb() const;
    Rgb lineRgb() const;                      // обводка: выбранная или тёмный оттенок основного цвета
    Rgb trailRgb() const;
    Rgb trailRgb2() const;
    static bool isHex(const std::string& s);
    static std::wstring dir();                // %APPDATA%\BattleLauncher\cursor-plus
};

Rgb parseHex(const std::string& s);
std::string toHex(Rgb c);

}  // namespace sp
