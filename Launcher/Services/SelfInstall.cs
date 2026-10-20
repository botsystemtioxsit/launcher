using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace BattleLauncher;

// Установка самого лаунчера: скачанный .exe при первом запуске предлагает поставить себя в
// %LOCALAPPDATA%\Programs\BattleLauncher, делает ярлыки (Пуск, рабочий стол) и запись в «Установленные
// приложения» с издателем и программой удаления. Права администратора не нужны — всё в профиле пользователя.
// Самообновление (SelfUpdate) потом меняет файл уже в папке установки.
static class SelfInstall
{
    const string AppName = "TIOXSIT Launcher";
    const string Publisher = "TIOXSIT";
    const string LegacyName = "БАТТЛ Лаунчер"; // так лаунчер назывался до 1.2.1: старые ярлыки убираем при установке
    const string RegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BattleLauncher";

    static readonly string InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "BattleLauncher");
    static readonly string InstalledExe = Path.Combine(InstallDir, "BattleLauncher.exe");
    static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");
    static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");

    public static bool IsInstalledCopy =>
        string.Equals(Environment.ProcessPath, InstalledExe, StringComparison.OrdinalIgnoreCase);

    static string? InstalledVersion()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RegKey);
        return k?.GetValue("DisplayVersion") as string;
    }

    // true — поставили и запустили установленную копию, эту можно закрывать
    public static bool OfferInstall()
    {
        var src = Environment.ProcessPath;
        if (IsInstalledCopy || src == null) return false;
        var have = InstalledVersion();
        var text = have == null
            ? $"Установить «{AppName}» на этот компьютер?\n\nПоявятся ярлыки в «Пуске» и на рабочем столе, лаунчер будет в списке установленных приложений. Права администратора не нужны."
            : $"На компьютере уже стоит лаунчер {have}. Заменить его этой версией ({SelfUpdate.CurrentVersion})?";
        if (MessageBox.Show(text, AppName, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return false;
        try
        {
            Install(src);
            Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true, WorkingDirectory = InstallDir });
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не удалось установить: " + ex.Message + "\n\nЕсли лаунчер сейчас открыт — закрой его и попробуй ещё раз.", AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    static void Install(string src)
    {
        Directory.CreateDirectory(InstallDir);
        File.Copy(src, InstalledExe, true);
        foreach (var dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) })
            try { File.Delete(Path.Combine(dir, LegacyName + ".lnk")); } catch { }
        MakeShortcut(StartMenuLink);
        MakeShortcut(DesktopLink);
        WriteRegistry();
    }

    static void MakeShortcut(string path)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Нет WScript.Shell");
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic lnk = shell.CreateShortcut(path);
        lnk.TargetPath = InstalledExe;
        lnk.WorkingDirectory = InstallDir;
        lnk.IconLocation = InstalledExe + ",0";
        lnk.Description = AppName;
        lnk.Save();
    }

    static void WriteRegistry()
    {
        using var k = Registry.CurrentUser.CreateSubKey(RegKey);
        k.SetValue("DisplayName", AppName);
        k.SetValue("DisplayVersion", SelfUpdate.CurrentVersion);
        k.SetValue("Publisher", Publisher);
        k.SetValue("InstallLocation", InstallDir);
        k.SetValue("DisplayIcon", InstalledExe);
        k.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
        k.SetValue("URLInfoAbout", "https://github.com/botsystemtioxsit/launcher");
        k.SetValue("NoModify", 1, RegistryValueKind.DWord);
        k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    // После самообновления версия в списке программ должна быть актуальной
    public static void RefreshRegistry()
    {
        if (!IsInstalledCopy) return;
        try { WriteRegistry(); } catch { /* не критично */ }
    }

    public static void Uninstall()
    {
        var wipeData = MessageBox.Show("Удалить «" + AppName + "»?\n\nУдалить вместе с ним и сохранённые аккаунты (вход придётся пройти заново)?\n\nДа — удалить всё, Нет — оставить аккаунты.", AppName,
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (wipeData == MessageBoxResult.Cancel) return;
        try { File.Delete(StartMenuLink); } catch { }
        try { File.Delete(DesktopLink); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(RegKey, false); } catch { }
        if (wipeData == MessageBoxResult.Yes)
        {
            try { Directory.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BattleLauncher"), true); } catch { }
        }
        // работающий .exe себя не удалит — папку стирает отдельный короткий процесс после нашего выхода
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"{InstallDir}\"") { CreateNoWindow = true, UseShellExecute = false });
    }
}
