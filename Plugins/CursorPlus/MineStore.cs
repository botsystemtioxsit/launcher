using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace CursorPlusApp;

// Свои курсоры: нарисованные в редакторе или привезённые из картинки. Лежат в %APPDATA%\BattleLauncher\cursor-plus\mine\<id>.png + <id>.json
sealed class MineEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Мой курсор";
    public int N { get; set; } = 32;           // сторона картинки в пикселях (32 — рисованный, 64 — из картинки)
    public int HotX { get; set; }              // клетка, которой курсор «целится»
    public int HotY { get; set; }
    public bool Smooth { get; set; }           // при увеличении сглаживать (картинка), иначе оставлять пиксели чёткими (рисунок)
}

static class MineStore
{
    public static string Dir => Path.Combine(CursorSettings.Dir, "mine");

    public static bool IsId(string s) => System.Text.RegularExpressions.Regex.IsMatch(s ?? "", "^[a-z0-9]{6,16}$");

    public static string NewId() => Guid.NewGuid().ToString("N")[..10];

    public static List<MineEntry> List()
    {
        var res = new List<MineEntry>();
        try
        {
            if (!Directory.Exists(Dir)) return res;
            foreach (var f in Directory.GetFiles(Dir, "*.json").OrderBy(File.GetCreationTimeUtc))
            {
                try
                {
                    var e = JsonSerializer.Deserialize<MineEntry>(File.ReadAllText(f));
                    if (e != null && IsId(e.Id) && File.Exists(Path.Combine(Dir, e.Id + ".png"))) res.Add(e);
                }
                catch { }
            }
        }
        catch { }
        return res;
    }

    public static MineEntry? Get(string id) => IsId(id) ? List().FirstOrDefault(e => e.Id == id) : null;

    // Рисунок (n×n, BGRA с обычной прозрачностью); null — файла нет.
    public static byte[]? Pixels(MineEntry e)
    {
        try
        {
            using var fs = File.OpenRead(Path.Combine(Dir, e.Id + ".png"));
            var dec = new PngBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var fr = dec.Frames[0];
            if (fr.PixelWidth != e.N || fr.PixelHeight != e.N) return null;
            return Img.Straight(fr);
        }
        catch { return null; }
    }

    public static void Save(MineEntry e, byte[] bgra)
    {
        Directory.CreateDirectory(Dir);
        if (!IsId(e.Id)) e.Id = NewId();
        var bmp = BitmapSource.Create(e.N, e.N, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, bgra, e.N * 4);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using (var ms = new MemoryStream()) { enc.Save(ms); File.WriteAllBytes(Path.Combine(Dir, e.Id + ".png"), ms.ToArray()); }
        File.WriteAllText(Path.Combine(Dir, e.Id + ".json"), JsonSerializer.Serialize(e));
    }

    public static void Delete(string id)
    {
        if (!IsId(id)) return;
        try { File.Delete(Path.Combine(Dir, id + ".png")); File.Delete(Path.Combine(Dir, id + ".json")); } catch { }
    }

    // Курсор нужного размера (квадрат px × px).
    public static CurImg? Render(string id, int px)
    {
        var e = Get(id);
        if (e == null) return null;
        var pix = Pixels(e);
        if (pix == null) return null;
        var (bgra, scale, ox, oy) = Img.Fit(Img.FromPixels(pix, e.N, e.N), px, e.Smooth);
        var hx = (int)Math.Round((e.HotX + 0.5) * scale + ox);
        var hy = (int)Math.Round((e.HotY + 0.5) * scale + oy);
        return new CurImg(px, px, Math.Clamp(hx, 0, px - 1), Math.Clamp(hy, 0, px - 1), bgra);
    }
}
