using System.Runtime.InteropServices;
using System.Text;

namespace ColorPlusApp;

// Вызовы Windows, нужные плагину. Окна других программ трогаем осторожно: любая ошибка ловится выше —
// плагин не должен ронять чужие окна или себя.
static class Native
{
    public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    public const long WS_CAPTION = 0x00C00000, WS_EX_LAYERED = 0x00080000, WS_EX_TOOLWINDOW = 0x00000080, WS_EX_NOACTIVATE = 0x08000000,
                      WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    public const uint LWA_COLORKEY = 1, LWA_ALPHA = 2, GW_OWNER = 4;
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_BORDER_COLOR = 34, DWMWA_CAPTION_COLOR = 35, DWMWA_TEXT_COLOR = 36,
                     DWMWA_SYSTEMBACKDROP_TYPE = 38, DWMWA_CLOAKED = 14;
    public const uint ColorDefault = 0xFFFFFFFF;   // DWMWA_COLOR_DEFAULT

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; public readonly int Width => Right - Left; public readonly int Height => Bottom - Top; }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public uint cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS { public int Left, Right, Top, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER { public int Size, Width, Height; public short Planes, BitCount; public int Compression, SizeImage, XPels, YPels, ClrUsed, ClrImportant; }

    [StructLayout(LayoutKind.Sequential)]
    struct AccentPolicy { public int AccentState; public int AccentFlags; public uint GradientColor; public int AnimationId; }

    [StructLayout(LayoutKind.Sequential)]
    struct WindowCompositionAttribData { public int Attribute; public IntPtr Data; public int SizeOfData; }

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int index, IntPtr value);
    [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] public static extern bool RedrawWindow(IntPtr h, IntPtr rect, IntPtr rgn, uint flags);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);
    [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr h, ref WindowCompositionAttribData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetSysColors(int count, int[] elements, int[] rgb);
    [DllImport("user32.dll")] public static extern int GetSysColor(int index);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);

    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref uint value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS margins);

    public static string ClassName(IntPtr h)
    {
        var sb = new StringBuilder(256);
        return GetClassNameW(h, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    public static long GetLong(IntPtr h, int index) => GetWindowLongPtr(h, index).ToInt64();
    public static void SetLong(IntPtr h, int index, long value) => SetWindowLongPtr(h, index, new IntPtr(value));

    // Окно закрывает весь монитор (полноэкранная игра или видео) — такие окна не трогаем.
    public static bool CoversMonitor(IntPtr h, RECT r)
    {
        var mon = MonitorFromWindow(h, 2 /* MONITOR_DEFAULTTONEAREST */);
        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (mon == IntPtr.Zero || !GetMonitorInfo(mon, ref mi)) return false;
        return r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
    }

    // Цвет в формате «B G R» для COLORREF и «A B G R» для градиента.
    public static uint ColorRef(string hex)
    {
        var rgb = Convert.ToUInt32(hex.TrimStart('#'), 16);
        return ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);
    }

    public static uint Abgr(string hex, int opacityPercent)
    {
        var a = (uint)Math.Round(Math.Min(100, Math.Max(0, opacityPercent)) * 255 / 100.0);
        return (a << 24) | ColorRef(hex);
    }

    // Состояния подложки окна (недокументированный, но давно известный интерфейс Windows).
    public const int AccentDisabled = 0, AccentGradient = 1, AccentTransparentGradient = 2, AccentBlurBehind = 3, AccentAcrylic = 4;

    public static bool SetAccent(IntPtr h, int state, uint gradientAbgr = 0)
    {
        var policy = new AccentPolicy { AccentState = state, AccentFlags = state == AccentGradient || state == AccentTransparentGradient ? 2 : 0, GradientColor = gradientAbgr };
        var size = Marshal.SizeOf(policy);
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, ptr, false);
            var data = new WindowCompositionAttribData { Attribute = 19 /* WCA_ACCENT_POLICY */, Data = ptr, SizeOfData = size };
            return SetWindowCompositionAttribute(h, ref data) != 0;
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    public static void SetDwm(IntPtr h, int attr, int value) { var v = value; DwmSetWindowAttribute(h, attr, ref v, sizeof(int)); }
    public static void SetDwmColor(IntPtr h, int attr, uint colorRef) { var v = colorRef; DwmSetWindowAttribute(h, attr, ref v, sizeof(uint)); }

    public static void Margins(IntPtr h, int m)
    {
        var mg = new MARGINS { Left = m, Right = m, Top = m, Bottom = m };
        DwmExtendFrameIntoClientArea(h, ref mg);
    }
}
