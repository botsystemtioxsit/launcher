using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IconPlusApp;

// Окно Icon Plus. Весь интерфейс собирается кодом: страницы «Цвет», «Наборы», «Сайт» и «Состояние».
sealed partial class MainWindow : Window
{
    readonly IconEngine engine;
    readonly IconSettings settings;
    readonly Grid host = new();
    readonly StackPanel[] pages = { new(), new(), new(), new() };
    readonly TextBlock statusLine = new();
    readonly StackPanel nav = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
    int current;

    static readonly string[] SwatchColors = { "#00F0FF", "#FF2A9D", "#6C4DFF", "#1E6BFF", "#20C27A", "#FFB020", "#FF4D4D", "#F2F2F2", "#8B86B8" };
    static readonly string[] PageNames = { "Цвет", "Наборы", "Сайт с иконками", "Состояние" };

    static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    public MainWindow(IconEngine engine, IconSettings settings, int page = 0)
    {
        this.engine = engine;
        this.settings = settings;
        Title = "Icon Plus";
        Width = 1000; Height = 840; MinWidth = 700; MinHeight = 520;
        Background = Res("Bg");
        Foreground = Res("Text");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var top = new StackPanel { Margin = new Thickness(28, 22, 28, 0) };
        top.Children.Add(new TextBlock { Text = "ICON PLUS", FontFamily = (FontFamily)Application.Current.FindResource("Mono"), FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Res("Accent") });
        top.Children.Add(new TextBlock
        {
            Text = "Плагин Ghost Shell Launcher. Значки Windows по-своему: один цвет для всех значков (как тема значков на Android) или готовый набор иконок. " +
                   "Меняются только настройки вашего пользователя, права администратора не нужны; «Вернуть как было» возвращает всё.",
            Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10),
        });
        statusLine.Foreground = Res("Accent2");
        statusLine.FontSize = 12;
        statusLine.TextWrapping = TextWrapping.Wrap;
        statusLine.Margin = new Thickness(0, 0, 0, 10);
        top.Children.Add(statusLine);
        top.Children.Add(nav);

