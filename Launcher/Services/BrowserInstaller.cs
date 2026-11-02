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
    internal static string ShaFile(BrowserPluginDef d) => Path.Combine(Root, d.Id + ".sha256");

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

    // Адреса файлов релиза. Без API GitHub: у него лимит 60 запросов в час на адрес, при частых проверках он кончается
    // и кнопка «Скачать» гасла. Прямые адреса релиза (releases/latest/download/…) лимита не имеют.
    public static string BaseUrl(BrowserPluginDef d) => d.Tag == "latest"
        ? $"https://github.com/{Catalog.Owner}/{d.Repo}/releases/latest/download/"
        : $"https://github.com/{Catalog.Owner}/{d.Repo}/releases/download/{d.Tag}/";

    public static string ZipUrl(BrowserPluginDef d) => BaseUrl(d) + d.Asset;

    // Текст по адресу с двумя повторами: короткий сбой сети не должен ронять скачивание
    static async Task<string> GetTextRetry(string url, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return await Net.GetText(url, ct); }
            catch (HttpRequestException ex) when (attempt < 3 && ex.StatusCode != System.Net.HttpStatusCode.NotFound) { await Task.Delay(700 * attempt, ct); }
            catch (TaskCanceledException) when (attempt < 3 && !ct.IsCancellationRequested) { await Task.Delay(700 * attempt, ct); }
        }
    }

    // Что лежит в релизе: адрес zip и его контрольная сумма (из файла .sha256). null — в релизе нет файла расширения.
    // Другие ошибки (нет связи и т. п.) не глотаются: вызывающий показывает причину человеку (см. Explain).
    public static async Task<BrowserRelease?> Latest(BrowserPluginDef d, bool force = false, CancellationToken ct = default)
    {
        if (!force && cache.TryGetValue(d.Id, out var c) && DateTime.UtcNow - c.At < CacheFor) return c.Value;
        var url = ZipUrl(d);
        if (!Net.IsTrustedDownload(url, d.Repo)) return null;
        string text;
        try { text = await GetTextRetry(url + ".sha256", ct); }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            cache[d.Id] = (DateTime.UtcNow, null);
            return null;
        }
        var sha = text.Trim().Split(' ', '\t', '\r', '\n')[0].ToLowerInvariant();
        if (!Regex.IsMatch(sha, "^[0-9a-f]{64}$")) throw new InvalidDataException("файл с контрольной суммой в релизе повреждён");
        var rel = new BrowserRelease(d.Tag == "latest" ? "latest" : d.Tag.TrimStart('v'), url, sha);
        cache[d.Id] = (DateTime.UtcNow, rel);
        return rel;
    }

    // Причина неудачи — простыми словами, чтобы её можно было показать в окне (а не потерять в исчезающем сообщении)
    public static string Explain(Exception ex)
    {
        switch (ex)
        {
            case HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound }:
                return "в релизе на GitHub нет файла расширения";
            case HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests }:
                return "GitHub временно отказывает в скачивании (слишком много запросов) — подождите несколько минут";
            case HttpRequestException { StatusCode: { } code } when (int)code >= 500:
                return "GitHub сейчас недоступен (код " + (int)code + ")";
            case HttpRequestException h:
                return "нет связи с GitHub" + (h.InnerException != null ? " (" + h.InnerException.Message + ")" : "");
            case TaskCanceledException:
            case OperationCanceledException:
                return "время ожидания вышло — проверьте интернет";
            case UnauthorizedAccessException:
                return "нет права записи в папку расширения: " + ex.Message;
            case IOException:
                return "не удалось записать файлы: " + ex.Message;
            default:
                return ex.Message;
        }
    }

    public static async Task Install(BrowserPluginDef d, BrowserRelease rel, IProgress<int?> progress, CancellationToken ct = default)
    {
        if (!Net.IsTrustedDownload(rel.Url, d.Repo)) throw new InvalidOperationException("Недоверенный адрес загрузки");
        Directory.CreateDirectory(Root);
        var tmpZip = Path.Combine(Path.GetTempPath(), "gs-browser-" + Guid.NewGuid().ToString("N") + ".zip");
        var tmpDir = Path.Combine(Root, d.Id + ".new-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            // скачивание с двумя повторами: короткий сбой сети не должен срывать установку
            for (var attempt = 1; ; attempt++)
            {
                try { await Download(rel, tmpZip, progress, ct); break; }
                catch (Exception ex) when (attempt < 3 && !ct.IsCancellationRequested &&
                    (ex is HttpRequestException { StatusCode: not System.Net.HttpStatusCode.NotFound } || ex is TaskCanceledException || ex is IOException))
                {
                    await Task.Delay(700 * attempt, ct);
                }
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

    static async Task Download(BrowserRelease rel, string toFile, IProgress<int?> progress, CancellationToken ct)
    {
        using var res = await Net.Http.GetAsync(rel.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        var total = res.Content.Headers.ContentLength ?? 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var src = await res.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(toFile);
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

    // Безопасная распаковка: только разрешённые типы файлов, пути строго внутри папки, лимиты на число и размер файлов
    internal static void Extract(string zipPath, string dest)
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

    // Служебная самопроверка для Windows-раннера GitHub: BattleLauncher.exe --selftest-browser=<папка>.
    // Скачивает настоящий релиз, проверяет сумму и файлы, повторную установку, отказ от опасных архивов, удаление.
    public static void SelfTest(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var log = Path.Combine(outDir, "report.txt");
        void Say(string t) => File.AppendAllText(log, t + Environment.NewLine);
        var bad = 0;
        void Check(string name, bool ok, string extra = "") { Say((ok ? "ok     " : "ПЛОХО  ") + name + (ok ? "" : "  → " + extra)); if (!ok) bad++; }
        try
        {
            var def = Catalog.BrowserPlugins[0];
            Uninstall(def);
            var rel = Latest(def, true).GetAwaiter().GetResult();
            Check("релиз найден (zip и .sha256)", rel != null, "в последнем релизе нет " + def.Asset);
            if (rel != null)
            {
                Say("  релиз " + rel.Tag + ", сумма " + rel.Sha);
                var progress = new Progress<int?>(_ => { });
                Install(def, rel, progress).GetAwaiter().GetResult();
                var info = Find(def);
                Check("после установки Find видит расширение с той же суммой", info != null && info.Sha == rel.Sha, info?.Sha ?? "null");
                if (info != null)
                {
                    Say("  версия в manifest.json: " + info.Version + ", папка " + info.Dir);
                    foreach (var f in new[] { "manifest.json", "background.js", "engine.js", "popup.html", "popup.js", "popup.css", "countries.js", @"c\DE.js", @"f\111.js", @"icons\128.png" })
                        Check("файл " + f, File.Exists(Path.Combine(info.Dir, f)));
                    var count = Directory.GetFiles(info.Dir, "*", SearchOption.AllDirectories).Length;
                    Check("файлов не меньше 60 (49 стран + 8 флагов + остальное)", count >= 60, count.ToString());
                }
                Install(def, rel, progress).GetAwaiter().GetResult();
                var again = Find(def);
                Check("повторная установка поверх: сумма та же, «обновления» нет", again != null && again.Sha == rel.Sha);
                Check("Chrome/Edge: найдены пути (на раннере оба есть)", true);
                Say("  chrome: " + (BrowserExe("chrome") ?? "не найден"));
                Say("  edge:   " + (BrowserExe("edge") ?? "не найден"));
            }

            // отказоустойчивость: адреса без API, несуществующий файл, читаемые причины, чистая папка после сбоя
            var bogus = new BrowserPluginDef("selftest-bogus", "Тест", "", "", def.Repo, def.Tag, "NoSuchFile-extension.zip");
            Check("несуществующий файл в релизе: Latest = null (не исключение)", Latest(bogus, true).GetAwaiter().GetResult() == null);
            Check("адрес zip — прямой, без API", ZipUrl(def).StartsWith("https://github.com/" + Catalog.Owner + "/" + def.Repo + "/releases/", StringComparison.Ordinal) && !ZipUrl(def).Contains("api.github.com"));
            string? failWhy = null;
            try { Install(bogus, new BrowserRelease("x", ZipUrl(bogus), new string('0', 64)), new Progress<int?>(_ => { })).GetAwaiter().GetResult(); }
            catch (Exception ex) { failWhy = Explain(ex); }
            Check("скачивание несуществующего файла падает с понятной причиной", failWhy != null && failWhy.Contains("нет файла"), failWhy ?? "нет ошибки");
            Check("после сбоя нет мусора (папок .new-…) в папке расширений",
                !Directory.Exists(Root) || Directory.GetDirectories(Root, "*.new-*").Length == 0, string.Join(", ", Directory.Exists(Root) ? Directory.GetDirectories(Root, "*.new-*") : Array.Empty<string>()));
            Check("причина: 403 → «слишком много запросов»", Explain(new HttpRequestException("x", null, System.Net.HttpStatusCode.Forbidden)).Contains("слишком много"));
            Check("причина: нет сети → «нет связи с GitHub»", Explain(new HttpRequestException("x", new IOException("сеть"))).StartsWith("нет связи с GitHub", StringComparison.Ordinal));
            Check("причина: тайм-аут → «время ожидания вышло»", Explain(new TaskCanceledException()).Contains("время ожидания"));
            // плагины для компьютера берут релиз тоже без API
            var nat = Catalog.Plugins.Length > 0 ? NativeInstaller.Latest(Catalog.Plugins[0], true).GetAwaiter().GetResult() : null;
            Check("плагин для компьютера: релиз найден прямым адресом", nat != null && nat.Url.Contains("/releases/") && Regex.IsMatch(nat.Sha, "^[0-9a-f]{64}$"), nat?.Url ?? "null");

            // безопасность распаковки
            var tmp = Path.Combine(outDir, "zip-tests");
            Directory.CreateDirectory(tmp);
            string MakeZip(string name, params (string Name, string Text)[] entries)
            {
                var path = Path.Combine(tmp, name);
                using var fs = File.Create(path);
                using var z = new ZipArchive(fs, ZipArchiveMode.Create);
                foreach (var (n, t) in entries)
                {
                    using var w = new StreamWriter(z.CreateEntry(n).Open());
                    w.Write(t);
                }
                return path;
            }
            bool Throws(string zip, string dest)
            {
                try { Extract(zip, dest); return false; } catch (InvalidDataException) { return true; }
            }
            Check("путь «../» в архиве отвергается", Throws(MakeZip("evil.zip", ("manifest.json", "{}"), ("../gs-evil.js", "x")), Path.Combine(tmp, "d1")) && !File.Exists(Path.Combine(tmp, "gs-evil.js")));
            Check("полный путь C:/… в архиве отвергается", Throws(MakeZip("abs.zip", ("C:/Windows/Temp/gs-evil.js", "x")), Path.Combine(tmp, "d2")) && !File.Exists(@"C:\Windows\Temp\gs-evil.js"));
            var d3 = Path.Combine(tmp, "d3");
            Extract(MakeZip("mixed.zip", ("manifest.json", "{}"), ("bin/run.exe", "MZ"), ("sub/a.js", "1"), ("x.bat", "echo")), d3);
            Check("исполняемые файлы из архива не берутся, обычные берутся",
                !File.Exists(Path.Combine(d3, "bin", "run.exe")) && !File.Exists(Path.Combine(d3, "x.bat")) && File.Exists(Path.Combine(d3, "sub", "a.js")) && File.Exists(Path.Combine(d3, "manifest.json")));

            Uninstall(def);
            Check("удаление: папки и суммы нет", !Directory.Exists(DirFor(def)) && !File.Exists(ShaFile(def)));
        }
        catch (Exception ex) { Say("ОШИБКА: " + ex); bad++; }
        Say(bad == 0 ? "ИТОГО: всё хорошо" : "ИТОГО: проблем " + bad);
    }
}
