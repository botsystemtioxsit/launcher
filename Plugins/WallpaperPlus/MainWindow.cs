using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WallpaperPlusApp;

// Окно Wallpaper Plus. Весь интерфейс собирается кодом: страницы «Мои обои», «Каталог», «Создать», «Сайты» и «Настройки».
sealed partial class MainWindow : Window
{
    readonly Controller ctl;
    readonly WpSettings settings;
    readonly Grid host = new();
    readonly StackPanel[] pages = { new(), new(), new(), new(), new() };
    readonly TextBlock statusLine = new();
    readonly StackPanel nav = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
    int current;
    bool screenshotMode;

    static readonly string[] PageNames = { "Мои обои", "Каталог", "Создать", "Сайты", "Настройки" };
    static readonly string[] SwatchColors = { "#00F0FF", "#FF2A9D", "#6C4DFF", "#1E6BFF", "#20C27A", "#FFB020", "#FF4D4D", "#F2F2F2", "#0B0A1A" };

    static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    public MainWindow(Controller ctl, WpSettings settings, int page = 0)
    {
        this.ctl = ctl;
        this.settings = settings;
        Title = "Wallpaper Plus";
        Width = Math.Min(1040, SystemParameters.WorkArea.Width - 40); Height = Math.Min(860, SystemParameters.WorkArea.Height - 40); MinWidth = 640; MinHeight = 480;
        Background = Res("Bg");
        Foreground = Res("Text");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        AllowDrop = true;
        Drop += OnDrop;

        var top = new StackPanel { Margin = new Thickness(28, 22, 28, 0) };
        top.Children.Add(new TextBlock { Text = "WALLPAPER PLUS", FontFamily = (FontFamily)Application.Current.FindResource("Mono"), FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Res("Accent") });
        top.Children.Add(new TextBlock
        {
            Text = "Плагин Ghost Shell Launcher. Живые обои рабочего стола: видео, GIF, анимированные страницы. Берите из каталога, с сайтов, из своих файлов или создавайте свои. " +
                   "Обои рисуются за значками, системные обои Windows не меняются: «Выключить» возвращает всё как было.",
            Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10),
        });
        statusLine.Foreground = Res("Accent2");
        statusLine.FontSize = 12;
        statusLine.TextWrapping = TextWrapping.Wrap;
        statusLine.Margin = new Thickness(0, 0, 0, 10);
        top.Children.Add(statusLine);
        top.Children.Add(nav);

