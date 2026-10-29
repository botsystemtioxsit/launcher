# Прототип «стекла»: под целевым окном — матовое стекло (акрил), само окно слегка прозрачное, а шапка окна накрыта чёткой копией (DWM-миниатюра).
param([long]$Target, [int]$Alpha = 85, [int]$Strip = 100, [int]$TintOpacity = 40, [string]$Tint = '101018', [int]$Seconds = 40)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class G
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] struct Accent { public int State; public int Flags; public uint Color; public int Anim; }
    [StructLayout(LayoutKind.Sequential)] struct Attr { public int A; public IntPtr D; public int S; }
    [StructLayout(LayoutKind.Sequential)] public struct THUMB { public uint Flags; public RECT Dest; public RECT Src; public byte Opacity; public bool Visible; public bool ClientOnly; }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetL(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetL(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte a, uint f);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr h, ref Attr d);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint f);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
    [DllImport("dwmapi.dll")] static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
    [DllImport("dwmapi.dll")] static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref THUMB p);
    [DllImport("dwmapi.dll")] static extern int DwmUnregisterThumbnail(IntPtr thumb);

    public static void ExStyle(IntPtr h, long add) { SetL(h, -20, (IntPtr)(GetL(h, -20).ToInt64() | add)); }
    public static void Alpha(IntPtr h, byte a) { ExStyle(h, 0x80000); SetLayeredWindowAttributes(h, 0, a, 2); }
    public static void Unlayer(IntPtr h) { SetL(h, -20, (IntPtr)(GetL(h, -20).ToInt64() & ~0x80000L)); }
    public static void Acrylic(IntPtr h, uint abgr)
    {
        var p = new Accent { State = 4, Flags = 0, Color = abgr };
        int size = Marshal.SizeOf(p); IntPtr ptr = Marshal.AllocHGlobal(size);
        Marshal.StructureToPtr(p, ptr, false);
        var a = new Attr { A = 19, D = ptr, S = size };
        SetWindowCompositionAttribute(h, ref a);
        Marshal.FreeHGlobal(ptr);
    }
    public static bool Bounds(IntPtr h, out RECT vis, out RECT win)
    {
        vis = new RECT(); win = new RECT();
        if (!GetWindowRect(h, out win)) return false;
        if (DwmGetWindowAttribute(h, 9, out vis, Marshal.SizeOf(typeof(RECT))) != 0) vis = win;
        return true;
    }
    public static IntPtr Thumb(IntPtr dest, IntPtr src) { IntPtr t; return DwmRegisterThumbnail(dest, src, out t) == 0 ? t : IntPtr.Zero; }
    public static void ThumbSet(IntPtr t, int dw, int dh, int sx, int sy, int sw, int sh)
    {
        var p = new THUMB { Flags = 1 | 2 | 4 | 8, Opacity = 255, Visible = true, ClientOnly = false };
        p.Dest = new RECT { L = 0, T = 0, R = dw, B = dh };
        p.Src = new RECT { L = sx, T = sy, R = sx + sw, B = sy + sh };
        DwmUpdateThumbnailProperties(t, ref p);
    }
    public static void ThumbOff(IntPtr t) { DwmUnregisterThumbnail(t); }
    public static void Above(IntPtr w, IntPtr target)
    {
        IntPtr prev = GetWindow(target, 3);                   // окно прямо над целевым
        if (prev == w) return;
        SetWindowPos(w, prev, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);   // NOSIZE | NOMOVE | NOACTIVATE
    }
    public static void Below(IntPtr w, IntPtr target)
    {
        if (GetWindow(target, 2) == w) return;
        SetWindowPos(w, target, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }
    public static void Place(IntPtr w, int x, int y, int cx, int cy) { SetWindowPos(w, IntPtr.Zero, x, y, cx, cy, 0x0004 | 0x0010); }
}
'@

$target = [IntPtr]$Target
function New-Tool([string]$name) {
    $f = New-Object System.Windows.Forms.Form
    $f.Text = $name; $f.FormBorderStyle = 'None'; $f.ShowInTaskbar = $false; $f.StartPosition = 'Manual'
    $f.BackColor = [System.Drawing.Color]::Black
    $f
}
$glass = New-Tool 'E2E-GLASS'
$strip = New-Tool 'E2E-STRIP'
$glass.Show(); $strip.Show()
[G]::ExStyle($glass.Handle, 0x08000080)                       # NOACTIVATE | TOOLWINDOW
[G]::ExStyle($strip.Handle, 0x08000080 -bor 0x20)             # + TRANSPARENT (мышь проходит сквозь)
$tint = [Convert]::ToUInt32($Tint, 16)
$abgr = ([uint32]([Math]::Round($TintOpacity * 255 / 100.0)) -shl 24) -bor (($tint -band 0xFF) -shl 16) -bor ($tint -band 0xFF00) -bor (($tint -shr 16) -band 0xFF)
[G]::Acrylic($glass.Handle, $abgr)
[G]::Alpha($target, [byte][Math]::Round($Alpha * 255 / 100.0))
if ($Strip -gt 0) { [G]::Alpha($strip.Handle, 255) }
$thumb = [IntPtr]::Zero
if ($Strip -gt 0) { $thumb = [G]::Thumb($strip.Handle, $target) }

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 30
$timer.Add_Tick({
    $vis = New-Object G+RECT; $win = New-Object G+RECT
    if (-not [G]::Bounds($target, [ref]$vis, [ref]$win)) { return }
    $w = $vis.R - $vis.L; $h = $vis.B - $vis.T
    [G]::Place($glass.Handle, $vis.L, $vis.T, $w, $h)
    [G]::Below($glass.Handle, $target)
    if ($Strip -gt 0) {
        [G]::Place($strip.Handle, $vis.L, $vis.T, $w, $Strip)
        [G]::Above($strip.Handle, $target)
        [G]::ThumbSet($thumb, $w, $Strip, ($vis.L - $win.L), ($vis.T - $win.T), $w, $Strip)
    }
})
$timer.Start()
$stop = New-Object System.Windows.Forms.Timer
$stop.Interval = $Seconds * 1000
$stop.Add_Tick({ $timer.Stop(); [G]::Unlayer($target); if ($thumb -ne [IntPtr]::Zero) { [G]::ThumbOff($thumb) }; $glass.Close(); $strip.Close(); [System.Windows.Forms.Application]::Exit() })
$stop.Start()
[System.Windows.Forms.Application]::Run()
