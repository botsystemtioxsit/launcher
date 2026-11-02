using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CursorPlusApp;

// Какие указатели есть в Windows: имя в реестре, номер системного указателя и как его красить.
sealed record Role(string Reg, int Ocr, string Title, bool Dark);

static class Roles
{
    // Dark — для указателей с тёмным телом и светлой каймой (в Windows 11 таких нет: все белые с тёмной обводкой), оставлено на будущее.
    public static readonly Role[] All =
    {
        new("Arrow", 32512, "Обычный", false),
        new("Help", 32651, "Справка", false),
        new("Crosshair", 32515, "Точный выбор", false),
        new("IBeam", 32513, "Текст", false),
        new("No", 32648, "Недоступно", false),
        new("SizeNS", 32645, "Размер ↕", false),
        new("SizeWE", 32644, "Размер ↔", false),
        new("SizeNWSE", 32642, "Размер ↘", false),
        new("SizeNESW", 32643, "Размер ↙", false),
        new("SizeAll", 32646, "Переместить", false),
        new("UpArrow", 32516, "Выбор вверх", false),
        new("Hand", 32649, "Ссылка", false),
    };

    public static Role Get(string reg) => All.First(r => r.Reg == reg);
}

// Указатели самой Windows (с размером) и их перекраска.
static class SysCursors
{
    // Системный указатель нужного размера. null — Windows не отдала.
    public static CurImg? Load(int ocr, int size)
    {
        var h = Native.LoadImage(IntPtr.Zero, (IntPtr)ocr, Native.IMAGE_CURSOR, size, size, Native.LR_SHARED);
        return h == IntPtr.Zero ? null : FromHandle(h, size, false);
    }

    // Указатель из файла (.cur / .ani): первый кадр.
    public static CurImg? LoadFile(string path, int size)
    {
        var h = Native.LoadImageFile(IntPtr.Zero, path, Native.IMAGE_CURSOR, size, size, Native.LR_LOADFROMFILE);
        return h == IntPtr.Zero ? null : FromHandle(h, size, true);
    }

    static CurImg? FromHandle(IntPtr h, int size, bool destroy)
    {
        try
        {
            if (!Native.GetIconInfo(h, out var ii)) return null;
            if (ii.hbmMask != IntPtr.Zero) Native.DeleteObject(ii.hbmMask);
            if (ii.hbmColor != IntPtr.Zero) Native.DeleteObject(ii.hbmColor);
            var src = Imaging.CreateBitmapSourceFromHIcon(h, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var (bgra, scale, ox, oy) = Img.Fit(src, size, true);
            return new CurImg(size, size, (int)Math.Round(ii.xHotspot * scale + ox), (int)Math.Round(ii.yHotspot * scale + oy), bgra);
        }
        catch { return null; }
        finally { if (destroy) Native.DestroyCursor(h); }
    }

    // Перекраска: светлое → основной цвет, тёмное → обводка. Для «тёмных» указателей (текст, размеры) наоборот: тело получает цвет, кайма светлеет.
    public static CurImg Tint(CurImg src, (int R, int G, int B) fill, (int R, int G, int B) line, bool dark)
    {
        var d = (byte[])src.Bgra.Clone();
        var light = Col.Mix(fill, (255, 255, 255), 0.82);
        for (var i = 0; i < d.Length; i += 4)
        {
            if (d[i + 3] == 0) continue;
            var lum = (0.299 * d[i + 2] + 0.587 * d[i + 1] + 0.114 * d[i]) / 255.0;
            var t = Math.Clamp((lum - 0.2) / 0.6, 0, 1);
            (int R, int G, int B) o;
            if (!dark) o = Col.Mix(line, fill, t);
            else o = Col.Mix(fill, light, t);   // тёмное → цвет, светлое → светлый оттенок цвета
            d[i] = (byte)o.B; d[i + 1] = (byte)o.G; d[i + 2] = (byte)o.R;
        }
        return new CurImg(src.W, src.H, src.HotX, src.HotY, d);
    }
}
