using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ColorPlusApp;

// Живой предпросмотр: выдуманный рабочий стол с двумя окнами и панелью задач. Показывает ровно то, что получится после «Применить».
sealed class Preview : Canvas
{
    public const double W = 760, H = 400;

    public Preview()
    {
        Width = W; Height = H; ClipToBounds = true;
    }

    static SolidColorBrush B(string hex, double opacity = 1)
    {
        var (r, g, b) = Col.Parse(hex);
        var br = new SolidColorBrush(Color.FromRgb((byte)r, (byte)g, (byte)b)) { Opacity = opacity };
        br.Freeze();
        return br;
    }

    void Put(UIElement e, double x, double y) { SetLeft(e, x); SetTop(e, y); Children.Add(e); }

    static TextBlock Text(string t, string color, double size, FontWeight? weight = null, double opacity = 1) =>
        new() { Text = t, Foreground = B(color, opacity), FontSize = size, FontWeight = weight ?? FontWeights.Normal, FontFamily = new FontFamily("Segoe UI") };

    // systemAccent — цвет акцента Windows сейчас (так выглядит «как есть», пока вы ничего не применили)
    public void Update(ColorSettings s, string systemAccent)
    {
        Children.Clear();
        var win = s.Windows;
        var acc = win ? s.Color : systemAccent;                       // выделение и полоски дисков
        var titleBg = win ? s.Color : "#2B2B33";
        var titleFg = win ? Col.Readable(s.Color) : "#E8E8EE";

        // обои
        Put(new Rectangle { Width = W, Height = H, Fill = new LinearGradientBrush(Color.FromRgb(0x24, 0x18, 0x52), Color.FromRgb(0x0B, 0x08, 0x18), 60) }, 0, 0);
        Put(new Ellipse { Width = 380, Height = 380, Fill = new RadialGradientBrush(Color.FromArgb(150, 0x6C, 0x4D, 0xFF), Color.FromArgb(0, 0x6C, 0x4D, 0xFF)) }, 470, -120);
        Put(new Ellipse { Width = 340, Height = 340, Fill = new RadialGradientBrush(Color.FromArgb(120, 0xFF, 0x2A, 0x9D), Color.FromArgb(0, 0xFF, 0x2A, 0x9D)) }, -90, 150);

        Window(24, 26, 380, 236, "Приложение", titleBg, titleFg, w =>
        {
            string[] names = { "Документы", "Загрузки", "Рабочий стол", "Музыка" };
            for (var i = 0; i < names.Length; i++)
            {
                var y = 48 + i * 44.0;
                var sel = i == 0;
                if (sel) w.Children.Add(Placed(new Border { Width = 356, Height = 38, CornerRadius = new CornerRadius(6), Background = B(acc, 0.92) }, 12, y - 3));
                w.Children.Add(Placed(new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Background = B(sel ? Col.Readable(acc) : "#2CC4B3", sel ? 0.85 : 0.9) }, 24, y + 5));
                w.Children.Add(Placed(Text(names[i], sel ? Col.Readable(acc) : "#E8E8EE", 14), 62, y + 5));
            }
        });
        Put(Text("Обычное окно: заголовок и выделение", "#FFFFFF", 11, null, 0.75), 26, 266);

        Window(430, 66, 306, 196, "Этот компьютер", titleBg, titleFg, w =>
        {
            w.Children.Add(Placed(Text("Windows (C:)", "#E8E8EE", 13), 18, 48));
            w.Children.Add(Placed(new Border { Width = 220, Height = 8, CornerRadius = new CornerRadius(4), Background = B(acc) }, 18, 72));
            w.Children.Add(Placed(Text("29,6 ГБ свободно из 149 ГБ", "#9A9AAA", 11), 18, 86));
            w.Children.Add(Placed(Text("Temp (D:)", "#E8E8EE", 13), 18, 118));
            w.Children.Add(Placed(new Border { Width = 220, Height = 8, CornerRadius = new CornerRadius(4), Background = B("#8B86B8", 0.5) }, 18, 142));
            w.Children.Add(Placed(Text("219 ГБ свободно из 219 ГБ", "#9A9AAA", 11), 18, 156));
        });
        Put(Text("Проводник", "#FFFFFF", 11, null, 0.75), 432, 266);

        // панель задач: цвет получают острова нашей панели (Taskbar Plus), обычная панель Windows цветом не заливается
        double y0 = H - 46;
        var tint = s.Taskbar;
        if (!tint) Put(new Rectangle { Width = W, Height = 46, Fill = B("#1C1C21", 0.93) }, 0, y0);
        var tile = tint ? B(s.Color, 0.85) : B("#2A2A31", 0.9);
        Put(new Border { Width = 250, Height = 32, CornerRadius = new CornerRadius(10), Background = tile }, (W - 250) / 2, y0 + 7);
        string[] icons = { "#00F0FF", "#FF2A9D", "#F2F2F2", "#2CFF9A", "#FFB020", "#6C4DFF" };
        for (var i = 0; i < icons.Length; i++)
            Put(new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(5), Background = B(icons[i], 0.95) }, (W - 250) / 2 + 18 + i * 36, y0 + 14);
        Put(new Border { Width = 124, Height = 32, CornerRadius = new CornerRadius(10), Background = tile }, W - 140, y0 + 7);
        Put(Text("12:00", tint ? Col.Readable(s.Color) : "#FFFFFF", 12, null, 0.95), W - 62, y0 + 15);
        Put(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = B(tint ? Col.Readable(s.Color) : "#FFFFFF", 0.75) }, W - 112, y0 + 17);
        Put(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = B(tint ? Col.Readable(s.Color) : "#FFFFFF", 0.75) }, W - 92, y0 + 17);
    }

    static UIElement Placed(UIElement e, double x, double y) { SetLeft(e, x); SetTop(e, y); return e; }

    // Окно с заголовком; содержимое рисует делегат на холсте окна
    void Window(double x, double y, double w, double h, string title, string titleBg, string titleFg, Action<Canvas> body)
    {
        var win = new Canvas { Width = w, Height = h };
        win.Children.Add(new Border { Width = w, Height = h, CornerRadius = new CornerRadius(8), Background = B("#1C1C21"), BorderBrush = B(titleBg, 0.9), BorderThickness = new Thickness(1) });
        win.Children.Add(new Border { Width = w, Height = 34, CornerRadius = new CornerRadius(8, 8, 0, 0), Background = B(titleBg) });
        win.Children.Add(Placed(Text(title, titleFg, 12, FontWeights.SemiBold), 12, 9));
        win.Children.Add(Placed(Text("—     ☐     ✕", titleFg, 12), w - 86, 8));
        body(win);
        Put(win, x, y);
    }
}
