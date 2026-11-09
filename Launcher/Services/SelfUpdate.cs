using System.Net.Http;
using System.Net.Http.Headers;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BattleLauncher;

record UpdateInfo(string Version, string ExeUrl, string Sha256);

// Лаунчер — один .exe. Новая версия берётся из последнего релиза репозитория launcher
// (BattleLauncher.exe + BattleLauncher.exe.sha256). Работающий .exe нельзя перезаписать, но можно
// переименовать: старый уходит в .old, новый встаёт на его место, запускаем и выходим.
static class SelfUpdate
{
    const string Repo = "launcher";
    const string ExeName = "BattleLauncher.exe";

    // Только для служебной проверки (--selftest-selfupdate): притвориться другой версией.
    internal static string? VersionOverride;

    public static string CurrentVersion
    {
        get
        {
            if (VersionOverride != null) return VersionOverride;
            var v = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            var plus = v.IndexOf('+');
            return plus > 0 ? v[..plus] : v;
        }
    }

    static string? ExePath => Environment.ProcessPath;

    public static void CleanupLeftovers()
    {
        try
        {
            var exe = ExePath;
            if (exe == null) return;
            foreach (var f in new[] { exe + ".old", exe + ".new" }) if (File.Exists(f)) File.Delete(f);
        }
        catch { /* старый процесс мог ещё не закрыться — уберём в следующий раз */ }
    }

    // Проверка идёт ПРЯМЫМИ адресами github.com — у них нет лимита; у API GitHub лимит 60 запросов в час на адрес
    // (его легко выбрать, когда с одного адреса ставят много обновлений), а раньше любая ошибка молча ждала ещё час.
    // API остался запасным путём.
    public static async Task<UpdateInfo?> Check(CancellationToken ct = default)
    {
        if (Packaged.IsPackaged) return null; // обновления из Store приходят через Store
        try { return await CheckDirect(ct); }
        catch (Exception direct)
        {
            try { return await CheckViaApi(ct); }
            catch { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(direct).Throw(); throw; }
        }
    }

    static readonly Regex TagRe = new(@"^/[^/]+/[^/]+/releases/tag/(v?\d+\.\d+\.\d+[0-9A-Za-z._-]{0,20})$", RegexOptions.Compiled);

    // «…/releases/tag/v1.50.0» → «v1.50.0»; всё остальное (в том числе попытки выйти из папки) — null
    internal static string? TagFromPath(string path)
    {
        var m = TagRe.Match(path);
        return m.Success && !m.Groups[1].Value.Contains("..") ? m.Groups[1].Value : null;
    }

