using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GatewayPlusApp;

// Общие кирпичики окон Gateway Plus (окна «Аккаунт» и «Создать свой VPN»; главное окно держит свои — так сложилось).
static class Ui
{
    public static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
    public static string Version { get { var v = typeof(Ui).Assembly.GetName().Version; return v == null ? "2.0.0" : $"{v.Major}.{v.Minor}.{v.Build}"; } }

    public static void Setup(Window w, string title, double width, double height, double minW = 520, double minH = 460)
    {
        w.Title = title;
        w.Width = Math.Min(width, SystemParameters.WorkArea.Width - 40);
        w.Height = Math.Min(height, SystemParameters.WorkArea.Height - 40);
        w.MinWidth = minW; w.MinHeight = minH;
        w.Background = Res("Bg"); w.Foreground = Res("Text");
        w.FontFamily = new FontFamily("Segoe UI"); w.FontSize = 14;
        w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }

    public static TextBlock Title(string text) => new()
    {
        Text = text, FontFamily = (FontFamily)Application.Current.FindResource("Mono"), FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Res("Accent"),
    };

    public static TextBlock Note(string text, double size = 12) => new() { Text = text, Foreground = Res("Muted"), FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10) };

    public static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 10, 0, 4) };

    public static Button Btn(string text, string? style, Action click, bool first = false)
    {
        var b = new Button { Content = text, Margin = new Thickness(first ? 0 : 10, 0, 0, 8), Padding = new Thickness(18, 9, 18, 9), HorizontalAlignment = HorizontalAlignment.Left };
        if (style != null) b.Style = (Style)Application.Current.FindResource(style);
        b.Click += (_, _) => click();
        return b;
    }

    public static Border Box(UIElement child, double? height = null) => new()
    {
        Background = Res("Panel"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(12), Child = child,
        Height = height ?? double.NaN,
    };

    // Служебный режим: рисует содержимое окна целиком в PNG без показа (для проверки внешнего вида в CI).
    public static void Render(Window w, ScrollViewer sv, FrameworkElement page, string file, int width = 760)
    {
        sv.Content = null;
        System.Windows.Documents.TextElement.SetForeground(page, w.Foreground);
        System.Windows.Documents.TextElement.SetFontFamily(page, w.FontFamily);
        System.Windows.Documents.TextElement.SetFontSize(page, w.FontSize);
        Loc.ApplyTree(page);   // служебный снимок: окно не показано, переводим вручную
        page.Measure(new Size(width, double.PositiveInfinity));
        var h = Math.Max(200, (int)Math.Ceiling(page.DesiredSize.Height));
        page.Arrange(new Rect(0, 0, width, h));
        page.UpdateLayout();
        var rtb = new RenderTargetBitmap(width, h, 96, 96, PixelFormats.Pbgra32);
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen()) dc.DrawRectangle(w.Background, null, new Rect(0, 0, width, h));
        rtb.Render(bg);
        rtb.Render(page);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(file);
        enc.Save(fs);
        sv.Content = page;
    }
}
