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
        // особые источники: «shell:<путь>» — значок как его рисует Проводник, «lnktarget:<ярлык>» — значок цели ярлыка
        if (location.StartsWith("shell:", StringComparison.Ordinal)) return ShellIcon.Render(location[6..], size);
        if (location.StartsWith("lnktarget:", StringComparison.Ordinal)) return ShellIcon.RenderLnkTarget(location[10..], size);
        if (location.StartsWith("pkg:", StringComparison.Ordinal)) return PackageIcons.Render(location[4..], size);
        ParseLocation(location, out var file, out var index);
        if (file.Length == 0 || !File.Exists(Environment.ExpandEnvironmentVariables(file))) return null;
        file = Environment.ExpandEnvironmentVariables(file);
        var ext = Path.GetExtension(file).ToLowerInvariant();
        try
        {
            if (ext is ".png" or ".bmp" or ".jpg" or ".jpeg" or ".gif") return LoadImageFile(file, size);
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

    internal static BitmapSource? LoadImageFile(string file, int size)
    {
        try
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
        catch { return null; }
    }

    // «Пустой» значок: почти ничего не нарисовано или это сплошная плашка одного цвета (пустая плитка магазинного приложения).
    // Такой значок красить нечего, а записать его в ярлык значит получить квадрат вместо значка.
    // Значок-силуэт одного цвета (белый глиф на прозрачном фоне) пустым не считается: у него неполное заполнение рамки.
    public static bool IsBlank(BitmapSource img) => BlankInfo(img).Blank;

    public static (bool Blank, string Info) BlankInfo(BitmapSource img)
    {
        const int n = 64;
        var px = Pixels(Resize(img, n));
        long count = 0, total = n * n;
        int minX = n, minY = n, maxX = -1, maxY = -1;
        double sb = 0, sg = 0, sr = 0, qb = 0, qg = 0, qr = 0;
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var i = (y * n + x) * 4;
                if (px[i + 3] < 40) continue;
                count++;
                if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y;
                double b = px[i], g = px[i + 1], r = px[i + 2];
                sb += b; sg += g; sr += r; qb += b * b; qg += g * g; qr += r * r;
            }
        if (count < total / 50) return (true, "почти прозрачный");
        double Sd(double sum, double sq) { var m = sum / count; return Math.Sqrt(Math.Max(0, sq / count - m * m)); }
        var sd = Math.Max(Sd(sb, qb), Math.Max(Sd(sg, qg), Sd(sr, qr)));
        var box = (long)(maxX - minX + 1) * (maxY - minY + 1);
        var cover = count / (double)box;
        var info = $"заполнение рамки {cover:0.00}, разброс цвета {sd:0.0}, рамка {box * 100 / total}%";
        return (sd < 12 && cover >= 0.9 && box >= total / 5, info);
    }

    // Значок «неизвестного типа файла» (белый листок с загнутым уголком), как его сейчас рисует Проводник. Красить такой значок ярлыку нельзя:
    // он значит «настоящий значок не нашёлся» (у ярлыков на .msc и т. п.), а не «у этой программы значок — листок».
    static BitmapSource? genericDoc;
    public static BitmapSource? GenericDocument()
    {
        lock (typeof(IconData))
        {
            if (genericDoc != null) return genericDoc;
            try
            {
                var dir = Path.Combine(Path.GetTempPath(), "iconplus-ext");
                Directory.CreateDirectory(dir);
                var f = Path.Combine(dir, "sample.gsunknownext");
                if (!File.Exists(f)) File.WriteAllBytes(f, Array.Empty<byte>());
                genericDoc = ShellIcon.Render(f, 48);
            }
            catch { }
            return genericDoc;
        }
    }

    public static bool IsGenericDocument(BitmapSource img)
    {
        var reference = GenericDocument();
        if (reference == null) return false;
        var a = Pixels(Resize(img, 48));
        var b = Pixels(Resize(reference, 48));
        long diff = 0; var n = 0;
        for (var i = 0; i + 3 < a.Length && i + 3 < b.Length; i += 4)
        {
            if (a[i + 3] < 40 && b[i + 3] < 40) continue;
            n++;
            diff += Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]) + Math.Abs(a[i + 3] - b[i + 3]);
        }
        return n > 100 && diff / (4.0 * n) < 10;
    }

    public static bool IsBlank(IReadOnlyList<BitmapSource> sizes)
    {
        // судим по самому большому размеру: на мелких пикселях рисунок может слипнуться
        var big = sizes.OrderByDescending(x => x.PixelWidth).FirstOrDefault();
        return big == null || IsBlank(big);
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