    static async Task<string> LatestTagDirect(CancellationToken ct)
    {
        // «releases/latest» отвечает переадресацией на «releases/tag/<тег>»: тег берём из неё, саму переадресацию не выполняем
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("battle-launcher", "1"));
        using var res = await c.GetAsync($"https://github.com/{Catalog.Owner}/{Repo}/releases/latest", HttpCompletionOption.ResponseHeadersRead, ct);
        var loc = res.Headers.Location;
        if ((int)res.StatusCode is < 300 or >= 400 || loc == null)
            throw new HttpRequestException("GitHub не показал последний выпуск лаунчера", null, res.StatusCode);
        var abs = loc.IsAbsoluteUri ? loc : new Uri(new Uri("https://github.com"), loc);
        if (abs.Host != "github.com" || !abs.AbsolutePath.StartsWith($"/{Catalog.Owner}/{Repo}/releases/", StringComparison.Ordinal))
            throw new InvalidDataException("GitHub направил не в наш репозиторий");
        return TagFromPath(abs.AbsolutePath) ?? throw new InvalidDataException("не разобрать номер последнего выпуска");
    }

    static string CleanSha(string text)
    {
        var sha = text.Trim().Split(' ', '\t', '\r', '\n')[0].ToLowerInvariant();
        if (!Regex.IsMatch(sha, "^[0-9a-f]{64}$")) throw new InvalidDataException("файл с контрольной суммой лаунчера повреждён");
        return sha;
    }

    internal static async Task<UpdateInfo?> CheckDirect(CancellationToken ct = default)
    {
        var tag = await LatestTagDirect(ct);
        var version = tag.TrimStart('v');
        if (Net.CompareVersions(version, CurrentVersion) <= 0) return null;
        var exeUrl = $"https://github.com/{Catalog.Owner}/{Repo}/releases/download/{tag}/{ExeName}";
        if (!Net.IsTrustedDownload(exeUrl, Repo)) return null;
        var sha = CleanSha(await Net.GetText(exeUrl + ".sha256", ct));
        return new UpdateInfo(version, exeUrl, sha);
    }

    internal static async Task<UpdateInfo?> CheckViaApi(CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(await Net.GetText($"https://api.github.com/repos/{Catalog.Owner}/{Repo}/releases/latest", ct));
        var root = doc.RootElement;
        var tag = (root.GetProperty("tag_name").GetString() ?? "").TrimStart('v');
        if (Net.CompareVersions(tag, CurrentVersion) <= 0) return null;
        string? exeUrl = null, shaUrl = null;
        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString();
            var url = a.GetProperty("browser_download_url").GetString();
            if (name == ExeName) exeUrl = url;
            else if (name == ExeName + ".sha256") shaUrl = url;
        }
        if (exeUrl == null || shaUrl == null || !Net.IsTrustedDownload(exeUrl, Repo)) return null;
        return new UpdateInfo(tag, exeUrl, CleanSha(await Net.GetText(shaUrl, ct)));
    }

    // Служебная проверка (--selftest-selfupdate=<папка>) на настоящем релизе: прямой путь находит версию, адрес и сумму,
    // «не новее» не предлагается, разбор тега не пускает посторонних, запасной путь через API даёт то же самое.
    public static void SelfTest(string dir)
    {
        Directory.CreateDirectory(dir);
        var lines = new List<string>();
        var ok = true;
        void Expect(string name, bool cond, string extra = "")
        {
            lines.Add((cond ? "ok   " : "НЕ ПРОШЛО ") + name + (extra.Length > 0 ? " — " + extra : ""));
            if (!cond) ok = false;
        }
        try
        {
            Expect("разбор тега: обычный", TagFromPath("/botsystemtioxsit/launcher/releases/tag/v1.50.0") == "v1.50.0");
            Expect("разбор тега: без «v»", TagFromPath("/a/b/releases/tag/1.2.3") == "1.2.3");
            Expect("разбор тега: страница релизов без тега", TagFromPath("/a/b/releases") == null);
            Expect("разбор тега: выход из папки", TagFromPath("/a/b/releases/tag/v1.0.0/../../x") == null && TagFromPath("/a/b/releases/tag/v1.0.0..") == null);
            Expect("разбор тега: посторонние знаки", TagFromPath("/a/b/releases/tag/v1.0.0;rm") == null && TagFromPath("/a/b/releases/tag/latest") == null);

            VersionOverride = "0.0.1";
            var direct = Task.Run(() => CheckDirect()).GetAwaiter().GetResult();
            Expect("прямой путь: версия найдена", direct != null && Regex.IsMatch(direct.Version, @"^\d+\.\d+\.\d+"), direct?.Version ?? "null");
            if (direct != null)
            {
                Expect("прямой путь: адрес из нашего репозитория", Net.IsTrustedDownload(direct.ExeUrl, Repo) && direct.ExeUrl.EndsWith("/" + ExeName), direct.ExeUrl);
                Expect("прямой путь: сумма — 64 hex", Regex.IsMatch(direct.Sha256, "^[0-9a-f]{64}$"));
                using var head = Task.Run(() => Net.Http.SendAsync(new HttpRequestMessage(HttpMethod.Head, direct.ExeUrl))).GetAwaiter().GetResult();
                Expect("прямой путь: файл скачивается (HEAD 200, больше 1 МБ)", head.IsSuccessStatusCode && (head.Content.Headers.ContentLength ?? 0) > 1_000_000,
                    $"{(int)head.StatusCode}, {(head.Content.Headers.ContentLength ?? 0) / 1_000_000} МБ");
                VersionOverride = direct.Version;
                Expect("прямой путь: «такая же версия» не предлагается", Task.Run(() => CheckDirect()).GetAwaiter().GetResult() == null);
                VersionOverride = "999.0.0";
                Expect("общая проверка: «новее нет» не предлагается", Task.Run(() => SelfUpdate.Check()).GetAwaiter().GetResult() == null);
                VersionOverride = "0.0.1";
                try
                {
                    var api = Task.Run(() => CheckViaApi()).GetAwaiter().GetResult();
                    Expect("запасной путь (API) даёт то же", api != null && api.Version == direct.Version && api.Sha256 == direct.Sha256, api?.Version ?? "null");
                }
                catch (Exception ex) { lines.Add("пропущено: запасной путь (API) сейчас недоступен — " + BrowserInstaller.Explain(ex)); }
                var viaCheck = Task.Run(() => SelfUpdate.Check()).GetAwaiter().GetResult();
                Expect("общая проверка Check() находит версию", viaCheck?.Version == direct.Version, viaCheck?.Version ?? "null");
            }
        }
        catch (Exception ex) { Expect("исключение: " + ex.GetType().Name, false, ex.Message); }
        finally { VersionOverride = null; }
        lines.Add(ok ? "ВСЁ ПРОШЛО" : "ЕСТЬ ОШИБКИ");
        File.WriteAllText(Path.Combine(dir, "report.txt"), string.Join("\n", lines) + "\n");
        Environment.ExitCode = ok ? 0 : 1;
    }

    public static async Task Apply(UpdateInfo info, IProgress<int?> progress, CancellationToken ct = default)
    {
        var exe = ExePath ?? throw new InvalidOperationException("Не знаю, где лежит лаунчер");
        var tmp = exe + ".new";
        try
        {
            using var res = await Net.Http.GetAsync(info.ExeUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            res.EnsureSuccessStatusCode();
            var total = res.Content.Headers.ContentLength ?? 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var src = await res.Content.ReadAsStreamAsync(ct))
            await using (var dst = File.Create(tmp))
            {
                var buf = new byte[81920];
                long got = 0; int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    hash.AppendData(buf, 0, n);
                    got += n;
                    if (total > 0) progress.Report((int)Math.Min(99, got * 100 / total));
                }
            }
            if (Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() != info.Sha256)
                throw new InvalidDataException("Контрольная сумма обновления не совпала");
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            throw;
        }
        File.Move(exe, exe + ".old", true);
        File.Move(tmp, exe, true);
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! });
        Environment.Exit(0);
    }
}
