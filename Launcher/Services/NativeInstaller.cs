using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;

namespace BattleLauncher;

record NativeInfo(string Version, string Exe, string? Sha);
record NativeRelease(string Tag, string Url, string Sha);

// EXE-версии приложений (настоящие Windows-программы без браузера). Сейчас такая есть только у админ-панели —
// «TIOXSIT Terminal»: лежит в релизе самого лаунчера рядом с BattleLauncher.exe (файл + .sha256).
// Ставится в %LOCALAPPDATA%\Programs\<имя>, с ярлыками и записью в «Установленные приложения».
static class NativeInstaller
{
    const string RegBase = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\";
    const string Publisher = "TIOXSIT";
    static (DateTime At, NativeRelease? Value)? cache;
    static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

    static string FolderName(AppDef d) => d.NativeExe != null ? Path.GetFileNameWithoutExtension(d.NativeExe) : (d.NativeName ?? d.Name).Replace(" ", "");
    public static string DirFor(AppDef d) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", FolderName(d));
    static string RegKey(AppDef d) => RegBase + "TIOXSIT_" + d.Id;
    static string StartLink(AppDef d) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), (d.NativeName ?? d.Name) + ".lnk");
    static string DesktopLink(AppDef d) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), (d.NativeName ?? d.Name) + ".lnk");

    public static NativeInfo? Find(AppDef d)
    {
        if (d.NativeExe == null) return null;
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RegKey(d));
            var loc = k?.GetValue("InstallLocation") as string;
            if (string.IsNullOrEmpty(loc)) return null;
            var exe = Path.Combine(loc, d.NativeExe);
            if (!File.Exists(exe)) return null;
            var v = FileVersionInfo.GetVersionInfo(exe).ProductVersion ?? "";
            var plus = v.IndexOf('+');
            return new NativeInfo(plus > 0 ? v[..plus] : v, exe, k!.GetValue("LauncherSha256") as string);
        }
        catch { return null; }
    }

    // Последний релиз лаунчера: в нём лежит и сам EXE, и его контрольная сумма
    public static async Task<NativeRelease?> Latest(AppDef d, bool force = false, CancellationToken ct = default)
    {
        if (d.NativeExe == null) return null;
        if (!force && cache is { } c && DateTime.UtcNow - c.At < CacheFor) return c.Value;
        using var doc = JsonDocument.Parse(await Net.GetText($"https://api.github.com/repos/{Catalog.Owner}/launcher/releases/latest", ct));
        var root = doc.RootElement;
        var tag = (root.GetProperty("tag_name").GetString() ?? "").TrimStart('v');
        string? exeUrl = null, shaUrl = null;
        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString();
            var url = a.GetProperty("browser_download_url").GetString();
            if (name == d.NativeExe) exeUrl = url;
            else if (name == d.NativeExe + ".sha256") shaUrl = url;
        }
        NativeRelease? rel = null;
        if (exeUrl != null && shaUrl != null && Net.IsTrustedDownload(exeUrl, "launcher"))
        {
            var sha = (await Net.GetText(shaUrl, ct)).Trim().Split(' ', '\t')[0].ToLowerInvariant();
            rel = new NativeRelease(tag, exeUrl, sha);
        }
        cache = (DateTime.UtcNow, rel);
        return rel;
    }

    static bool IsRunning(string exe)
    {
        try
        {
            return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)).Any(p =>
            {
                try { return string.Equals(p.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            });
        }
        catch { return false; }
    }

    public static async Task Install(AppDef d, NativeRelease rel, IProgress<int?> progress, CancellationToken ct = default)
    {
        if (d.NativeExe == null) throw new InvalidOperationException("У этого приложения нет EXE-версии");
        if (!Net.IsTrustedDownload(rel.Url, "launcher")) throw new InvalidOperationException("Недоверенный адрес загрузки");
        var dir = DirFor(d);
        var exe = Path.Combine(dir, d.NativeExe);
        if (IsRunning(exe)) throw new InvalidOperationException($"Закрой «{d.NativeName}» и повтори.");
        var tmp = Path.Combine(Path.GetTempPath(), "battle-launcher-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            using (var res = await Net.Http.GetAsync(rel.Url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                res.EnsureSuccessStatusCode();
                var total = res.Content.Headers.ContentLength ?? 0;
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using var src = await res.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(tmp);
                var buf = new byte[81920];
                long got = 0; int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    hash.AppendData(buf, 0, n);
                    got += n;
                    if (total > 0) progress.Report((int)Math.Min(99, got * 100 / total));
                }
                if (Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() != rel.Sha)
                    throw new InvalidDataException("Контрольная сумма не совпала — файл повреждён");
            }
            progress.Report(null);
            Directory.CreateDirectory(dir);
            File.Move(tmp, exe, true);
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
        // старое название ярлыка («TIOXSIT Terminal») убираем, чтобы не было двух значков
        try { File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "TIOXSIT Terminal.lnk")); } catch { }
        try { File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "TIOXSIT Terminal.lnk")); } catch { }
        MakeShortcut(StartLink(d), exe, d.NativeName ?? d.Name);
        MakeShortcut(DesktopLink(d), exe, d.NativeName ?? d.Name);
        var version = FileVersionInfo.GetVersionInfo(exe).ProductVersion ?? "";
        var plus = version.IndexOf('+');
        using var k = Registry.CurrentUser.CreateSubKey(RegKey(d));
        k.SetValue("DisplayName", d.NativeName ?? d.Name);
        k.SetValue("DisplayVersion", plus > 0 ? version[..plus] : version);
        k.SetValue("Publisher", Publisher);
        k.SetValue("InstallLocation", dir);
        k.SetValue("DisplayIcon", exe);
        k.SetValue("UninstallString", $"\"{Environment.ProcessPath}\" --uninstall-native={d.Id}");
        k.SetValue("NoModify", 1, RegistryValueKind.DWord);
        k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        k.SetValue("LauncherSha256", rel.Sha);
    }

    static void MakeShortcut(string path, string target, string description)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Нет WScript.Shell");
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic lnk = shell.CreateShortcut(path);
        lnk.TargetPath = target;
        lnk.WorkingDirectory = Path.GetDirectoryName(target)!;
        lnk.IconLocation = target + ",0";
        lnk.Description = description;
        lnk.Save();
    }

    public static void Launch(NativeInfo n) =>
        Process.Start(new ProcessStartInfo(n.Exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(n.Exe)! });

    public static void Uninstall(AppDef d)
    {
        var info = Find(d);
        if (info != null && IsRunning(info.Exe)) throw new InvalidOperationException($"Закрой «{d.NativeName}» и повтори.");
        try { File.Delete(StartLink(d)); } catch { }
        try { File.Delete(DesktopLink(d)); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(RegKey(d), false); } catch { }
        try { Directory.Delete(DirFor(d), true); } catch { }
    }

    // Из списка «Установленные приложения»: BattleLauncher.exe --uninstall-native=<id>
    public static void UninstallById(string id)
    {
        var d = Catalog.ById(id);
        if (d?.NativeExe == null) return;
        if (MessageBox.Show($"Удалить «{d.NativeName}» с этого компьютера?", "TIOXSIT Launcher", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { Uninstall(d); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "TIOXSIT Launcher", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
