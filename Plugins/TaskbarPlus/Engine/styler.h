#pragma once
#include "config.h"
#include <ocidl.h>
#include <xamlom.h>

// Применяет настройки к элементам панели задач (XAML внутри Проводника).
// OnChange вызывается самой панелью (поток XAML) для каждого элемента, который появился или исчез.
struct StylerStats
{
    int threads = 0, nodes = 0, bgFill = 0, bgStroke = 0, apps = 0, widgets = 0, system = 0, islands = 0, hosts = 0, rects = 0;
    int coverAppsL = -1, coverAppsR = -1, coverBarL = -1, coverBarR = -1, coverWidth = 0;   // края значков и отступы плашки (px панели)
    std::wstring rectNames;   // имена прямоугольников на панели — для отчёта, если фон не нашёлся
    bool applied = false;
    bool queueOk = false;
};

namespace Styler
{
    HRESULT OnChange(IXamlDiagnostics* diag, const ParentChildRelation& relation, const VisualElement& element, VisualMutationType mutation);
    void SetConfig(const Config& cfg);   // любой поток: применение переносится в поток XAML
    void WriteTree();                    // любой поток: записать дерево элементов в файл
    StylerStats GetStats();              // любой поток
}
