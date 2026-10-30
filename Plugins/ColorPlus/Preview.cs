using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace ColorPlusApp;

// Живой предпросмотр: мини-рабочий стол с двумя окнами и панелью задач. Показывает, как будут выглядеть выбранные цвета и размытие, ещё до того,
// как человек нажмёт «Применить». Рисуется кодом заново при каждом изменении настроек.
// Честно по смыслу: слева современное окно (оттенок стекла виден), справа Проводник и обычные окна (системное размытие у рамки, содержимое и текст не трогаются).
sealed class DesktopPreview : Border
{
    const double W = 680, H = 360;
    readonly Canvas canvas = new() { Width = W, Height = H, ClipToBounds = true, Background = Brushes.Black };

    public DesktopPreview()
    {
        CornerRadius = new CornerRadius(14);
        BorderThickness = new Thickness(1);
        BorderBrush = (Brush)Application.Current.FindResource("Line");
        ClipToBounds = true;
        Margin = new Thickness(0, 0, 0, 12);
        Child = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, MaxWidth = 760, Child = canvas };
    }

    static Color C(string hex, double alpha = 1)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        c.A = (byte)Math.Clamp((int)Math.Round(alpha * 255), 0, 255);
        return c;
    }
    static SolidColorBrush B(string hex, double alpha = 1) => new(C(hex, alpha));
    static double Lum(string hex) { var c = C(hex); return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0; }

    static T Put<T>(Canvas c, T el, double x, double y) where T : UIElement { Canvas.SetLeft(el, x); Canvas.SetTop(el, y); c.Children.Add(el); return el; }

    // «Обои»: тёмный градиент и цветные пятна, чтобы размытие и прозрачность были заметны. Смещение нужно, чтобы размытая копия под окном совпала с настоящими обоями.
    static Canvas Wallpaper(double offX, double offY)
    {
        var c = new Canvas { Width = W, Height = H };
        var bg = new Rectangle { Width = W, Height = H, Fill = new LinearGradientBrush(C("#0B0A1A"), C("#2A1250"), 35) };
        Put(c, bg, offX, offY);
        void Blob(string hex, double x, double y, double d, double a) =>
            Put(c, new Ellipse { Width = d, Height = d, Fill = B(hex, a), Effect = new BlurEffect { Radius = 26 } }, offX + x, offY + y);
        Blob("#00F0FF", 40, 30, 170, 0.85);
        Blob("#FF2A9D", 430, 110, 210, 0.85);
        Blob("#6C4DFF", 250, 10, 150, 0.8);
        Blob("#1E6BFF", 520, 20, 130, 0.7);
        Blob("#2CFF9A", 150, 220, 120, 0.55);
        return c;
    }

    // Слой стекла для прямоугольника (x, y, w, h): размытая копия обоев под ним и сверху оттенок.
    static FrameworkElement Glass(double x, double y, double w, double h, double blur, string? tint, double tintAlpha, double radius = 0)
    {
        var host = new Border { Width = w, Height = h, ClipToBounds = true, CornerRadius = new CornerRadius(radius) };
        var grid = new Grid();
        var wall = Wallpaper(-x, -y);
        wall.Effect = blur > 0 ? new BlurEffect { Radius = blur, RenderingBias = RenderingBias.Performance } : null;
        grid.Children.Add(wall);
        if (tint != null && tintAlpha > 0) grid.Children.Add(new Rectangle { Fill = B(tint, tintAlpha) });
        host.Child = grid;
        return host;
    }

    public void Update(ColorSettings s)
    {
        canvas.Children.Clear();
        Put(canvas, Wallpaper(0, 0), 0, 0);

        var dark = s.DarkTitle;
        var baseBody = dark ? "#202024" : "#F5F5F7";
        var text = dark ? "#ECECF2" : "#1B1B1F";
        var muted = dark ? "#9A9AA6" : "#6A6A74";
        var windowsOn = s.WindowsOn;
        var backdrop = windowsOn ? s.Backdrop : ColorSettings.BdNone;
        var tintAlpha = Math.Clamp(s.TintOpacity / 100.0, 0, 1) * 0.92;
        var whole = windowsOn && s.WindowMode == ColorSettings.WmAlpha ? Math.Clamp(s.WindowOpacity / 100.0, 0.2, 1) : 1.0;
        var titleBg = windowsOn ? s.TitleColor : null;
        var titleFg = windowsOn ? s.TitleTextColor : null;

        // ---------- окно слева: современное приложение (оттенок виден по всему окну) ----------
        {
            double x = 30, y = 26, w = 330, h = 218;
            var win = new Canvas { Width = w, Height = h, Opacity = whole };
            FrameworkElement body;
            if (backdrop is ColorSettings.BdBlur or ColorSettings.BdAcrylic) body = Glass(x, y, w, h, 18, s.Tint, tintAlpha, 8);
            else if (backdrop is ColorSettings.BdMica or ColorSettings.BdTabbed) body = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(8), Background = B(dark ? "#2A2A30" : "#E8E8EE") };
            else body = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(8), Background = B(baseBody) };
            Put(win, body, 0, 0);
            // строка заголовка
            var tb = new Border { Width = w, Height = 30, CornerRadius = new CornerRadius(8, 8, 0, 0), Background = titleBg != null ? B(titleBg) : Brushes.Transparent };
            Put(win, tb, 0, 0);
            var tcolor = titleFg ?? (titleBg != null ? (Lum(titleBg) > 0.55 ? "#111111" : "#F2F2F2") : text);
            Put(win, new TextBlock { Text = "Приложение", Foreground = B(tcolor), FontSize = 12, FontWeight = FontWeights.SemiBold }, 12, 7);
            Put(win, new TextBlock { Text = "—    ☐    ✕", Foreground = B(tcolor), FontSize = 12 }, w - 78, 6);
            // содержимое: строки списка, выделенная строка, подписи
            var rowsTop = 44;
            string[] names = { "Документы", "Загрузки", "Рабочий стол", "Музыка" };
            for (var i = 0; i < names.Length; i++)
            {
                var ry = rowsTop + i * 34;
                var selected = i == 1;
                if (selected)
                {
                    var selBg = s.SelectionOn ? s.SelectionColor : (dark ? "#3A3A44" : "#CFE3F7");
                    Put(win, new Border { Width = w - 28, Height = 28, CornerRadius = new CornerRadius(5), Background = B(selBg, s.SelectionOn ? 1 : 0.9) }, 14, ry);
                }
                Put(win, new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(4), Background = B(i % 2 == 0 ? "#00F0FF" : "#FF2A9D", 0.9) }, 24, ry + 5);
                var fg = selected && s.SelectionOn && !s.SelectionTextAuto ? s.SelectionTextColor : text;
                Put(win, new TextBlock { Text = names[i], Foreground = B(fg), FontSize = 12.5 }, 52, ry + 5);
            }
            Put(win, new TextBlock { Text = "Текст остаётся как есть — цвет красит только фон", Foreground = B(muted), FontSize = 11 }, 14, h - 24);
            Put(canvas, win, x, y);
            Put(canvas, new TextBlock { Text = "Современное окно", Foreground = B("#FFFFFF", 0.75), FontSize = 11 }, x + 2, y + h + 3);
        }

        // ---------- окно справа: Проводник (системное размытие у рамки, содержимое и текст не трогаются) ----------
        {
            double x = 330, y = 92, w = 320, h = 190;
            var win = new Canvas { Width = w, Height = h, Opacity = whole };
            var chrome = backdrop switch
            {
                ColorSettings.BdNone => new Border { Width = w, Height = 34, CornerRadius = new CornerRadius(8, 8, 0, 0), Background = B(dark ? "#1C1C20" : "#EDEDF2") } as FrameworkElement,
                ColorSettings.BdMica or ColorSettings.BdTabbed => new Border { Width = w, Height = 34, CornerRadius = new CornerRadius(8, 8, 0, 0), Background = B(dark ? "#2B2B31" : "#E3E3EA") },
                _ => Glass(x, y, w, 34, 18, dark ? "#2B2B31" : "#ECECF0", 0.55, 8),   // системное размытие без вашего оттенка
            };
            Put(win, new Border { Width = w, Height = h, CornerRadius = new CornerRadius(8), Background = B(baseBody) }, 0, 0);
            Put(win, chrome, 0, 0);
            var tcolor = titleFg ?? (titleBg != null ? (Lum(titleBg) > 0.55 ? "#111111" : "#F2F2F2") : text);
            if (titleBg != null) Put(win, new Border { Width = w, Height = 34, CornerRadius = new CornerRadius(8, 8, 0, 0), Background = B(titleBg) }, 0, 0);
            Put(win, new TextBlock { Text = "Этот компьютер", Foreground = B(tcolor), FontSize = 12, FontWeight = FontWeights.SemiBold }, 12, 9);
            Put(win, new TextBlock { Text = "Windows (C:)", Foreground = B(text), FontSize = 12.5 }, 18, 50);
            Put(win, new Border { Width = 150, Height = 8, CornerRadius = new CornerRadius(4), Background = B("#1E6BFF") }, 18, 74);
            Put(win, new TextBlock { Text = "29,6 ГБ свободно из 149 ГБ", Foreground = B(muted), FontSize = 11 }, 18, 88);
            Put(win, new TextBlock { Text = "Temp (D:)", Foreground = B(text), FontSize = 12.5 }, 18, 116);
            Put(win, new Border { Width = 150, Height = 8, CornerRadius = new CornerRadius(4), Background = B("#8B86B8", 0.5) }, 18, 140);
            Put(win, new TextBlock { Text = "219 ГБ свободно из 219 ГБ", Foreground = B(muted), FontSize = 11 }, 18, 154);
            Put(canvas, win, x, y);
            Put(canvas, new TextBlock { Text = "Проводник и обычные окна", Foreground = B("#FFFFFF", 0.75), FontSize = 11 }, x + 2, y + h + 3);
        }

        // ---------- панель задач ----------
        {
            double h = 34, y = H - h;
            var mode = s.TaskbarOn ? s.TaskbarMode : ColorSettings.TbDefault;
            var a = Math.Clamp(s.TaskbarOpacity / 100.0, 0, 1);
            switch (mode)
            {
                case ColorSettings.TbClear: break;
                case ColorSettings.TbBlur: Put(canvas, Glass(0, y, W, h, 16, s.TaskbarColor, a * 0.9), 0, y); break;
                case ColorSettings.TbSolid: Put(canvas, new Rectangle { Width = W, Height = h, Fill = B(s.TaskbarColor, Math.Max(0.05, a)) }, 0, y); break;
                default: Put(canvas, new Rectangle { Width = W, Height = h, Fill = B("#1C1C21", 0.93) }, 0, y); break;
            }
            Put(canvas, new Border { Width = 230, Height = 26, CornerRadius = new CornerRadius(8), Background = B("#2A2A31", 0.88) }, (W - 230) / 2, y + 4);
            string[] icons = { "#00F0FF", "#FF2A9D", "#F2F2F2", "#2CFF9A", "#FFB020", "#6C4DFF" };
            for (var i = 0; i < icons.Length; i++)
                Put(canvas, new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(4), Background = B(icons[i], 0.9) }, (W - 230) / 2 + 16 + i * 34, y + 9);
            Put(canvas, new TextBlock { Text = "12:00", Foreground = B("#FFFFFF", 0.9), FontSize = 11 }, W - 44, y + 9);
            Put(canvas, new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = B("#FFFFFF", 0.7) }, W - 82, y + 11);
            Put(canvas, new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = B("#FFFFFF", 0.7) }, W - 64, y + 11);
        }
    }
}
