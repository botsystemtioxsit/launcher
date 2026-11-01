using System.IO;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Microsoft.Win32;

namespace IconPlusApp;

// Значок магазинного приложения (UWP/MSIX: Claude, Блокнот, Minecraft Launcher…) прямо из файлов его пакета.
// Проводник для таких приложений рисует «плитку», и у процесса без идентификатора пакета она часто выходит пустой цветной квадрат;
// а сами картинки логотипа лежат рядом с манифестом пакета и читаются без прав (менять их нельзя — нам и не нужно, мы только читаем).
static class PackageIcons
{
    const string ReposKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    sealed record Pkg(string Name, string Publisher, Version Ver, string Root);

    static List<Pkg>? packages;
    static readonly Dictionary<string, string?> logoCache = new(StringComparer.OrdinalIgnoreCase);

    // Установленные для этого пользователя пакеты из реестра.
    static List<Pkg> Packages()
    {
        if (packages != null) return packages;
        var list = new List<Pkg>();
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(ReposKey);
            if (k != null)
                foreach (var full in k.GetSubKeyNames())
                {
                    try
                    {
                        // «Имя_версия_архитектура_язык_издатель»; в имени пакета подчёркиваний нет
                        var parts = full.Split('_');
                        if (parts.Length < 5) continue;
                        using var sk = k.OpenSubKey(full);
                        var root = sk?.GetValue("PackageRootFolder") as string;
                        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                        Version.TryParse(parts[1], out var ver);
                        list.Add(new Pkg(parts[0], parts[^1], ver ?? new Version(0, 0), root));
                    }
                    catch { }
                }
        }
        catch { }
        packages = list;
        return list;
    }

    // «Microsoft.WindowsNotepad_8wekyb3d8bbwe!App» → самый новый установленный пакет этого семейства.
    static Pkg? Find(string family)
    {
        var us = family.LastIndexOf('_');
        if (us <= 0) return null;
        var name = family[..us]; var pub = family[(us + 1)..];
        return Packages().Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && p.Publisher.Equals(pub, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.Ver).FirstOrDefault();
    }

    // Все приложения установленных пакетов: идентификатор программы (AUMID) и папка пакета. Для самопроверки.
    public static List<(string Aumid, string Root)> All(int limit = 400)
    {
        var res = new List<(string, string)>();
        foreach (var p in Packages())
        {
            foreach (var id in AppIds(p.Root))
            {
                res.Add((p.Name + "_" + p.Publisher + "!" + id, p.Root));
                if (res.Count >= limit) return res;
            }
        }
        return res;
    }

    static List<string> AppIds(string root)
    {
        var ids = new List<string>();
        try
        {
            var doc = XDocument.Load(Path.Combine(root, "AppxManifest.xml"));
            foreach (var app in doc.Descendants().Where(e => e.Name.LocalName == "Application"))
            {
                var id = (string?)app.Attribute("Id");
                if (!string.IsNullOrEmpty(id)) ids.Add(id);
            }
        }
        catch { }
        return ids;
    }

    // Файл с картинкой значка приложения или null (нет такого пакета, нет логотипа на диске).
    public static string? FindLogo(string aumid)
    {
        if (logoCache.TryGetValue(aumid, out var cached)) return cached;
        string? res = null;
        try
        {
            var bang = aumid.IndexOf('!');
            var pkg = bang > 0 ? Find(aumid[..bang]) : null;
            if (pkg != null) res = LogoOf(pkg.Root, bang > 0 ? aumid[(bang + 1)..] : "");
        }
        catch { }
        logoCache[aumid] = res;
        return res;
    }

    static string? LogoOf(string root, string appId)
    {
        var doc = XDocument.Load(Path.Combine(root, "AppxManifest.xml"));
        var apps = doc.Descendants().Where(e => e.Name.LocalName == "Application").ToList();
        var app = apps.FirstOrDefault(a => string.Equals((string?)a.Attribute("Id"), appId, StringComparison.OrdinalIgnoreCase)) ?? apps.FirstOrDefault();
        if (app == null) return null;
        var ve = app.Descendants().FirstOrDefault(e => e.Name.LocalName == "VisualElements");
        if (ve == null) return null;
        foreach (var attr in new[] { "Square44x44Logo", "Square30x30Logo", "Square71x71Logo", "Square150x150Logo", "Logo" })
        {
            var rel = (string?)ve.Attribute(attr);
            if (string.IsNullOrWhiteSpace(rel)) continue;
            var file = BestAsset(root, rel);
            if (file != null) return file;
        }
        return null;
    }

    // «Assets\Square44x44Logo.png» → лучшая из соседних картинок (Square44x44Logo.targetsize-256_altform-unplated.png и т. п.).
    static string? BestAsset(string root, string rel)
    {
        var path = Path.Combine(root, rel.Replace('/', '\\'));
        var dir = Path.GetDirectoryName(path);
        var stem = Path.GetFileNameWithoutExtension(path);
        if (dir == null || !Directory.Exists(dir)) return null;
        var baseSize = stem.Contains("310") ? 310 : stem.Contains("150") ? 150 : stem.Contains("71") ? 71 : stem.Contains("30") ? 30 : 44;
        string? best = null; long bestScore = -1;
        foreach (var f in Directory.EnumerateFiles(dir, stem + "*.png"))
        {
            var name = Path.GetFileNameWithoutExtension(f);
            if (!name.StartsWith(stem, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = name[stem.Length..];
            if (rest.Length > 0 && rest[0] != '.' && rest[0] != '_') continue;   // другая картинка с похожим началом
            int size = 0, scale = 100; var unplated = false; var bad = false; var hasTarget = false;
            foreach (var tok in rest.Split('.', '_'))
            {
                var t = tok.ToLowerInvariant();
                if (t.Length == 0) continue;
                if (t.StartsWith("targetsize-")) { if (int.TryParse(t[11..], out var n)) { size = n; hasTarget = true; } }
                else if (t.StartsWith("scale-")) { int.TryParse(t[6..], out scale); if (scale <= 0) scale = 100; }
                else if (t == "altform-unplated") unplated = true;
                else if (t.StartsWith("altform-") || t.StartsWith("contrast-") || t == "theme-light") bad = true;   // светлая тема, высокая контрастность и прочее
            }
            if (bad) continue;
            var px = hasTarget ? size : baseSize * scale / 100;
            if (px <= 0) px = baseSize;
            // «без плитки» (altform-unplated) — чистый значок без фона; берём его, если он не мелкий, потом самый большой из остальных
            long score = px + (unplated && px >= 48 ? 100000 : 0);
            if (score > bestScore) { bestScore = score; best = f; }
        }
        return best;
    }

    // Значок приложения нужного размера (вписан в квадрат); null — не нашлось.
    public static BitmapSource? Render(string aumid, int size)
    {
        var file = FindLogo(aumid);
        return file == null ? null : IconData.LoadImageFile(file, size);
    }
}
