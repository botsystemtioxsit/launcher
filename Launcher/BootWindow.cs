using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace BattleLauncher;

// Экран «Окружение»: плашка «Нажмите подтвердить» → поток кусочков кода → безымянные плашки по программам → закрытие.
// Только код, без XAML. Названия плагинов не показываем (владелец: «непонятно, что это»).
class BootWindow : Window
{
    static readonly string[] Bits =
    {
        "0x7F3A91", "void Apply()", "{ \"shape\": \"islands\" }", "taskbar.cfg", "SELECT * FROM sync", "01101001 01110100",
        "cursor.cur", "GET /health 200", "sha256:9f2c1ab4", "0xDEADBEEF", "engine.Ready = true", "=> LoadModule(id)",
        "░▒▓█▓▒░", "wallpaper.json", "if (ok) return;", "PUSH rbp", "xaml::Rectangle", "#FF2E97", "mov eax, [esi]",
        "{ \"ver\": 1 }", "await Task.Run(", "HKCU\\Software\\…", "tick=250ms", "fps=60", "0b1011_0010", "//ghost-shell",
        "init()", "stream.flush()", "ptr→node", "@@ -12,7 +12,9 @@", "config.sync()", "OK", "LINK 0x4F", "bind(socket)",
    };

    class P { public TextBlock T = null!; public double Ang, R, V; public RotateTransform Rot = null!; public ScaleTransform Sc = null!; public TranslateTransform Tr = null!; }

    readonly List<(AppDef Def, NativeInfo Info)> items;
    readonly Canvas stream = new();
    readonly List<P> parts = new();
    readonly Random rnd = new();
    readonly StackPanel card = new();
    readonly TaskCompletionSource<bool> confirmed = new();
    double warp = 0.5;
    DateTime last = DateTime.UtcNow;
    readonly bool demo;

    static Brush Res(string k) => (Brush)Application.Current.FindResource(k);

