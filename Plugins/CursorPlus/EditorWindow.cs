using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace CursorPlusApp;

// Редактор своего курсора: рисуйте пиксели, укажите точку, которой курсор «целится», и сохраните. Или привезите картинку (.png, .ico, .cur, .ani).
sealed class EditorWindow : Window
{
    const int Canvas = 448;

    static readonly string[] Palette =
    {
        "#FFFFFF", "#101018", "#FF2A9D", "#FF4D4D", "#FF8A3D", "#FFB020", "#F5D90A", "#20C27A",
        "#00F0FF", "#1E6BFF", "#6C4DFF", "#C04DFF", "#8B86B8", "#6B7280", "#B5651D", "#2CFF9A",
    };

    int n = 32;
    byte[] px = new byte[32 * 32 * 4];
    WriteableBitmap wb;
    int hotX, hotY;
    string tool = "pencil";
    (int R, int G, int B) color = (255, 42, 157);
    string existingId = "";
    bool smooth;

    readonly Image art = new();
    readonly Image prevDark = new() { Width = 64, Height = 64 };
    readonly Image prevLight = new() { Width = 64, Height = 64 };
    readonly Rectangle marker = new() { Stroke = Brushes.Cyan, StrokeThickness = 2, IsHitTestVisible = false };
    readonly System.Windows.Controls.Canvas markerLayer = new() { IsHitTestVisible = false };
    readonly Grid stage = new() { Width = Canvas, Height = Canvas };
    readonly System.Windows.Shapes.Path gridLines = new() { IsHitTestVisible = false, Stroke = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)), StrokeThickness = 0.5 };
    readonly TextBox nameBox = new() { Text = "Мой курсор", Width = 220 };
    readonly TextBlock info = new() { Foreground = Brushes.Gray, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 190 };
    readonly StackPanel toolRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
    readonly TextBox hexBox = new() { Text = "#FF2A9D", Width = 112, Padding = new Thickness(8, 4, 8, 4), VerticalContentAlignment = VerticalAlignment.Center };
    bool painting;

    public MineEntry? Result { get; private set; }

    static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    public EditorWindow(MineEntry? edit = null)
    {
        Title = "Свой курсор";
        Width = 760; Height = 780; MinWidth = 700; MinHeight = 600;
        Background = Res("Bg"); Foreground = Res("Text");
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        wb = new WriteableBitmap(n, n, 96, 96, PixelFormats.Bgra32, null);
        if (edit != null)
        {
            var p = MineStore.Pixels(edit);
            if (p != null) { n = edit.N; px = p; hotX = edit.HotX; hotY = edit.HotY; existingId = edit.Id; smooth = edit.Smooth; nameBox.Text = edit.Name; wb = new WriteableBitmap(n, n, 96, 96, PixelFormats.Bgra32, null); }
        }
        else { hotX = 2; hotY = 2; }
        Build();
        Redraw();
    }

    // ---------- сборка окна ----------
    void Build()
    {
        var page = new StackPanel { Margin = new Thickness(24, 18, 24, 18) };
        page.Children.Add(new TextBlock { Text = "СВОЙ КУРСОР", FontFamily = (FontFamily)Application.Current.FindResource("Mono"), FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Res("Accent") });
        page.Children.Add(new TextBlock { Text = "Левая кнопка — рисовать, правая — стирать. Инструмент «Точка клика» отмечает клетку, которой курсор попадает в кнопки и ссылки.", Foreground = Res("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12) });

        foreach (var (id, title) in new[] { ("pencil", "Карандаш"), ("eraser", "Ластик"), ("fill", "Заливка"), ("hot", "Точка клика") })
        {
            var t = id;
            var b = new Button { Content = title, Tag = id, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 6, 0) };
            b.Click += (_, _) => { tool = t; PaintTools(); };
            toolRow.Children.Add(b);
        }
        page.Children.Add(toolRow);
        PaintTools();

        // холст
        var checker = new DrawingBrush
        {
            TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute,
            Drawing = new DrawingGroup
            {
                Children =
                {
                    new GeometryDrawing(new SolidColorBrush(Color.FromRgb(58, 54, 84)), null, new RectangleGeometry(new Rect(0, 0, 16, 16))),
                    new GeometryDrawing(new SolidColorBrush(Color.FromRgb(44, 40, 68)), null, new GeometryGroup { Children = { new RectangleGeometry(new Rect(0, 0, 8, 8)), new RectangleGeometry(new Rect(8, 8, 8, 8)) } }),
                },
            },
        };
        stage.Background = checker;
        RenderOptions.SetBitmapScalingMode(art, BitmapScalingMode.NearestNeighbor);
        art.Width = Canvas; art.Height = Canvas; art.Stretch = Stretch.Fill;
        stage.Children.Add(art);
        stage.Children.Add(gridLines);
        markerLayer.Children.Add(marker);
        stage.Children.Add(markerLayer);
        stage.MouseDown += (_, e) => { painting = true; stage.CaptureMouse(); Apply(e, e.ChangedButton == MouseButton.Right); };
        stage.MouseMove += (_, e) => { if (painting) Apply(e, e.RightButton == MouseButtonState.Pressed); };
        stage.MouseUp += (_, _) => { painting = false; stage.ReleaseMouseCapture(); };

        var board = new Border { BorderBrush = Res("Line"), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, Child = stage };
        var side = new StackPanel { Margin = new Thickness(20, 0, 0, 0) };
        side.Children.Add(new TextBlock { Text = "Так выглядит", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        side.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(12, 10, 24)), Padding = new Thickness(8), Child = prevDark, Margin = new Thickness(0, 0, 0, 8) });
        side.Children.Add(new Border { Background = Brushes.White, Padding = new Thickness(8), Child = prevLight, Margin = new Thickness(0, 0, 0, 8) });
        RenderOptions.SetBitmapScalingMode(prevDark, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetBitmapScalingMode(prevLight, BitmapScalingMode.NearestNeighbor);
        side.Children.Add(info);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(board);
        row.Children.Add(side);
        page.Children.Add(row);

        // цвета
        var pal = new WrapPanel { Margin = new Thickness(0, 12, 0, 6), MaxWidth = 700 };
        foreach (var hex in Palette)
        {
            var h = hex;
            var sw = new Button { Width = 28, Height = 28, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(0), Background = new SolidColorBrush(Col.ToColor(hex)), BorderBrush = Res("Line"), ToolTip = hex };
            sw.Click += (_, _) => { SetColor(h); tool = tool == "eraser" || tool == "hot" ? "pencil" : tool; PaintTools(); };
            pal.Children.Add(sw);
        }
        hexBox.TextChanged += (_, _) => { if (Col.IsHex(hexBox.Text.Trim())) { color = Col.Parse(hexBox.Text.Trim()); } };
        pal.Children.Add(hexBox);
        page.Children.Add(pal);

        // действия
        var acts = new WrapPanel { Margin = new Thickness(0, 4, 0, 10) };
        void Add(string text, Action a, string? style = null)
        {
            var b = new Button { Content = text, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 6) };
            if (style != null) b.Style = (Style)FindResource(style);
            b.Click += (_, _) => a();
            acts.Children.Add(b);
        }
        Add("Очистить", Clear);
        Add("Отразить ↔", Flip);
        Add("Из картинки…", Import);
        page.Children.Add(acts);

        var saveRow = new StackPanel { Orientation = Orientation.Horizontal };
        saveRow.Children.Add(new TextBlock { Text = "Название:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        saveRow.Children.Add(nameBox);
        var save = new Button { Content = "Сохранить", Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(14, 0, 8, 0), Style = (Style)FindResource("Primary") };
        save.Click += (_, _) => Save();
        var cancel = new Button { Content = "Отмена", Padding = new Thickness(18, 8, 18, 8) };
        cancel.Click += (_, _) => { Result = null; DialogResult = false; };
        saveRow.Children.Add(save); saveRow.Children.Add(cancel);
        page.Children.Add(saveRow);

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
    }

    void PaintTools()
    {
        foreach (var o in toolRow.Children.OfType<Button>())
        {
            var id = o.Tag as string ?? "hot";   // инструмент определяется по Tag, а не по подписи (подпись переводится)
            o.Style = id == tool ? (Style)FindResource("Primary") : null;
        }
    }

    void SetColor(string hex) { color = Col.Parse(hex); hexBox.Text = hex; }

    // ---------- рисование ----------
    void Apply(MouseEventArgs e, bool erase)
    {
        var p = e.GetPosition(stage);
        var cell = (double)Canvas / n;
        var x = (int)Math.Floor(p.X / cell); var y = (int)Math.Floor(p.Y / cell);
        if (x < 0 || y < 0 || x >= n || y >= n) return;
        if (tool == "hot") { hotX = x; hotY = y; Redraw(); return; }
        if (tool == "fill" && !erase) { if (e.LeftButton == MouseButtonState.Pressed && Mouse.LeftButton == MouseButtonState.Pressed) { Flood(x, y); painting = false; } Redraw(); return; }
        var i = (y * n + x) * 4;
        if (erase || tool == "eraser") { px[i] = px[i + 1] = px[i + 2] = px[i + 3] = 0; }
        else { px[i] = (byte)color.B; px[i + 1] = (byte)color.G; px[i + 2] = (byte)color.R; px[i + 3] = 255; }
        Redraw();
    }

    void Flood(int sx, int sy)
    {
        var si = (sy * n + sx) * 4;
        var target = (px[si], px[si + 1], px[si + 2], px[si + 3]);
        var repl = ((byte)color.B, (byte)color.G, (byte)color.R, (byte)255);
        if (target == repl) return;
        var stack = new Stack<(int, int)>();
        stack.Push((sx, sy));
        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            if (x < 0 || y < 0 || x >= n || y >= n) continue;
            var i = (y * n + x) * 4;
            if ((px[i], px[i + 1], px[i + 2], px[i + 3]) != target) continue;
            px[i] = repl.Item1; px[i + 1] = repl.Item2; px[i + 2] = repl.Item3; px[i + 3] = repl.Item4;
            stack.Push((x + 1, y)); stack.Push((x - 1, y)); stack.Push((x, y + 1)); stack.Push((x, y - 1));
        }
    }

    void Clear() { Array.Clear(px, 0, px.Length); Redraw(); }

    void Flip()
    {
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n / 2; x++)
                for (var k = 0; k < 4; k++)
                {
                    var a = (y * n + x) * 4 + k; var b = (y * n + (n - 1 - x)) * 4 + k;
                    (px[a], px[b]) = (px[b], px[a]);
                }
        hotX = n - 1 - hotX;
        Redraw();
    }

    void Redraw()
    {
        if (wb.PixelWidth != n) wb = new WriteableBitmap(n, n, 96, 96, PixelFormats.Bgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, n, n), px, n * 4, 0);
        art.Source = wb; prevDark.Source = wb; prevLight.Source = wb;
        RenderOptions.SetBitmapScalingMode(prevDark, smooth ? BitmapScalingMode.HighQuality : BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetBitmapScalingMode(prevLight, smooth ? BitmapScalingMode.HighQuality : BitmapScalingMode.NearestNeighbor);
        var cell = (double)Canvas / n;
        var g = new GeometryGroup();
        for (var i = 1; i < n; i++) { g.Children.Add(new LineGeometry(new Point(i * cell, 0), new Point(i * cell, Canvas))); g.Children.Add(new LineGeometry(new Point(0, i * cell), new Point(Canvas, i * cell))); }
        gridLines.Data = g;
        marker.Width = cell; marker.Height = cell;
        System.Windows.Controls.Canvas.SetLeft(marker, hotX * cell); System.Windows.Controls.Canvas.SetTop(marker, hotY * cell);
        info.Text = $"Картинка {n}×{n}. Точка клика — клетка {hotX + 1}, {hotY + 1} (голубая рамка).";
    }

    // ---------- привоз картинки ----------
    void Import()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Картинки и курсоры|*.png;*.ico;*.cur;*.ani;*.jpg;*.jpeg;*.bmp;*.gif|Все файлы|*.*", Title = "Выберите картинку или курсор" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var ext = System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant();
            byte[]? data = null; var hx = 0; var hy = 0;
            if (ext is ".cur" or ".ani")
            {
                var c = SysCursors.LoadFile(dlg.FileName, 64);
                if (c != null) { data = c.Bgra; hx = c.HotX; hy = c.HotY; }
            }
            if (data == null)
            {
                var dec = BitmapDecoder.Create(new Uri(dlg.FileName), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var best = dec.Frames.OrderByDescending(f => f.PixelWidth).First();
                var (bgra, scale, ox, oy) = Img.Fit(best, 64, true);
                data = bgra; hx = (int)Math.Round(2 * scale + ox); hy = (int)Math.Round(2 * scale + oy);
            }
            n = 64; px = data; hotX = Math.Clamp(hx, 0, 63); hotY = Math.Clamp(hy, 0, 63); smooth = true;
            nameBox.Text = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
            Redraw();
        }
        catch (Exception ex) { Loc.Box(this, "Не получилось открыть: " + ex.Message, "Cursor Plus"); }
    }

    void Save()
    {
        var any = false;
        for (var i = 3; i < px.Length; i += 4) if (px[i] > 0) { any = true; break; }
        if (!any) { Loc.Box(this, "Курсор пустой — нарисуйте что-нибудь.", "Cursor Plus"); return; }
        var name = nameBox.Text.Trim();
        if (name.Length == 0) name = "Мой курсор";
        if (name.Length > 40) name = name[..40];
        var e = new MineEntry { Id = existingId, Name = name, N = n, HotX = hotX, HotY = hotY, Smooth = smooth };
        try { MineStore.Save(e, px); Result = e; DialogResult = true; }
        catch (Exception ex) { Loc.Box(this, "Не сохранилось: " + ex.Message, "Cursor Plus"); }
    }

    // Для снимков окна
    public void RenderTo(string file)
    {
        var content = (ScrollViewer)Content;
        var page = (FrameworkElement)content.Content;
        content.Content = null;
        System.Windows.Documents.TextElement.SetForeground(page, Foreground);
        System.Windows.Documents.TextElement.SetFontFamily(page, FontFamily);
        System.Windows.Documents.TextElement.SetFontSize(page, FontSize);
        Loc.ApplyTree(page);   // служебный снимок: окно не показано, переводим вручную
        page.Measure(new Size(740, double.PositiveInfinity));
        var w = 740; var h = Math.Max(200, (int)Math.Ceiling(page.DesiredSize.Height));
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
        content.Content = page;
    }

    // Нарисовать пример в редакторе (для снимков и проверки): стрелка из пикселей
    public void Demo()
    {
        Array.Clear(px, 0, px.Length);
        var rows = new[] { "X", "XX", "XXX", "XXXX", "XXXXX", "XXXXXX", "XXXXXXX", "XXXXXXXX", "XXXXXXXXX", "XXXXXXXXXX", "XXXXXXXXXXX", "XXXXXXXXXXXX", "XXXXXXXX", "XXX XXXX", "XX  XXXX", "X    XXXX", "     XXXX", "      XXX" };
        for (var y = 0; y < rows.Length; y++)
            for (var x = 0; x < rows[y].Length; x++)
                if (rows[y][x] == 'X') { var i = ((y + 2) * n + x + 2) * 4; px[i] = 0x9D; px[i + 1] = 0x2A; px[i + 2] = 0xFF; px[i + 3] = 255; }
        hotX = 2; hotY = 2;
        Redraw();
    }
}
