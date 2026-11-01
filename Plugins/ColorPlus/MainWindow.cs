using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ColorPlusApp;

// Окно Color Plus: один экран. Сверху живой предпросмотр, ниже — готовые темы, цвет, две галочки и кнопка «Применить».
// Пока вы не нажали «Применить», ничего в Windows не меняется — меняется только предпросмотр (как в Theme Park на Samsung).
sealed class MainWindow : Window
{
    static readonly (string Name, string Hex)[] Presets =
    {
        ("Неон", "#FF2A9D"), ("Океан", "#1E6BFF"), ("Бирюза", "#00B8D4"), ("Лес", "#20C27A"),
        ("Закат", "#FF8A3D"), ("Лаванда", "#8B6CFF"), ("Огонь", "#FF4D4D"), ("Золото", "#FFB020"), ("Графит", "#6B7280"),
    };
    static readonly string[] Swatches = { "#FF2A9D", "#FF4D4D", "#FF8A3D", "#FFB020", "#F5D90A", "#20C27A", "#00B8D4", "#1E6BFF", "#6C4DFF", "#8B6CFF", "#C04DFF", "#6B7280", "#F2F2F2", "#101018" };

    readonly ColorSettings draft;
    readonly Applier applier = new();
    readonly Preview preview = new();
    readonly string systemAccent = Applier.CurrentAccent();
    readonly TextBlock status = new();
    readonly TextBlock engineLine = new();
    readonly StackPanel page = new();
    readonly DispatcherTimer engineTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    TextBox? hexBox;
    Slider? hueSlider, valSlider;
    bool lockSync;
    double sat = 0.85;

    static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    public MainWindow(ColorSettings settings)
    {
        draft = settings.Clone();
        Title = "Color Plus";
        Width = Math.Min(1000, SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(880, SystemParameters.WorkArea.Height - 40);
        MinWidth = 640; MinHeight = 480;
        Background = Res("Bg");
        Foreground = Res("Text");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        page.Margin = new Thickness(28, 22, 28, 24);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
        BuildPage();
        Closed += (_, _) => Application.Current.Shutdown();
        engineTimer.Tick += (_, _) => ShowEngine();
        engineTimer.Start();
    }

    // ---------- сборка окна ----------
    void BuildPage()
    {
        page.Children.Clear();
        page.Children.Add(new TextBlock { Text = "COLOR PLUS", FontFamily = (FontFamily)Application.Current.FindResource("Mono"), FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Res("Accent") });
        page.Children.Add(Note("Выберите цвет — внизу сразу видно, как будет. Понравилось — нажмите «Применить». Пока не нажали, в Windows ничего не меняется."));

        // предпросмотр
        var holder = new Border { Width = Preview.W, Height = Preview.H, BorderBrush = Res("Line"), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, Child = preview, Margin = new Thickness(0, 0, 0, 10) };
        page.Children.Add(holder);

        // готовые темы
        var themes = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        foreach (var (name, hex) in Presets)
        {
            var h = hex;
            var dot = new Ellipse { Width = 14, Height = 14, Fill = new SolidColorBrush(ParseColor(hex)), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(dot);
            content.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Content = content, Padding = new Thickness(12, 7, 14, 7), Margin = new Thickness(0, 0, 8, 8) };
            b.Click += (_, _) => SetColor(h, false);
            themes.Children.Add(b);
        }
        page.Children.Add(Card("Готовые темы", new UIElement[] { themes }));

        // свой цвет
        var swatchRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var hex in Swatches)
        {
            var h = hex;
            var sw = new Button
            {
                Width = 30, Height = 30, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(0),
                Background = new SolidColorBrush(ParseColor(hex)), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), ToolTip = hex,
            };
            sw.Click += (_, _) => SetColor(h, false);
            swatchRow.Children.Add(sw);
        }
        hexBox = new TextBox { Text = draft.Color, Width = 110, Padding = new Thickness(8, 5, 8, 5), VerticalContentAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        hexBox.TextChanged += (_, _) =>
        {
            if (lockSync) return;
            var t = hexBox.Text.Trim();
            if (Col.IsHex(t)) SetColor(t.ToUpperInvariant(), false, fromHex: true);
        };
        var (hh, ss, vv) = Col.ToHsv(draft.Color);
        if (ss > 0.05) sat = ss;
        hueSlider = new Slider { Minimum = 0, Maximum = 360, Value = hh, Width = 360, VerticalAlignment = VerticalAlignment.Center };
        valSlider = new Slider { Minimum = 25, Maximum = 100, Value = vv * 100, Width = 360, VerticalAlignment = VerticalAlignment.Center };
        hueSlider.ValueChanged += (_, _) => FromSliders();
        valSlider.ValueChanged += (_, _) => FromSliders();
        page.Children.Add(Card("Свой цвет", new UIElement[]
        {
            swatchRow,
            Row("Цвет в виде #RRGGBB", "Можно вписать любой", hexBox),
            Row("Оттенок", "", hueSlider),
            Row("Яркость", "", valSlider),
        }));

        // что покрасить
        page.Children.Add(Card("Что покрасить", new UIElement[]
        {
            Switch("Окна и выделение", "Заголовки и рамки окон, подсветка выделенного, акцентный цвет Windows. Меню «Пуск» и панель задач этим цветом не красятся.", () => draft.Windows, v => draft.Windows = v),
            Switch("Панель задач", "Цвет получают острова нашей панели (нужен запущенный Taskbar Plus). Обычная панель Windows цветом не заливается.", () => draft.Taskbar, v => draft.Taskbar = v),
            engineLine,
        }));

        // кнопки
        status.Foreground = Res("Accent2"); status.FontSize = 13; status.TextWrapping = TextWrapping.Wrap; status.Margin = new Thickness(0, 12, 0, 0);
        status.Text = draft.Applied ? "Сейчас применено: " + draft.Color + ". Можно выбрать другой цвет и нажать «Применить» ещё раз." : "Ничего не применено — Windows как обычно.";
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        buttons.Children.Add(Btn("Применить", "Primary", Apply, first: true));
        buttons.Children.Add(Btn("Вернуть как было", "Danger", Revert));
        page.Children.Add(buttons);
        page.Children.Add(status);
        page.Children.Add(Note("Что нужно знать: заголовки и выделение обновляются сразу, но часть программ (современные из магазина, Chrome, Steam) рисует свои окна сама и подхватит цвет только после перезапуска или не подхватит совсем. Игры Color Plus не трогает — он ничего не рисует поверх программ."));

        ShowEngine();
        Refresh();
    }

