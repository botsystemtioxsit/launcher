using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TaskbarPlusApp;

// Живой предпросмотр: выдуманный рабочий стол и панель задач в выбранной форме. Плашки строит ТОТ ЖЕ ShapeBuilder, что рисует настоящее окно
// под панелью, из выдуманных, но похожих на настоящие положений значков — поэтому то, что видно здесь, получится после «Применить».
sealed class Preview : Canvas
{
    public const double W = 760, H = 250;
    const double VirtualW = 1000, BarH = 48;    // выдуманная панель: 1000 px в ширину, 48 в высоту (как на экране со 100% масштабом)
    const double K = W / VirtualW;

    public Preview() { Width = W; Height = H; ClipToBounds = true; }

    static SolidColorBrush B(string hex, double opacity = 1)
    {
        var br = new SolidColorBrush(Palette.Parse(hex)) { Opacity = opacity };
        br.Freeze();
        return br;
    }

    void Put(UIElement e, double x, double y) { SetLeft(e, x); SetTop(e, y); Children.Add(e); }

    // Положение значков на выдуманной панели: слева виджеты, по центру «Пуск» и значки программ, справа часы с треем
    static BarGeometry FakeGeometry(int icons)
    {
        var g = new BarGeometry { RootW = (int)VirtualW, RootH = (int)BarH, Scale100 = 100 };
        const int step = 48;
        var startX = (int)((VirtualW - icons * step) / 2);
        for (var i = 0; i < icons; i++) g.Icons.Add(new Box(startX + i * step, 4, startX + i * step + 44, 44));
        g.Apps = new Box(startX, 4, startX + icons * step - 4, 44);
        g.Widgets = new Box(8, 4, 112, 44);
        g.System = new Box((int)VirtualW - 178, 4, (int)VirtualW - 8, 44);
        return g;
    }

    public void Update(AppearanceSettings s)
    {
        Children.Clear();
        var fill = Palette.Resolve();
        var plate = fill.Color is { } c ? new SolidColorBrush(c) { Opacity = Math.Max(0.5, fill.Alpha) } : null;
        plate?.Freeze();

        // обои и окно
        Put(new Rectangle { Width = W, Height = H, Fill = new LinearGradientBrush(Color.FromRgb(0x24, 0x18, 0x52), Color.FromRgb(0x0B, 0x08, 0x18), 60) }, 0, 0);
        Put(new Ellipse { Width = 330, Height = 330, Fill = new RadialGradientBrush(Color.FromArgb(150, 0x6C, 0x4D, 0xFF), Color.FromArgb(0, 0x6C, 0x4D, 0xFF)) }, 430, -140);
        Put(new Ellipse { Width = 300, Height = 300, Fill = new RadialGradientBrush(Color.FromArgb(110, 0xFF, 0x2A, 0x9D), Color.FromArgb(0, 0xFF, 0x2A, 0x9D)) }, -90, 20);
        Put(new Border { Width = 300, Height = 120, CornerRadius = new CornerRadius(8), Background = B("#1C1C21", 0.9), BorderBrush = B("#34343C"), BorderThickness = new Thickness(1) }, 40, 24);
        Put(new Border { Width = 300, Height = 26, CornerRadius = new CornerRadius(8, 8, 0, 0), Background = B("#2B2B33") }, 40, 24);

        var g = FakeGeometry(7);
        var barTop = H - BarH * K;

        // «Обычная» — родная панель Windows на всю ширину; остальные — плашки из ShapeBuilder
        if (s.Shape == AppearanceSettings.ShapeNone)
            Put(new Rectangle { Width = W, Height = BarH * K, Fill = B("#1C1C21", 0.94) }, 0, barTop);
        else
        {
            var rects = ShapeBuilder.Build(s, (int)VirtualW, (int)BarH, 1.0, g);
            if (rects.Count > 0)
            {
                var group = new GeometryGroup { FillRule = FillRule.Nonzero };
                foreach (var q in rects) group.Children.Add(new RectangleGeometry(new Rect(q.X * K, q.Y * K, q.W * K, q.H * K), q.R * K, q.R * K));
                Put(new Path { Data = group, Fill = plate ?? Brushes.Transparent, Stroke = plate == null ? B("#FFFFFF", 0.25) : null, StrokeThickness = 1 }, 0, barTop);
            }
        }

        // значки приложений
        string[] colors = { "#00F0FF", "#FF2A9D", "#F2F2F2", "#2CFF9A", "#FFB020", "#6C4DFF", "#00F0FF" };
        for (var i = 0; i < g.Icons.Count; i++)
        {
            var b = g.Icons[i];
            Put(new Border { Width = 26 * K, Height = 26 * K, CornerRadius = new CornerRadius(4), Background = B(colors[i % colors.Length], 0.95) },
                (b.L + 9) * K, barTop + 11 * K);
        }
        // виджеты и часы с треем
        Put(new Ellipse { Width = 16 * K * 1.3, Height = 16 * K * 1.3, Fill = B("#FFD84D") }, 22 * K, barTop + 16 * K);
        Put(new TextBlock { Text = "12:00", Foreground = B("#FFFFFF", 0.9), FontSize = 12, FontFamily = new FontFamily("Segoe UI") }, (VirtualW - 62) * K, barTop + 14 * K);
        for (var i = 0; i < 3; i++)
            Put(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(2), Background = B("#FFFFFF", 0.7) }, (VirtualW - 168 + i * 24) * K, barTop + 19 * K);
    }
}
