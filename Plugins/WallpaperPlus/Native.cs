using System.Runtime.InteropServices;
using System.Text;

namespace WallpaperPlusApp;

// Вызовы Windows: рабочий стол (Progman / WorkerW), мониторы, окна переднего плана.
static class Native
{
    public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
    public delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, ref RECT rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice; }

    public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    public const long WS_POPUP = 0x80000000L, WS_CHILD = 0x40000000L, WS_VISIBLE = 0x10000000L, WS_CAPTION = 0x00C00000L, WS_THICKFRAME = 0x00040000L,
        WS_SYSMENU = 0x00080000L, WS_MINIMIZEBOX = 0x00020000L, WS_MAXIMIZEBOX = 0x00010000L, WS_CLIPSIBLINGS = 0x04000000L, WS_CLIPCHILDREN = 0x02000000L;
    public const long WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TRANSPARENT = 0x20;
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20, SWP_SHOWWINDOW = 0x40, SWP_HIDEWINDOW = 0x80;
    public static readonly IntPtr HWND_TOP = IntPtr.Zero, HWND_BOTTOM = new(1);
    public const int SW_HIDE = 0, SW_SHOWNA = 8;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int cx, int cy, bool repaint);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetLongPtr(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, UIntPtr w, IntPtr l, uint flags, uint timeout, out UIntPtr result);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] public static extern bool RedrawWindow(IntPtr h, IntPtr rect, IntPtr rgn, uint flags);
    [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);

    public static long GetLong(IntPtr h, int i) => GetLongPtr(h, i).ToInt64();
    public static void SetLong(IntPtr h, int i, long v) => SetLongPtr(h, i, new IntPtr(v));

    public static string ClassName(IntPtr h)
    {
        var sb = new StringBuilder(256);
        return GetClassName(h, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    public sealed record Monitor(IntPtr Handle, RECT Rect, bool Primary, string Name);

    public static List<Monitor> Monitors()
    {
        var list = new List<Monitor>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr dc, ref RECT r, IntPtr data) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(m, ref info)) list.Add(new Monitor(m, info.rcMonitor, (info.dwFlags & 1) != 0, info.szDevice));
            return true;
        }, IntPtr.Zero);
        return list.OrderByDescending(x => x.Primary).ThenBy(x => x.Rect.Left).ToList();
    }

    // true — на экране сейчас полноэкранная игра, видео или презентация (Windows сама это знает)
    public static bool FullscreenAppRunning()
    {
        try
        {
            // 2 — занят, 3 — полноэкранное приложение Direct3D, 4 — режим презентации; 1 — экран заблокирован
            return SHQueryUserNotificationState(out var s) == 0 && s is 2 or 3 or 4;
        }
        catch { return false; }
    }

    // Окно переднего плана закрывает весь монитор (развёрнутое или полноэкранное) — обои под ним не видны.
    public static bool DesktopCovered(RECT monitor)
    {
        try
        {
            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || !IsWindowVisible(fg) || IsIconic(fg)) return false;
            var cls = ClassName(fg);
            if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "SHELLDLL_DefView" or "XamlExplorerHostIslandWindow" or "Windows.UI.Core.CoreWindow") return false;
            if (!GetWindowRect(fg, out var r)) return false;
            // окно (в том числе развёрнутое) закрывает монитор почти целиком
            var cx = Math.Max(0, Math.Min(r.Right, monitor.Right) - Math.Max(r.Left, monitor.Left));
            var cy = Math.Max(0, Math.Min(r.Bottom, monitor.Bottom) - Math.Max(r.Top, monitor.Top));
            var covered = (double)cx * cy / Math.Max(1, (double)monitor.Width * monitor.Height);
            return covered >= 0.92;
        }
        catch { return false; }
    }
}
