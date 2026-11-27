#pragma once
#include <string>
#include "tb_logic.hpp"

namespace tb {
Appearance loadAppearance();
void saveAppearance(const Appearance& a);          // неизвестные поля файла (расположение групп и т. п.) сохраняются как были
std::string appearanceJson(const Appearance& a);   // для отчёта
std::wstring dataDir();                            // %APPDATA%\BattleLauncher
}
