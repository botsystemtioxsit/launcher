using System.Net.Http;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace BattleLauncher;

record UpdateInfo(string Version, string ExeUrl, string Sha256);

// Лаунчер — один .exe. Новая версия берётся из последнего релиза репозитория launcher
// (BattleLauncher.exe + BattleLauncher.exe.sha256). Работающий .exe нельзя перезаписать, но можно
// переименовать: старый уходит в .old, новый встаёт на его место, запускаем и выходим.
static class SelfUpdate
{
    const string Repo = "launcher";
    const string ExeName = "BattleLauncher.exe";

    public static string CurrentVersion
    {
        get
        {
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

    public static async Task<UpdateInfo?> Check(CancellationToken ct = default)
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
        var sha = (await Net.GetText(shaUrl, ct)).Trim().Split(' ', '\t')[0].ToLowerInvariant();
        return new UpdateInfo(tag, exeUrl, sha);
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
