using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SeasonPlusApp;

// Окно Season Plus: один экран. Сверху живой предпросмотр (те же сезоны и праздники на выдуманном рабочем столе),
// ниже — что падает, праздник, гирлянда и кнопки «Включить» / «Выключить». Пока не нажато «Включить», на экране ничего не меняется.
sealed class MainWindow : Window
{
    const int PW = 680, PH = 320;

    static readonly string[] SeasonNames = { "По календарю", "Зима", "Весна", "Лето", "Осень", "Выключено" };
    static readonly string[] SeasonIds = { "auto", "winter", "spring", "summer", "autumn", "off" };
    static readonly string[] HolidayNames = { "Нет", "По датам", "Новый год", "Рождество", "Тет", "Хэллоуин", "День влюблённых", "8 Марта", "Масленица", "Пасха", "Свой праздник" };
    static readonly string[] HolidayIds = { "off", "auto", "newyear", "xmas", "tet", "halloween", "valentine", "women", "maslenitsa", "easter", "custom" };

    readonly SeasonSettings draft;
    readonly SeasonEngine eng = new(7);
    readonly Raster ras = new();
    readonly WriteableBitmap wb = new(PW, PH, 96, 96, PixelFormats.Pbgra32, null);
    readonly Image overlayImg = new();
    readonly Border skyBox = new();
    readonly StackPanel page = new();
    readonly TextBlock status = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    bool warmedOnce;

