using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;

namespace GatewayPlusApp;

// Действия, которым нужны права администратора (поставить и убрать службу туннеля). Выполняет сам плагин, запущенный отдельно с запросом разрешения Windows:
// «GatewayPlus.exe --helper=<файл задания>». Результат кладёт рядом, в файл .result.
sealed class Job
{
    public string Action { get; set; } = "";   // connect | disconnect | cleanup | gsp-connect | gsp-disconnect
    public string Name { get; set; } = "";
    public string Conf { get; set; } = "";     // для connect: временный файл с конфигом
    public string Result { get; set; } = "";   // куда записать итог
}

sealed class JobResult
{
    public bool Ok { get; set; }
    public string Message { get; set; } = "";
}

static class Helper
{
    public static string WorkDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GhostShell", "GatewayPlus");

    public static bool IsAdmin()
    {
        try { using var id = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }
        catch { return false; }
    }

    // Выполнить задание: сразу (если мы уже администратор) или отдельным процессом с запросом разрешения.
    public static async Task<JobResult> RunAsync(Job job)
    {
        if (IsAdmin()) return await Task.Run(() => Execute(job));
        var tmp = Path.Combine(Path.GetTempPath(), "gp-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(tmp);
        try
        {
            job.Result = Path.Combine(tmp, "result.json");
            var jobFile = Path.Combine(tmp, "job.json");
            await File.WriteAllTextAsync(jobFile, JsonSerializer.Serialize(job));
            var psi = new ProcessStartInfo(Environment.ProcessPath!, "--helper=\"" + jobFile + "\"") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            Process? p;
            try { p = Process.Start(psi); }
            catch { return new JobResult { Ok = false, Message = "Разрешение администратора не получено — ничего не изменено." }; }
            if (p == null) return new JobResult { Ok = false, Message = "Не удалось запустить помощника." };
            var done = await Task.Run(() => p.WaitForExit(90000));
            if (!done) return new JobResult { Ok = false, Message = "Помощник не ответил за 90 секунд." };
            if (!File.Exists(job.Result)) return new JobResult { Ok = false, Message = "Помощник не оставил ответа (возможно, окно разрешения закрыли)." };
            return JsonSerializer.Deserialize<JobResult>(File.ReadAllText(job.Result)) ?? new JobResult { Ok = false, Message = "Ответ не читается." };
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    // Точка входа помощника с повышенными правами.
    public static int Main(string jobFile)
    {
        JobResult r;
        Job? job = null;
        try
        {
            job = JsonSerializer.Deserialize<Job>(File.ReadAllText(jobFile));
            r = job == null ? new JobResult { Message = "Задание не читается." } : Execute(job);
        }
        catch (Exception ex) { r = new JobResult { Ok = false, Message = ex.Message }; }
        try { if (job != null && job.Result.Length > 0) File.WriteAllText(job.Result, JsonSerializer.Serialize(r)); } catch { }
        return r.Ok ? 0 : 1;
    }

    public static JobResult Execute(Job job)
    {
        try
        {
            switch (job.Action)
            {
                case "gsp-connect": return GspConnect(job);
                case "gsp-disconnect": return GspDisconnect(job.Name);
                case "cleanup": return Cleanup(Wg.ExePath());
            }
            var exe = Wg.ExePath();
            if (exe == null) return new JobResult { Message = "WireGuard не установлен." };
            switch (job.Action)
            {
                case "connect": return Connect(exe, job);
                case "disconnect": return Disconnect(exe, job.Name);
                default: return new JobResult { Message = "Неизвестное действие." };
            }
        }
        catch (Exception ex) { return new JobResult { Message = ex.Message }; }
    }

    static void Lock(string dir)
    {
        // папка с конфигом (там закрытый ключ) — только система и администраторы
        Wg.Run("icacls.exe", "\"" + dir + "\" /inheritance:r /grant:r *S-1-5-18:(OI)(CI)F *S-1-5-32-544:(OI)(CI)F", 15000);
    }

    static JobResult Connect(string exe, Job job)
    {
        if (!Store.ValidName(job.Name)) return new JobResult { Message = "Плохое имя туннеля." };
        if (!File.Exists(job.Conf)) return new JobResult { Message = "Файл конфига не найден." };
        var conf = File.ReadAllText(job.Conf);
        var bad = Store.Validate(conf);
        if (bad != null) return new JobResult { Message = bad };

        Directory.CreateDirectory(WorkDir);
        Lock(WorkDir);
        var dst = Path.Combine(WorkDir, job.Name + ".conf");
        if (Wg.ServiceExists(job.Name)) { Wg.Run(exe, "/uninstalltunnelservice " + job.Name, 20000); WaitGone(job.Name, 10); }
        File.WriteAllText(dst, conf);
        var (code, o) = Wg.Run(exe, "/installtunnelservice \"" + dst + "\"", 30000);
        for (var i = 0; i < 20; i++) { if (Wg.IsRunning(job.Name)) return new JobResult { Ok = true, Message = "Шлюз включён." }; Thread.Sleep(500); }
        Wg.Run(exe, "/uninstalltunnelservice " + job.Name, 20000);
        try { File.Delete(dst); } catch { }
        return new JobResult { Message = "Служба шлюза не запустилась (код " + code + (o.Length > 0 ? ": " + o : "") + ")." };
    }

    static JobResult Disconnect(string exe, string name)
    {
        if (!Store.ValidName(name)) return new JobResult { Message = "Плохое имя туннеля." };
        if (Wg.ServiceExists(name)) { Wg.Run(exe, "/uninstalltunnelservice " + name, 20000); WaitGone(name, 15); }
        try { File.Delete(Path.Combine(WorkDir, name + ".conf")); } catch { }
        return Wg.ServiceExists(name) ? new JobResult { Message = "Служба шлюза не снялась." } : new JobResult { Ok = true, Message = "Шлюз выключен." };
    }

    static JobResult Cleanup(string? exe)
    {
        var any = false;
        if (Directory.Exists(WorkDir))
        {
            if (exe != null)
                foreach (var f in Directory.GetFiles(WorkDir, "*.conf"))
                {
                    var n = Path.GetFileNameWithoutExtension(f);
                    if (n.StartsWith("gsp-", StringComparison.Ordinal)) continue;
                    if (Store.ValidName(n) && Wg.ServiceExists(n)) { Wg.Run(exe, "/uninstalltunnelservice " + n, 20000); WaitGone(n, 15); any = true; }
                    try { File.Delete(f); } catch { }
                }
            // службы GSP: каждый gsp-<имя>.conf — один включённый шлюз
            foreach (var f in Directory.GetFiles(WorkDir, "gsp-*.conf"))
            {
                var n = Path.GetFileNameWithoutExtension(f)["gsp-".Length..];
                if (Store.ValidName(n) && GspClient.ServiceExists(n)) { StopGspService(n); any = true; }
                try { File.Delete(f); } catch { }
                try { File.Delete(GspClient.StatusFile(n)); } catch { }
            }
            try { if (Directory.Exists(GspClient.ProgDir)) Directory.Delete(GspClient.ProgDir, true); } catch { }
        }
        try { if (Directory.Exists(GspClient.StatusDir) && !Directory.EnumerateFileSystemEntries(GspClient.StatusDir).Any()) Directory.Delete(GspClient.StatusDir); } catch { }
        try { if (Directory.Exists(WorkDir) && !Directory.EnumerateFileSystemEntries(WorkDir).Any()) Directory.Delete(WorkDir); } catch { }
        return new JobResult { Ok = true, Message = any ? "Шлюз выключен, следы убраны." : "Убирать было нечего." };
    }

    // ---------- GSP ----------
    static void StopGspService(string name)
    {
        if (File.Exists(GspClient.ExePath)) Wg.Run(GspClient.ExePath, "remove-service -name " + name, 60000);
        else Wg.Run("sc.exe", "stop \"" + Gsp.ServiceName(name) + "\"", 15000);
        for (var i = 0; i < 30 && GspClient.ServiceExists(name); i++)
        {
            if (i == 10) Wg.Run("sc.exe", "delete \"" + Gsp.ServiceName(name) + "\"", 15000);
            Thread.Sleep(500);
        }
    }

    static JobResult GspConnect(Job job)
    {
        if (!Store.ValidName(job.Name)) return new JobResult { Message = "Плохое имя шлюза." };
        if (!File.Exists(job.Conf)) return new JobResult { Message = "Файл конфига не найден." };
        var conf = File.ReadAllText(job.Conf);
        if (!Gsp.IsGsp(conf)) return new JobResult { Message = "Это не конфиг GSP." };
        var bad = Gsp.Validate(conf);
        if (bad != null) return new JobResult { Message = bad };
        if (!GspClient.Available) return new JobResult { Message = "В этой сборке плагина нет программы GSP. Обновите Gateway Plus из лаунчера." };

        Directory.CreateDirectory(WorkDir);
        Lock(WorkDir);
        if (GspClient.ServiceExists(job.Name)) StopGspService(job.Name);   // пока старая служба работает, её gsp.exe занят
        var err = GspClient.ExtractTo(GspClient.ProgDir);
        if (err != null) return new JobResult { Message = err };

        var dst = GspClient.ConfFile(job.Name);
        File.WriteAllText(dst, Gsp.WithName(conf, "GhostShell-" + job.Name));
        Directory.CreateDirectory(GspClient.StatusDir);
        // состояние читает обычный пользователь (окно плагина), пишет служба; больше никому ничего не нужно
        Wg.Run("icacls.exe", "\"" + GspClient.StatusDir + "\" /inheritance:r /grant:r *S-1-5-18:(OI)(CI)F *S-1-5-32-544:(OI)(CI)F *S-1-5-32-545:(OI)(CI)R", 15000);
        var st = GspClient.StatusFile(job.Name);
        try { File.Delete(st); } catch { }

        var (code, o) = Wg.Run(GspClient.ExePath, "install-service -c \"" + dst + "\" -name " + job.Name + " -status \"" + st + "\"", 90000);
        if (code != 0)
        {
            var why = Gsp.ParseStatus(File.Exists(st) ? File.ReadAllText(st) : null)?.Error ?? "";
            try { File.Delete(dst); } catch { }
            return new JobResult { Message = "Служба GSP не запустилась" + (why.Length > 0 ? ": " + why : o.Length > 0 ? ": " + o : " (код " + code + ")") + "." };
        }
        return new JobResult { Ok = true, Message = "Шлюз включён (GSP)." };
    }

    static JobResult GspDisconnect(string name)
    {
        if (!Store.ValidName(name)) return new JobResult { Message = "Плохое имя шлюза." };
        if (GspClient.ServiceExists(name)) StopGspService(name);
        try { File.Delete(GspClient.ConfFile(name)); } catch { }
        try { File.Delete(GspClient.StatusFile(name)); } catch { }
        return GspClient.ServiceExists(name) ? new JobResult { Message = "Служба шлюза не снялась." } : new JobResult { Ok = true, Message = "Шлюз выключен." };
    }

    static void WaitGone(string name, int seconds)
    {
        for (var i = 0; i < seconds * 2 && Wg.ServiceExists(name); i++) Thread.Sleep(500);
    }
}