    // ---------- действия ----------
    static Color ParseColor(string hex) { var (r, g, b) = Col.Parse(hex); return Color.FromRgb((byte)r, (byte)g, (byte)b); }

    void Refresh() => preview.Update(draft, systemAccent);

    void SetColor(string hex, bool fromSlider, bool fromHex = false)
    {
        draft.Color = hex.ToUpperInvariant();
        lockSync = true;
        try
        {
            if (!fromHex && hexBox != null) hexBox.Text = draft.Color;
            if (!fromSlider && hueSlider != null && valSlider != null)
            {
                var (h, s, v) = Col.ToHsv(draft.Color);
                if (s > 0.05) { hueSlider.Value = h; sat = s; }
                valSlider.Value = Math.Clamp(v * 100, valSlider.Minimum, valSlider.Maximum);
            }
        }
        finally { lockSync = false; }
        Refresh();
    }

    void FromSliders()
    {
        if (lockSync || hueSlider == null || valSlider == null) return;
        SetColor(Col.FromHsv(hueSlider.Value, sat, valSlider.Value / 100.0), true);
    }

    void Apply()
    {
        var err = applier.Apply(draft);
        draft.Applied = true;
        draft.Save();
        status.Text = err == null
            ? "Применено: " + draft.Color + ". " + (draft.Taskbar ? TaskbarLink.EngineState().Text : "")
            : "Применилось не всё: " + err;
    }

    void Revert()
    {
        applier.RevertAll();
        draft.Applied = false;
        draft.Save();
        status.Text = "Всё возвращено, как было до Color Plus.";
    }

    void ShowEngine()
    {
        var (ready, text) = TaskbarLink.EngineState();
        engineLine.Text = draft.Taskbar ? text : "";
        engineLine.Foreground = ready ? Res("Green") : Res("Muted");
        engineLine.FontSize = 12; engineLine.TextWrapping = TextWrapping.Wrap; engineLine.Margin = new Thickness(0, 0, 0, 4);
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

    // Выключатель «Выкл / Вкл»: кнопки сами перекрашиваются при нажатии, предпросмотр обновляется
    UIElement Switch(string title, string hint, Func<bool> get, Action<bool> set)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var off = new Button { Content = "Выкл", Padding = new Thickness(14, 6, 14, 6) };
        var on = new Button { Content = "Вкл", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(6, 0, 0, 0) };
        void Paint() { off.Style = get() ? null : (Style)FindResource("Primary"); on.Style = get() ? (Style)FindResource("Primary") : null; }
        off.Click += (_, _) => { set(false); Paint(); Refresh(); ShowEngine(); };
        on.Click += (_, _) => { set(true); Paint(); Refresh(); ShowEngine(); };
        panel.Children.Add(off); panel.Children.Add(on);
        Paint();
        return Row(title, hint, panel);
    }

    TextBlock Note(string text) => new()
    {
        Text = text, Foreground = Res("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12),
    };

    Button Btn(string text, string? style, Action click, bool first = false)
    {
        var b = new Button { Content = text, Margin = new Thickness(first ? 0 : 10, 0, 0, 0), Padding = new Thickness(18, 9, 18, 9) };
        if (style != null) b.Style = (Style)FindResource(style);
        b.Click += (_, _) => click();
        return b;
    }

    // Служебный режим: рисует окно целиком в PNG без показа (для проверки внешнего вида)
    public void RenderTo(string file)
    {
        System.Windows.Documents.TextElement.SetForeground(page, Foreground);
        System.Windows.Documents.TextElement.SetFontFamily(page, FontFamily);
        System.Windows.Documents.TextElement.SetFontSize(page, FontSize);
        var sv = (ScrollViewer)Content;
        sv.Content = null;
        page.Measure(new Size(980, double.PositiveInfinity));
        var w = 980; var h = Math.Max(200, (int)Math.Ceiling(page.DesiredSize.Height));
        page.Arrange(new Rect(0, 0, w, h));
        page.UpdateLayout();
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen()) dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));
        rtb.Render(bg);
        rtb.Render(page);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        using var fs = File.Create(file);
        enc.Save(fs);
        sv.Content = page;
    }
}
