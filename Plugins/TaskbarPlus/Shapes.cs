namespace TaskbarPlusApp;

// Скруглённый прямоугольник в пикселях окна панели (от его левого верхнего угла).
readonly record struct RoundRect(double X, double Y, double W, double H, double R);

// Из настроек и измеренного положения значков получает набор плашек. Это ЕДИНСТВЕННОЕ место, где решается, что где рисовать:
// по нему рисуется и настоящее окно под панелью, и предпросмотр в окне настроек — поэтому они не могут разойтись.
//
// Главный принцип: плашка считается от настоящих значков (их положение измеряет движок), а не от «отступов по бокам», —
// поэтому она не может оказаться уже или шире значков, как было у прежнего Taskbar Plus.
static class ShapeBuilder
{
    public static List<RoundRect> Build(AppearanceSettings s, int barW, int barH, double scale, BarGeometry g)
    {
        var rects = new List<RoundRect>();
        if (s.Shape == AppearanceSettings.ShapeNone || barW < 10 || barH < 10) return rects;

        var pad = s.IslandPadding * scale;
        var radius = s.Radius * scale;
        var inset = 3 * scale;
        var attached = s.Shape == AppearanceSettings.ShapeTab || (s.Shape == AppearanceSettings.ShapeIslands && s.IslandStyle == "tab");

        // Вертикаль: у «вкладки» плашка идёт от верхнего края и уходит за нижний (скруглён только верх); у плавающих форм
        // остаётся зазор снизу, но не больше, чем позволяют сами значки (они сидят на своих местах и должны оказаться на плашке).
        double contentTop = double.MaxValue, contentBottom = 0;
        foreach (var b in new[] { g.Apps, g.Widgets, g.System })
            if (b is { } bb) { contentTop = Math.Min(contentTop, bb.T); contentBottom = Math.Max(contentBottom, bb.B); }
        if (contentTop == double.MaxValue) return rects;
        double top, bottom;
        if (attached) { top = 0; bottom = barH + radius; }
        else
        {
            top = Math.Min(inset, contentTop);
            bottom = Math.Min(barH, Math.Max(barH - s.BottomGap * scale, contentBottom));
            if (bottom - top < 8) { top = 0; bottom = barH; }
        }
        var visibleH = (attached ? barH : bottom) - top;

        void Add(double l, double r)
        {
            l = Math.Max(0, l); r = Math.Min(barW, r);
            if (r - l < 4) return;
            // скругление не больше ~40% меньшей стороны видимой фигуры: узкий остров остаётся скруглённым квадратом, а не кругом
            var rr = Math.Min(radius, 0.4 * Math.Min(r - l, visibleH));
            rects.Add(new RoundRect(l, top, r - l, bottom - top, Math.Max(0, rr)));
        }

        var edges = new List<(double L, double R)>();   // виджеты и системная область — отдельными островами (или одной плашкой, если так выбрано)
        if (g.Widgets is { } w) edges.Add((w.L - pad, w.R + pad));
        if (g.System is { } sy) edges.Add((sy.L - pad, sy.R + pad));

        switch (s.Shape)
        {
            case AppearanceSettings.ShapePill:
            case AppearanceSettings.ShapeTab:
                if (g.Apps is not { } a)
                {
                    foreach (var e in edges) Add(e.L, e.R);
                    break;
                }
                if (s.KeepEdges)
                {
                    Add(a.L - pad, a.R + pad);
                    foreach (var e in edges) Add(e.L, e.R);
                }
                else
                {
                    // одна общая плашка от первого до последнего значка и часов
                    var l = a.L - pad; var r = a.R + pad;
                    foreach (var e in edges) { l = Math.Min(l, e.L); r = Math.Max(r, e.R); }
                    Add(l, r);
                }
                break;

            case AppearanceSettings.ShapeIslands:
                foreach (var e in edges) Add(e.L, e.R);
                if (s.IslandMode == "group")
                {
                    var join = 16 * scale;
                    var spans = Icons(g).Select(i => (L: (double)i.L, R: (double)i.R)).ToList();
                    var cur = default((double L, double R)?);
                    foreach (var sp in spans)
                    {
                        if (cur is { } c && sp.L - c.R <= join) cur = (c.L, Math.Max(c.R, sp.R));
                        else { if (cur is { } done) Add(done.L - pad, done.R + pad); cur = sp; }
                    }
                    if (cur is { } last) Add(last.L - pad, last.R + pad);
                }
                else
                {
                    var half = s.IslandGap * scale / 2;
                    foreach (var i in Icons(g)) Add(i.L + half, i.R - half);
                }
                break;
        }
        return rects;
    }

    // Значки по порядку слева направо без повторов (вложенный элемент внутри уже взятого выбрасывается).
    static List<Box> Icons(BarGeometry g)
    {
        var list = new List<Box>();
        foreach (var b in g.Icons.OrderBy(i => i.L).ThenBy(i => i.R))
        {
            if (list.Count > 0)
            {
                var prev = list[^1];
                if (b.L >= prev.L - 2 && b.R <= prev.R + 2) continue;
            }
            list.Add(b);
        }
        return list;
    }
}
