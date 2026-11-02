using System.Runtime.InteropServices;

namespace CursorPlusApp;

static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] public struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }

    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO info);
    [DllImport("user32.dll")] public static extern bool DestroyCursor(IntPtr h);
    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr LoadImage(IntPtr hInst, IntPtr name, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadImageW", SetLastError = true)]
    public static extern IntPtr LoadImageFile(IntPtr hInst, string name, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SystemParametersInfo(uint action, uint p1, IntPtr p2, uint flags);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
    [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
    [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint ms);

    public const uint IMAGE_CURSOR = 2, LR_LOADFROMFILE = 0x10, LR_SHARED = 0x8000;
    public const uint SPI_SETCURSORS = 0x57;
    public const int SM_CXCURSOR = 13, SM_CYCURSOR = 14;

    // Сообщить Windows, что указатели в реестре поменялись: она перечитает их файлы.
    public static void ReloadCursors() => SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0x03);

    // Полноэкранная игра, видео, презентация — шлейф в это время не рисуем.
    public static bool FullscreenBusy()
    {
        try { return SHQueryUserNotificationState(out var s) == 0 && (s == 2 || s == 3 || s == 4); }
        catch { return false; }
    }
}
