using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace IconPlusApp;

// Значок «так, как его рисует Проводник» — для того, чему нельзя достать значок из файла: магазинные приложения (UWP/MSIX), ярлыки без значка,
// ярлыки Steam-игр, особые папки. Работает через IShellItemImageFactory (как миниатюры в Проводнике).
static class ShellIcon
{
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, uint flags, out IntPtr hbitmap);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public short bmPlanes, bmBitsPixel; public IntPtr bmBits; }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER { public int Size, Width, Height; public short Planes, BitCount; public int Compression, SizeImage, XPels, YPels, ClrUsed, ClrImportant; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    [DllImport("shell32.dll", PreserveSig = false)]
    static extern void SHCreateItemFromIDList(IntPtr pidl, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int cb, out BITMAP bmp);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr hbm, uint start, uint lines, byte[]? bits, ref BITMAPINFOHEADER bi, uint usage);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr p);

    static readonly Guid Iid = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    // Значок файла, папки или объекта оболочки по пути.
    public static BitmapSource? Render(string path, int size)
    {
        try
        {
            var iid = Iid;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var f);
            try { return FromFactory(f, size); } finally { Marshal.ReleaseComObject(f); }
        }
        catch { return null; }
    }

    // Значок цели ярлыка (у магазинных приложений цель — не файл, а объект оболочки).
    public static BitmapSource? RenderLnkTarget(string lnk, int size)
    {
        var pidl = Shortcuts.ReadIdList(lnk);
        if (pidl == IntPtr.Zero) return null;
        try
        {
            var iid = Iid;
            SHCreateItemFromIDList(pidl, ref iid, out var f);
            try { return FromFactory(f, size); } finally { Marshal.ReleaseComObject(f); }
        }
        catch { return null; }
        finally { CoTaskMemFree(pidl); }
    }

    static BitmapSource? FromFactory(IShellItemImageFactory f, int size)
    {
        // 0x4 — только значок (не миниатюра), 0x1 — размер можно побольше
        if (f.GetImage(new SIZE { cx = size, cy = size }, 0x4 | 0x1, out var hbm) != 0 || hbm == IntPtr.Zero) return null;
        try
        {
            if (GetObject(hbm, Marshal.SizeOf<BITMAP>(), out var bmp) == 0) return null;
            int w = bmp.bmWidth, h = Math.Abs(bmp.bmHeight);
            if (w <= 0 || h <= 0 || w > 4096 || h > 4096) return null;
            var bi = new BITMAPINFOHEADER { Size = 40, Width = w, Height = -h, Planes = 1, BitCount = 32 };
            var data = new byte[w * h * 4];
            var dc = GetDC(IntPtr.Zero);
            try { if (GetDIBits(dc, hbm, 0, (uint)h, data, ref bi, 0) == 0) return null; }
            finally { ReleaseDC(IntPtr.Zero, dc); }
            Unpremultiply(data);
            return IconData.Resize(IconData.FromPixels(data, w, h), size);
        }
        finally { DeleteObject(hbm); }
    }

    // Проводник может отдавать картинку с «предумноженной» прозрачностью — тогда цвета не больше прозрачности; возвращаем обычный вид.
    static void Unpremultiply(byte[] d)
    {
        var any = false;
        for (var i = 0; i < d.Length; i += 4)
        {
            var a = d[i + 3];
            if (a == 0 || a == 255) continue;
            any = true;
            if (d[i] > a || d[i + 1] > a || d[i + 2] > a) return;   // уже обычная прозрачность
        }
        if (!any) return;
        for (var i = 0; i < d.Length; i += 4)
        {
            var a = d[i + 3];
            if (a == 0 || a == 255) continue;
            d[i] = (byte)Math.Min(255, d[i] * 255 / a);
            d[i + 1] = (byte)Math.Min(255, d[i + 1] * 255 / a);
            d[i + 2] = (byte)Math.Min(255, d[i + 2] * 255 / a);
        }
    }
}
