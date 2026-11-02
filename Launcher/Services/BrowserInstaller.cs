using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BattleLauncher;

record BrowserInfo(string Version, string Dir, string? Sha);
record BrowserRelease(string Tag, string Url, string Sha);

// Расширения для браузера. Лаунчер только скачивает zip из релиза (проверяет контрольную сумму), безопасно распаковывает
// в %LOCALAPPDATA%\GhostShell\BrowserPlugins\<id> и открывает страницу расширений браузера. Подключает папку сам человек
// («Загрузить распакованное расширение») — установить расширение в браузер снаружи Windows не даёт, и мы это не обходим.
static class BrowserInstaller
{
    static readonly Dictionary<string, (DateTime At, BrowserRelease? Value)> cache = new();
    static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);
    static readonly Regex VersionRe = new(@"^[0-9][0-9A-Za-z.\-]{0,19}$", RegexOptions.Compiled);
    // Из архива берутся только такие файлы: расширение — это страницы, скрипты и картинки, ничего исполняемого
    static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase)
        { ".js", ".json", ".html", ".css", ".png", ".jpg", ".jpeg", ".svg", ".ico", ".txt", ".md", ".woff", ".woff2" };
    const int MaxEntries = 3000;
    const long MaxFile = 20L * 1024 * 1024, MaxTotal = 80L * 1024 * 1024;

    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GhostShell", "BrowserPlugins");
    public static string DirFor(BrowserPluginDef d) => Path.Combine(Root, d.Id);
    static string ShaFile(BrowserPluginDef d) => Path.Combine(Root, d.Id + ".sha256");

    // Что лежит на компьютере сейчас (null — не скачано)
    public static BrowserInfo? Find(BrowserPluginDef d)
    {
        try
        {
            var dir = DirFor(d);
            var manifest = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifest)) return null;
            string version = "?";
            using (var doc = JsonDocument.Parse(File.ReadAllText(manifest)))
                if (doc.RootElement.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { } vs && VersionRe.IsMatch(vs)) version = vs;
            string? sha = null;
            try { if (File.Exists(ShaFile(d))) sha = File.ReadAllText(ShaFile(d)).Trim().ToLowerInvariant(); } catch { }
            return new BrowserInfo(version, dir, sha);
        }
        catch { return null; }
    }

    // Последний релиз: в нём лежат zip и его контрольная сумма
    public static async Task<BrowserRelease?> Latest(BrowserPluginDef d, bool force = false, CancellationToken ct = default)
    {
        if (!force && cache.TryGetValue(d.Id, out var c) && DateTime.UtcNow - c.At < CacheFor) return c.Value;
        var which = d.Tag == "latest" ? "latest" : "tags/" + d.Tag;
        string text;
        try { text = await Net.GetText($"https://api.github.com/repos/{Catalog.Owner}/{d.Repo}/releases/{which}", ct); }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            cache[d.Id] = (DateTime.UtcNow, null);
            return null;
        }
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        var tag = (root.GetProperty("tag_name").GetString() ?? "").TrimStart('v');
        string? zipUrl = null, shaUrl = null;
        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString();
            var url = a.GetProperty("browser_download_url").GetString();
            if (name == d.Asset) zipUrl = url;
            else if (name == d.Asset + ".sha256") shaUrl = url;
        }
        BrowserRelease? rel = null;
        if (zipUrl != null && shaUrl != null && Net.IsTrustedDownload(zipUrl, d.Repo))
        {
            var sha = (await Net.GetText(shaUrl, ct)).Trim().Split(' ', '\t')[0].ToLowerInvariant();
            if (Regex.IsMatch(sha, "^[0-9a-f]{64}$")) rel = new BrowserRelease(tag, zipUrl, sha);
        }
        cache[d.Id] = (DateTime.UtcNow, rel);
        return rel;
    }

    public static async Task Install(BrowserPluginDef d, BrowserRelease rel, IProgress<int?> progress, CancellationToken ct = default)
    {
        if (!Net.IsTrustedDownload(rel.Url, d.Repo)) throw new InvalidOperationException("Недоверенный адрес загрузки");
        Directory.CreateDirectory(Root);
        var tmpZip = Path.Combine(Path.GetTempPath(), "gs-browser-" + Guid.NewGuid().ToString("N") + ".zip");
        var tmpDir = Path.Combine(Root, d.Id + ".new-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            using (var res = await Net.Http.GetAsync(rel.Url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                res.EnsureSuccessStatusCode();
                var total = res.Content.Headers.ContentLength ?? 0;
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using var src = await res.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(tmpZip);
                var buf = new byte[81920];
                long got = 0; int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    hash.AppendData(buf, 0, n);
                    got += n;
                    if (got > MaxTotal) throw new InvalidDataException("Архив слишком большой");
                    if (total > 0) progress.Report((int)Math.Min(99, got * 100 / total));
                }
                if (Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() != rel.Sha)
                    throw new InvalidDataException("Контрольная сумма не совпала — файл повреждён");
            }
            progress.Report(null);
            await Task.Run(() =>
            {
                Directory.CreateDirectory(tmpDir);
                Extract(tmpZip, tmpDir);
                if (!File.Exists(Path.Combine(tmpDir, "manifest.json"))) throw new InvalidDataException("В архиве нет manifest.json — это не расширение");
                var dir = DirFor(d);
                try
                {
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);
                    Directory.Move(tmpDir, dir);
                }
                catch
                {
                    // папку что-то держит (например, проводник открыт на ней) — кладём новые файлы поверх
                    CopyTree(tmpDir, dir);
                    try { Directory.Delete(tmpDir, true); } catch { }
                }
                File.WriteAllText(ShaFile(d), rel.Sha);
            }, ct);
        }
        finally
        {
            try { File.Delete(tmpZip); } catch { }
            try { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true); } catch { }
        }
    }

    // Безопасная распаковка: только разрешённые типы файлов, пути строго внутри папки, лимиты на число и размер файлов
    static void Extract(string zipPath, string dest)
    {
        var full = Path.GetFullPath(dest).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(zipPath);
        if (zip.Entries.Count > MaxEntries) throw new InvalidDataException("В архиве слишком много файлов");
        long total = 0;
        foreach (var e in zip.Entries)
        {
            if (string.IsNullOrEmpty(e.Name)) continue;                   // это папка
            if (e.Length > MaxFile || (total += e.Length) > MaxTotal) throw new InvalidDataException("Файлы в архиве слишком большие");
            if (!AllowedExt.Contains(Path.GetExtension(e.Name))) continue; // всё остальное не берём
            var target = Path.GetFullPath(Path.Combine(dest, e.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(full, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Недопустимый путь в архиве");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            e.ExtractToFile(target, true);
        }
    }

    static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
        foreach (var sub in Directory.GetDirectories(from)) CopyTree(sub, Path.Combine(to, Path.GetFileName(sub)));
    }

    public static void Uninstall(BrowserPluginDef d)
    {
        try { Directory.Delete(DirFor(d), true); } catch (DirectoryNotFoundException) { }
        try { File.Delete(ShaFile(d)); } catch { }
    }

    // ---------- браузеры ----------
    public static string? BrowserExe(string kind)
    {
        var exe = kind == "edge" ? "msedge.exe" : "chrome.exe";
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var k = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe);
                if (k?.GetValue(null) is string p && File.Exists(p)) return p;
            }
            catch { }
        }
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var guess = kind == "edge"
            ? new[] { Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"), Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe") }
            : new[] { Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"), Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"), Path.Combine(local, @"Google\Chrome\Application\chrome.exe") };
        return guess.FirstOrDefault(File.Exists);
    }

    // Открывает страницу расширений браузера (chrome://extensions или edge://extensions)
    public static void OpenExtensionsPage(string kind)
    {
        var exe = BrowserExe(kind) ?? throw new InvalidOperationException(kind == "edge" ? "Edge не найден на этом компьютере" : "Chrome не найден на этом компьютере");
        Process.Start(new ProcessStartInfo(exe, kind == "edge" ? "edge://extensions/" : "chrome://extensions/") { UseShellExecute = false });
    }

    public static void OpenFolder(BrowserPluginDef d)
    {
        var dir = DirFor(d);
        if (!Directory.Exists(dir)) throw new DirectoryNotFoundException("Папки расширения нет — скачайте его заново");
        Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
    }
}
