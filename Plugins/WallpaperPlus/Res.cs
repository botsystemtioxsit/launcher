using System.IO;
using System.Reflection;

namespace WallpaperPlusApp;

// Служебные файлы внутри программы: gs.js (данные компьютера для страниц обоев) и compose.html (фон + слой поверх).
static class Res
{
    public static string Text(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var s = asm.GetManifestResourceStream("res/" + name);
        if (s == null) return "";
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    static string? gs;
    public static string GsJs => gs ??= Text("gs.js");

    public static string SystemDir => Path.Combine(Paths.Data, "system");

    // compose.html кладётся в отдельную папку: на неё смотрит виртуальный адрес составных обоев
    public static void EnsureSystem()
    {
        try
        {
            Directory.CreateDirectory(SystemDir);
            var f = Path.Combine(SystemDir, "compose.html");
            var text = Text("compose.html");
            if (text.Length > 0 && (!File.Exists(f) || File.ReadAllText(f) != text)) File.WriteAllText(f, text);
        }
        catch (Exception ex) { Paths.Log("служебные файлы: " + ex.Message); }
    }
}
