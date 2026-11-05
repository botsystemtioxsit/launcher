using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Win32;

namespace BattleLauncher;

record NativeInfo(string Version, string Exe, string? Sha);
record NativeRelease(string Tag, string Url, string Sha);

// EXE-версии приложений (настоящие Windows-программы без браузера). Сейчас такая есть только у админ-панели —
// Desktop-версия приложения: файл .exe + .sha256 в релизе репозитория, указанного в catalog.json (сейчас — «Терминал» в релизе лаунчера).
// Ставится в %LOCALAPPDATA%\Programs\<имя>, с ярлыками и записью в «Установленные приложения».
static class NativeInstaller
{
    const string RegBase = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\";
    const string Publisher = "TIOXSIT";
    static readonly Dictionary<string, (DateTime At, NativeRelease? Value)> cache = new();
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

    // Последний релиз: в нём лежит и сам EXE, и его контрольная сумма.
    // Берём прямые адреса файлов релиза, а не API GitHub: у API лимит 60 запросов в час на адрес, и при проверке нескольких
    // плагинов подряд он быстро кончается — кнопка «Скачать» тогда гасла без объяснений. У прямых адресов лимита нет.
    public static async Task<NativeRelease?> Latest(AppDef d, bool force = false, CancellationToken ct = default)
    {
        if (d.NativeExe == null) return null;
        if (!force && cache.TryGetValue(d.Id, out var c) && DateTime.UtcNow - c.At < CacheFor) return c.Value;
        var baseUrl = d.NativeTag == "latest"
            ? $"https://github.com/{Catalog.Owner}/{d.NativeRepo}/releases/latest/download/"
            : $"https://github.com/{Catalog.Owner}/{d.NativeRepo}/releases/download/{d.NativeTag}/";
        var exeUrl = baseUrl + d.NativeExe;
        string text;
        try { text = await Net.GetText(exeUrl + ".sha256", ct); }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // релиза (или файла в нём) пока нет — Desktop-версия ещё не вышла; кнопка просто остаётся закрытой
            cache[d.Id] = (DateTime.UtcNow, null);
            return null;
        }
        var sha = text.Trim().Split(' ', '\t', '\r', '\n')[0].ToLowerInvariant();
        NativeRelease? rel = null;
        if (Net.IsTrustedDownload(exeUrl, d.NativeRepo) && Regex.IsMatch(sha, "^[0-9a-f]{64}$"))
            rel = new NativeRelease(d.NativeTag == "latest" ? "latest" : d.NativeTag.TrimStart('v'), exeUrl, sha);
        cache[d.Id] = (DateTime.UtcNow, rel);
        return rel;
    }

    // Плагин может работать в фоне без окна: вежливо просим его выйти (он сам вернёт всё, что менял) и ждём.
    static void AskPluginToQuit(AppDef d, string exe)
    {
        if (!d.IsPlugin || !IsRunning(exe)) return;
        try
        {
            using var ev = EventWaitHandle.OpenExisting("GhostShell.Plugin.Quit." + d.Id);
            ev.Set();
        }
        catch { /* события нет — старая версия плагина: ниже попросим закрыть вручную */ }
        for (var i = 0; i < 80 && IsRunning(exe); i++) Thread.Sleep(100);
    }

    internal static bool IsRunning(string exe)
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

    // Запускает плагин после обновления так же, как он работал до него. Плагины с фоновым режимом (панель, обои, курсор, цвет) — тихо, с --background:
    // они сами применят сохранённое и, если делать нечего, сразу выйдут; плагины только с окном — открываем окно, которое было открыто.
    public static string[] RelaunchArgs(AppDef d) => Boot.BackgroundIds.Contains(d.Id) ? new[] { "--background" } : Array.Empty<string>();

    public static void Relaunch(AppDef d, string exe)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! };
        foreach (var a in RelaunchArgs(d)) psi.ArgumentList.Add(a);
        Process.Start(psi);
    }

    // true — плагин работал до обновления и запущен снова (иначе после обновления он оставался бы выключенным, пока человек не запустит его сам)
    public static async Task<bool> Install(AppDef d, NativeRelease rel, IProgress<int?> progress, CancellationToken ct = default)
    {
        if (d.NativeExe == null) throw new InvalidOperationException("У этого приложения нет EXE-версии");
        if (!Net.IsTrustedDownload(rel.Url, d.NativeRepo)) throw new InvalidOperationException("Недоверенный адрес загрузки");
        var dir = DirFor(d);
        var exe = Path.Combine(dir, d.NativeExe);
        var wasRunning = d.IsPlugin && IsRunning(exe);
        AskPluginToQuit(d, exe);
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
        // старые названия ярлыка («TIOXSIT Terminal», «Терминал») убираем, чтобы не было двух значков
        foreach (var old in new[] { "TIOXSIT Terminal", "Терминал" })
        {
            if (old == (d.NativeName ?? d.Name)) continue;
            try { File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), old + ".lnk")); } catch { }
            try { File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), old + ".lnk")); } catch { }
        }
        MakeShortcut(StartLink(d), exe, d.NativeName ?? d.Name);
        if (!d.IsPlugin) MakeShortcut(DesktopLink(d), exe, d.NativeName ?? d.Name); // плагин — только в меню «Пуск»
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
        if (!wasRunning) return false;
        try { Relaunch(d, exe); return true; }
        catch { return false; }   // не вышло запустить — обновление всё равно удалось; человек запустит из лаунчера
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
        if (info != null) AskPluginToQuit(d, info.Exe);
        if (info != null && IsRunning(info.Exe)) throw new InvalidOperationException($"Закрой «{d.NativeName}» и повтори.");
        try { File.Delete(StartLink(d)); } catch { }
        try { File.Delete(DesktopLink(d)); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(RegKey(d), false); } catch { }
        try { Directory.Delete(DirFor(d), true); } catch { }
    }

    // Убранные плагины (Catalog.Retired): остановить, стереть файлы, ярлыки, запись в «Установленные приложения» и автозапуск.
    // Если плагин сейчас не закрылся, попробуем при следующем запуске лаунчера.
    public static void RemoveRetired()
    {
        foreach (var d in Catalog.Retired)
        {
            try
            {
                if (Find(d) != null || Directory.Exists(DirFor(d))) Uninstall(d);
                using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                run?.DeleteValue(FolderName(d), false);
            }
            catch { }
        }
    }

    // Из списка «Установленные приложения»: BattleLauncher.exe --uninstall-native=<id>
    public static void UninstallById(string id)
    {
        var d = Catalog.ById(id);
        if (d?.NativeExe == null) return;
        if (MessageBox.Show($"Удалить «{d.NativeName}» с этого компьютера?", "Ghost Shell Launcher", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { Uninstall(d); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Ghost Shell Launcher", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    // BattleLauncher.exe --selftest-update=<папка>: служебная проверка «обновление не выключает плагин» на Windows-раннере GitHub.
    // Берёт настоящий релиз плагина Icon Plus (у него есть окно и выход по событию), ставит, запускает, обновляет поверх работающего и смотрит,
    // что он запущен снова новым процессом; обновление НЕработающего плагина его не запускает.
    public static void SelfTestUpdate(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var log = Path.Combine(outDir, "report.txt");
        void Say(string t) => File.AppendAllText(log, t + Environment.NewLine);
        var bad = 0;
        void Check(string name, bool ok, string extra = "") { Say((ok ? "ok     " : "ПЛОХО  ") + name + (ok ? "" : "  → " + extra)); if (!ok) bad++; }
        var thread = new Thread(() =>
        {
            try
            {
                var def = Catalog.Plugins.FirstOrDefault(p => p.Id == "icon-plus") ?? throw new InvalidOperationException("в каталоге нет icon-plus");
                var progress = new Progress<int?>(_ => { });
                var pname = Path.GetFileNameWithoutExtension(def.NativeExe!);
                static void KillAll(string name) { foreach (var p in Process.GetProcessesByName(name)) { try { p.Kill(true); p.WaitForExit(5000); } catch { } } }
                int[] Pids(string exe) => Process.GetProcessesByName(pname).Where(p => { try { return string.Equals(p.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase); } catch { return false; } }).Select(p => p.Id).ToArray();
                bool WaitFor(Func<bool> cond, int seconds) { for (var i = 0; i < seconds * 10; i++) { if (cond()) return true; Thread.Sleep(100); } return cond(); }

                try { if (Find(def) != null || Directory.Exists(DirFor(def))) { KillAll(pname); Uninstall(def); } } catch { }
                var rel = Latest(def, true).GetAwaiter().GetResult();
                Check("релиз плагина найден", rel != null, "в последнем релизе нет " + def.NativeExe);
                if (rel == null) return;

                var first = Install(def, rel, progress).GetAwaiter().GetResult();
                var info = Find(def);
                Check("первая установка: плагин не работал — запускать нечего (Install вернул false)", !first && info != null, "вернул " + first);
                if (info == null) return;

                Launch(info);
                var up = WaitFor(() => Pids(info.Exe).Length > 0, 30);
                Check("плагин запущен (окно открыто, как после «Запустить»)", up);
                var before = Pids(info.Exe);
                Say("  процесс до обновления: " + string.Join(",", before));

                var second = Install(def, rel, progress).GetAwaiter().GetResult();
                Check("обновление поверх работающего: Install вернул true (запущен снова)", second);
                var back = WaitFor(() => { var n = Pids(info.Exe); return n.Length > 0 && !n.Intersect(before).Any(); }, 30);
                Check("после обновления работает НОВЫЙ процесс плагина", back, "до: " + string.Join(",", before) + "; сейчас: " + string.Join(",", Pids(info.Exe)));

                // выходим по просьбе (как перед обновлением) и обновляем уже выключенный плагин
                AskPluginToQuit(def, info.Exe);
                Check("плагин вышел по событию выхода", !IsRunning(info.Exe));
                var third = Install(def, rel, progress).GetAwaiter().GetResult();
                Thread.Sleep(3000);
                Check("обновление выключенного плагина его не запускает", !third && Pids(info.Exe).Length == 0, "вернул " + third + "; процессы: " + string.Join(",", Pids(info.Exe)));

                // как запускаются после обновления разные плагины
                Check("плагины с фоновым режимом возвращаются с --background", Boot.BackgroundIds.All(id => { var d = Catalog.Plugins.FirstOrDefault(p => p.Id == id); return d != null && RelaunchArgs(d).SequenceEqual(new[] { "--background" }); }));
                Check("плагин только с окном возвращается без аргументов", RelaunchArgs(def).Length == 0);

                KillAll(pname);
                Uninstall(def);
                Check("после проверки плагин удалён", Find(def) == null && !Directory.Exists(DirFor(def)));
            }
            catch (Exception ex) { Check("самопроверка не упала", false, ex.ToString()); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromMinutes(4))) { Say("ПЛОХО  самопроверка не завершилась за 4 минуты"); bad++; }
        Say(bad == 0 ? "ВСЕ ПРОВЕРКИ ПРОШЛИ" : "НЕ ПРОШЛО ПРОВЕРОК: " + bad);
    }
}
