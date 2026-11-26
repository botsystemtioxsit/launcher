#pragma once
#include "config.h"
#include <ocidl.h>
#include <xamlom.h>

// «Стилист» движка 7: в потоке XAML панели задач (а) делает родной фон прозрачным, пока он включён, и (б) измеряет, где лежат значки,
// часы с треем и виджеты, — координаты отдаются плагину файлом taskbar-plus-geometry.txt (плашки рисует плагин в своём окне).
struct StylerStats
{
    int threads = 0, nodes = 0, bgFill = 0, bgStroke = 0, apps = 0, widgets = 0, system = 0, bars = 0, geoBars = 0, rects = 0, touched = 0;
    std::wstring rectNames;   // имена прямоугольников на панели — для отчёта, если фон не нашёлся
    bool queueOk = false;
};

namespace Styler
{
    HRESULT OnChange(IXamlDiagnostics* diag, const ParentChildRelation& relation, const VisualElement& element, VisualMutationType mutation);
    void SetConfig(const Config& cfg);   // любой поток: применение переносится в поток XAML
    void WriteTree();                    // любой поток: записать дерево элементов в файл
    StylerStats GetStats();              // любой поток
}
