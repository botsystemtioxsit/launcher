using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ColorPlusApp;

sealed record GlassConfig(string Tint, int Blur, bool KeepStrip);

// «Стекло» для окон, у которых нельзя сделать прозрачным только фон (Chrome, Edge, Electron).
// Устройство: само окно становится чуть прозрачнее (это делает WindowEffects), прямо ПОД ним лежит наше окно с размытым матовым стеклом
// (акрил), а шапка окна (вкладки, адресная строка, заголовок) накрыта живой копией себя самой (миниатюра DWM) — поэтому она остаётся плотной.
// Оба наших окна не берут нажатия мыши и фокус: стекло закрыто самим окном, копия шапки «прозрачна» для мыши. Все окна живут в этом процессе —
// закрылся плагин, пропали и они (а прозрачность самого окна возвращает RecoverLeftovers при следующем запуске).
// Честно: пока Windows даёт делать прозрачным только окно целиком, текст и видео внутри страницы тоже слегка просвечивают — плотной остаётся только шапка.
sealed class GlassManager : IDisposable
{
    sealed class Item
    {
        public IntPtr Target, GlassH, StripH, Thumb;
        public Window Glass = null!;
        public Window? Strip;
        public bool Shown, StripShown;
        public Native.RECT Vis, StripRect;
        public int ThumbKey;
    }

    readonly Dispatcher ui;
    readonly DispatcherTimer timer;
    readonly Dictionary<IntPtr, Item> items = new();
    GlassConfig cfg = new("#101018", 45, true);

    public GlassManager(Dispatcher ui)
    {
        this.ui = ui;
        timer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (_, _) => { try { Tick(); } catch { /* окно закрылось на ходу — на следующем шаге разберёмся */ } };
    }

    // Вызывается из любого потока: список окон, которым нужно стекло (пустой — убрать всё).
    public void SetTargets(IReadOnlyCollection<IntPtr> targets, GlassConfig config)
    {
        var copy = targets.ToArray();
        ui.BeginInvoke(new Action(() => Apply(copy, config)));
    }

    void Apply(IntPtr[] targets, GlassConfig config)
    {
        var wanted = new HashSet<IntPtr>(targets);
        foreach (var h in items.Keys.Where(h => !wanted.Contains(h)).ToList()) Remove(h);
        var changed = config != cfg;
        cfg = config;
        foreach (var h in wanted)
        {
            if (!items.TryGetValue(h, out var it)) { try { items[h] = it = Create(h); } catch { continue; } }
            else if (changed) Restyle(it);
            if (it.Strip == null && cfg.KeepStrip) AddStrip(it);
            else if (it.Strip != null && !cfg.KeepStrip) RemoveStrip(it);
        }
        if (items.Count > 0) { if (!timer.IsEnabled) timer.Start(); }
        else timer.Stop();
    }

    static Window MakeTool(string title) => new()
    {
        Title = title, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
        Focusable = false, Left = -32000, Top = -32000, Width = 100, Height = 60, WindowStartupLocation = WindowStartupLocation.Manual,
        Background = Brushes.Transparent,
    };

