using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WallpaperPlusApp;

sealed class CatalogItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Type { get; set; } = "html";
    public List<string> Files { get; set; } = new();
    public long SizeKb { get; set; }
    public string LibraryId => "c-" + Id;
}

// Каталог обоев для скачивания: список лежит в репозитории проекта, поэтому новые обои появляются без нового выпуска плагина.
// Скачивается только то, что перечислено в списке, и только файлы, которые бывают частью обоев (страницы, картинки, видео, шрифты); программ среди них нет.
static class Catalog
{
    const string BaseUrl = "https://raw.githubusercontent.com/botsystemtioxsit/launcher/main/Plugins/WallpaperPlus/catalog/";
    static readonly Regex IdRe = new("^[a-z0-9-]{1,40}$");
    static readonly Regex FileRe = new(@"^[A-Za-z0-9_\-./]{1,120}$");
    static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase)
        { ".json", ".html", ".js", ".css", ".png", ".jpg", ".jpeg", ".webp", ".gif", ".svg", ".mp4", ".webm", ".woff", ".woff2", ".ttf", ".otf", ".txt" };
    const long MaxFile = 60_000_000;

    static HttpClient NewClient() => new() { Timeout = TimeSpan.FromSeconds(40) };

    public static async Task<List<CatalogItem>> FetchAsync()
    {
        using var http = NewClient();
        var text = await http.GetStringAsync(BaseUrl + "catalog.json?_=" + DateTime.UtcNow.Ticks);
        using var doc = JsonDocument.Parse(text);
        var list = new List<CatalogItem>();
        if (!doc.RootElement.TryGetProperty("items", out var items)) return list;
        foreach (var it in items.EnumerateArray())
        {
            var item = JsonSerializer.Deserialize<CatalogItem>(it.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (item == null || !IdRe.IsMatch(item.Id) || item.Files.Count == 0 || item.Files.Count > 60) continue;
            if (item.Files.Any(f => !FileRe.IsMatch(f) || f.Contains("..") || f.StartsWith('/') || !AllowedExt.Contains(Path.GetExtension(f)))) continue;
            list.Add(item);
        }
        return list;
    }

    public static bool Installed(CatalogItem item) => Library.Get(item.LibraryId) != null;

    public static async Task<(Wallpaper? Wallpaper, string Message)> InstallAsync(CatalogItem item, Action<string>? progress = null)
    {
        var temp = Path.Combine(Paths.Downloads, "catalog-" + item.Id + "-" + Guid.NewGuid().ToString("N")[..6]);
        try
        {
            Directory.CreateDirectory(temp);
            using var http = NewClient();
            var n = 0;
            foreach (var f in item.Files)
            {
                progress?.Invoke($"Скачиваю «{item.Title}»: {++n} из {item.Files.Count}…");
                using var resp = await http.GetAsync(BaseUrl + item.Id + "/" + f, HttpCompletionOption.ResponseHeadersRead);
                if (!resp.IsSuccessStatusCode) return (null, $"Файл {f} не скачался (ответ {(int)resp.StatusCode}).");
                if (resp.Content.Headers.ContentLength > MaxFile) return (null, $"Файл {f} слишком большой.");
                var dest = Path.GetFullPath(Path.Combine(temp, f.Replace('/', Path.DirectorySeparatorChar)));
                if (!dest.StartsWith(Path.GetFullPath(temp) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return (null, "Недопустимый путь файла в каталоге.");
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                await using var fs = File.Create(dest);
                await resp.Content.CopyToAsync(fs);
            }
            return Library.InstallDownloaded(temp, item.LibraryId);
        }
        catch (Exception ex) { Paths.Log("каталог: " + ex); return (null, "Не получилось: " + ex.Message); }
        finally { try { Directory.Delete(temp, true); } catch { } }
    }
}
