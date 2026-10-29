using System.IO;
using System.Text.Json;

namespace ColorPlusApp;

// Связь с Taskbar Plus. Фон панели задач Windows 11 рисует сама панель (XAML внутри Проводника); поменять его может только
// движок Taskbar Plus. Color Plus не внедряется в Проводник сам, а кладёт пожелание в color-plus-taskbar.json,
// которое Taskbar Plus читает и применяет (читает только он, пишет только Color Plus).
static class TaskbarLink
{
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BattleLauncher");
    static readonly string LinkPath = Path.Combine(Dir, "color-plus-taskbar.json");
    static readonly string EngineStatusPath = Path.Combine(Dir, "taskbar-plus-engine-status.txt");

    class Link
    {
        public bool Active { get; set; }
        public string BgMode { get; set; } = "default";   // default | solid | clear | acrylic — как в движке Taskbar Plus
        public string BgColor { get; set; } = "#101018";
        public int BgOpacity { get; set; } = 60;
        public bool HideStroke { get; set; }
    }

    public static void Write(ColorSettings s)
    {
        var on = s.TaskbarOn && s.TaskbarMode != ColorSettings.TbDefault;
        var link = new Link
        {
            Active = on,
            // «Размытая»: фон панели делаем прозрачным, размытие за ним рисует Windows (см. WindowEffects.ApplyTaskbar)
            BgMode = s.TaskbarMode switch { ColorSettings.TbSolid => "solid", ColorSettings.TbDefault => "default", _ => "clear" },
            BgColor = s.TaskbarColor,
            BgOpacity = s.TaskbarOpacity,
            HideStroke = on && s.TaskbarHideStroke,
        };
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = LinkPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(link));
            File.Move(tmp, LinkPath, true);
        }
        catch { /* Taskbar Plus не увидит изменений — панель останется как была */ }
    }

    // Работает ли движок панели (Taskbar Plus с включённым «Новым движком»).
    public static (bool Ready, string Text) EngineState()
    {
        try
        {
            using var fs = new FileStream(EngineStatusPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var r = new StreamReader(fs);
            var d = new Dictionary<string, string>();
            string? line;
            while ((line = r.ReadLine()) != null)
            {
                var i = line.IndexOf('=');
                if (i > 0) d[line[..i]] = line[(i + 1)..];
            }
            long.TryParse(d.GetValueOrDefault("tick"), out var tick);
            int.TryParse(d.GetValueOrDefault("bgfill"), out var bg);
            var fresh = Environment.TickCount64 - tick < 15_000;
            if (d.GetValueOrDefault("state") == "ready" && fresh)
                return bg > 0 ? (true, "Движок панели (Taskbar Plus) работает — фон панели можно менять.")
                              : (false, "Движок панели подключился, но фон панели пока не нашёл. Размытие и прозрачность панели могут не сработать.");
        }
        catch { }
        return (false, "Движок панели не запущен. Чтобы менять фон панели Windows 11, запустите Taskbar Plus и включите там «Новый движок».");
    }
}
