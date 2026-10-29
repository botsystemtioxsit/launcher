using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace IconPlusApp;

sealed class IconPack
{
    public string Name { get; init; } = "";
    public string Dir { get; init; } = "";
    public List<string> Icons { get; init; } = new();   // полные пути к .ico
}

// Наборы значков: хранение, ввоз из архива или папки, подбор значков к целям по именам файлов.
static class Packs
{
    const int MaxFiles = 5000;
    const long MaxTotal = 400L * 1024 * 1024, MaxOne = 25L * 1024 * 1024;

    public static List<IconPack> List()
    {
        var list = new List<IconPack>();
        try
        {
            Paths.Ensure();
            foreach (var d in Directory.EnumerateDirectories(Paths.Packs).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var icons = Directory.EnumerateFiles(d, "*.ico", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                if (icons.Count > 0) list.Add(new IconPack { Name = Path.GetFileName(d), Dir = d, Icons = icons });
            }
        }
        catch { }
        return list;
    }

    public static void Delete(string name)
    {
        var dir = Path.Combine(Paths.Packs, name);
        if (Path.GetFullPath(dir).StartsWith(Path.GetFullPath(Paths.Packs), StringComparison.OrdinalIgnoreCase) && Directory.Exists(dir))
            Directory.Delete(dir, true);
    }

    // Ввоз: папка со значками или архив (zip, 7z, rar, tar…). Из архива берутся только .ico и .png — ничего не запускается и не распаковывается куда попало.
    public static string Import(string path, string? password, Action<string>? log = null)
    {
        Paths.Ensure();
        var baseName = Sanitize(Directory.Exists(path) ? Path.GetFileName(path.TrimEnd('\\', '/')) : Path.GetFileNameWithoutExtension(path));
        if (baseName.Length == 0) baseName = "Набор";
        var name = baseName; var n = 2;
        while (Directory.Exists(Path.Combine(Paths.Packs, name))) name = baseName + " " + n++;
        var dir = Path.Combine(Paths.Packs, name);
        Directory.CreateDirectory(dir);
        var count = 0;
        try
        {
            if (Directory.Exists(path))
            {
                foreach (var f in Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories))
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext is not (".ico" or ".png")) continue;
                    if (new FileInfo(f).Length > MaxOne) continue;
                    using var fs = File.OpenRead(f);
                    if (Save(dir, Path.GetFileName(f), fs)) count++;
                    if (count >= MaxFiles) break;
                }
            }
            else
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext is ".ico" or ".png") { using var fs = File.OpenRead(path); if (Save(dir, Path.GetFileName(path), fs)) count++; }
                else count = ImportArchive(path, dir, password);
            }
        }
        catch
        {
            try { Directory.Delete(dir, true); } catch { }
            throw;
        }
        if (count == 0)
        {
            try { Directory.Delete(dir, true); } catch { }
            throw new InvalidDataException("В этом файле нет значков (.ico или .png). Если это установщик (.exe) — запускать его не нужно: найдите на сайте вариант с обычными значками.");
        }
        log?.Invoke($"Набор «{name}»: значков {count}");
        return name;
    }

    static int ImportArchive(string path, string dir, string? password)
    {
        var count = 0; long total = 0; var entries = 0;
        var opts = new ReaderOptions();
        if (!string.IsNullOrEmpty(password)) opts.Password = password;
        using var archive = ArchiveFactory.Open(path, opts);
        using var reader = archive.ExtractAllEntries();
        while (reader.MoveToNextEntry())
        {
            if (++entries > 50000) break;
            var e = reader.Entry;
            if (e.IsDirectory || e.Key == null) continue;
            var ext = Path.GetExtension(e.Key).ToLowerInvariant();
            if (ext is not (".ico" or ".png")) continue;
            if (e.Size > MaxOne) continue;
            total += e.Size;
            if (total > MaxTotal || count >= MaxFiles) break;
            using var ms = new MemoryStream();
            reader.WriteEntryTo(ms);
            ms.Position = 0;
            if (Save(dir, Path.GetFileName(e.Key.Replace('/', '\\')), ms)) count++;
        }
        return count;
    }

    // Записать значок в набор: .ico — как есть (после проверки), .png — превращается в .ico. Имя — только имя файла, без путей.
    static bool Save(string dir, string fileName, Stream data)
    {
        var baseName = Sanitize(Path.GetFileNameWithoutExtension(fileName));
        if (baseName.Length == 0) baseName = "icon";
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var target = Path.Combine(dir, baseName + ".ico");
        var k = 2;
        while (File.Exists(target)) target = Path.Combine(dir, baseName + " " + k++ + ".ico");
        using var buf = new MemoryStream();
        data.CopyTo(buf);
        var bytes = buf.ToArray();
        if (ext == ".ico")
        {
            if (bytes.Length < 22 || bytes[0] != 0 || bytes[1] != 0 || bytes[2] != 1 || bytes[3] != 0) return false;   // это не значок
            File.WriteAllBytes(target, bytes);
            return true;
        }
        try
        {
            var tmp = Path.Combine(Path.GetTempPath(), "iconplus-" + Guid.NewGuid().ToString("N") + ".png");
            File.WriteAllBytes(tmp, bytes);
            try
            {
                var imgs = new List<System.Windows.Media.Imaging.BitmapSource>();
                foreach (var s in IconData.Sizes)
                {
                    var img = IconData.Load(tmp, s);
                    if (img != null) imgs.Add(img);
                }
                if (imgs.Count == 0) return false;
                IconData.WriteIco(target, imgs);
                return true;
            }
            finally { try { File.Delete(tmp); } catch { } }
        }
        catch { return false; }
    }

    static string Sanitize(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var c in s) sb.Append(invalid.Contains(c) || c < ' ' ? '_' : c);
        return sb.ToString().Trim().Trim('.');
    }

    // ---------- подбор по именам ----------
    static readonly Regex CamelRe = new("(?<=[a-zа-я0-9])(?=[A-ZА-Я])", RegexOptions.Compiled);

    static string Normalize(string name)
    {
        var withBreaks = CamelRe.Replace(Path.GetFileNameWithoutExtension(name), " ");
        return Regex.Replace(withBreaks.ToLowerInvariant(), @"[_\-\.\(\)\[\]]+", " ").Trim();
    }

    static int Score(Target t, string text, string[] tokens)
    {
        var best = 0;
        foreach (var kw in t.Keywords)
        {
            var k = kw.ToLowerInvariant();
            var s = 0;
            if (k.Contains(' ')) { if (text.Contains(k)) s = 4; }
            else if (tokens.Contains(k)) s = 3;
            else if (k.Length >= 4 && tokens.Any(x => x.StartsWith(k))) s = 2;
            else if (k.Length >= 4 && text.Contains(k)) s = 1;
            best = Math.Max(best, s);
        }
        if (best > 0 && t.Reject != null && t.Reject.Any(r => tokens.Contains(r.ToLowerInvariant()))) best -= 4;
        return best;
    }

    // Подбор: цель → путь к значку набора. Каждый значок достаётся не больше чем одной цели.
    public static Dictionary<string, string> Suggest(IconPack pack)
    {
        var scored = new List<(int Score, string Target, string File)>();
        foreach (var f in pack.Icons)
        {
            var text = Normalize(Path.GetFileName(f));
            var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var t in Target.All)
            {
                if (t.Kind == TargetKind.Shortcuts) continue;
                var s = Score(t, text, tokens);
                if (s >= 2) scored.Add((s, t.Id, f));
            }
        }
        var map = new Dictionary<string, string>();
        var used = new HashSet<string>();
        foreach (var (_, target, file) in scored.OrderByDescending(x => x.Score).ThenBy(x => x.File.Length))
        {
            if (map.ContainsKey(target) || used.Contains(file)) continue;
            map[target] = file;
            used.Add(file);
        }
        return map;
    }
}
