using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CursorPlusApp;

// Тёмная площадка в окне: на ней по-настоящему работает шлейф — водите мышью. Пока мышь не трогаете, курсор сам катается по кругу.
sealed class TrailPreview : FrameworkElement
{
    public const int W = 640, H = 220;

    readonly TrailEngine engine = new();
    readonly WriteableBitmap wb = new(W, H, 96, 96, PixelFormats.Pbgra32, null);
    readonly byte[] buf = new byte[W * H * 4];
    readonly Stopwatch clock = Stopwatch.StartNew();
    BitmapSource? cursorSrc;
    int hotX, hotY;
    double mx, my, lastMove = -10, last, demoT, curX = W / 2, curY = H / 2;
    bool mouseIn, hooked;

    public TrailPreview()
    {
        Width = W; Height = H; ClipToBounds = true;
        Cursor = System.Windows.Input.Cursors.None;
        Loaded += (_, _) => Hook(true);
        Unloaded += (_, _) => Hook(false);
    }

    void Hook(bool on)
    {
        if (on == hooked) return;
        hooked = on;
        if (on) CompositionTarget.Rendering += OnFrame; else CompositionTarget.Rendering -= OnFrame;
    }

    public void Update(CursorSettings s)
    {
        var img = CursorApplier.Build(Roles.Get("Arrow"), s, new[] { 48 })?.FirstOrDefault();
        cursorSrc = img == null ? null : Img.ToSource(img);
        hotX = img?.HotX ?? 0; hotY = img?.HotY ?? 0;
        engine.SetSpec(TrailSpec.From(s, img));
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        mx = p.X; my = p.Y; mouseIn = true; lastMove = clock.Elapsed.TotalSeconds;
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e) { mouseIn = false; }

    protected override void OnMouseDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        engine.Click(p.X, p.Y);
    }

    void Advance(double dt, double now)
    {
        double x, y;
        if (mouseIn && now - lastMove < 1.5) { x = mx; y = my; }
        else
        {
            demoT += dt;
            x = W * 0.5 + W * 0.36 * Math.Sin(demoT * 1.4);
            y = H * 0.5 + H * 0.28 * Math.Sin(demoT * 2.3 + 0.8);
        }
        curX = x; curY = y;
        engine.Move(x, y);
        engine.Step(dt);
    }

    void Paint()
    {
        engine.Render(buf, W, H, 0, 0);
        wb.WritePixels(new Int32Rect(0, 0, W, H), buf, W * 4, 0);
        InvalidateVisual();
    }

    void OnFrame(object? sender, EventArgs e)
    {
        var now = clock.Elapsed.TotalSeconds;
        var dt = Math.Min(0.05, now - last); last = now;
        Advance(dt, now);
        Paint();
    }

    // Для снимков окна: прокрутить шлейф на seconds секунд вперёд.
    public void Simulate(double seconds)
    {
        for (var t = 0.0; t < seconds; t += 1.0 / 60) Advance(1.0 / 60, 1000 + t);
        Paint();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(10, 8, 20)), null, new Rect(0, 0, W, H));
        var grid = new Pen(new SolidColorBrush(Color.FromArgb(18, 140, 130, 255)), 1);
        for (var x = 0; x <= W; x += 40) dc.DrawLine(grid, new Point(x, 0), new Point(x, H));
        for (var y = 0; y <= H; y += 40) dc.DrawLine(grid, new Point(0, y), new Point(W, y));
        dc.DrawImage(wb, new Rect(0, 0, W, H));
        if (cursorSrc != null) dc.DrawImage(cursorSrc, new Rect(curX - hotX, curY - hotY, cursorSrc.PixelWidth, cursorSrc.PixelHeight));
    }
}
