using System.IO;

namespace WallpaperPlusApp;

static class Paths
{
    public static readonly string Data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GhostShell", "WallpaperPlus");
    public static string Library => Path.Combine(Data, "library");
    public static string Downloads => Path.Combine(Data, "downloads");
    public static string HostWeb => Path.Combine(Data, "webview-host");
    public static string SiteWeb => Path.Combine(Data, "webview-site");
    public static string LogFile => Path.Combine(Data, "log.txt");
    public static readonly string Config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BattleLauncher");
    public static string SettingsFile => Path.Combine(Config, "wallpaper-plus-settings.json");

    public static void Ensure() { foreach (var d in new[] { Data, Library, Downloads, Config }) Directory.CreateDirectory(d); }

    static readonly object logGate = new();
    public static void Log(string text)
    {
        try
        {
            lock (logGate)
            {
                Directory.CreateDirectory(Data);
                var f = new FileInfo(LogFile);
                if (f.Exists && f.Length > 400_000) f.Delete();
                File.AppendAllText(LogFile, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + text + Environment.NewLine);
            }
        }
        catch { }
    }
}
