#pragma once
#include "common.h"
#include <map>
#include <string>
#include <vector>

// Стиль одного острова (-1 / пусто — «как у всех»): ключи "system", "widgets", "apps" (все значки сразу), "app.<hex>" (один значок).
struct IslandStyle
{
    int radius = -1;
    int color = -1;      // RGB
    int opacity = -1;
    int mode = -1;       // 0 как в Windows, 1 цвет, 2 прозрачный, 3 размытие
    bool hidden = false; // острова нет — значки лежат прямо на панели
};

// Правило «какой элемент панели к какой группе относится»: тип и имя с «*» (пусто — любой).
struct Rule
{
    int kind = 0;        // 1 фон, 2 обводка, 3 значок приложения, 4 виджеты, 5 системная область, 6 «плашка» значка трея
    std::string type, name;
};

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
    bool unifySystem = true;  // убрать отдельные плашки у значков системной области: она — один блок
    bool keepEdges = true;    // у плавающей панели и вкладки: трей и виджеты остаются отдельными островами
    int islandMode = 0;       // 0 каждый значок, 1 группа
    int islandStyle = 0;      // 0 плавающие, 1 вкладки
    int islandPad = 8;
    int islandGap = 8;
    int dump = 0;             // счётчик: изменился — записать дерево элементов панели в файл

    // расположение трёх групп: 0 виджеты, 1 системная область (часы, сеть, трей), 2 значки приложений
    int zone[3] = { 0, 0, 0 };   // 0 как в Windows, 1 слева, 2 по центру, 3 справа
    int order[3] = { 0, 1, 2 };  // порядок групп слева направо (номера групп)
    int edgeMargin = 12;         // отступ от края панели для групп «слева» и «справа»
    std::map<std::string, IslandStyle> styles;
    std::vector<Rule> rules;     // пусто — встроенные правила
    std::string rulesSig;        // подпись правил: изменилась — заново разобрать уже найденные элементы

    bool LayoutActive() const { return zone[0] != 0 || zone[1] != 0 || zone[2] != 0; }

    bool SameLook(const Config& o) const
    {
        return enabled == o.enabled && bgMode == o.bgMode && color == o.color && opacity == o.opacity && shape == o.shape &&
               radius == o.radius && side == o.side && gap == o.gap && hideStroke == o.hideStroke && keepEdges == o.keepEdges &&
               islandMode == o.islandMode && islandStyle == o.islandStyle && islandPad == o.islandPad && islandGap == o.islandGap;
    }
};

std::wstring ConfigPath();
bool LoadConfig(Config& out, FILETIME& mtime);   // false — файла нет или он не читается
