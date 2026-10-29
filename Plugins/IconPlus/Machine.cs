using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace IconPlusApp;

sealed class MachineLnk { public string File { get; set; } = ""; public string Ico { get; set; } = ""; }

// Задание для действий, которые нужно делать с правами администратора: значок обычных папок и ярлыки «для всех пользователей»
// (общий рабочий стол и общее меню «Пуск»). Всё собирается в одно задание — окно Windows «Разрешить?» появляется один раз.
sealed class MachineJob
{
    public string? FolderIco { get; set; }
    public List<MachineLnk> Lnks { get; set; } = new();
    public string BackupFile { get; set; } = "";     // сюда запоминаются прежние значки ярлыков
    public string ResultFile { get; set; } = "";
    public bool Restore { get; set; }
    public bool IsEmpty => FolderIco == null && Lnks.Count == 0;
}

sealed class MachineBackup { public List<LnkChange> Lnk { get; set; } = new(); }

sealed record MachineResult(bool FolderOk, int LnkOk, int LnkFailed, string Error, List<(string File, string Reason)> Failures);

static class Machine
{
    static string JobFile => Path.Combine(Paths.Root, "machine-job.json");
    static string ResultFile => Path.Combine(Paths.Root, "machine-result.txt");
    public static string BackupFile => Path.Combine(Paths.Root, "machine-lnk-backup.json");

    // ---------- со стороны обычного окна ----------
    public static MachineResult Run(MachineJob job)
    {
        Paths.Ensure();
        job.BackupFile = BackupFile;
        job.ResultFile = ResultFile;
        File.WriteAllText(JobFile, JsonSerializer.Serialize(job));
        try { File.Delete(ResultFile); } catch { }
        var args = "--machine=\"" + JobFile + "\"";
        if (FolderIcon.IsAdmin()) Work(JobFile);
        else
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, args) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
                p?.WaitForExit(120000);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return new MachineResult(false, 0, 0, "разрешение администратора не дано", new()); }
        }
        return ReadResult();
    }

    static MachineResult ReadResult()
    {
        try
        {
            if (!File.Exists(ResultFile)) return new MachineResult(false, 0, 0, "нет ответа от действия администратора", new());
            bool folder = false; int ok = 0, fail = 0; var err = ""; var failures = new List<(string, string)>();
            foreach (var line in File.ReadAllLines(ResultFile))
            {
                var i = line.IndexOf('='); if (i < 0) continue;
                var k = line[..i]; var v = line[(i + 1)..];
                if (k == "folder") folder = v == "1";
                else if (k == "ok") int.TryParse(v, out ok);
                else if (k == "fail") int.TryParse(v, out fail);
                else if (k == "error") err = v;
                else if (k == "failitem") { var bar = v.LastIndexOf('|'); if (bar > 0) failures.Add((v[..bar], v[(bar + 1)..])); }
            }
            return new MachineResult(folder, ok, fail, err, failures);
        }
        catch (Exception ex) { return new MachineResult(false, 0, 0, ex.Message, new()); }
    }

    // ---------- с правами администратора (запуск: IconPlus.exe --machine=<файл задания>) ----------
    public static int Work(string jobFile)
    {
        var sb = new StringBuilder();
        string resultFile = "";
        try
        {
            var job = JsonSerializer.Deserialize<MachineJob>(File.ReadAllText(jobFile)) ?? new MachineJob();
            resultFile = job.ResultFile;
            if (!FolderIcon.IsAdmin()) { sb.AppendLine("error=нет прав администратора"); return Finish(resultFile, sb, 5); }

            MachineBackup backup;
            try { backup = JsonSerializer.Deserialize<MachineBackup>(File.ReadAllText(job.BackupFile)) ?? new MachineBackup(); }
            catch { backup = new MachineBackup(); }

            int ok = 0, fail = 0; var folder = false;
            void Fail(string file, string why) { fail++; if (fail <= 400) sb.AppendLine("failitem=" + file + "|" + why); }
            if (job.Restore)
            {
                foreach (var b in backup.Lnk.ToList())
                {
                    try { if (File.Exists(b.File) && Shortcuts.WriteIcon(b.File, b.OldPath, b.OldIndex)) ok++; else Fail(b.File, "не вернулся"); } catch (Exception ex) { Fail(b.File, ex.Message); }
                }
                backup.Lnk.Clear();
                FolderIcon.RestoreMachine();
                folder = true;
            }
            else
            {
                if (job.FolderIco != null) { folder = FolderIcon.SetMachine(job.FolderIco) == 0; }
                foreach (var l in job.Lnks)
                {
                    try
                    {
                        if (backup.Lnk.All(x => x.File != l.File))
                        {
                            if (!Shortcuts.ReadIcon(l.File, out var op, out var oi)) { Fail(l.File, "ярлык не читается"); continue; }
                            backup.Lnk.Add(new LnkChange { File = l.File, OldPath = op, OldIndex = oi });
                        }
                        if (Shortcuts.WriteIcon(l.File, l.Ico, 0)) ok++; else Fail(l.File, "ярлык не записывается (нет доступа или файл занят)");
                    }
                    catch (Exception ex) { Fail(l.File, ex.Message); }
                }
            }
            File.WriteAllText(job.BackupFile, JsonSerializer.Serialize(backup));
            sb.AppendLine("folder=" + (folder ? "1" : "0"));
            sb.AppendLine("ok=" + ok);
            sb.AppendLine("fail=" + fail);
            return Finish(resultFile, sb, 0);
        }
        catch (Exception ex) { sb.AppendLine("error=" + ex.Message.Replace('\n', ' ')); return Finish(resultFile, sb, 1); }
    }

    static int Finish(string resultFile, StringBuilder sb, int code)
    {
        try { if (resultFile.Length > 0) File.WriteAllText(resultFile, sb.ToString()); } catch { }
        return code;
    }
}
