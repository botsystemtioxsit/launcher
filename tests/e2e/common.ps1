# Общее для проверок на Windows-машине GitHub: снимок экрана, список окон, чтение свойств окон.
$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class W
{
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetLong(IntPtr h, int i);
    [DllImport("user32.dll")] public static extern bool GetLayeredWindowAttributes(IntPtr h, out uint key, out byte alpha, out uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int s);

    public static List<string> List()
    {
        var res = new List<string>();
        EnumWindows((h, l) =>
        {
            var c = new StringBuilder(256); GetClassName(h, c, 256);
            var t = new StringBuilder(256); GetWindowText(h, t, 256);
            uint pid; GetWindowThreadProcessId(h, out pid);
            RECT r; GetWindowRect(h, out r);
            res.Add(h.ToInt64() + "|" + c + "|" + t + "|pid=" + pid + "|vis=" + (IsWindowVisible(h) ? 1 : 0) + "|" + r.L + "," + r.T + "," + r.R + "," + r.B);
            return true;
        }, IntPtr.Zero);
        return res;
    }

    // Состояние окна, которое меняет Color Plus: слоёность и прозрачность, подложка, цвет заголовка, тёмный режим.
    public static string Describe(IntPtr h)
    {
        long ex = GetLong(h, -20).ToInt64();
        string s = "ex=0x" + ex.ToString("X") + " layered=" + (((ex & 0x80000) != 0) ? 1 : 0);
        uint key, flags; byte alpha;
        if ((ex & 0x80000) != 0 && GetLayeredWindowAttributes(h, out key, out alpha, out flags)) s += " alpha=" + alpha + " flags=" + flags + " key=0x" + key.ToString("X6");
        int v;
        if (DwmGetWindowAttribute(h, 38, out v, 4) == 0) s += " backdrop=" + v;
        if (DwmGetWindowAttribute(h, 35, out v, 4) == 0) s += " caption=0x" + ((uint)v).ToString("X8");
        if (DwmGetWindowAttribute(h, 20, out v, 4) == 0) s += " dark=" + v;
        return s;
    }
}
'@

function Shot([string]$path) {
    try {
        $b = [System.Windows.Forms.SystemInformation]::VirtualScreen
        $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
        $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $bmp.Dispose()
        "снимок: $path ($($b.Width)x$($b.Height))"
    } catch { "снимок не удался: $($_.Exception.Message)" }
}

function Wait-File([string]$path, [scriptblock]$ok, [int]$seconds) {
    $end = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $end) {
        if (Test-Path $path) { $t = Get-Content $path -Raw -ErrorAction SilentlyContinue; if ($t -and (& $ok $t)) { return $t } }
        Start-Sleep -Seconds 2
    }
    if (Test-Path $path) { return (Get-Content $path -Raw -ErrorAction SilentlyContinue) }
    return $null
}
