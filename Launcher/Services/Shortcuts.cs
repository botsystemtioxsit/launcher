using System.IO;

namespace BattleLauncher;

// Значки приложений на рабочем столе: человек видит, что приложение скачалось, и открывает его оттуда,
// а не через список в лаунчере. Имя значка — название приложения (как у ярлыка, который делает сам установщик).
static class Shortcuts
{
    static string DesktopDir => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    static string FileFor(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return Path.Combine(DesktopDir, name + ".lnk");
    }

    public static bool Exists(string name) => File.Exists(FileFor(name));

    public static void Create(string name, string target, string description)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Нет WScript.Shell");
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic lnk = shell.CreateShortcut(FileFor(name));
        lnk.TargetPath = target;
        lnk.WorkingDirectory = Path.GetDirectoryName(target)!;
        lnk.IconLocation = target + ",0";
        lnk.Description = description;
        lnk.Save();
    }

    public static void Remove(string name)
    {
        try { File.Delete(FileFor(name)); } catch { /* значок могли уже убрать */ }
    }
}
