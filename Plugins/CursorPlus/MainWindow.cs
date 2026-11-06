using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CursorPlusApp;

// Окно Cursor Plus: один экран. Сверху живой предпросмотр (шлейф работает на тёмной площадке), ниже — форма и цвет курсора, шлейф, клик и «Применить».
// Пока вы не нажали «Применить», в Windows ничего не меняется.
sealed class MainWindow : Window
{
    static readonly string[] Swatches = { "#FF2A9D", "#FF4D4D", "#FF8A3D", "#FFB020", "#F5D90A", "#20C27A", "#00F0FF", "#1E6BFF", "#6C4DFF", "#C04DFF", "#F2F2F2", "#101018" };

    static readonly (string Id, string Title)[] TrailStyles =
    {
        ("none", "Нет"), ("comet", "Комета"), ("sparks", "Искры"), ("glow", "Свечение"), ("stars", "Звёзды"), ("bubbles", "Пузырьки"), ("hearts", "Сердечки"), ("ghost", "Призраки"),
    };

    readonly CursorSettings draft;
    readonly CursorApplier applier = new();
    readonly TrailPreview preview = new();
    readonly StackPanel page = new();
    readonly TextBlock status = new();
    readonly StackPanel roleStrip = new() { Orientation = Orientation.Horizontal };
    readonly List<(Image Img, string Shape)> thumbs = new();
    TextBox? hexBox;
    bool busy;

