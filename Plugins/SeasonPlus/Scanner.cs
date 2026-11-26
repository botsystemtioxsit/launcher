using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SeasonPlusApp;

static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    public delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);
    [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
    [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint ms);

    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, WS_EX_TRANSPARENT = 0x20, WS_EX_NOACTIVATE = 0x08000000;

    // Полноэкранная игра, видео, презентация — снег в это время не нужен.
    public static bool FullscreenBusy()
    {
        try { return SHQueryUserNotificationState(out var s) == 0 && (s == 2 || s == 3 || s == 4); }
        catch { return false; }
    }
}

// Какие окна сейчас есть на экране и какие куски их верхнего края видны.
static class WindowScanner
{
    static readonly HashSet<string> SkipClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Windows.UI.Core.CoreWindow", "ApplicationFrameInputSinkWindow", "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland",
    };

    static bool IsWin11() => Environment.OSVersion.Version.Build >= 22000;

    static Native.RECT FrameRect(IntPtr h)
    {
        if (Native.DwmGetWindowAttribute(h, 9, out Native.RECT r, Marshal.SizeOf<Native.RECT>()) == 0 && r.Right > r.Left) return r;
        Native.GetWindowRect(h, out r);
        return r;
    }

    // Куски отрезка [a, b], не попавшие в «закрытые» промежутки.
    public static List<(double A, double B)> Subtract(double a, double b, List<(double A, double B)> cut)
    {
        var res = new List<(double A, double B)> { (a, b) };
        foreach (var c in cut)
        {
            var next = new List<(double A, double B)>();
            foreach (var s in res)
            {
                if (c.B <= s.A || c.A >= s.B) { next.Add(s); continue; }
                if (c.A > s.A) next.Add((s.A, c.A));
                if (c.B < s.B) next.Add((c.B, s.B));
            }
            res = next;
            if (res.Count == 0) break;
        }
        return res;
    }

    // vx, vy, vw, vh — виртуальный рабочий стол (все мониторы). Окна снимаются сверху вниз.
    public static List<Surf> Scan(uint ownPid, int vx, int vy, int vw, int vh)
    {
        var result = new List<Surf>();
        var covers = new List<Native.RECT>();
        var radius = IsWin11() ? 8.0 : 0.0;
        var cb = new Native.EnumProc((h, _) =>
        {
            try
            {
                if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
                if (Native.DwmGetWindowAttribute(h, 14, out int cloaked, 4) == 0 && cloaked != 0) return true;
                Native.GetWindowThreadProcessId(h, out var pid);
                if (pid == ownPid) return true;
                var sb = new StringBuilder(64);
                Native.GetClassName(h, sb, 64);
                var cls = sb.ToString();
                var tray = cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd";
                if (!tray && SkipClasses.Contains(cls)) return true;
                var ex = (long)Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE);
                if (!tray)
                {
                    if ((ex & Native.WS_EX_TRANSPARENT) != 0 || (ex & Native.WS_EX_NOACTIVATE) != 0) return true;
                    if ((ex & Native.WS_EX_TOOLWINDOW) != 0 && (ex & Native.WS_EX_APPWINDOW) == 0) return true;
                }
                var r = FrameRect(h);
                int w = r.Right - r.Left, hgt = r.Bottom - r.Top;
                if (w < 10 || hgt < 10) return true;

                if (tray)
                {
                    // панель задач: снег ложится на верхний край, если она внизу и не спрятана
                    var mon = Native.MonitorFromWindow(h, 2);
                    var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
                    if (mon != IntPtr.Zero && Native.GetMonitorInfo(mon, ref mi))
                    {
                        var atBottom = w > hgt && r.Top > (mi.rcMonitor.Top + mi.rcMonitor.Bottom) / 2;
                        var hidden = r.Top >= mi.rcMonitor.Bottom - 3;
                        if (atBottom && !hidden)
                        {
                            var s = new Surf { Id = (long)h, Left = r.Left, Right = r.Right, Top = r.Top, Radius = 0, Taskbar = true };
                            s.Vis = Subtract(s.Left, s.Right, CoveredAt(covers, s.Top));
                            if (s.Vis.Count > 0) result.Add(s);
                        }
                    }
                    covers.Add(r);
                    return true;
                }

                var maximized = Native.IsZoomed(h);
                if (!maximized && w >= 140 && hgt >= 80 && r.Top > vy + 24)
                {
                    var s = new Surf { Id = (long)h, Left = r.Left, Right = r.Right, Top = r.Top, Radius = radius };
                    s.Vis = Subtract(s.Left, s.Right, CoveredAt(covers, s.Top));
                    if (s.Vis.Count > 0) result.Add(s);
                }
                covers.Add(r);
            }
            catch { }
            return true;
        });
        try { Native.EnumWindows(cb, IntPtr.Zero); } catch { }
        GC.KeepAlive(cb);
        return result;
    }

    // Промежутки по горизонтали, где окна выше перекрывают линию на высоте y.
    static List<(double A, double B)> CoveredAt(List<Native.RECT> covers, double y)
    {
        var res = new List<(double A, double B)>();
        foreach (var c in covers)
            if (c.Top <= y && y < c.Bottom) res.Add((c.Left, c.Right));
        return res;
    }
}