        host.Margin = new Thickness(28, 0, 28, 24);
        foreach (var p in pages)
        {
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed, Content = p };
            host.Children.Add(sv);
        }
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(host);
        Content = root;

        BuildNav();
        BuildColor();
        BuildPacks();
        BuildState();
        BuildSite();
        ShowPage(page);
    }

    void BuildNav()
    {
        nav.Children.Clear();
        for (var i = 0; i < PageNames.Length; i++)
        {
            var idx = i;
            var b = new Button { Content = PageNames[i], Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0) };
            if (i == current) b.Style = (Style)FindResource("Primary");
            b.Click += (_, _) => ShowPage(idx);
            nav.Children.Add(b);
        }
    }

    public void ShowPage(int i)
    {
        current = Math.Clamp(i, 0, pages.Length - 1);
        for (var k = 0; k < host.Children.Count; k++) host.Children[k].Visibility = k == current ? Visibility.Visible : Visibility.Collapsed;
        BuildNav();
        if (current == 3) BuildState();
    }

    void Say(string text) => statusLine.Text = text;

    // ---------- кирпичики интерфейса ----------
    Border Card(string title, IEnumerable<UIElement> rows)
    {
        var col = new StackPanel();
        col.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        foreach (var r in rows) col.Children.Add(r);
        return new Border
        {
            Background = Res("Panel"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(22), Margin = new Thickness(0, 0, 0, 14), Child = col,
        };
    }

    Grid Row(string title, string hint, UIElement control)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        if (hint.Length > 0) text.Children.Add(new TextBlock { Text = hint, Foreground = Res("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap });
        row.Children.Add(text);
        if (control is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    TextBlock Note(string text) => new()
    {
        Text = text, Foreground = Res("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
    };

    Button Btn(string text, string? style, Action click, bool first = false)
    {
        var b = new Button { Content = text, Margin = new Thickness(first ? 0 : 8, 0, 0, 0), Padding = new Thickness(12, 6, 12, 6) };
        if (style != null) b.Style = (Style)FindResource(style);
        b.Click += (_, _) => click();
        return b;
    }

    WrapPanel Seg(string[] labels, int selected, Action<int> choose)
    {
        var seg = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = 460 };
        for (var i = 0; i < labels.Length; i++)
        {
            var idx = i;
            var b = new Button { Content = labels[i], Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 4) };
            if (i == selected) b.Style = (Style)FindResource("Primary");
            b.Click += (_, _) => { if (idx != selected) choose(idx); };
            seg.Children.Add(b);
        }
        return seg;
    }

    // Переключатель-кнопка: подсвечена, когда включена.
    Button Toggle(string title, bool on, Action<bool> changed)
    {
        var b = new Button { Content = title, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 6) };
        if (on) b.Style = (Style)FindResource("Primary");
        b.Click += (_, _) => changed(!on);
        return b;
    }

    Grid SliderRow(string title, string hint, int min, int max, int value, string unit, Action<int> changed)
    {
        var valueText = new TextBlock { Text = value + (unit.Length > 0 ? " " + unit : ""), Width = 64, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = Res("Accent") };
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, Width = 220, SmallChange = 1, LargeChange = 5, IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, e) =>
        {
            var v = (int)Math.Round(e.NewValue);
            valueText.Text = v + (unit.Length > 0 ? " " + unit : "");
            changed(v);
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(slider);
        panel.Children.Add(valueText);
        return Row(title, hint, panel);
    }

    StackPanel ColorPicker(string current, Action<string> set)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var box = new TextBox { Text = current, Width = 86, Padding = new Thickness(6, 4, 6, 4), VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        box.TextChanged += (_, _) =>
        {
            var t = box.Text.Trim();
            if (System.Text.RegularExpressions.Regex.IsMatch(t, "^#[0-9A-Fa-f]{6}$")) set(t.ToUpperInvariant());
        };
        panel.Children.Add(box);
        foreach (var hex in SwatchColors)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var sw = new Button
            {
                Width = 24, Height = 24, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(0),
                Background = new SolidColorBrush(color), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), ToolTip = hex,
            };
            sw.Click += (_, _) => { box.Text = hex; set(hex); };
            panel.Children.Add(sw);
        }
        return panel;
    }

    // Картинка-значок для превью.
    Image Thumb(System.Windows.Media.Imaging.BitmapSource? src, int size = 40) => new()
    {
        Source = src, Width = size, Height = size, Margin = new Thickness(0, 0, 8, 0), SnapsToDevicePixels = true,
    };

    // Служебный режим для проверки внешнего вида: рисует все страницы (кроме сайта) в PNG без показа окна.
    public void RenderTo(string folder)
    {
        for (var i = 0; i < pages.Length; i++)
        {
            if (i == 2) continue;
            ShowPage(i);
            var content = pages[i];
            var sv = (ScrollViewer)host.Children[i];
            sv.Content = null;
            System.Windows.Documents.TextElement.SetForeground(content, Foreground);
            System.Windows.Documents.TextElement.SetFontFamily(content, FontFamily);
            System.Windows.Documents.TextElement.SetFontSize(content, FontSize);
            content.Measure(new Size(940, double.PositiveInfinity));
            var w = 940;
            var h = Math.Max(200, (int)Math.Ceiling(content.DesiredSize.Height));
            content.Arrange(new Rect(0, 0, w, h));
            content.UpdateLayout();
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            var bg = new DrawingVisual();
            using (var dc = bg.RenderOpen()) dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));
            rtb.Render(bg);
            rtb.Render(content);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using var fs = System.IO.File.Create(System.IO.Path.Combine(folder, "icon-plus-ui-" + i + ".png"));
            enc.Save(fs);
            sv.Content = content;
        }
    }
}