    static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    public MainWindow(CursorSettings settings)
    {
        draft = settings.Clone();
        draft.Normalize();
        Title = "Cursor Plus";
        Width = Math.Min(1000, SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(900, SystemParameters.WorkArea.Height - 40);
        MinWidth = 700; MinHeight = 500;
        Background = Res("Bg"); Foreground = Res("Text");
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        page.Margin = new Thickness(28, 22, 28, 24);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
        BuildPage();
        Closed += (_, _) => { if (!App.OverlayRunning) Application.Current.Shutdown(); };
    }

    // ---------- сборка окна ----------
    void BuildPage()
    {
        // площадка и полоска указателей переезжают в новую карточку — сначала отцепляем от старой
        if (roleStrip.Parent is Panel rp) rp.Children.Remove(roleStrip);
        if (preview.Parent is Border pb) pb.Child = null;
        page.Children.Clear();
        thumbs.Clear();
        page.Children.Add(new TextBlock { Text = "CURSOR PLUS", FontFamily = (FontFamily)Application.Current.FindResource("Mono"), FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Res("Accent") });
        page.Children.Add(Note("Свой курсор: цвет, форма, шлейф за мышью и эффекты при клике. Выберите — внизу сразу видно, как будет. Понравилось — «Применить». Пока не нажали, в Windows ничего не меняется."));

        // 1. предпросмотр
        var holder = new Border { Width = TrailPreview.W, Height = TrailPreview.H, BorderBrush = Res("Line"), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, Child = preview, Margin = new Thickness(0, 0, 0, 10) };
        page.Children.Add(Card("1. Так будет выглядеть", new UIElement[]
        {
            Note("Поводите мышью по тёмной площадке: там по-настоящему работает шлейф. Кликните — увидите эффект клика. Курсор там нарисован таким, какой станет."),
            holder,
            new TextBlock { Text = "Остальные указатели Windows", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 6) },
            roleStrip,
        }));

        // 2. курсор
        page.Children.Add(Card("2. Курсор", new UIElement[] { ShapeGallery(), MineButtons(), ColorRow("Цвет курсора", draft.Fill, hex => { draft.Fill = hex; Changed(true); }, true), OutlineRow(), SizeRow(),
            Switch("Перекрасить и остальные указатели", "Рука, текст, стрелки изменения размера, «недоступно» и другие тоже получат ваш цвет. Крутящийся указатель загрузки остаётся как в Windows.", () => draft.All, v => { draft.All = v; Changed(false); }) }));

        // 3. шлейф
        page.Children.Add(Card("3. Шлейф за курсором", new UIElement[]
        {
            Seg("Вид", "", TrailStyles.Select(t => t.Title).ToArray(), Array.FindIndex(TrailStyles, t => t.Id == draft.Trail), i => { draft.Trail = TrailStyles[i].Id; Changed(false); }),
            Seg("Цвет шлейфа", "«Как курсор» — цвет курсора; «Радуга» — переливается; «Два цвета» — плавный переход от первого ко второму.",
                new[] { "Как курсор", "Радуга", "Два цвета", "Свой цвет" }, new[] { "cursor", "rainbow", "two", "custom" }.ToList().IndexOf(draft.TrailColorMode), i => { draft.TrailColorMode = new[] { "cursor", "rainbow", "two", "custom" }[i]; Changed(false); BuildPage(); }),
            (draft.TrailColorMode is "custom" or "two") ? ColorRow(draft.TrailColorMode == "two" ? "Первый цвет" : "Цвет шлейфа", draft.TrailColor, hex => { draft.TrailColor = hex; Changed(false); }, false) : new Grid(),
            draft.TrailColorMode == "two" ? ColorRow("Второй цвет", draft.TrailColor2, hex => { draft.TrailColor2 = hex; Changed(false); }, false) : new Grid(),
            SliderRow("Длина шлейфа", "Как долго след держится за курсором", draft.TrailLength, 0, 100, v => { draft.TrailLength = v; Changed(false); }),
            SliderRow("Размер", "Толщина следа и размер частиц", draft.TrailSize, 0, 100, v => { draft.TrailSize = v; Changed(false); }),
        }));

        // 4. клик
        page.Children.Add(Card("4. При клике", new UIElement[]
        {
            Seg("Эффект", "Вспыхивает там, где вы нажали кнопку мыши. Нажатия клавиатуры плагин не читает.", new[] { "Нет", "Круги", "Вспышка" }, new[] { "none", "ring", "burst" }.ToList().IndexOf(draft.Click), i => { draft.Click = new[] { "none", "ring", "burst" }[i]; Changed(false); }),
        }));

        // кнопки
        status.Foreground = Res("Accent2"); status.FontSize = 13; status.TextWrapping = TextWrapping.Wrap; status.Margin = new Thickness(0, 12, 0, 0);
        status.Text = draft.Applied ? "Сейчас применено. Можно изменить и нажать «Применить» ещё раз." : "Ничего не применено — курсоры Windows как обычно.";
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        buttons.Children.Add(Btn("Применить", "Primary", Apply, first: true));
        buttons.Children.Add(Btn("Вернуть как было", "Danger", Revert));
        page.Children.Add(buttons);
        page.Children.Add(status);
        page.Children.Add(Note("Что нужно знать: форма и цвет курсора записываются в Windows и работают во всех программах (иногда окно программы подхватывает их после перезапуска). " +
                               "Шлейф и эффекты клика рисует прозрачное окно поверх экрана — для этого Cursor Plus остаётся работать в фоне и запускается вместе с Windows; в полноэкранных играх и видео шлейф сам выключается. " +
                               "«Вернуть как было» возвращает прежние указатели и останавливает шлейф."));

        Changed(false);
    }

    // ---------- изменения ----------
    void Changed(bool colorChanged)
    {
        draft.Normalize();
        preview.Update(draft);
        RefreshThumbs();
        RefreshRoles();
        if (colorChanged && hexBox != null && hexBox.Text != draft.Fill) hexBox.Text = draft.Fill;
    }

    Image Thumb(string shape)
    {
        var img = new Image { Width = 48, Height = 48, Stretch = Stretch.Uniform };
        thumbs.Add((img, shape));
        return img;
    }

    BitmapSource? ShapeSource(string shape, int px)
    {
        var tmp = draft.Clone(); tmp.Shape = shape;
        var c = CursorApplier.Build(Roles.Get("Arrow"), tmp, new[] { px })?.FirstOrDefault();
        return c == null ? null : Img.ToSource(c);
    }

