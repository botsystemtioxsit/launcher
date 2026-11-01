using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TaskbarPlusApp;

// Живой предпросмотр панели: выдуманный рабочий стол и панель задач в выбранной форме. Рисуется по черновику настроек — то, что видно здесь,
// получится после «Применить». Цвет островов берётся у Color Plus (если он применён), иначе острова серые, как в Windows.
sealed class Preview : Canvas
{
    public const double W = 760, H = 250;
    const double K = 0.4;                       // во сколько раз предпросмотр меньше настоящего экрана по ширине

    public Preview() { Width = W; Height = H; ClipToBounds = true; }

    static SolidColorBrush B(string hex, double opacity = 1)
    {
        var h = hex.TrimStart('#');
        var v = Convert.ToInt32(h, 16);
        var br = new SolidColorBrush(Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v)) { Opacity = opacity };
        br.Freeze();
        return br;
    }

    void Put(UIElement e, double x, double y) { SetLeft(e, x); SetTop(e, y); Children.Add(e); }

    static double Zone(string zone, string fallback) => zone switch { AppearanceSettings.ZoneLeft => 0, AppearanceSettings.ZoneCenter => 0.5, AppearanceSettings.ZoneRight => 1, _ => fallback == "left" ? 0 : fallback == "right" ? 1 : 0.5 };

    public void Update(AppearanceSettings s)
    {
        Children.Clear();
        var link = ColorPlusLink.Current;
        var tint = link.Active && link.Mode is "solid" or "acrylic";
        var tileBrush = tint ? B(link.Color, Math.Max(0.35, link.Opacity / 100.0)) : B("#2A2A31", 0.92);
        var barBrush = B("#1C1C21", 0.94);

        // обои и окно
        Put(new Rectangle { Width = W, Height = H, Fill = new LinearGradientBrush(Color.FromRgb(0x24, 0x18, 0x52), Color.FromRgb(0x0B, 0x08, 0x18), 60) }, 0, 0);
        Put(new Ellipse { Width = 330, Height = 330, Fill = new RadialGradientBrush(Color.FromArgb(150, 0x6C, 0x4D, 0xFF), Color.FromArgb(0, 0x6C, 0x4D, 0xFF)) }, 430, -140);
        Put(new Ellipse { Width = 300, Height = 300, Fill = new RadialGradientBrush(Color.FromArgb(110, 0xFF, 0x2A, 0x9D), Color.FromArgb(0, 0xFF, 0x2A, 0x9D)) }, -90, 20);
        Put(new Border { Width = 300, Height = 120, CornerRadius = new CornerRadius(8), Background = B("#1C1C21", 0.9), BorderBrush = B("#34343C"), BorderThickness = new Thickness(1) }, 40, 24);
        Put(new Border { Width = 300, Height = 26, CornerRadius = new CornerRadius(8, 8, 0, 0), Background = B("#2B2B33") }, 40, 24);

        // панель задач
        var barH = 40.0;
        var shape = s.Shape;
        var floating = shape == AppearanceSettings.ShapePill || (shape == AppearanceSettings.ShapeIslands && s.IslandStyle == "float");
        var gap = floating ? Math.Min(18, s.BottomGap * 0.9 + 2) : 0;
        var y0 = H - barH - gap;
        var side = shape is AppearanceSettings.ShapePill or AppearanceSettings.ShapeTab ? Math.Min(W / 2 - 120, s.SideMargin * K) : 0;
        var rad = shape == AppearanceSettings.ShapeNone ? 0 : Math.Min(s.Radius * 0.75, barH / 2);

        // «Обычная» — на всю ширину; плавающая и вкладка — один остров с отступами; острова — рисуются у значков ниже
        if (shape == AppearanceSettings.ShapeNone)
            Put(new Rectangle { Width = W, Height = barH, Fill = barBrush }, 0, y0);
        else if (shape == AppearanceSettings.ShapePill)
            Put(new Border { Width = W - 2 * side, Height = barH, CornerRadius = new CornerRadius(rad), Background = tint ? tileBrush : barBrush }, side, y0);
        else if (shape == AppearanceSettings.ShapeTab)
            Put(new Border { Width = W - 2 * side, Height = barH + 8, CornerRadius = new CornerRadius(rad, rad, 0, 0), Background = tint ? tileBrush : barBrush }, side, y0);

        // значки приложений: где стоят (как в Windows — по центру)
        const int n = 6;
        var iconSize = 20.0; var step = shape == AppearanceSettings.ShapeIslands && s.IslandMode == "each" ? iconSize + 10 + s.IslandGap * 0.4 : iconSize + 14;
        var groupW = n * step - (step - iconSize);
        var areaL = side + 10; var areaR = W - side - 10 - (shape == AppearanceSettings.ShapeNone || shape == AppearanceSettings.ShapeIslands || s.KeepEdges ? 110 : 0);
        var fa = Zone(s.ZoneApps, "center");
        var startX = areaL + (areaR - areaL - groupW) * fa;
        string[] icons = { "#00F0FF", "#FF2A9D", "#F2F2F2", "#2CFF9A", "#FFB020", "#6C4DFF" };
        var edgesOnly = shape is AppearanceSettings.ShapePill or AppearanceSettings.ShapeTab;

        if (shape == AppearanceSettings.ShapeIslands)
        {
            var tabStyle = s.IslandStyle == "tab";
            if (s.IslandMode == "group")
                Put(new Border { Width = groupW + 2 * s.IslandPadding * 0.8, Height = barH - 6, CornerRadius = tabStyle ? new CornerRadius(rad, rad, 0, 0) : new CornerRadius(rad), Background = tileBrush }, startX - s.IslandPadding * 0.8, tabStyle ? H - barH + 2 : y0 + 3);
            else
                for (var i = 0; i < n; i++)
                    Put(new Border { Width = iconSize + 10, Height = barH - 6, CornerRadius = tabStyle ? new CornerRadius(rad, rad, 0, 0) : new CornerRadius(rad), Background = tileBrush }, startX + i * step - 5, tabStyle ? H - barH + 2 : y0 + 3);
        }
        for (var i = 0; i < n; i++)
            Put(new Border { Width = iconSize, Height = iconSize, CornerRadius = new CornerRadius(5), Background = B(icons[i], 0.95) }, startX + i * step, (shape == AppearanceSettings.ShapeTab ? y0 + 4 : y0) + (barH - iconSize) / 2 - (shape == AppearanceSettings.ShapeTab ? 2 : 0));

        // часы и трей: у обычной и островов — на своём месте справа; у плавающей и вкладки — отдельным островом по краям (если оставлено)
        var showTray = shape is AppearanceSettings.ShapeNone or AppearanceSettings.ShapeIslands || s.KeepEdges;
        if (showTray)
        {
            var trayW = 104.0;
            var fs = Zone(s.ZoneSystem, "right");
            var trayX = fs <= 0 ? 8 : fs >= 1 ? W - trayW - 8 : (W - trayW) / 2;
            if (shape != AppearanceSettings.ShapeNone)
                Put(new Border { Width = trayW, Height = barH - 6, CornerRadius = new CornerRadius(Math.Min(rad, 14)), Background = tileBrush }, trayX, y0 + 3);
            Put(new TextBlock { Text = "12:00", Foreground = B("#FFFFFF", 0.9), FontSize = 12, FontFamily = new FontFamily("Segoe UI") }, trayX + trayW - 44, y0 + (barH - 16) / 2);
            Put(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = B("#FFFFFF", 0.7) }, trayX + 14, y0 + (barH - 12) / 2);
            Put(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = B("#FFFFFF", 0.7) }, trayX + 34, y0 + (barH - 12) / 2);
        }
        _ = edgesOnly;
    }
}
