using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace IconPlusApp;

// Значок обычных папок Windows задаётся только общим для компьютера параметром (HKLM → Shell Icons, значения «3» и «4»), поэтому нужны права администратора:
// плагин сам запускает свою копию с запросом разрешения (окно Windows «Разрешить?») и только на это одно действие. Прежние значения сохраняются тут же и возвращаются.
static class FolderIcon
{
    const string KeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons";
    const string BackupPrefix = "IconPlus.Backup.", CreatedMark = "IconPlus.Created";
    static readonly string[] Names = { "3", "4" };   // 3 — закрытая папка, 4 — открытая

    public static bool IsAdmin()
    {
        try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
        catch { return false; }
    }

    // Выполняется только с правами администратора. Возвращает 0 — готово, 5 — прав нет.
    public static int SetMachine(string ico)
    {
        if (!IsAdmin()) return 5;
        bool existed;
        using (var probe = Registry.LocalMachine.OpenSubKey(KeyPath)) existed = probe != null;
        using var k = Registry.LocalMachine.CreateSubKey(KeyPath, true);
        if (!existed) k.SetValue(CreatedMark, "1", RegistryValueKind.String);
        foreach (var name in Names)
        {
            var old = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (old != null && k.GetValue(BackupPrefix + name) == null) k.SetValue(BackupPrefix + name, old.ToString() ?? "", RegistryValueKind.String);
            k.SetValue(name, ico + ",0", RegistryValueKind.String);
        }
        return 0;
    }

    public static int RestoreMachine()
    {
        if (!IsAdmin()) return 5;
        using var k = Registry.LocalMachine.OpenSubKey(KeyPath, true);
        if (k == null) return 0;
        foreach (var name in Names)
        {
            var backup = k.GetValue(BackupPrefix + name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            if (backup != null) { k.SetValue(name, backup, RegistryValueKind.String); k.DeleteValue(BackupPrefix + name, false); }
            else if (k.GetValue(name) != null) k.DeleteValue(name, false);
        }
        var created = k.GetValue(CreatedMark) != null;
        if (created) k.DeleteValue(CreatedMark, false);
        var empty = k.ValueCount == 0 && k.SubKeyCount == 0;
        k.Close();
        if (created && empty)
        {
            using var parent = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", true);
            try { parent?.DeleteSubKey("Shell Icons", false); } catch { }
        }
        return 0;
    }

    public static string? Current()
    {
        try { using var k = Registry.LocalMachine.OpenSubKey(KeyPath); return k?.GetValue("3") as string; } catch { return null; }
    }

    static void Run(string args)
    {
        if (IsAdmin())
        {
            var code = args.StartsWith("--folder-icon-restore", StringComparison.Ordinal) ? RestoreMachine() : SetMachine(args[(args.IndexOf('=') + 1)..].Trim('"'));
            if (code != 0) throw new InvalidOperationException("не хватает прав");
            return;
        }
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, args) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            p?.WaitForExit(30000);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { throw new InvalidOperationException("разрешение администратора не дано"); }
    }

    // Поставить значок папок (с запросом прав администратора, если их нет).
    public static void SetElevated(string ico)
    {
        Run("--folder-icon=\"" + ico + "\"");
        var cur = Current();
        if (cur == null || !cur.StartsWith(ico, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("значок папок не изменился (нужно разрешение администратора)");
    }

    public static void RestoreElevated() => Run("--folder-icon-restore");
}
