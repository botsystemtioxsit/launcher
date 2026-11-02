using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SkinLoaderApp;

sealed record PluginDef(string Part, string Id, string Name, string Exe, string Folder);

// Наши плагины: где лежит программа и файл настроек, как попросить её выйти. Skin Loader с ними не «сливается» — он пишет
// их собственные файлы настроек (только допустимые поля) и запускает их с теми же ключами, что и CI-проверки плагинов.
static class Plugins
{
    public static readonly PluginDef[] All =
    {
        new("taskbar", "taskbar-plus", "Taskbar Plus", "TaskbarPlus.exe", "TaskbarPlus"),
        new("color", "color-plus", "Color Plus", "ColorPlus.exe", "ColorPlus"),
        new("icons", "icon-plus", "Icon Plus", "IconPlus.exe", "IconPlus"),
        new("wallpaper", "wallpaper-plus", "Wallpaper Plus", "WallpaperPlus.exe", "WallpaperPlus"),
        new("cursor", "cursor-plus", "Cursor Plus", "CursorPlus.exe", "CursorPlus"),
    };

    public static PluginDef ForPart(string part) => All.First(p => p.Part == part);

    // %LOCALAPPDATA%\Programs — туда лаунчер ставит плагины (NativeInstaller); для проверок можно подменить
    public static string? ProgramsDirOverride;
    public static string ProgramsDir => ProgramsDirOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
    public static string? DataDirOverride;   // только для проверок: чтобы не трогать настоящие настройки
    public static string DataDir => DataDirOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BattleLauncher");

    public static string? ExePath(PluginDef d)
    {
        var p = Path.Combine(ProgramsDir, d.Folder, d.Exe);
        return File.Exists(p) ? p : null;
    }

    public static string SettingsFile(string part) => part switch
    {
        "taskbar" => Path.Combine(DataDir, "taskbar-plus-settings.json"),
        "color" => Path.Combine(DataDir, "color-plus-settings.json"),
        "icons" => Path.Combine(DataDir, "icon-plus", "settings.json"),
        "wallpaper" => Path.Combine(DataDir, "wallpaper-plus-settings.json"),
        "cursor" => Path.Combine(DataDir, "cursor-plus", "settings.json"),
        _ => throw new ArgumentException("неизвестная часть"),
    };

    public static JsonObject ReadSettings(string part)
    {
        try { return JsonNode.Parse(File.ReadAllText(SettingsFile(part))) as JsonObject ?? new JsonObject(); }
        catch { return new JsonObject(); }
    }

    // Записывает в файл настроек плагина только перечисленные поля; всё остальное, что там было, остаётся как есть
    public static void MergeSettings(string part, IReadOnlyDictionary<string, object> values)
    {
        var file = SettingsFile(part);
        var root = ReadSettings(part);
        foreach (var kv in values) root[kv.Key] = JsonSerializer.SerializeToNode(kv.Value);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString());
        File.Move(tmp, file, true);
    }

    static bool Running(PluginDef d)
    {
        try { return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(d.Exe)).Length > 0; }
        catch { return false; }
    }

    // Просим работающий плагин выйти (он сам вернёт всё, что менял), ждём до waitMs. true — его больше нет.
    public static bool AskQuit(PluginDef d, int waitMs = 8000)
    {
        if (!Running(d)) return true;
        try { using var ev = EventWaitHandle.OpenExisting("GhostShell.Plugin.Quit." + d.Id); ev.Set(); } catch { }
        var until = DateTime.UtcNow.AddMilliseconds(waitMs);
        while (DateTime.UtcNow < until && Running(d)) Thread.Sleep(150);
        return !Running(d);
    }

    public static Process? Start(PluginDef d, string args)
    {
        var exe = ExePath(d);
        if (exe == null) return null;
        return Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! });
    }

    // Ждёт, пока условие станет истинным (или время выйдет)
    public static bool WaitFor(Func<bool> cond, int timeoutMs)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until)
        {
            try { if (cond()) return true; } catch { }
            Thread.Sleep(250);
        }
        try { return cond(); } catch { return false; }
    }
}
