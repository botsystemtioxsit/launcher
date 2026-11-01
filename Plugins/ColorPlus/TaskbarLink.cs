using System.IO;
using System.Text.Json;

namespace ColorPlusApp;

// Связь с Taskbar Plus. Окно обычной панели Windows Color Plus НЕ трогает никогда: цвет получают только острова нашей панели, а красит их
// движок Taskbar Plus. Color Plus лишь кладёт пожелание в color-plus-taskbar.json (пишет только он, читает только Taskbar Plus).
// Пока Taskbar Plus не запущен, панель остаётся как в Windows.
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
        public int BgOpacity { get; set; } = 85;
        public bool HideStroke { get; set; }
    }

    public static void Write(bool on, string color)
    {
        var link = new Link { Active = on, BgMode = on ? "solid" : "default", BgColor = color, BgOpacity = 85, HideStroke = false };
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = LinkPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(link));
            File.Move(tmp, LinkPath, true);
        }
        catch { /* Taskbar Plus не увидит изменений — панель останется как была */ }
    }

    // Работает ли движок панели (Taskbar Plus с включённым «Новым движком»)
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
            if (d.GetValueOrDefault("state") == "ready" && fresh && bg > 0) return (true, "Taskbar Plus работает: его острова получат цвет.");
        }
        catch { }
        return (false, "Taskbar Plus сейчас не запущен — панель Windows не меняется. Цвет появится на островах, когда вы запустите Taskbar Plus (там включён «Новый движок»).");
    }
}