    public BootWindow(List<(AppDef, NativeInfo)> list, bool demo = false)
    {
        items = list; this.demo = demo;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized;
        Topmost = !demo; ShowInTaskbar = false; Background = Res("BgSolid"); Title = "Ghost Shell · Окружение";
        var grid = new Grid();
        grid.Children.Add(stream);
        card.HorizontalAlignment = HorizontalAlignment.Center; card.VerticalAlignment = VerticalAlignment.Center; card.Width = 520;
        grid.Children.Add(card);
        Content = grid;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) confirmed.TrySetResult(false); };
        Loaded += async (_, _) => { MakeParts(); CompositionTarget.Rendering += Frame; await Run(); };
        Closed += (_, _) => CompositionTarget.Rendering -= Frame;
    }

    void MakeParts()
    {
        var cols = new[] { "Accent", "Accent2", "Purple", "Green", "Yellow", "Muted" };
        for (int i = 0; i < 70; i++)
        {
            var p = new P
            {
                T = new TextBlock { Text = Bits[rnd.Next(Bits.Length)], FontFamily = (FontFamily)Application.Current.FindResource("Mono"), FontSize = 14, Foreground = Res(cols[rnd.Next(cols.Length)]), IsHitTestVisible = false, Opacity = 0 },
                Rot = new RotateTransform(), Sc = new ScaleTransform(), Tr = new TranslateTransform(),
            };
            var g = new TransformGroup(); g.Children.Add(p.Sc); g.Children.Add(p.Rot); g.Children.Add(p.Tr);
            p.T.RenderTransform = g;
            Respawn(p, true);
            stream.Children.Add(p.T); parts.Add(p);
        }
    }

    void Respawn(P p, bool scatter)
    {
        p.Ang = rnd.NextDouble() * Math.PI * 2;
        p.R = scatter ? rnd.NextDouble() * 700 : 20 + rnd.NextDouble() * 60;
        p.V = 60 + rnd.NextDouble() * 140;
        p.T.Text = Bits[rnd.Next(Bits.Length)];
    }

    void Frame(object? s, EventArgs e)
    {
        var now = DateTime.UtcNow; var dt = Math.Min(0.1, (now - last).TotalSeconds); last = now;
        double cx = stream.ActualWidth / 2, cy = stream.ActualHeight / 2, maxR = Math.Sqrt(cx * cx + cy * cy) + 80;
        foreach (var p in parts)
        {
            p.R += p.V * warp * dt * (1 + p.R / 220);
            if (p.R > maxR) Respawn(p, false);
            double sc = 0.35 + p.R / 420;
            p.Sc.ScaleX = p.Sc.ScaleY = sc;
            var deg = p.Ang * 180 / Math.PI; if (Math.Cos(p.Ang) < 0) deg -= 180;   // чтобы текст не был вверх ногами
            p.Rot.Angle = deg;
            p.Tr.X = cx + Math.Cos(p.Ang) * p.R - (Math.Cos(p.Ang) < 0 ? Math.Cos(p.Ang - Math.PI) * 0 : 0); p.Tr.Y = cy + Math.Sin(p.Ang) * p.R;
            p.T.Opacity = Math.Min(1, p.R / 160) * Math.Min(1, (maxR - p.R) / 200) * 0.85;
        }
    }

    TextBlock Mono(string text, double size, string brush, FontWeight? w = null) => new()
    {
        Text = text, FontFamily = (FontFamily)Application.Current.FindResource("Mono"), FontSize = size, Foreground = Res(brush),
        FontWeight = w ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 8),
    };

    Border Plate(UIElement child, string stroke = "Accent") => new()
    {
        Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x0D, 0x08, 0x21)), BorderBrush = Res(stroke), BorderThickness = new Thickness(1),
        Padding = new Thickness(22), Child = child,
        Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0, 0xF0, 0xFF), BlurRadius = 18, ShadowDepth = 0, Opacity = 0.35 },
    };

    async Task Run()
    {
        // 1. Плашка подтверждения
        var box = new StackPanel();
        box.Children.Add(Mono("GHOST SHELL · ОКРУЖЕНИЕ", 12, "Accent2"));
        box.Children.Add(Mono("Нажмите «Подтвердить», чтобы включить окружение", 20, "Text", FontWeights.Bold));
        box.Children.Add(Mono($"Будет запущено модулей: {items.Count}", 12, "Muted"));
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        var ok = new Button { Content = "Подтвердить", Style = (Style)Application.Current.FindResource("Primary"), MinWidth = 150 };
        var no = new Button { Content = "Не сейчас", Style = (Style)Application.Current.FindResource("Ghost"), Margin = new Thickness(10, 0, 0, 0) };
        ok.Click += (_, _) => confirmed.TrySetResult(true);
        no.Click += (_, _) => confirmed.TrySetResult(false);
        row.Children.Add(ok); row.Children.Add(no); box.Children.Add(row);
        card.Children.Add(Plate(box));
        if (!demo) ok.Focus();

        if (!demo && !await confirmed.Task) { Close(); return; }
        if (demo) { await Task.Delay(App.ScreenshotDir != null ? 300 : 1500); }

        // 2. Поток данных разгоняется, плашки по модулям
        card.Children.Clear();
        var list = new StackPanel();
        var head = Mono("ЗАГРУЗКА ОКРУЖЕНИЯ", 14, "Accent", FontWeights.Bold);
        list.Children.Add(head);
        card.Children.Add(Plate(list));
        for (int w = 0; w < 12; w++) { warp += 0.5; await Task.Delay(40); }

        int n = 0;
        foreach (var (def, info) in items)
        {
            n++;
            var line = new Border { BorderBrush = Res("Line"), BorderThickness = new Thickness(1), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 4, 0, 0) };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBlock { Text = $"▸ МОДУЛЬ {n:00}", FontFamily = (FontFamily)Application.Current.FindResource("Mono"), Foreground = Res("Text") };
            var state = new TextBlock { Text = "данные подгружаются", FontFamily = (FontFamily)Application.Current.FindResource("Mono"), Foreground = Res("Accent2"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(state, 1);
            g.Children.Add(name); g.Children.Add(state); line.Child = g; line.Opacity = 0;
            list.Children.Add(line);
            for (int a = 1; a <= 5; a++) { line.Opacity = a / 5.0; await Task.Delay(30); }

            bool good = true;
            if (!demo)
            {
                var t = Task.Run(() => { try { Boot.Start(info); return true; } catch { return false; } });
                int tick = 0;
                while (!t.IsCompleted || tick < 16) { state.Text = "данные подгружаются" + new string('.', tick++ % 4); await Task.Delay(90); }
                good = t.Result;
            }
            else await Task.Delay(500);
            state.Text = good ? "✓ готово" : "✗ не вышло"; state.Foreground = Res(good ? "Green" : "Red");
            await Task.Delay(250);
            line.Opacity = 0.45;
        }

        var done = Mono(items.Count == 0 ? "Выбранных модулей нет" : "ОКРУЖЕНИЕ ВКЛЮЧЕНО", 14, items.Count == 0 ? "Muted" : "Green", FontWeights.Bold);
        done.Margin = new Thickness(0, 14, 0, 0);
        list.Children.Add(done);
        if (demo) { if (App.ScreenshotDir == null) { await Task.Delay(2500); Close(); } return; }
        await Task.Delay(1400);
        for (int f = 10; f >= 0; f--) { ((UIElement)Content).Opacity = f / 10.0; await Task.Delay(35); }
        Close();
    }
}
