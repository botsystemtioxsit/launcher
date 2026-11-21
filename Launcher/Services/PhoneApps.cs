using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace BattleLauncher;

// Android-приложения проекта, которые лаунчер ставит и обновляет на телефоне по кабелю (отладка по USB, adb).
// Новое приложение = одна строка в списке All (пакет, где лежит релиз и где в репозитории лежит build.gradle с версией).
record PhoneApp(string Id, string Name, string Package, string Repo, string Tag, string Asset, string GradlePath);

record PhoneState(PhoneApp App, string Installed, string Latest)
{
    public bool IsInstalled => Installed.Length > 0;
    public bool HasUpdate => Latest.Length > 0 && (!IsInstalled || Net.CompareVersions(Installed, Latest) < 0);
}

record PhoneDevice(string Serial, string State);

record PhoneInstall(bool Ok, string Text, bool SignatureMismatch);

static class PhoneApps
{
    public static readonly PhoneApp[] All =
    {
        new("lockscreen-plus", "Lock Screen Plus", "com.tioxsit.lockscreenplus", "launcher", "lockscreen-plus-latest", "LockScreenPlus.apk", "LockScreenPlus/app/build.gradle"),
        new("gateway-plus-android", "Gateway Plus (Android)", "com.tioxsit.gatewayplus", "launcher", "android-gateway-latest", "GatewayPlus.apk", "AndroidGateway/app/build.gradle"),
        new("launcher-android", "Ghost Shell Launcher (Android)", "com.tioxsit.launcher", "launcher", "android-latest", "TIOXSITLauncher.apk", "Android/app/build.gradle"),
    };

    static readonly Regex SerialRe = new("^[A-Za-z0-9._:-]{1,64}$", RegexOptions.Compiled);
    const string PlatformToolsUrl = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip";

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GhostShell", "phone");

