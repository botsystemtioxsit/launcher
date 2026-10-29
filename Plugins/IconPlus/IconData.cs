using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IconPlusApp;

// Значок: загрузка из .ico / .png / .exe / .dll / .icl, перекраска и запись в .ico (все размеры, PNG внутри).
static class IconData
{
    public static readonly int[] Sizes = { 16, 24, 32, 48, 64, 128, 256 };

    // Один значок нужного размера как картинка в формате BGRA (без предумножения прозрачности). null — не вышло.
    public static BitmapSource? Load(string location, int size)
    {
        ParseLocation(location, out var file, out var index);
        if (file.Length == 0 || !File.Exists(Environment.ExpandEnvironmentVariables(file))) return null;
        file = Environment.ExpandEnvironmentVariables(file);
        var ext = Path.GetExtension(file).ToLowerInvariant();
        try
        {
            if (ext is ".png" or ".bmp" or ".jpg" or ".jpeg" or ".gif") return FromImageFile(file, size);
            var h = Native.ExtractIcon(file, index, size);
            if (h == IntPtr.Zero) return null;
            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(h, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                return ToBgra(src);
            }
            finally { Native.DestroyIcon(h); }
        }
        catch { return null; }
    }

    // «C:\Windows\System32\imageres.dll,-3» → файл и номер. Номер может быть отрицательным (идентификатор ресурса).
    public static void ParseLocation(string location, out string file, out int index)
    {
        var loc = location.Trim();
        file = loc.Trim('"'); index = 0;
        var comma = loc.LastIndexOf(',');
        if (comma > 0 && int.TryParse(loc[(comma + 1)..].Trim(), out var i)) { index = i; file = loc[..comma].Trim().Trim('"'); }
    }

    static BitmapSource? FromImageFile(string file, int size)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bmp.UriSource = new Uri(file);
        bmp.EndInit();
        bmp.Freeze();
        return Resize(ToBgra(bmp), size);
    }

    public static BitmapSource ToBgra(BitmapSource src)
    {
        if (src.Format == PixelFormats.Bgra32) { if (!src.IsFrozen && src.CanFreeze) src.Freeze(); return src; }
        var c = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        c.Freeze();
        return c;
    }

    // Вписать в квадрат size×size с сохранением пропорций (на прозрачном фоне).
    public static BitmapSource Resize(BitmapSource src, int size)
    {
        if (src.PixelWidth == size && src.PixelHeight == size) return src;
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        var scale = Math.Min((double)size / src.PixelWidth, (double)size / src.PixelHeight);
        var w = src.PixelWidth * scale; var h = src.PixelHeight * scale;
        using (var dc = visual.RenderOpen()) dc.DrawImage(src, new Rect((size - w) / 2, (size - h) / 2, w, h));
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var straight = new FormatConvertedBitmap(rtb, PixelFormats.Bgra32, null, 0);
        straight.Freeze();
        return straight;
    }

    // Копия пикселей: массив BGRA.
    public static byte[] Pixels(BitmapSource src)
    {
        var bgra = ToBgra(src);
        var stride = bgra.PixelWidth * 4;
        var data = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(data, stride, 0);
        return data;
    }

    public static BitmapSource FromPixels(byte[] data, int w, int h)
    {
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, data, w * 4);
        bmp.Freeze();
        return bmp;
    }

    public static byte[] Png(BitmapSource src)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    // Записать .ico из набора картинок разных размеров (в каждом — PNG).
    public static void WriteIco(string path, IReadOnlyList<BitmapSource> images)
    {
        var pngs = images.Select(Png).ToList();
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write((ushort)0); bw.Write((ushort)1); bw.Write((ushort)images.Count);
        var offset = 6 + 16 * images.Count;
        for (var i = 0; i < images.Count; i++)
        {
            var w = images[i].PixelWidth; var h = images[i].PixelHeight;
            bw.Write((byte)(w >= 256 ? 0 : w)); bw.Write((byte)(h >= 256 ? 0 : h));
            bw.Write((byte)0); bw.Write((byte)0);
            bw.Write((ushort)1); bw.Write((ushort)32);
            bw.Write((uint)pngs[i].Length); bw.Write((uint)offset);
            offset += pngs[i].Length;
        }
        foreach (var p in pngs) bw.Write(p);
    }

    // Все размеры одного значка (из исходного расположения) в виде набора картинок; null, если исходный значок не достать.
    public static List<BitmapSource>? LoadAllSizes(string location, Func<byte[], int, int, byte[]>? recolor = null)
    {
        var list = new List<BitmapSource>();
        foreach (var s in Sizes)
        {
            var img = Load(location, s);
            if (img == null) continue;
            if (img.PixelWidth != s || img.PixelHeight != s) img = Resize(img, s);
            if (recolor != null) img = FromPixels(recolor(Pixels(img), s, s), s, s);
            list.Add(img);
        }
        return list.Count > 0 ? list : null;
    }
}
