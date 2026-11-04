using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace BattleLauncher;

// «Окружение»: лаунчер сам стартует вместе с Windows, спрашивает подтверждение и запускает выбранные плагины.
// Запускаются только плагины, которые умеют работать в фоне (--background): остальные открыли бы своё окно.
static class Boot
{
    // Плагины с режимом --background (применяют своё и сидят в фоне без окна)
    public static readonly string[] BackgroundIds = { "taskbar-plus", "wallpaper-plus", "cursor-plus", "color-plus" };
    public const string RunName = "GhostShellLauncher";
    public const string Arg = "--boot";

    public class Config
    {
        public bool Autostart { get; set; }
        public List<string> Items { get; set; } = new();
    }

    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BattleLauncher", "boot.json");

    public static Config Load()
    {
        if (AccountStore.NoDisk) return new Config();
        try
        {
            var c = JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath)) ?? new Config();
            c.Items = c.Items.Where(i => BackgroundIds.Contains(i)).Distinct().ToList();   // чужие значения из файла не берём
            return c;
        }
        catch { return new Config(); }
    }

    public static void Save(Config c)
    {
        if (AccountStore.NoDisk) return;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(c));
        SetAutostart(c.Autostart);
    }

    public static bool AutostartEnabled()
    {
        try { using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); return k?.GetValue(RunName) is string; }
        catch { return false; }
    }

    static void SetAutostart(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (on) k.SetValue(RunName, "\"" + Environment.ProcessPath + "\" " + Arg);
            else k.DeleteValue(RunName, false);
        }
        catch { /* нет доступа к реестру — автозапуск просто не включится */ }
    }

    // Выбранные и установленные плагины
    public static List<(AppDef Def, NativeInfo Info)> Resolve(Config c)
    {
        var list = new List<(AppDef, NativeInfo)>();
        foreach (var id in c.Items)
        {
            var def = Catalog.Plugins.FirstOrDefault(p => p.Id == id);
            if (def == null) continue;
            var n = NativeInstaller.Find(def);
            if (n != null) list.Add((def, n));
        }
        return list;
    }

    public static void Start(NativeInfo n)
    {
        var psi = new ProcessStartInfo(n.Exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(n.Exe)! };
        psi.ArgumentList.Add("--background");
        Process.Start(psi);
    }
}
