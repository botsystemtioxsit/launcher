using System.Runtime.InteropServices;

namespace TaskbarPlusApp;

// Вызовы Windows, нужные плагину. Всё, что относится к чужим окнам (панель задач — окно Проводника),
// вызывается осторожно: любая ошибка ловится выше, плагин не должен ронять Проводник или себя.
static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA { public uint cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam; }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public uint cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [StructLayout(LayoutKind.Sequential)]
    struct ACCENT_POLICY { public int AccentState; public int AccentFlags; public uint GradientColor; public int AnimationId; }

    [StructLayout(LayoutKind.Sequential)]
    struct WINCOMPATTRDATA { public int Attribute; public IntPtr Data; public int SizeOfData; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern int SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);
    [DllImport("user32.dll")] public static extern int GetWindowRgnBox(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
    [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr h, ref WINCOMPATTRDATA data);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] static extern bool SpiGetInt(uint action, uint param, out int value, uint winIni);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] static extern bool SpiSetPtr(uint action, uint param, IntPtr value, uint winIni);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);

    [DllImport("gdi32.dll")] public static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);
    [DllImport("gdi32.dll")] public static extern int CombineRgn(IntPtr dest, IntPtr src1, IntPtr src2, int mode);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint message, ref APPBARDATA data);

    public const int RGN_OR = 2;

    public static double Scale(IntPtr hwnd)
    {
        try { var dpi = GetDpiForWindow(hwnd); return dpi == 0 ? 1.0 : dpi / 96.0; }
        catch { return 1.0; }
    }

    // Панель лежит у нижнего края своего монитора (формы поддерживаются только для такой панели).
    public static bool IsAtBottom(IntPtr hwnd, RECT rc)
    {
        var mon = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (mon == IntPtr.Zero || !GetMonitorInfo(mon, ref mi)) return false;
        return rc.Width > rc.Height && rc.Bottom >= mi.rcMonitor.Bottom - 3 && rc.Top > mi.rcMonitor.Top + 50;
    }

    // Размытие/прозрачность панели: недокументированный, но давно известный вызов Windows (тот же, что у TranslucentTB).
    // state: 0 выкл, 2 прозрачный градиент, 3 размытие, 4 акрил. abgr — цвет подложки вместе с прозрачностью.
    public static void SetAccent(IntPtr hwnd, int state, uint abgr)
    {
        var policy = new ACCENT_POLICY { AccentState = state, AccentFlags = 2, GradientColor = abgr };
        var size = Marshal.SizeOf<ACCENT_POLICY>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, ptr, false);
            var data = new WINCOMPATTRDATA { Attribute = 19 /* WCA_ACCENT_POLICY */, Data = ptr, SizeOfData = size };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    // Говорим Проводнику, что настройки панели изменились (часть параметров он подхватывает сразу).
    // Рассылка идёт в фоне: окна, которые долго отвечают, не должны вешать окно плагина.
    public static void NotifySettingsChanged()
    {
        Task.Run(() =>
        {
            try
            {
                SendMessageTimeout((IntPtr)0xFFFF, 0x1A /* WM_SETTINGCHANGE */, UIntPtr.Zero, "TraySettings", 2, 1500, out _);
                SendMessageTimeout((IntPtr)0xFFFF, 0x1A, UIntPtr.Zero, "ImmersiveColorSet", 2, 1500, out _);
            }
            catch { /* уведомление не обязательно */ }
        });
    }

    // ---------- автоскрытие ----------
    const uint ABM_GETSTATE = 4, ABM_SETSTATE = 10, ABS_AUTOHIDE = 1, ABS_ALWAYSONTOP = 2;

    static APPBARDATA NewAppBar() => new() { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), hWnd = FindWindow("Shell_TrayWnd", null) };

    public static bool GetAutoHide()
    {
        try { var d = NewAppBar(); return ((uint)SHAppBarMessage(ABM_GETSTATE, ref d) & ABS_AUTOHIDE) != 0; }
        catch { return false; }
    }

    public static void SetAutoHide(bool on)
    {
        var d = NewAppBar();
        d.lParam = (IntPtr)(on ? ABS_AUTOHIDE | ABS_ALWAYSONTOP : ABS_ALWAYSONTOP);
        SHAppBarMessage(ABM_SETSTATE, ref d);
    }

    // ---------- анимации Windows ----------
    // «Эффекты анимации» (Параметры → Специальные возможности → Визуальные эффекты). Если они выключены,
    // панель при автоскрытии появляется и пропадает мгновенно.
    public static bool SystemAnimationsOn
    {
        get { try { return SpiGetInt(0x1042 /* SPI_GETCLIENTAREAANIMATION */, 0, out var v, 0) && v != 0; } catch { return true; } }
    }

    public static bool EnableSystemAnimations()
    {
        try { return SpiSetPtr(0x1043 /* SPI_SETCLIENTAREAANIMATION */, 0, (IntPtr)1, 0x01 | 0x02); }
        catch { return false; }
    }
}
