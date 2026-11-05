using System.IO;
using System.Text;

namespace TaskbarPlusApp;

// Проверка без окна и без Проводника: разбор измерений и построение плашек (Windows-раннер GitHub, сценарий taskbar).
static class SelfTest
{
    public static void Run(string dir)
    {
        Directory.CreateDirectory(dir);
        var report = new StringBuilder();
        var bad = 0;
        void Check(string name, bool ok, string extra = "")
        {
            report.AppendLine((ok ? "OK    " : "ОШИБКА ") + name + (extra.Length > 0 ? " — " + extra : ""));
            if (!ok) bad++;
        }
        try { Body(Check); }
        catch (Exception ex) { Check("самопроверка не упала", false, ex.ToString()); }
        report.AppendLine(bad == 0 ? "ВСЁ В ПОРЯДКЕ" : $"ОШИБОК: {bad}");
        File.WriteAllText(Path.Combine(dir, "report.txt"), report.ToString());
        Environment.ExitCode = bad == 0 ? 0 : 1;
    }

    delegate void CheckFn(string name, bool ok, string extra = "");

    static void Body(CheckFn check)
    {
        // ---- разбор файла измерений ----
        var text = "v=1\npid=4242\ntick=1000\nbars=2\nbar|0|1920|48|100\nw|0|12|4|390|44\ns|0|1520|4|1913|44\na|0|1127|4|1507|44\ni|0|1127|4|1171|44\ni|0|1175|4|1219|44\n" +
                   "bar|1|1280|48|125\na|1|100|4|300|44\nмусор|строка\ni|7|1|2|3|4\n";
        var f = GeometryFile.Parse(text);
        check("файл измерений читается", f != null && f.Bars.Count == 2 && f.Pid == 4242 && f.Tick == 1000);
        check("группы разобраны", f != null && f.Bars[0].Widgets == new Box(12, 4, 390, 44) && f.Bars[0].System == new Box(1520, 4, 1913, 44) && f.Bars[0].Apps == new Box(1127, 4, 1507, 44) && f.Bars[0].Icons.Count == 2);
        check("масштаб и вторая панель", f != null && f.Bars[1].Scale100 == 125 && f.Bars[1].Apps == new Box(100, 4, 300, 44) && f.Bars[1].Widgets == null);
        check("мусор не ломает разбор", GeometryFile.Parse("ааа\n|||\nbar|x|y") is { } g0 && g0.Bars.Count == 0);

        // ---- как на снимке владельца: значки у правого края, много значков ----
        BarGeometry Owner()
        {
            var g = new BarGeometry { RootW = 1920, RootH = 48, Scale100 = 100, Widgets = new Box(12, 4, 390, 44), System = new Box(1520, 4, 1913, 44), Apps = new Box(1127, 4, 1507, 44) };
            for (var x = 1127; x + 44 <= 1507; x += 48) g.Icons.Add(new Box(x, 4, x + 44, 44));
            return g;
        }

        bool Covers(List<RoundRect> rects, Box b) => rects.Any(r => r.X <= b.L + 0.5 && r.X + r.W >= b.R - 0.5);

        var pill = new AppearanceSettings { Shape = AppearanceSettings.ShapePill, Radius = 18, IslandPadding = 10, BottomGap = 6, KeepEdges = true, WidgetsIsland = true };
        var g1 = Owner();
        var r1 = ShapeBuilder.Build(pill, 1920, 48, 1.0, g1);
        check("плавающая: накрывает ВСЕ значки", g1.Icons.All(i => Covers(r1, i)), $"плашек {r1.Count}");
        check("плавающая: накрывает трей и виджеты отдельно", Covers(r1, g1.System!.Value) && Covers(r1, g1.Widgets!.Value));
        check("плавающая: не шире окна и не выше", r1.All(r => r.X >= 0 && r.X + r.W <= 1920 && r.Y >= 0 && r.Y + r.H <= 48.01));
        check("плавающая: накрывает значки по высоте", r1.All(r => r.Y <= 4.01 && r.Y + r.H >= 43.99));
        check("плавающая: плашка не уже значков при любом «отступе» (его больше нет)", r1.Any(r => r.W >= g1.Apps!.Value.Width));

        pill.KeepEdges = false;
        var r2 = ShapeBuilder.Build(pill, 1920, 48, 1.0, g1);
        check("одна общая плашка: от виджетов до часов", r2.Count == 1 && r2[0].X <= 12 && r2[0].X + r2[0].W >= 1913, $"плашек {r2.Count}");
        pill.KeepEdges = true;

        var tab = new AppearanceSettings { Shape = AppearanceSettings.ShapeTab, Radius = 16, IslandPadding = 10, WidgetsIsland = true };
        var r3 = ShapeBuilder.Build(tab, 1920, 48, 1.0, g1);
        check("вкладка: от верхнего края и уходит за нижний", r3.All(r => r.Y == 0 && r.Y + r.H > 48), string.Join(",", r3.Select(r => $"{r.Y:F0}+{r.H:F0}")));
        check("вкладка: накрывает все значки", g1.Icons.All(i => Covers(r3, i)));

        var isl = new AppearanceSettings { Shape = AppearanceSettings.ShapeIslands, IslandMode = "each", IslandStyle = "float", Radius = 14, IslandGap = 8, BottomGap = 6, WidgetsIsland = true };
        var r4 = ShapeBuilder.Build(isl, 1920, 48, 1.0, g1);
        check("острова: по одному на значок + виджеты + трей", r4.Count == g1.Icons.Count + 2, $"плашек {r4.Count}, значков {g1.Icons.Count}");
        check("острова: каждый значок на своём острове", g1.Icons.All(i => r4.Any(r => r.X <= i.L + 4 && r.X + r.W >= i.R - 4)));
        var sorted = r4.OrderBy(r => r.X).ToList();
        var noOverlap = true;
        for (var i = 1; i < sorted.Count; i++) if (sorted[i].X < sorted[i - 1].X + sorted[i - 1].W - 0.01) noOverlap = false;
        check("острова не налезают друг на друга", noOverlap);

        isl.IslandMode = "group"; isl.IslandPadding = 8;
        var r5 = ShapeBuilder.Build(isl, 1920, 48, 1.0, g1);
        check("острова-группы: значки одной группой", r5.Count == 3 && Covers(r5, g1.Apps!.Value), $"плашек {r5.Count}");

        // по умолчанию острова под виджетами нет (на части компьютеров там пусто)
        var noWidgets = new AppearanceSettings { Shape = AppearanceSettings.ShapePill, Radius = 18, IslandPadding = 10, KeepEdges = true };
        var rw = ShapeBuilder.Build(noWidgets, 1920, 48, 1.0, g1);
        check("виджеты по умолчанию без острова", rw.Count == 2 && !rw.Any(r => r.X < 100), $"плашек {rw.Count}");

        // скругление не делает из узкого острова круг
        var narrow = new BarGeometry { RootW = 400, RootH = 48, Apps = new Box(100, 4, 120, 44) };
        narrow.Icons.Add(new Box(100, 4, 120, 44));
        var r6 = ShapeBuilder.Build(new AppearanceSettings { Shape = AppearanceSettings.ShapePill, Radius = 30, IslandPadding = 2 }, 400, 48, 1.0, narrow);
        check("скругление ограничено 40% меньшей стороны", r6.Count == 1 && r6[0].R <= 0.4 * Math.Min(r6[0].W, r6[0].H) + 0.01, r6.Count == 1 ? $"R={r6[0].R:F1} W={r6[0].W:F0} H={r6[0].H:F0}" : "");

        // масштаб 150%: отступы и скругление растут, плашка по-прежнему накрывает значки
        var g7 = new BarGeometry { RootW = 2880, RootH = 72, Scale100 = 150, Apps = new Box(1500, 6, 2000, 66) };
        g7.Icons.Add(new Box(1500, 6, 1566, 66));
        var r7 = ShapeBuilder.Build(new AppearanceSettings { Shape = AppearanceSettings.ShapePill, Radius = 10, IslandPadding = 10 }, 2880, 72, 1.5, g7);
        check("масштаб 150%: отступ 10 → 15 px", r7.Count == 1 && Math.Abs(r7[0].X - (1500 - 15)) < 0.01 && Math.Abs(r7[0].X + r7[0].W - (2000 + 15)) < 0.01, r7.Count == 1 ? $"X={r7[0].X:F1} W={r7[0].W:F1}" : "нет плашек");

        // пустые случаи
        check("форма «обычная» — плашек нет", ShapeBuilder.Build(new AppearanceSettings(), 1920, 48, 1.0, g1).Count == 0);
        check("нет измерений — плашек нет", ShapeBuilder.Build(pill, 1920, 48, 1.0, new BarGeometry { RootW = 1920, RootH = 48 }).Count == 0);

        // большой «зазор снизу» не уводит плашку со значков
        var gap = new AppearanceSettings { Shape = AppearanceSettings.ShapePill, Radius = 18, IslandPadding = 10, BottomGap = 20 };
        var r8 = ShapeBuilder.Build(gap, 1920, 48, 1.0, g1);
        check("большой зазор снизу не отрывает плашку от значков", r8.All(r => r.Y + r.H >= 43.99), string.Join(",", r8.Select(r => $"{r.Y + r.H:F0}")));
    }
}