    static void ToolStyle(IntPtr h, long extra) =>
        Native.SetLong(h, Native.GWL_EXSTYLE, Native.GetLong(h, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | extra);

    Item Create(IntPtr target)
    {
        var it = new Item { Target = target, Glass = MakeTool("Color Plus — стекло") };
        it.GlassH = new WindowInteropHelper(it.Glass).EnsureHandle();
        HwndSource.FromHwnd(it.GlassH).CompositionTarget.BackgroundColor = Colors.Transparent;   // прозрачная поверхность — под ней виден акрил
        ToolStyle(it.GlassH, 0);
        Native.Margins(it.GlassH, -1);
        Native.SetDwm(it.GlassH, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, 2 /* скруглённые */);
        Restyle(it);
        it.Glass.Show();
        Native.ShowWindow(it.GlassH, 0);
        return it;
    }

    void Restyle(Item it)
    {
        Native.SetAccent(it.GlassH, Native.AccentAcrylic, Native.Abgr(cfg.Tint, cfg.Blur));
    }

    void AddStrip(Item it)
    {
        it.Strip = MakeTool("Color Plus — шапка");
        it.Strip.Background = Brushes.Black;
        it.StripH = new WindowInteropHelper(it.Strip).EnsureHandle();
        ToolStyle(it.StripH, Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED);                  // мышь проходит сквозь копию к настоящему окну
        Native.SetLayeredWindowAttributes(it.StripH, 0, 255, Native.LWA_ALPHA);
        it.Strip.Show();
        Native.ShowWindow(it.StripH, 0);
        if (Native.DwmRegisterThumbnail(it.StripH, it.Target, out var th) == 0) it.Thumb = th;
        it.StripShown = false; it.ThumbKey = 0;
    }

    static void RemoveStrip(Item it)
    {
        if (it.Thumb != IntPtr.Zero) { Native.DwmUnregisterThumbnail(it.Thumb); it.Thumb = IntPtr.Zero; }
        try { it.Strip?.Close(); } catch { }
        it.Strip = null; it.StripH = IntPtr.Zero; it.StripShown = false;
    }

    void Remove(IntPtr target)
    {
        if (!items.TryGetValue(target, out var it)) return;
        items.Remove(target);
        RemoveStrip(it);
        try { it.Glass.Close(); } catch { }
    }

    // Высота шапки: у Chrome — от верха окна до начала страницы (дочернее окно страницы), у остальных — до начала рабочей области (заголовок и меню).
    static int StripHeight(IntPtr target, Native.RECT vis)
    {
        var top = 0;
        Native.EnumChildWindows(target, (c, _) =>
        {
            if (Native.ClassName(c) != "Chrome_RenderWidgetHostHWND") return true;
            if (Native.GetWindowRect(c, out var r) && r.Height > 100 && r.Top - vis.Top > top) top = r.Top - vis.Top;
            return true;
        }, IntPtr.Zero);
        if (top <= 0)
        {
            var p = new Native.POINT();
            if (Native.ClientToScreen(target, ref p)) top = p.Y - vis.Top;
        }
        return Math.Clamp(top, 0, 400);
    }

    void Tick()
    {
        foreach (var it in items.Values.ToList())
        {
            var t = it.Target;
            var alive = Native.IsWindow(t);
            var show = alive && Native.IsWindowVisible(t) && !Native.IsIconic(t)
                       && !(Native.DwmGetWindowAttribute(t, Native.DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0);
            if (!show)
            {
                if (it.Shown) { Native.ShowWindow(it.GlassH, 0); it.Shown = false; }
                if (it.StripShown) { Native.ShowWindow(it.StripH, 0); it.StripShown = false; }
                continue;
            }
            if (!Native.Bounds(t, out var vis, out var win)) continue;
            var w = vis.Right - vis.Left;
            var h = vis.Bottom - vis.Top;
            if (w < 50 || h < 50) continue;

            // стекло — прямо под окном, того же размера
            if (!it.Shown) { Native.SetWindowPos(it.GlassH, IntPtr.Zero, vis.Left, vis.Top, w, h, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE); Native.ShowWindow(it.GlassH, 4 /* SW_SHOWNOACTIVATE */); it.Shown = true; it.Vis = vis; }
            else if (!SameRect(vis, it.Vis)) { Native.SetWindowPos(it.GlassH, IntPtr.Zero, vis.Left, vis.Top, w, h, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE); it.Vis = vis; }
            if (Native.GetWindow(t, Native.GW_HWNDNEXT) != it.GlassH)
                Native.SetWindowPos(it.GlassH, t, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

            // шапка — живая копия верхней части окна поверх него
            if (it.Strip == null || it.Thumb == IntPtr.Zero) continue;
            var sh = StripHeight(t, vis);
            if (sh <= 0) { if (it.StripShown) { Native.ShowWindow(it.StripH, 0); it.StripShown = false; } continue; }
            var rect = new Native.RECT { Left = vis.Left, Top = vis.Top, Right = vis.Right, Bottom = vis.Top + sh };
            if (!it.StripShown || !SameRect(rect, it.StripRect))
            {
                Native.SetWindowPos(it.StripH, IntPtr.Zero, rect.Left, rect.Top, w, sh, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                var props = new Native.THUMBNAIL
                {
                    Flags = 1 | 2 | 4 | 8, Opacity = 255, Visible = true, ClientOnly = false,
                    Dest = new Native.RECT { Left = 0, Top = 0, Right = w, Bottom = sh },
                    Src = new Native.RECT { Left = vis.Left - win.Left, Top = vis.Top - win.Top, Right = vis.Left - win.Left + w, Bottom = vis.Top - win.Top + sh },
                };
                Native.DwmUpdateThumbnailProperties(it.Thumb, ref props);
                if (!it.StripShown) { Native.ShowWindow(it.StripH, 4); it.StripShown = true; }
                it.StripRect = rect;
            }
            var above = Native.GetWindow(t, Native.GW_HWNDPREV);
            if (above != it.StripH)
                Native.SetWindowPos(it.StripH, above, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }
    }

    static bool SameRect(Native.RECT a, Native.RECT b) => a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;

    public void Dispose()
    {
        void Cleanup()
        {
            timer.Stop();
            foreach (var h in items.Keys.ToList()) Remove(h);
        }
        if (ui.CheckAccess()) Cleanup();
        else { try { ui.Invoke(Cleanup); } catch { } }
    }
}
