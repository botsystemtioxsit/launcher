using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SkinLoaderApp;

// Снимок экрана для превью скина и сжатие картинок в небольшой JPEG
static class Shots
{
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);

    public static BitmapSource CaptureScreen()
    {
        int x = GetSystemMetrics(76), y = GetSystemMetrics(77), w = GetSystemMetrics(78), h = GetSystemMetrics(79);
        if (w <= 0 || h <= 0) { x = 0; y = 0; w = GetSystemMetrics(0); h = GetSystemMetrics(1); }
        var screen = GetDC(IntPtr.Zero);
        var mem = CreateCompatibleDC(screen);
        var bmp = CreateCompatibleBitmap(screen, w, h);
        var old = SelectObject(mem, bmp);
        try
        {
            if (!BitBlt(mem, 0, 0, w, h, screen, x, y, 0x00CC0020 | 0x40000000)) throw new InvalidOperationException("Не удалось снять экран");
            var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally { SelectObject(mem, old); DeleteObject(bmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen); }
    }

    // JPEG не больше maxBytes: стороны и качество понижаются, пока не поместится
    public static byte[] ToJpeg(BitmapSource src, int maxSide = 800, int maxBytes = 120_000)
    {
        BitmapSource flat = new FormatConvertedBitmap(src, PixelFormats.Bgr24, null, 0);
        foreach (var (side, q) in new[] { (maxSide, 80), (maxSide * 3 / 4, 70), (maxSide / 2, 60), (maxSide / 3, 50), (200, 40) })
        {
            var k = Math.Min(1.0, (double)side / Math.Max(flat.PixelWidth, flat.PixelHeight));
            BitmapSource scaled = k < 1 ? new TransformedBitmap(flat, new ScaleTransform(k, k)) : flat;
            var enc = new JpegBitmapEncoder { QualityLevel = q };
            enc.Frames.Add(BitmapFrame.Create(scaled));
            using var ms = new MemoryStream();
            enc.Save(ms);
            if (ms.Length <= maxBytes) return ms.ToArray();
        }
        throw new InvalidDataException("Картинка не сжимается до нужного размера");
    }

    // Чужую картинку не храним как есть: разбираем, проверяем и пересохраняем заново (так в файле не остаётся ничего лишнего)
    public static byte[] Sanitize(byte[] image)
    {
        if (image.Length > 8 * 1024 * 1024) throw new InvalidDataException("Картинка слишком большая");
        using var ms = new MemoryStream(image);
        var dec = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var f = dec.Frames[0];
        if (f.PixelWidth < 1 || f.PixelHeight < 1 || f.PixelWidth > 12000 || f.PixelHeight > 12000) throw new InvalidDataException("Недопустимый размер картинки");
        f.Freeze();
        return ToJpeg(f);
    }

    public static BitmapImage? Load(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = new MemoryStream(bytes);
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch { return null; }
    }
}
