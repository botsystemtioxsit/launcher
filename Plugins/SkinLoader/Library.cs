using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace SkinLoaderApp;

sealed record SavedSkin(Skin Skin, byte[]? Preview);

// Библиотека скинов на этом компьютере: %APPDATA%\BattleLauncher\skin-loader\skins\<номер>\{skin.json, preview.jpg}.
// Файл скина для обмена (.ghostskin) — обычный zip из этих двух файлов; при импорте всё проверяется заново.
static class Library
{
    public static string Root => Path.Combine(Plugins.DataDir, "skin-loader", "skins");
    public const string Extension = ".ghostskin";

    static bool ValidId(string id) => id.Length is > 1 and <= 40 && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
    static string NewId() => "s-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant();

    public static List<SavedSkin> List()
    {
        var list = new List<SavedSkin>();
        try
        {
            if (!Directory.Exists(Root)) return list;
            foreach (var dir in Directory.GetDirectories(Root))
            {
                var id = Path.GetFileName(dir);
                if (!ValidId(id)) continue;
                try
                {
                    var skin = Skin.Parse(File.ReadAllText(Path.Combine(dir, "skin.json")));
                    skin.Id = id;
                    var pf = Path.Combine(dir, "preview.jpg");
                    list.Add(new SavedSkin(skin, File.Exists(pf) ? File.ReadAllBytes(pf) : null));
                }
                catch { /* повреждённый скин в списке не показываем */ }
            }
        }
        catch { }
        return list.OrderByDescending(s => s.Skin.Created).ToList();
    }

    public static string Save(Skin skin, byte[]? preview)
    {
        var id = ValidId(skin.Id) ? skin.Id : NewId();
        var dir = Path.Combine(Root, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "skin.json"), skin.ToJson());
        var pf = Path.Combine(dir, "preview.jpg");
        if (preview != null) File.WriteAllBytes(pf, preview); else File.Delete(pf);
        skin.Id = id;
        return id;
    }

    public static void Delete(string id)
    {
        if (!ValidId(id)) return;
        try { Directory.Delete(Path.Combine(Root, id), true); } catch (DirectoryNotFoundException) { }
    }

    public static void Export(Skin skin, byte[]? preview, string file)
    {
        if (File.Exists(file)) File.Delete(file);
        using var zip = ZipFile.Open(file, ZipArchiveMode.Create);
        Add(zip, "skin.json", System.Text.Encoding.UTF8.GetBytes(skin.ToJson()));
        if (preview != null) Add(zip, "preview.jpg", preview);
    }

    static void Add(ZipArchive zip, string name, byte[] data)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = e.Open();
        s.Write(data, 0, data.Length);
    }

    // Импорт: берём только skin.json и preview.jpg, размеры ограничены, скин очищается, картинка пересохраняется
    public static SavedSkin Import(string file, List<string>? warnings = null)
    {
        warnings ??= new();
        var info = new FileInfo(file);
        if (!info.Exists) throw new FileNotFoundException("Файла нет");
        if (info.Length > 2 * 1024 * 1024) throw new InvalidDataException("Файл больше 2 МБ — это не скин");
        if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            var only = Skin.Parse(File.ReadAllText(file), warnings);
            return new SavedSkin(only, null);
        }
        using var zip = ZipFile.OpenRead(file);
        if (zip.Entries.Count > 8) throw new InvalidDataException("В файле слишком много вложений — это не скин");
        string? json = null; byte[]? preview = null;
        foreach (var e in zip.Entries)
        {
            var name = e.FullName;
            if (name.Equals("skin.json", StringComparison.OrdinalIgnoreCase))
            {
                if (e.Length > Skin.MaxFileBytes) throw new InvalidDataException("skin.json слишком большой");
                using var r = new StreamReader(e.Open(), System.Text.Encoding.UTF8);
                json = r.ReadToEnd();
            }
            else if (name.Equals("preview.jpg", StringComparison.OrdinalIgnoreCase) || name.Equals("preview.png", StringComparison.OrdinalIgnoreCase))
            {
                if (e.Length > 4 * 1024 * 1024) { warnings.Add("Превью слишком большое — пропущено"); continue; }
                using var ms = new MemoryStream();
                using (var s = e.Open()) s.CopyTo(ms);
                try { preview = Shots.Sanitize(ms.ToArray()); } catch { warnings.Add("Превью не читается — пропущено"); }
            }
            else if (!string.IsNullOrEmpty(e.Name)) warnings.Add($"Лишний файл «{e.Name}» проигнорирован");
        }
        if (json == null) throw new InvalidDataException("В файле нет skin.json — это не скин");
        return new SavedSkin(Skin.Parse(json, warnings), preview);
    }
}
