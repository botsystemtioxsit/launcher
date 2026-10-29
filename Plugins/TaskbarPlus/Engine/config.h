#pragma once
#include "common.h"

// Настройки, которые плагин (C#) кладёт в taskbar-plus-engine.cfg простыми строками «ключ=значение».
struct Config
{
    bool enabled = false;     // false — движок вернул всё как было и ничего не трогает
    int bgMode = 0;           // 0 как в Windows, 1 свой цвет, 2 прозрачный, 3 размытие (акрил)
    unsigned color = 0x202020; // RGB
    int opacity = 85;         // 0..100
    int shape = 0;            // 0 обычная, 1 плавающая, 2 вкладка, 3 острова
    int radius = 14;          // DIP
    int side = 0;             // отступ по бокам
    int gap = 0;              // зазор снизу
    bool hideStroke = false;  // убрать тонкую линию панели
    bool keepEdges = true;    // у плавающей панели и вкладки: трей и виджеты остаются отдельными островами
    int islandMode = 0;       // 0 каждый значок, 1 группа
    int islandStyle = 0;      // 0 плавающие, 1 вкладки
    int islandPad = 8;
    int islandGap = 8;
    int dump = 0;             // счётчик: изменился — записать дерево элементов панели в файл

    bool SameLook(const Config& o) const
    {
        return enabled == o.enabled && bgMode == o.bgMode && color == o.color && opacity == o.opacity && shape == o.shape &&
               radius == o.radius && side == o.side && gap == o.gap && hideStroke == o.hideStroke && keepEdges == o.keepEdges &&
               islandMode == o.islandMode && islandStyle == o.islandStyle && islandPad == o.islandPad && islandGap == o.islandGap;
    }
};

std::wstring ConfigPath();
bool LoadConfig(Config& out, FILETIME& mtime);   // false — файла нет или он не читается