    // adb уже есть: у нас в папке, рядом с программой или в PATH
    public static string? FindAdb()
    {
        var local = Path.Combine(Dir, "platform-tools", "adb.exe");
        if (File.Exists(local)) return local;
        foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try
            {
                var f = Path.Combine(d.Trim(), "adb.exe");
                if (d.Trim().Length > 0 && File.Exists(f)) return f;
            }
            catch { /* странный путь в PATH */ }
        }
        return null;
    }

    // Официальный набор platform-tools от Google (около 7 МБ). Скачивается только по согласию человека.
    public static async Task<string> FetchAdb(IProgress<int?>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Dir);
        var zip = Path.Combine(Dir, "platform-tools.zip");
        using (var resp = await Net.Http.GetAsync(PlatformToolsUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(zip);
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress?.Report((int)(done * 100 / total.Value));
            }
        }
        ZipFile.ExtractToDirectory(zip, Dir, true);   // .NET сам не даёт выйти за папку (../)
        File.Delete(zip);
        var adb = Path.Combine(Dir, "platform-tools", "adb.exe");
        if (!File.Exists(adb)) throw new InvalidOperationException("в скачанном наборе нет adb.exe");
        return adb;
    }

    // Запуск adb: вывод и ошибки вместе. По таймауту процесс убивается.
    public static async Task<string> Run(string adb, string args, int timeoutMs, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(adb, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("adb не запустился");
        var o = p.StandardOutput.ReadToEndAsync();
        var e = p.StandardError.ReadToEndAsync();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { }
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException("adb не ответил вовремя");
        }
        return await o + "\n" + await e;
    }

    public static async Task<List<PhoneDevice>> Devices(string adb, CancellationToken ct)
    {
        var text = await Run(adb, "devices", 15000, ct);
        var list = new List<PhoneDevice>();
        foreach (var raw in text.Split('\n'))
        {
            var l = raw.Trim();
            if (l.Length == 0 || l.StartsWith("List of") || l.StartsWith("*")) continue;
            var parts = l.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && SerialRe.IsMatch(parts[0])) list.Add(new PhoneDevice(parts[0], parts[1]));
        }
        return list;
    }

    static string S(string serial)
    {
        if (!SerialRe.IsMatch(serial)) throw new ArgumentException("странный номер устройства");
        return "-s " + serial + " ";
    }

    public static async Task<string> InstalledVersion(string adb, string serial, PhoneApp app, CancellationToken ct)
    {
        var o = await Run(adb, S(serial) + "shell dumpsys package " + app.Package, 20000, ct);
        var m = Regex.Match(o, @"versionName=([0-9][0-9.]*)");
        return m.Success ? m.Groups[1].Value : "";
    }

    public static async Task<string> LatestVersion(PhoneApp app, CancellationToken ct)
    {
        try
        {
            var g = await Net.GetText($"https://raw.githubusercontent.com/{Catalog.Owner}/{app.Repo}/main/{app.GradlePath}", ct);
            var m = Regex.Match(g, "versionName \"([0-9][0-9.]*)\"");
            return m.Success ? m.Groups[1].Value : "";
        }
        catch (OperationCanceledException) { throw; }
        catch { return ""; }
    }

    // Что стоит на телефоне и что вышло: по каждому приложению проекта
    public static async Task<List<PhoneState>> Check(string adb, string serial, CancellationToken ct)
    {
        var res = new List<PhoneState>();
        foreach (var app in All)
        {
            var inst = await InstalledVersion(adb, serial, app, ct);
            var latest = await LatestVersion(app, ct);
            res.Add(new PhoneState(app, inst, latest));
        }
        return res;
    }

    // Скачивает APK из релиза и сверяет контрольную сумму из <файл>.sha256. Не совпало — ничего не ставим.
    public static async Task<string> DownloadApk(PhoneApp app, IProgress<int?>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Dir);
        var baseUrl = $"https://github.com/{Catalog.Owner}/{app.Repo}/releases/download/{app.Tag}/";
        if (!Net.IsTrustedDownload(baseUrl + app.Asset, app.Repo)) throw new InvalidOperationException("адрес файла не из нашего репозитория");
        var want = (await Net.GetText(baseUrl + app.Asset + ".sha256", ct)).Trim().ToLowerInvariant();
        if (want.Length >= 64) want = want[..64];
        if (!Regex.IsMatch(want, "^[0-9a-f]{64}$")) throw new InvalidOperationException("в релизе нет контрольной суммы приложения");
        var file = Path.Combine(Dir, app.Asset);
        using (var resp = await Net.Http.GetAsync(baseUrl + app.Asset, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(file);
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress?.Report((int)(done * 100 / total.Value));
            }
        }
        string got;
        await using (var fs = File.OpenRead(file)) got = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
        if (got != want)
        {
            try { File.Delete(file); } catch { }
            throw new InvalidOperationException("скачанный файл не совпал с контрольной суммой — установку отменил");
        }
        return file;
    }

    public static async Task<PhoneInstall> Install(string adb, string serial, string apk, CancellationToken ct)
    {
        var o = await Run(adb, S(serial) + $"install -r \"{apk}\"", 300000, ct);
        if (o.Contains("Success")) return new PhoneInstall(true, "", false);
        var mismatch = o.Contains("INSTALL_FAILED_UPDATE_INCOMPATIBLE") || o.Contains("signatures do not match");
        return new PhoneInstall(false, Explain(o), mismatch);
    }

    public static async Task Uninstall(string adb, string serial, PhoneApp app, CancellationToken ct)
        => await Run(adb, S(serial) + "uninstall " + app.Package, 60000, ct);

    // Что значит ответ adb — простыми словами
    public static string Explain(string o)
    {
        if (o.Contains("INSTALL_FAILED_USER_RESTRICTED")) return "телефон запретил установку по кабелю: в «Параметрах разработчика» включите «Установка через USB» и подтвердите окно на экране телефона";
        if (o.Contains("INSTALL_FAILED_INSUFFICIENT_STORAGE")) return "на телефоне не хватает места";
        if (o.Contains("INSTALL_FAILED_VERSION_DOWNGRADE")) return "на телефоне уже стоит более новая версия";
        if (o.Contains("INSTALL_FAILED_UPDATE_INCOMPATIBLE") || o.Contains("signatures do not match")) return "подпись приложения на телефоне другая — поверх оно не ставится";
        if (o.Contains("unauthorized")) return "на телефоне не нажато «Разрешить отладку по USB»";
        if (o.Contains("offline") || o.Contains("no devices")) return "телефон отключился";
        var line = o.Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0) ?? "неизвестная ошибка";
        return line.Length > 160 ? line[..160] : line;
    }
}
