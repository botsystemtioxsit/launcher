using System.IO;

namespace WallpaperPlusApp;

// Служебная проверка для CI (--selftest=<папка>): ввозит тестовые файлы из <папка>\samples, делает свои обои из шаблона, правит и пишет отчёт selftest.txt.
// Окна не показывает и обои на рабочий стол не ставит.
static class SelfTest
{
    public static void Run(string folder)
    {
        var log = new List<string>();
        void L(string s) => log.Add(s);
        try
        {
            Paths.Ensure();
            Library.EnsureBuiltins();
            L("встроенные: " + string.Join(", ", Library.List().Select(w => w.Id + (w.Params.Count > 0 ? "*" : ""))));
            var samples = Path.Combine(folder, "samples");
            if (Directory.Exists(samples))
                foreach (var f in Directory.EnumerateFileSystemEntries(samples).OrderBy(x => x, StringComparer.Ordinal))
                {
                    var (w, msg) = Library.InstallPath(f, "тест");
                    L($"{Path.GetFileName(f)} -> {(w != null ? "OK " + w.Id + " тип=" + w.Type + " файл=" + w.Entry : "отказ")} | {msg}");
                    if (w != null) L("   url: " + WallpaperView.EntryUrl(w, new WpSettings()));
                }
            var tpl = Library.Get("b-neon-clock");
            if (tpl == null) L("нет шаблона b-neon-clock");
            else
            {
                var (c, m) = Library.CreateFrom(tpl, "Тест красные", new Dictionary<string, string> { ["c1"] = "#FF0000", ["c2"] = "#FFAA00" });
                L("создание: " + (c?.Id ?? "нет") + " | " + m);
                if (c != null)
                {
                    File.WriteAllText(Path.Combine(folder, "created-id.txt"), c.Id);
                    L("url: " + WallpaperView.EntryUrl(c, new WpSettings()));
                    Library.UpdateValues(c, new Dictionary<string, string> { ["c1"] = "#00FF00" }, "Тест зелёные");
                    var c2 = Library.Get(c.Id);
                    L("после правки: " + c2?.Title + " c1=" + (c2 != null && c2.Values.TryGetValue("c1", out var v) ? v : "?"));
                }
            }
            L("всего обоев в библиотеке: " + Library.List().Count);
        }
        catch (Exception ex) { L("ОШИБКА: " + ex); }
        try { File.WriteAllLines(Path.Combine(folder, "selftest.txt"), log); } catch { }
    }
}
