using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace WallpaperPlusApp;

sealed class Wallpaper
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Type { get; set; } = "html";        // html | video | gif | image
    public string Entry { get; set; } = "index.html"; // главный файл внутри папки обоев
    public string Description { get; set; } = "";
    public string Author { get; set; } = "";
    public string Source { get; set; } = "";          // откуда: встроенные, каталог, файл, сайт
    public int Version { get; set; } = 1;
    public bool Online { get; set; }                  // обоям разрешён доступ в интернет (по умолчанию нет)

    [JsonIgnore] public string Dir => Path.Combine(Paths.Library, Id);
    [JsonIgnore] public bool Builtin => Id.StartsWith("b-", StringComparison.Ordinal);
    [JsonIgnore] public long SizeBytes { get; set; }

    public string TypeLabel => Type switch { "video" => "видео", "gif" => "анимация GIF", "image" => "картинка", _ => "страница" };
}

// Библиотека установленных обоев: %LOCALAPPDATA%\GhostShell\WallpaperPlus\library\<id>\ (manifest.json + файлы обоев).
static class Library
{
    const string ManifestName = "manifest.json";
    const long MaxTotal = 1_200_000_000, MaxFile = 700_000_000;
    const int MaxFiles = 3000;

    // Что вообще может лежать в обоях: страницы, картинки, видео, звук, шрифты, данные. Программы и сценарии (.exe, .dll, .bat, .ps1, .lnk…) не копируются никогда.
    static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".js", ".mjs", ".css", ".json", ".txt", ".xml", ".map", ".glsl", ".frag", ".vert", ".svg",
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".ico", ".avif",
        ".mp4", ".webm", ".m4v", ".ogv", ".mov", ".mp3", ".ogg", ".wav", ".m4a", ".flac",
        ".woff", ".woff2", ".ttf", ".otf", ".eot", ".glb", ".gltf", ".bin", ".obj", ".mtl", ".hdr", ".dds", ".ktx2",
    };
    static readonly string[] VideoExt = { ".mp4", ".webm", ".m4v", ".ogv", ".mov" };
    static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".avif" };
    public const string PickerFilter = "Обои (видео, GIF, картинки, страницы, архивы)|*.mp4;*.webm;*.m4v;*.mov;*.gif;*.png;*.jpg;*.jpeg;*.webp;*.html;*.htm;*.zip|Все файлы|*.*";

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // ---------- чтение ----------
    public static List<Wallpaper> List()
    {
        var list = new List<Wallpaper>();
        try
        {
            if (!Directory.Exists(Paths.Library)) return list;
            foreach (var dir in Directory.GetDirectories(Paths.Library))
            {
                var w = Read(dir);
                if (w != null) list.Add(w);
            }
        }
        catch (Exception ex) { Paths.Log("библиотека: " + ex.Message); }
        return list.OrderByDescending(w => w.Builtin).ThenBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static Wallpaper? Get(string id) => string.IsNullOrEmpty(id) || !IdRe.IsMatch(id) ? null : Read(Path.Combine(Paths.Library, id));

    static readonly Regex IdRe = new("^[A-Za-z0-9_-]{1,64}$");

    static Wallpaper? Read(string dir)
    {
        try
        {
            var mf = Path.Combine(dir, ManifestName);
            if (!File.Exists(mf)) return null;
            var w = JsonSerializer.Deserialize<Wallpaper>(File.ReadAllText(mf));
            if (w == null || w.Id != Path.GetFileName(dir) || !IdRe.IsMatch(w.Id)) return null;
            if (w.Type is not ("html" or "video" or "gif" or "image")) w.Type = "html";
            var entry = Path.Combine(dir, w.Entry);
            if (!File.Exists(entry)) return null;
            w.SizeBytes = DirSize(dir);
            return w;
        }
        catch { return null; }
    }

    static long DirSize(string dir)
    {
        try { return new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); } catch { return 0; }
    }

    public static void Save(Wallpaper w) => File.WriteAllText(Path.Combine(w.Dir, ManifestName), JsonSerializer.Serialize(w, Json));

    public static void Delete(string id)
    {
        var w = Get(id);
        if (w == null || w.Builtin) return;
        try { Directory.Delete(w.Dir, true); } catch (Exception ex) { Paths.Log("не удалось удалить " + id + ": " + ex.Message); }
    }

    // ---------- встроенные обои ----------
    // Лежат внутри программы (ресурсы wp/<имя>/<файл>); при первом запуске и после обновления раскладываются в библиотеку.
    public static void EnsureBuiltins()
    {
        try
        {
            Paths.Ensure();
            var asm = Assembly.GetExecutingAssembly();
            var version = asm.GetName().Version?.ToString() ?? "0";
            var groups = asm.GetManifestResourceNames().Where(n => n.StartsWith("wp/", StringComparison.Ordinal))
                .Select(n => (Name: n, Rel: n[3..].Replace('\\', '/'))).Where(x => x.Rel.Contains('/'))
                .GroupBy(x => x.Rel[..x.Rel.IndexOf('/')]);
            foreach (var g in groups)
            {
                var id = "b-" + g.Key;
                var dir = Path.Combine(Paths.Library, id);
                var stamp = Path.Combine(dir, ".builtin-version");
                if (File.Exists(stamp) && File.ReadAllText(stamp) == version && File.Exists(Path.Combine(dir, ManifestName))) continue;
                Directory.CreateDirectory(dir);
                foreach (var (name, rel) in g)
                {
                    var file = rel[(rel.IndexOf('/') + 1)..];
                    var dest = Path.Combine(dir, file.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    using var src = asm.GetManifestResourceStream(name)!;
                    using var fs = File.Create(dest);
                    src.CopyTo(fs);
                }
                // в манифесте встроенных обоев id пишем по имени папки
                var mf = Path.Combine(dir, ManifestName);
                if (File.Exists(mf))
                {
                    var w = JsonSerializer.Deserialize<Wallpaper>(File.ReadAllText(mf));
                    if (w != null) { w.Id = id; w.Source = "встроенные"; File.WriteAllText(mf, JsonSerializer.Serialize(w, Json)); }
                }
                File.WriteAllText(stamp, version);
            }
        }
        catch (Exception ex) { Paths.Log("встроенные обои: " + ex.Message); }
    }

    // ---------- установка ----------
    // Добавить файл (видео, GIF, картинка, страница .html) или архив .zip. Возвращает установленные обои или текст, почему не вышло.
    public static (Wallpaper? Wallpaper, string Message) InstallPath(string path, string source = "из файла")
    {
        try
        {
            if (Directory.Exists(path)) return InstallFolder(path, source);
            if (!File.Exists(path)) return (null, "Файл не найден.");
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".zip") return InstallZip(path, source);
            if (ext is ".exe" or ".msi" or ".bat" or ".cmd" or ".ps1" or ".scr" or ".dll" or ".lnk" or ".7z" or ".rar" or ".pkg")
                return (null, ext is ".7z" or ".rar" or ".pkg"
                    ? $"Формат {ext} не поддерживается. Нужны видео (mp4, webm), GIF, картинка, страница (html) или архив zip."
                    : $"Файл {ext} — это программа, а не обои. Он не запускается и не добавляется.");
            var title = Path.GetFileNameWithoutExtension(path);
            if (VideoExt.Contains(ext)) return InstallSingle(path, "video", title, source);
            if (ext == ".gif") return InstallSingle(path, "gif", title, source);
            if (ImageExt.Contains(ext)) return InstallSingle(path, "image", title, source);
            if (ext is ".html" or ".htm") return InstallSingle(path, "html", title, source);
            return (null, $"Не знаю, как сделать обои из файла {ext}. Подходят видео (mp4, webm), GIF, картинки, страницы (html) и архивы zip.");
        }
        catch (Exception ex)
        {
            Paths.Log("установка " + path + ": " + ex);
            return (null, "Не получилось: " + ex.Message);
        }
    }

    static (Wallpaper?, string) InstallSingle(string file, string type, string title, string source)
    {
        var size = new FileInfo(file).Length;
        if (size > MaxFile) return (null, "Файл слишком большой (больше 700 МБ).");
        var w = NewWallpaper(title, type, source);
        Directory.CreateDirectory(w.Dir);
        var name = SafeName(Path.GetFileName(file));
        File.Copy(file, Path.Combine(w.Dir, name), true);
        if (type == "html") { w.Entry = name; }
        else { w.Entry = "_player.html"; File.WriteAllText(Path.Combine(w.Dir, w.Entry), PlayerHtml); w.Description = name; PlayerFile[w.Id] = name; File.WriteAllText(Path.Combine(w.Dir, "_file.txt"), name); }
        Save(w);
        return (Read(w.Dir), "Добавлено: " + w.Title);
    }

    // имя главного файла для обоев-видео/картинки (читается при запуске)
    public static readonly Dictionary<string, string> PlayerFile = new();
    public static string MediaFile(Wallpaper w)
    {
        if (PlayerFile.TryGetValue(w.Id, out var n)) return n;
        try { var f = Path.Combine(w.Dir, "_file.txt"); if (File.Exists(f)) return File.ReadAllText(f).Trim(); } catch { }
        return w.Description;
    }

    static (Wallpaper?, string) InstallFolder(string folder, string source)
    {
        var info = ReadLively(folder);
        string? entry = null, type = "html", title = Path.GetFileName(folder.TrimEnd('\\', '/')), desc = "", author = "";
        if (info != null)
        {
            title = info.Title ?? title; desc = info.Desc ?? ""; author = info.Author ?? "";
            if (info.Type is "video" or "gif" or "picture" or "web" or "webaudio") { type = info.Type switch { "video" => "video", "gif" => "gif", "picture" => "image", _ => "html" }; entry = info.FileName; }
            else if (info.Type is "app" or "bizhawk" or "unity" or "godot" or "unityaudio" or "videostream" or "url") return (null, "Это обои-программа или поток ('" + info.Type + "') — такие не поддерживаются.");
        }
        entry ??= File.Exists(Path.Combine(folder, "index.html")) ? "index.html" : Directory.GetFiles(folder, "*.html", SearchOption.TopDirectoryOnly).Select(Path.GetFileName).FirstOrDefault();
        if (entry == null)
        {
            var media = Directory.GetFiles(folder).FirstOrDefault(f => VideoExt.Contains(Path.GetExtension(f).ToLowerInvariant()) || Path.GetExtension(f).Equals(".gif", StringComparison.OrdinalIgnoreCase));
            if (media == null) return (null, "В папке нет страницы index.html и нет видео или GIF.");
            return InstallSingle(media, Path.GetExtension(media).Equals(".gif", StringComparison.OrdinalIgnoreCase) ? "gif" : "video", title, source);
        }
        entry = entry.Replace('\\', '/');
        var w = NewWallpaper(title, type, source);
        w.Description = desc; w.Author = author;
        Directory.CreateDirectory(w.Dir);
        var (copied, skipped, err) = CopyFolder(folder, w.Dir);
        if (err != null) { try { Directory.Delete(w.Dir, true); } catch { } return (null, err); }
        if (!File.Exists(Path.Combine(w.Dir, entry.Replace('/', Path.DirectorySeparatorChar)))) { try { Directory.Delete(w.Dir, true); } catch { } return (null, "Главный файл обоев «" + entry + "» не найден."); }
        if (type == "html") w.Entry = entry;
        else { w.Entry = "_player.html"; File.WriteAllText(Path.Combine(w.Dir, w.Entry), PlayerHtml); File.WriteAllText(Path.Combine(w.Dir, "_file.txt"), entry); PlayerFile[w.Id] = entry; }
        Save(w);
        return (Read(w.Dir), "Добавлено: " + w.Title + (skipped > 0 ? $" (пропущено файлов, которые не бывают частью обоев: {skipped})" : ""));
    }

    sealed class LivelyInfo
    {
        public string? Title { get; set; }
        public string? Desc { get; set; }
        public string? Author { get; set; }
        public string? FileName { get; set; }
        [JsonPropertyName("Type")] public JsonElement TypeRaw { get; set; }
        [JsonIgnore] public string Type => TypeName();
        string TypeName()
        {
            // у Lively тип — число (0 web, 1 webaudio, 2 url, 3 video, 4 gif, 5 videostream, 6 picture, 100 app…) или строка
            if (TypeRaw.ValueKind == JsonValueKind.Number && TypeRaw.TryGetInt32(out var n))
                return n switch { 0 => "web", 1 => "webaudio", 2 => "url", 3 => "video", 4 => "gif", 5 => "videostream", 6 => "picture", _ => "app" };
            var s = TypeRaw.ValueKind == JsonValueKind.String ? (TypeRaw.GetString() ?? "") : "";
            return s.ToLowerInvariant() switch { "web" => "web", "webaudio" => "webaudio", "url" => "url", "video" => "video", "gif" => "gif", "picture" => "picture", "videostream" => "videostream", "" => "", _ => "app" };
        }
    }

    static LivelyInfo? ReadLively(string folder)
    {
        try
        {
            var f = Path.Combine(folder, "LivelyInfo.json");
            if (!File.Exists(f)) return null;
            return JsonSerializer.Deserialize<LivelyInfo>(File.ReadAllText(f), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    static (int Copied, int Skipped, string? Error) CopyFolder(string from, string to)
    {
        int copied = 0, skipped = 0; long total = 0;
        foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(from, f);
            if (rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel)) { skipped++; continue; }
            if (!Allowed.Contains(Path.GetExtension(f))) { skipped++; continue; }
            var fi = new FileInfo(f);
            if (fi.Length > MaxFile) return (copied, skipped, "В обоях есть файл больше 700 МБ.");
            total += fi.Length;
            if (total > MaxTotal || copied >= MaxFiles) return (copied, skipped, "Обои слишком большие (больше 1,2 ГБ или 3000 файлов).");
            var dest = Path.Combine(to, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(f, dest, true);
            copied++;
        }
        return (copied, skipped, null);
    }

    static (Wallpaper?, string) InstallZip(string zip, string source)
    {
        var temp = Path.Combine(Paths.Downloads, "unzip-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(temp);
            long total = 0; var count = 0;
            using (var za = ZipFile.OpenRead(zip))
            {
                foreach (var e in za.Entries)
                {
                    if (e.FullName.EndsWith('/') || e.FullName.EndsWith('\\') || e.Name.Length == 0) continue;
                    if (++count > MaxFiles * 2) return (null, "В архиве слишком много файлов.");
                    if (!Allowed.Contains(Path.GetExtension(e.Name)) && !e.Name.Equals("LivelyInfo.json", StringComparison.OrdinalIgnoreCase)) continue;
                    if (e.Length > MaxFile) return (null, "В архиве есть файл больше 700 МБ.");
                    total += e.Length;
                    if (total > MaxTotal) return (null, "Архив слишком большой (после распаковки больше 1,2 ГБ).");
                    // защита от путей вида ..\..\ — файл всегда остаётся внутри временной папки
                    var dest = Path.GetFullPath(Path.Combine(temp, e.FullName.Replace('/', Path.DirectorySeparatorChar)));
                    if (!dest.StartsWith(Path.GetFullPath(temp) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    e.ExtractToFile(dest, true);
                }
            }
            // если всё лежит в одной вложенной папке — берём её
            var root = temp;
            for (var i = 0; i < 3; i++)
            {
                var dirs = Directory.GetDirectories(root);
                if (Directory.GetFiles(root).Length == 0 && dirs.Length == 1) root = dirs[0]; else break;
            }
            var res = InstallFolder(root, source);
            if (res.Item1 != null && string.IsNullOrWhiteSpace(res.Item1.Title)) { }
            if (res.Item1 != null && res.Item1.Title == Path.GetFileName(root)) { res.Item1.Title = Path.GetFileNameWithoutExtension(zip); Save(res.Item1); }
            return res;
        }
        catch (InvalidDataException) { return (null, "Архив повреждён или это не zip."); }
        finally { try { Directory.Delete(temp, true); } catch { } }
    }

    // ---------- вспомогательное ----------
    static Wallpaper NewWallpaper(string title, string type, string source)
    {
        title = title.Trim();
        if (title.Length == 0) title = "Обои";
        if (title.Length > 60) title = title[..60];
        var slug = Regex.Replace(title.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (slug.Length > 20) slug = slug[..20].Trim('-');
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(title + DateTime.UtcNow.Ticks + Guid.NewGuid()))).ToLowerInvariant()[..6];
        var id = (slug.Length > 0 ? "u-" + slug + "-" : "u-") + hash;
        return new Wallpaper { Id = id, Title = title, Type = type, Source = source };
    }

    static string SafeName(string name)
    {
        var s = Regex.Replace(name, @"[^\p{L}\p{N}._ -]", "_").Trim();
        return s.Length > 0 ? s : "file";
    }

    // Страница-проигрыватель для видео, GIF и картинок: параметры (файл, масштаб, звук) приходят в адресе.
    public const string PlayerHtml = @"<!doctype html><html><head><meta charset=""utf-8""><title>Wallpaper</title>
<style>html,body{margin:0;height:100%;background:#000;overflow:hidden}video,img{position:fixed;left:0;top:0;width:100vw;height:100vh;object-fit:var(--fit,cover)}</style></head><body>
<script>
const q=new URLSearchParams(location.search);const f=q.get('f')||'';const t=q.get('t')||'video';
document.documentElement.style.setProperty('--fit',q.get('fit')==='fill'?'fill':q.get('fit')==='contain'?'contain':'cover');
let el;
if(t==='video'){el=document.createElement('video');el.loop=true;el.autoplay=true;el.muted=q.get('muted')!=='0';el.playsInline=true;
 el.addEventListener('canplay',()=>el.play().catch(()=>{}));el.src=f;}
else{el=document.createElement('img');el.src=f;}
document.body.appendChild(el);
</script></body></html>";
}
