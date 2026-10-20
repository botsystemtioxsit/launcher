using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BattleLauncher;

record Release(string Version, string File, string Sha512, string Url);

// Последняя версия приложения — из релизов GitHub (их публикует electron-builder вместе с
// latest.yml: версия, имя файла и sha512 установщика).
static class Releases
{
    static readonly Dictionary<string, (DateTime At, Release Value)> Cache = new();
    static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5); // без токена у GitHub лимит 60 запросов в час

    public static (string Version, string File, string Sha512) ParseLatestYml(string text)
    {
        string? Pick(string key)
        {
            var m = Regex.Match(text, $@"^{key}:\s*(.+?)\s*$", RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value.Trim('\'', '"') : null;
        }
        var v = Pick("version"); var f = Pick("path"); var h = Pick("sha512");
        if (v == null || f == null || h == null || !Regex.IsMatch(v, @"^\d+\.\d+\.\d+")) throw new InvalidDataException("latest.yml повреждён");
        return (v, f, h);
    }

    static string Base(string repo) => $"https://github.com/{Catalog.Owner}/{repo}/releases";

    // Берём не просто «latest»: в репозитории бывают релизы без установщика (например, APK),
    // поэтому ищем самый свежий, где есть latest.yml.
    static async Task<Release> ViaApi(string repo, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await Net.GetText($"https://api.github.com/repos/{Catalog.Owner}/{repo}/releases?per_page=10", ct));
        foreach (var rel in doc.RootElement.EnumerateArray())
        {
            if (rel.GetProperty("draft").GetBoolean() || rel.GetProperty("prerelease").GetBoolean()) continue;
            var assets = rel.GetProperty("assets").EnumerateArray().Select(a => (Name: a.GetProperty("name").GetString() ?? "", Url: a.GetProperty("browser_download_url").GetString() ?? "")).ToList();
            var yml = assets.FirstOrDefault(a => a.Name == "latest.yml");
            if (string.IsNullOrEmpty(yml.Url)) continue;
            var info = ParseLatestYml(await Net.GetText(yml.Url, ct));
            // GitHub переименовывает файлы с пробелами и не-ASCII, поэтому ищем по имени из yml,
            // а запасным вариантом берём единственный .exe
            var exes = assets.Where(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToList();
            var hit = exes.FirstOrDefault(a => a.Name == info.File);
            if (string.IsNullOrEmpty(hit.Url) && exes.Count == 1) hit = exes[0];
            if (string.IsNullOrEmpty(hit.Url)) continue;
            return new Release(info.Version, info.File, info.Sha512, hit.Url);
        }
        throw new InvalidOperationException("В релизах нет установщика");
    }

    // Запасной путь без API (и без его лимита): ссылка «latest/download».
    static async Task<Release> Direct(string repo, CancellationToken ct)
    {
        var info = ParseLatestYml(await Net.GetText($"{Base(repo)}/latest/download/latest.yml", ct));
        return new Release(info.Version, info.File, info.Sha512, $"{Base(repo)}/latest/download/{Uri.EscapeDataString(info.File)}");
    }

    public static async Task<Release> Latest(string repo, bool force = false, CancellationToken ct = default)
    {
        if (!force && Cache.TryGetValue(repo, out var hit) && DateTime.UtcNow - hit.At < CacheFor) return hit.Value;
        Release value;
        try { value = await ViaApi(repo, ct); }
        catch { value = await Direct(repo, ct); }
        Cache[repo] = (DateTime.UtcNow, value);
        return value;
    }
}
