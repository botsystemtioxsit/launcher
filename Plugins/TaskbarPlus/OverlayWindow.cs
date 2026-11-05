using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace TaskbarPlusApp;

// Окно с плашками: тонкое окно без рамки, которое лежит прямо ПОД панелью задач (в порядке окон — сразу за ней), пропускает все клики
// и не берёт фокус. Родной фон панели прозрачный (его делает движок), поэтому плашки видны сквозь неё, а значки и часы рисуются поверх них.
// Рисует WPF на окне с попиксельной прозрачностью: края скруглений гладкие.
sealed class OverlayWindow : IDisposable
{
    public const string Title = "GhostShellTaskbarPlusOverlay";

    // Холст: рисует набор плашек одной заливкой (пересечения не темнеют дважды).
    sealed class Surface : FrameworkElement
    {
        List<RoundRect> rects = new();
        Brush? fill;

        public Surface() { IsHitTestVisible = false; SnapsToDevicePixels = false; }

        public void Set(List<RoundRect> r, Brush? brush)
        {
            rects = r;
            fill = brush;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (fill == null || rects.Count == 0) return;
            var k = 1.0 / VisualTreeHelper.GetDpi(this).DpiScaleX;   // входные числа — физические пиксели, WPF рисует в «независимых» единицах
            var group = new GeometryGroup { FillRule = FillRule.Nonzero };
            foreach (var q in rects)
                group.Children.Add(new RectangleGeometry(new Rect(q.X * k, q.Y * k, q.W * k, q.H * k), q.R * k, q.R * k));
            group.Freeze();
            dc.DrawGeometry(fill, null, group);
        }
    }

    readonly HwndSource source;
    readonly Surface surface = new();
    List<RoundRect> lastRects = new();
    Color? lastColor;
    double lastAlpha;
    Native.RECT lastRect;
    bool shown;

    public IntPtr Hwnd => source.Handle;

    public OverlayWindow(Native.RECT rc)
    {
        const int WS_POPUP = unchecked((int)0x80000000);
        const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
        var p = new HwndSourceParameters(Title)
        {
            WindowStyle = WS_POPUP,
            ExtendedWindowStyle = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST,
            PositionX = rc.Left, PositionY = rc.Top, Width = Math.Max(1, rc.Width), Height = Math.Max(1, rc.Height),
            UsesPerPixelOpacity = true,
        };
        source = new HwndSource(p);
        source.CompositionTarget.BackgroundColor = Colors.Transparent;
        source.RootVisual = surface;
        // окно создано скрытым: показываем, когда есть что рисовать и известно место в порядке окон
        Native.ShowWindow(source.Handle, Native.SW_HIDE);
    }

    // Что рисовать. Перерисовка — только если что-то поменялось.
    public void SetShapes(List<RoundRect> rects, Color? color, double alpha)
    {
        if (color == lastColor && Math.Abs(alpha - lastAlpha) < 0.001 && rects.SequenceEqual(lastRects)) return;
        lastRects = rects; lastColor = color; lastAlpha = alpha;
        Brush? brush = null;
        if (color is { } c && rects.Count > 0)
        {
            var b = new SolidColorBrush(c) { Opacity = alpha };
            b.Freeze();
            brush = b;
        }
        surface.Set(rects, brush);
    }

    // Ставит окно на место панели — СРАЗУ ПОД ней в порядке окон (она выше, мы сразу за ней).
    public void Place(IntPtr taskbar, Native.RECT rc)
    {
        var below = Native.GetWindow(taskbar, Native.GW_HWNDNEXT) == Hwnd;
        var moved = rc.Left != lastRect.Left || rc.Top != lastRect.Top || rc.Width != lastRect.Width || rc.Height != lastRect.Height;
        if (shown && below && !moved) return;
        Native.SetWindowPos(Hwnd, taskbar, rc.Left, rc.Top, Math.Max(1, rc.Width), Math.Max(1, rc.Height), Native.SWP_NOACTIVATE | (shown ? 0 : Native.SWP_SHOWWINDOW));
        lastRect = rc;
        shown = true;
    }

    public void Hide()
    {
        if (!shown) return;
        Native.ShowWindow(Hwnd, Native.SW_HIDE);
        shown = false;
    }

    public bool IsShown => shown;

    public void Dispose()
    {
        try { source.Dispose(); } catch { }
    }
}
