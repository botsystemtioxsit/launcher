using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;

namespace GatewayPlusApp;

// Действия, которым нужны права администратора (поставить и убрать службу туннеля). Выполняет сам плагин, запущенный отдельно с запросом разрешения Windows:
// «GatewayPlus.exe --helper=<файл задания>». Результат кладёт рядом, в файл .result.
sealed class Job
{
    public string Action { get; set; } = "";   // connect | disconnect | cleanup
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
            var exe = Wg.ExePath();
            if (exe == null) return new JobResult { Message = "WireGuard не установлен." };
            switch (job.Action)
            {
                case "connect": return Connect(exe, job);
                case "disconnect": return Disconnect(exe, job.Name);
                case "cleanup": return Cleanup(exe);
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

    static JobResult Cleanup(string exe)
    {
        var any = false;
        if (Directory.Exists(WorkDir))
            foreach (var f in Directory.GetFiles(WorkDir, "*.conf"))
            {
                var n = Path.GetFileNameWithoutExtension(f);
                if (Store.ValidName(n) && Wg.ServiceExists(n)) { Wg.Run(exe, "/uninstalltunnelservice " + n, 20000); WaitGone(n, 15); any = true; }
                try { File.Delete(f); } catch { }
            }
        try { if (Directory.Exists(WorkDir) && !Directory.EnumerateFileSystemEntries(WorkDir).Any()) Directory.Delete(WorkDir); } catch { }
        return new JobResult { Ok = true, Message = any ? "Шлюз выключен, следы убраны." : "Убирать было нечего." };
    }

    static void WaitGone(string name, int seconds)
    {
        for (var i = 0; i < seconds * 2 && Wg.ServiceExists(name); i++) Thread.Sleep(500);
    }
}