    void RefreshThumbs()
    {
        foreach (var (img, shape) in thumbs) img.Source = ShapeSource(shape, 48);
    }

    void RefreshRoles()
    {
        roleStrip.Children.Clear();
        foreach (var role in Roles.All)
        {
            var imgs = CursorApplier.Build(role, draft, new[] { 40 });
            var cell = new StackPanel { Margin = new Thickness(0, 0, 14, 8), Width = 70 };
            var holder = new Border { Background = new SolidColorBrush(Color.FromRgb(20, 17, 40)), Width = 54, Height = 54, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(7) };
            if (imgs != null) holder.Child = new Image { Source = Img.ToSource(imgs[0]), Width = 40, Height = 40 };
            else holder.Child = new TextBlock { Text = "как было", Foreground = Res("Muted"), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            cell.Children.Add(holder);
            cell.Children.Add(new TextBlock { Text = role.Title, FontSize = 11, Foreground = Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
            roleStrip.Children.Add(cell);
        }
    }

    // ---------- формы ----------
    UIElement ShapeGallery()
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        void AddShape(string id, string title)
        {
            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(Thumb(id));
            content.Children.Add(new TextBlock { Text = title, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 76 });
            var b = new Button { Content = content, Width = 86, Padding = new Thickness(4, 8, 4, 6), Margin = new Thickness(0, 0, 8, 8) };
            if (draft.Shape == id) b.Style = (Style)FindResource("Primary");
            b.Click += (_, _) => { draft.Shape = id; BuildPage(); };
            wrap.Children.Add(b);
        }
        foreach (var s in Shapes.All) AddShape(s.Id, s.Title);
        foreach (var m in MineStore.List()) AddShape("mine:" + m.Id, m.Name);
        return wrap;
    }

    UIElement MineButtons()
    {
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var draw = new Button { Content = "＋ Нарисовать свой…", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 6) };
        draw.Click += (_, _) => OpenEditor(null);
        row.Children.Add(draw);
        if (draft.Shape.StartsWith("mine:", StringComparison.Ordinal))
        {
            var id = draft.Shape[5..];
            var edit = new Button { Content = "Изменить выбранный", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 6) };
            edit.Click += (_, _) => OpenEditor(MineStore.Get(id));
            var del = new Button { Content = "Удалить выбранный", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 6), Style = (Style)FindResource("Danger") };
            del.Click += (_, _) => { MineStore.Delete(id); draft.Shape = "classic"; BuildPage(); };
            row.Children.Add(edit); row.Children.Add(del);
        }
        return row;
    }

    void OpenEditor(MineEntry? edit)
    {
        var w = new EditorWindow(edit) { Owner = this };
        if (w.ShowDialog() == true && w.Result != null) { draft.Shape = "mine:" + w.Result.Id; BuildPage(); }
    }

    // ---------- цвет, размер ----------
    UIElement ColorRow(string title, string current, Action<string> set, bool main)
    {
        var col = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        col.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        var wrap = new WrapPanel();
        var box = new TextBox { Text = current, Width = 96, Padding = new Thickness(8, 5, 8, 5), VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 6) };
        if (main) hexBox = box;
        bool lockText = false;
        box.TextChanged += (_, _) =>
        {
            if (lockText) return;
            var t = box.Text.Trim();
            if (Col.IsHex(t)) set(t.ToUpperInvariant());
        };
        wrap.Children.Add(box);
        foreach (var hex in Swatches)
        {
            var h = hex;
            var sw = new Button { Width = 30, Height = 30, Margin = new Thickness(0, 0, 7, 6), Padding = new Thickness(0), Background = new SolidColorBrush(Col.ToColor(hex)), BorderBrush = Res("Line"), BorderThickness = new Thickness(h.Equals(current, StringComparison.OrdinalIgnoreCase) ? 3 : 1), ToolTip = hex };
            sw.Click += (_, _) => { lockText = true; box.Text = h; lockText = false; set(h); if (!main) BuildPage(); };
            wrap.Children.Add(sw);
        }
        col.Children.Add(wrap);
        return col;
    }

    UIElement OutlineRow()
    {
        var options = new[] { "Авто", "Белая", "Чёрная" };
        var values = new[] { "auto", "#FFFFFF", "#101018" };
        var idx = Math.Max(0, Array.IndexOf(values, draft.Outline));
        return Seg("Обводка", "«Авто» — тёмный оттенок выбранного цвета.", options, idx, i => { draft.Outline = values[i]; Changed(false); });
    }

    UIElement SizeRow()
    {
        var lbl = new TextBlock { Text = draft.Size + " %", Width = 56, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = Res("Accent") };
        var sl = new Slider { Minimum = 100, Maximum = 300, Value = draft.Size, Width = 260, SmallChange = 10, LargeChange = 50, IsSnapToTickEnabled = true, TickFrequency = 10, VerticalAlignment = VerticalAlignment.Center };
        sl.ValueChanged += (_, e) => { draft.Size = (int)Math.Round(e.NewValue); lbl.Text = draft.Size + " %"; };
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        p.Children.Add(sl); p.Children.Add(lbl);
        return Row("Размер курсора", "100 % — обычный. Больше — крупнее. Меняется у всех указателей.", p);
    }

    UIElement SliderRow(string title, string hint, int value, int min, int max, Action<int> set)
    {
        var lbl = new TextBlock { Text = value.ToString(), Width = 40, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = Res("Accent") };
        var sl = new Slider { Minimum = min, Maximum = max, Value = value, Width = 260, VerticalAlignment = VerticalAlignment.Center };
        sl.ValueChanged += (_, e) => { var v = (int)Math.Round(e.NewValue); lbl.Text = v.ToString(); set(v); };
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        p.Children.Add(sl); p.Children.Add(lbl);
        return Row(title, hint, p);
    }

    // Выбор из нескольких кнопок
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

    // ---------- применить / вернуть ----------
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (App.OverlayRunning && !App.QuitRequested) { e.Cancel = true; Hide(); return; }
        base.OnClosing(e);
    }

    async void Apply()
    {
        if (busy) return;
        busy = true;
        status.Text = "Применяю…";
        try
        {
            var s = draft.Clone(); s.Normalize();
            var err = await Task.Run(() => applier.Apply(s));
            draft.Applied = true;
            draft.OverlayOn = draft.WantsOverlay;
            draft.Save();
            if (draft.WantsOverlay) App.StartOverlay(draft); else App.StopOverlay();
            App.SetAutostart(draft.WantsOverlay);
            status.Text = err == null
                ? "Применено. " + (draft.WantsOverlay ? "Шлейф работает; Cursor Plus остаётся в фоне." : "") + " Если какая-то программа показывает старый курсор — перезапустите её."
                : "Применилось не всё: " + err;
        }
        catch (Exception ex) { status.Text = "Не вышло: " + ex.Message; }
        finally { busy = false; }
    }

    async void Revert()
    {
        if (busy) return;
        busy = true;
        try
        {
            App.StopOverlay();
            var err = await Task.Run(() => applier.Revert());
            draft.Applied = false; draft.OverlayOn = false;
            draft.Save();
            App.SetAutostart(false);
            status.Text = err == null ? "Всё возвращено: курсоры Windows как были, шлейф выключен." : "Вернулось не всё: " + err;
        }
        catch (Exception ex) { status.Text = "Не вышло: " + ex.Message; }
        finally { busy = false; }
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

    // Служебный режим: рисует окно целиком в PNG без показа
    public void RenderTo(string file)
    {
        preview.Update(draft);
        preview.Simulate(1.6);
        var sv = (ScrollViewer)Content;
        sv.Content = null;
        System.Windows.Documents.TextElement.SetForeground(page, Foreground);
        System.Windows.Documents.TextElement.SetFontFamily(page, FontFamily);
        System.Windows.Documents.TextElement.SetFontSize(page, FontSize);
        Loc.ApplyTree(page);   // служебный снимок: окно не показано, переводим вручную
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
}