    static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    public MainWindow(SeasonSettings settings)
    {
        draft = settings.Clone();
        draft.Normalize();
        Title = "Season Plus";
        Width = Math.Min(1000, SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(900, SystemParameters.WorkArea.Height - 40);
        MinWidth = 720; MinHeight = 520;
        Background = Res("Bg"); Foreground = Res("Text");
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        page.Margin = new Thickness(28, 22, 28, 24);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
        ras.Resize(PW, PH);
        eng.W = PW; eng.H = PH; eng.OX = 0; eng.OY = 0;
        BuildPreview();
        BuildPage();
        Sync();
        timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => timer.Start();
        Closed += (_, _) => { timer.Stop(); if (!App.OverlayRunning) Application.Current.Shutdown(); };
    }

    // ---------- предпросмотр ----------
    Canvas mock = new();

    void BuildPreview()
    {
        mock = new Canvas { Width = PW, Height = PH, ClipToBounds = true };
        mock.Children.Add(skyBox);
        skyBox.Width = PW; skyBox.Height = PH;
        MockWindow(36, 64, 294, 206, "Проводник");
        MockWindow(290, 118, 310, 152, "Браузер");
        var tb = new Border { Width = PW, Height = 30, Background = new SolidColorBrush(Color.FromRgb(24, 24, 34)) };
        Canvas.SetTop(tb, 270);
        mock.Children.Add(tb);
        for (var i = 0; i < 5; i++)
        {
            var ic = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.FromRgb((byte)(80 + i * 30), (byte)(120 + i * 12), 200)) };
            Canvas.SetLeft(ic, 250 + i * 34); Canvas.SetTop(ic, 276);
            mock.Children.Add(ic);
        }
        overlayImg.Width = PW; overlayImg.Height = PH; overlayImg.Source = wb; overlayImg.IsHitTestVisible = false;
        mock.Children.Add(overlayImg);
    }

    void MockWindow(double x, double y, double w, double h, string title)
    {
        var body = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromRgb(240, 242, 248)), BorderBrush = new SolidColorBrush(Color.FromRgb(150, 156, 176)), BorderThickness = new Thickness(1) };
        var bar = new Border { Height = 26, CornerRadius = new CornerRadius(8, 8, 0, 0), Background = new SolidColorBrush(Color.FromRgb(214, 220, 236)), VerticalAlignment = VerticalAlignment.Top, Child = new TextBlock { Text = title, Margin = new Thickness(10, 4, 0, 0), FontSize = 12, Foreground = Brushes.Black } };
        var g = new Grid(); g.Children.Add(bar); body.Child = g;
        for (var i = 0; i < 4; i++)
            g.Children.Add(new Border { Height = 8, Width = 120 + i * 30, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(14, 44 + i * 20, 0, 0), Background = new SolidColorBrush(Color.FromRgb(200, 204, 218)), CornerRadius = new CornerRadius(2) });
        Canvas.SetLeft(body, x); Canvas.SetTop(body, y);
        mock.Children.Add(body);
    }

    void Sync()
    {
        eng.S = draft.Clone();
        eng.Clock = null;
        var surfaces = new List<Surf>
        {
            new() { Id = 1, Left = 36, Right = 330, Top = 64, Radius = 8, Vis = { (36, 330) } },
            new() { Id = 2, Left = 290, Right = 600, Top = 118, Radius = 8, Vis = { (290, 600) } },
            new() { Id = 3, Left = 0, Right = PW, Top = 270, Radius = 0, Taskbar = true, Vis = { (0, PW) } },
        };
        eng.SetSurfaces(surfaces);
        eng.StepTo(0.001);
        var (a, b) = eng.Season switch
        {
            0 => (Color.FromRgb(42, 58, 92), Color.FromRgb(120, 146, 190)),
            1 => (Color.FromRgb(120, 182, 232), Color.FromRgb(214, 236, 208)),
            2 => (Color.FromRgb(74, 155, 224), Color.FromRgb(191, 227, 168)),
            3 => (Color.FromRgb(224, 160, 96), Color.FromRgb(122, 90, 74)),
            _ => (Color.FromRgb(70, 74, 92), Color.FromRgb(110, 116, 140)),
        };
        if (eng.Holiday == "halloween") { a = Color.FromRgb(30, 20, 50); b = Color.FromRgb(90, 50, 80); }
        skyBox.Background = new LinearGradientBrush(a, b, 90);
        if (!warmedOnce) { warmedOnce = true; eng.Warm(14); }
    }

    void Tick()
    {
        if (!IsVisible) return;
        eng.StepTo(0.033);
        ras.Clear();
        eng.Render(ras);
        wb.WritePixels(new Int32Rect(0, 0, PW, PH), ras.Px, PW * 4, 0);
    }

    // Что изменили в черновике: показать в предпросмотре.
    void Changed()
    {
        Sync();
        status.Text = "Предпросмотр обновлён. Чтобы это появилось на экране, нажмите «Включить».";
    }

    // ---------- страница ----------
    void BuildPage()
    {
        if (mock.Parent is Panel mp) mp.Children.Remove(mock);
        if (mock.Parent is Border mb) mb.Child = null;
        page.Children.Clear();
        page.Children.Add(new TextBlock { Text = "SEASON PLUS", FontFamily = (FontFamily)Application.Current.FindResource("Mono"), FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Res("Accent") });
        page.Children.Add(Note("Снег, листья, лепестки и пух падают поверх окон и копятся на их верхнем краю; весной и летом на окнах растёт трава с цветами. Есть праздники с гирляндами, украшениями и салютом. Клики проходят сквозь эффекты, окна не мешают работе."));

        var holder = new Border { Width = PW, Height = PH, BorderBrush = Res("Line"), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, Child = mock, Margin = new Thickness(0, 0, 0, 10) };
        page.Children.Add(Card("1. Так будет выглядеть", new UIElement[] { Note("Это выдуманный рабочий стол: на нём работает то же самое, что появится у вас. Пока не нажали «Включить», на настоящем экране ничего не меняется."), holder }));

        page.Children.Add(Card("2. Время года", new UIElement[]
        {
            Seg("Сезон", "«По календарю» — само меняется зимой, весной, летом и осенью.", SeasonNames, Math.Max(0, Array.IndexOf(SeasonIds, draft.Season)), i => { draft.Season = SeasonIds[i]; Changed(); }),
            SliderRow("Сколько падает", "от редких снежинок до метели", draft.Intensity, 5, 100, v => { draft.Intensity = v; Changed(); }),
            SliderRow("Ветер", "минус — влево, плюс — вправо; порывы бывают всегда", draft.Wind, -100, 100, v => { draft.Wind = v; Changed(); }),
            SliderRow("Скорость", "в процентах", draft.Speed, 50, 200, v => { draft.Speed = v; Changed(); }),
            SliderRow("Размер", "снежинок, листьев, лепестков", draft.Size, 50, 200, v => { draft.Size = v; Changed(); }),
        }));

        page.Children.Add(Card("3. Сугробы, листва и трава на окнах", new UIElement[]
        {
            Switch("Копится на окнах", "снег зимой, лепестки весной, пух летом, листья осенью; если окно сдвинуть — осыпается комьями", () => draft.Piles, v => { draft.Piles = v; Changed(); }),
            SliderRow("Высота", "насколько высокие сугробы и листва", draft.PileHeight, 10, 100, v => { draft.PileHeight = v; Changed(); }),
            Switch("И на панели задач", "панель задач — тоже подоконник (только если она внизу экрана)", () => draft.TaskbarPile, v => { draft.TaskbarPile = v; Changed(); }),
        }));

        var holidayItems = new List<UIElement>
        {
            Seg("Праздник", "«По датам» — включается сам: Новый год, Хэллоуин, 14 февраля, 8 Марта, Масленица, Пасха. Или выберите вручную.", HolidayNames, Math.Max(0, Array.IndexOf(HolidayIds, draft.Holiday)), i => { draft.Holiday = HolidayIds[i]; Changed(); BuildPage(); }),
            Switch("Украшения на окнах", "тыквы, ёлки и подарки, яйца, тюльпаны, блины — сидят на верхнем крае окон", () => draft.Props, v => { draft.Props = v; Changed(); }),
            Switch("Отсчёт до Нового года", "в последние 10 секунд года — большие цифры, потом поздравление", () => draft.Countdown, v => { draft.Countdown = v; Changed(); }),
            Switch("Салют в полночь", "в Новый год ровно в 00:00", () => draft.NyFireworks, v => { draft.NyFireworks = v; Changed(); }),
            Row("Салют сейчас", "запустить на пробу (в предпросмотре и на экране, если включено)", Btn("Запустить салют", null, () => { eng.StartFireworks(10); App.Fireworks(); }, true)),
            Row("Проверить новогоднюю полночь", "отсчёт 10…1, поздравление и салют — как будет в Новый год", Btn("Проверить", null, () => { draft.Holiday = "newyear"; Changed(); eng.NyDemo = 0; })),
        };
        if (draft.Holiday == "custom")
        {
            holidayItems.Add(CustomRow());
        }
        page.Children.Add(Card("4. Праздники", holidayItems));

        page.Children.Add(Card("5. Гирлянда", new UIElement[]
        {
            Seg("Когда", "", new[] { "Никогда", "В праздники", "Всегда" }, draft.Garland, i => { draft.Garland = i; Changed(); }),
            Seg("Где", "", new[] { "На окнах и панели задач", "Только на панели задач" }, draft.GarlandWhere, i => { draft.GarlandWhere = i; Changed(); }),
            Seg("Как горит", "", new[] { "Переливы", "Бегущие огни", "Мерцание", "Ровный свет", "Вспышки", "Смена цвета" }, draft.GarlandMode, i => { draft.GarlandMode = i; Changed(); }),
            Seg("Цвет", "", new[] { "Разноцветная", "Тёплая", "Холодная", "Синяя", "Красно-зелёная", "Золотая" }, draft.GarlandColor, i => { draft.GarlandColor = i; Changed(); }),
            Seg("Лампочки", "", new[] { "Лампочки", "Шарики", "Звёздочки", "Бахрома" }, draft.GarlandBulb, i => { draft.GarlandBulb = i; Changed(); }),
        }));

        var apply = Btn(draft.Active ? "Применить изменения" : "Включить", "Primary", Apply, true);
        var off = Btn("Выключить", null, Revert);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 10) };
        bar.Children.Add(apply); bar.Children.Add(off);
        page.Children.Add(bar);
        status.Text = draft.Active ? "Включено: Season Plus работает в фоне и запускается вместе с Windows." : "Не включено.";
        status.Foreground = Res("Muted"); status.TextWrapping = TextWrapping.Wrap;
        if (status.Parent is Panel sp) sp.Children.Remove(status);
        page.Children.Add(status);
        page.Children.Add(Note("Полноэкранные игры, видео и презентации плагин определяет сам и в это время ничего не рисует. «Выключить» убирает всё с экрана и из автозапуска."));
    }

    UIElement CustomRow()
    {
        var day = new TextBox { Width = 46, Text = draft.CustomDay.ToString(), Margin = new Thickness(0, 0, 6, 0) };
        var mon = new TextBox { Width = 46, Text = draft.CustomMonth.ToString(), Margin = new Thickness(0, 0, 10, 0) };
        void Read() { int.TryParse(day.Text, out var d); int.TryParse(mon.Text, out var m); draft.CustomDay = Math.Clamp(d, 0, 31); draft.CustomMonth = Math.Clamp(m, 0, 12); Changed(); }
        day.LostFocus += (_, _) => Read(); mon.LostFocus += (_, _) => Read();
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        p.Children.Add(new TextBlock { Text = "день", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Foreground = Res("Muted") }); p.Children.Add(day);
        p.Children.Add(new TextBlock { Text = "месяц", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Foreground = Res("Muted") }); p.Children.Add(mon);
        var fx = Seg("Что падает", "", new[] { "Шарики", "Конфетти", "Цветы", "Салют" }, draft.CustomFx, i => { draft.CustomFx = i; Changed(); });
        var col = new StackPanel();
        col.Children.Add(Row("Дата праздника", "каждый год в этот день", p));
        col.Children.Add(fx);
        return col;
    }

    // ---------- применить / выключить ----------
    void Apply()
    {
        draft.Active = true; draft.Normalize(); draft.Save();
        App.StartOverlay(draft);
        App.SetAutostart(true);
        BuildPage();
        status.Text = "Включено. Season Plus остаётся в фоне и запускается вместе с Windows. Окно можно закрыть.";
    }

    void Revert()
    {
        draft.Active = false; draft.Save();
        App.StopOverlay();
        App.SetAutostart(false);
        BuildPage();
        status.Text = "Выключено: с экрана и из автозапуска всё убрано.";
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (App.OverlayRunning && !App.QuitRequested) { e.Cancel = true; Hide(); return; }
        base.OnClosing(e);
    }

    // Служебный режим: рисует окно целиком в PNG без показа
    public void RenderTo(string file, double simSeconds = 20)
    {
        for (var tt = 0.0; tt < simSeconds; tt += 0.05) eng.StepTo(0.05);
        ras.Clear(); eng.Render(ras);
        wb.WritePixels(new Int32Rect(0, 0, PW, PH), ras.Px, PW * 4, 0);
        var sv = (ScrollViewer)Content;
        sv.Content = null;
        System.Windows.Documents.TextElement.SetForeground(page, Foreground);
        System.Windows.Documents.TextElement.SetFontFamily(page, FontFamily);
        System.Windows.Documents.TextElement.SetFontSize(page, FontSize);
        Loc.ApplyTree(page);
        page.Measure(new Size(980, double.PositiveInfinity));
        var w = 980; var h = Math.Max(200, (int)Math.Ceiling(page.DesiredSize.Height));
        page.Arrange(new Rect(0, 0, w, h));
        page.UpdateLayout();
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen()) dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));
        rtb.Render(bg);
        rtb.Render(page);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(file);
        enc.Save(fs);
        sv.Content = page;
    }

    public void SetPreview(SeasonSettings s) { draft.Season = s.Season; draft.Holiday = s.Holiday; Sync(); }

    // ---------- кирпичики интерфейса ----------
    UIElement SliderRow(string title, string hint, int value, int min, int max, Action<int> set)
    {
        var lbl = new TextBlock { Text = value.ToString(), Width = 40, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = Res("Accent") };
        var sl = new Slider { Minimum = min, Maximum = max, Value = value, Width = 260, VerticalAlignment = VerticalAlignment.Center };
        sl.ValueChanged += (_, e) => { var v = (int)Math.Round(e.NewValue); lbl.Text = v.ToString(); set(v); };
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        p.Children.Add(sl); p.Children.Add(lbl);
        return Row(title, hint, p);
    }

    UIElement Seg(string title, string hint, string[] labels, int selected, Action<int> choose)
    {
        var wrap = new WrapPanel { MaxWidth = 520 };
        for (var i = 0; i < labels.Length; i++)
        {
            var idx = i;
            var b = new Button { Content = labels[i], Padding = new Thickness(11, 6, 11, 6), Margin = new Thickness(0, 0, 6, 6) };
            if (i == selected) b.Style = (Style)FindResource("Primary");
            b.Click += (_, _) => { if (idx != selected) { choose(idx); BuildPage(); } };
            wrap.Children.Add(b);
        }
        return Row(title, hint, wrap);
    }

    UIElement Switch(string title, string hint, Func<bool> get, Action<bool> set)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var off = new Button { Content = "Выкл", Padding = new Thickness(14, 6, 14, 6) };
        var on = new Button { Content = "Вкл", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(6, 0, 0, 0) };
        void Paint() { off.Style = get() ? null : (Style)FindResource("Primary"); on.Style = get() ? (Style)FindResource("Primary") : null; }
        off.Click += (_, _) => { set(false); Paint(); };
        on.Click += (_, _) => { set(true); Paint(); };
        panel.Children.Add(off); panel.Children.Add(on);
        Paint();
        return Row(title, hint, panel);
    }

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
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 });
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
        Text = text, Foreground = Res("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12),
    };

    Button Btn(string text, string? style, Action click, bool first = false)
    {
        var b = new Button { Content = text, Margin = new Thickness(first ? 0 : 10, 0, 0, 0), Padding = new Thickness(18, 9, 18, 9) };
        if (style != null) b.Style = (Style)FindResource(style);
        b.Click += (_, _) => click();
        return b;
    }
}