        host.Margin = new Thickness(28, 0, 28, 24);
        foreach (var p in pages) host.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed, Content = p });
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(host);
        Content = root;

        ctl.Changed += () => Dispatcher.BeginInvoke(new Action(() => { ShowStatus(); if (current == 0) BuildLibrary(); }));
        fpsTimer.Tick += (_, _) => { fpsTimer.Stop(); _ = ctl.ApplyAsync(settings); };
        BuildNav();
        BuildLibrary();
        BuildCatalog();
        BuildSite();
        BuildSettings();
        ShowStatus();
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
        var prev = current;
        current = Math.Clamp(i, 0, pages.Length - 1);
        if (prev == 2 && current != 2) ReleasePreview();
        for (var k = 0; k < host.Children.Count; k++) host.Children[k].Visibility = k == current ? Visibility.Visible : Visibility.Collapsed;
        BuildNav();
        if (current == 0) BuildLibrary();
        if (current == 1) _ = LoadCatalogAsync();
        if (current == 2) BuildCreate();
        if (current == 3) Dispatcher.BeginInvoke(new Action(async () => await StartWeb()), System.Windows.Threading.DispatcherPriority.Loaded);
        if (current == 4) BuildSettings();
    }

    void ShowStatus() { statusLine.Text = ctl.Status; }
    void Say(string text) => statusLine.Text = text;

    // Обои включаются и выключаются сразу; настройки сохраняются
    void Activate(string id)
    {
        settings.Active = id;
        settings.Paused = false;
        settings.Save();
        _ = ctl.ApplyAsync(settings);
        BuildLibrary();
    }

    void ApplySettings()
    {
        settings.Save();
        _ = ctl.ApplyAsync(settings);
    }

    // Перетаскивание файлов на окно: каждый файл или архив добавляется в библиотеку
    void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) _ = InstallFilesAsync(files);
    }

    async Task InstallFilesAsync(IEnumerable<string> paths, string source = "из файла")
    {
        var done = 0; var msgs = new List<string>();
        foreach (var p in paths)
        {
            Say("Добавляю: " + Path.GetFileName(p) + "…");
            var (w, msg) = await Task.Run(() => Library.InstallPath(p, source));
            if (w != null) done++; else msgs.Add(Path.GetFileName(p) + ": " + msg);
        }
        Say(done > 0 ? $"Добавлено обоев: {done}." + (msgs.Count > 0 ? " Не вышло: " + string.Join("; ", msgs) : "") : string.Join("; ", msgs));
        BuildLibrary();
        ShowPage(0);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Обои держатся, пока работает плагин: закрытие окна просто прячет его в фон.
        if (!App.Quitting && settings.Active.Length > 0)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
        Application.Current.Shutdown();
    }

    // ---------- кирпичики интерфейса ----------
    Border Card(string title, IEnumerable<UIElement> rows)
    {
        var col = new StackPanel();
        if (title.Length > 0) col.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
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

    WrapPanel Seg(string[] labels, int selected, Action<int> choose, double maxWidth = 460)
    {
        var seg = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = maxWidth, HorizontalAlignment = HorizontalAlignment.Left };
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

    Grid SliderRow(string title, string hint, double min, double max, double step, double value, string unit, Action<double> changed)
    {
        string Fmt(double v) => (step >= 1 ? ((int)Math.Round(v)).ToString() : v.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture)) + (unit.Length > 0 ? " " + unit : "");
        var valueText = new TextBlock { Text = Fmt(value), Width = 64, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = Res("Accent") };
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, Width = 220, SmallChange = step, LargeChange = step * 5, IsSnapToTickEnabled = true, TickFrequency = step, VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, e) => { valueText.Text = Fmt(e.NewValue); changed(e.NewValue); };
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
                Width = 22, Height = 22, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(0),
                Background = new SolidColorBrush(color), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), ToolTip = hex,
            };
            sw.Click += (_, _) => { box.Text = hex; set(hex); };
            panel.Children.Add(sw);
        }
        return panel;
    }

    static string SizeText(long bytes) => bytes >= 1_048_576 ? (bytes / 1_048_576.0).ToString("0.#") + " МБ" : Math.Max(1, bytes / 1024) + " КБ";

    // Служебный режим для проверки внешнего вида: рисует страницы (кроме «Сайтов» и предпросмотра) в PNG без показа окна.
    public void RenderTo(string folder)
    {
        screenshotMode = true;
        BuildLibrary(); BuildCreate(); BuildSettings();
        for (var i = 0; i < pages.Length; i++)
        {
            if (i == 3) continue;
            ShowPage(i);
            if (i == 1) { pages[1].Children.Clear(); pages[1].Children.Add(Card("Каталог", new UIElement[] { Note("Каталог загружается из интернета; на снимке — заглушка.") })); }
            var content = pages[i];
            var sv = (ScrollViewer)host.Children[i];
            sv.Content = null;
            System.Windows.Documents.TextElement.SetForeground(content, Foreground);
            System.Windows.Documents.TextElement.SetFontFamily(content, FontFamily);
            System.Windows.Documents.TextElement.SetFontSize(content, FontSize);
            Loc.ApplyTree(content);   // служебный снимок: окно не показано, переводим вручную
            content.Measure(new Size(980, double.PositiveInfinity));
            var w = 980;
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
            using var fs = File.Create(Path.Combine(folder, "wallpaper-plus-ui-" + i + ".png"));
            enc.Save(fs);
            sv.Content = content;
        }
    }
}
