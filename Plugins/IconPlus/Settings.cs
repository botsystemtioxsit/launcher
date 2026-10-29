using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IconPlusApp;

// Настройки окна: что и каким цветом перекрашивать, какой набор выбран. Хранятся в %APPDATA%\BattleLauncher\icon-plus\settings.json.
sealed class IconSettings
{
    static readonly Regex HexRe = new("^#[0-9A-Fa-f]{6}$");

    public string Color { get; set; } = "#00F0FF";
    public string Style { get; set; } = Tint.Mono;       // mono — один цвет; hue — сменить оттенок, сохранив краски
    public int Strength { get; set; } = 100;             // 0–100
    public List<string> Enabled { get; set; } = new() { "folder", "drive-fixed", "drive-removable", "drive-optical", "thispc", "recycle-empty", "recycle-full", "network", "userfiles",
        "ft-text", "ft-doc", "ft-sheet", "ft-slides", "ft-pdf", "ft-archive", "ft-image", "ft-audio", "ft-video", "ft-web", "ft-code", "ft-script", "ft-all", "kf", "lnk-desktop", "lnk-desktop-all", "lnk-start", "lnk-start-all", "lnk-taskbar" };
    public bool AutoReport { get; set; } = true;          // после применения отправлять отчёт разработчику сам (названия ярлыков и причины; имя пользователя и компьютера вычищаются)
    public string? Pack { get; set; }                    // имя выбранного набора
    public Dictionary<string, Dictionary<string, string>> Maps { get; set; } = new();   // набор → (цель → файл значка): ручные правки подбора

    public void Normalize()
    {
        if (!HexRe.IsMatch(Color ?? "")) Color = "#00F0FF";
        Color = Color.ToUpperInvariant();
        if (Style is not (Tint.Mono or Tint.Hue)) Style = Tint.Mono;
        Strength = Math.Clamp(Strength, 0, 100);
        Enabled = (Enabled ?? new()).Where(id => Target.ById(id) != null).Distinct().ToList();
        Maps ??= new();
    }

    public static IconSettings Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<IconSettings>(File.ReadAllText(Paths.SettingsFile)) ?? new IconSettings();
            s.Normalize();
            return s;
        }
        catch { return new IconSettings(); }
    }

    public void Save()
    {
        try
        {
            Paths.Ensure();
            var tmp = Paths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this));
            File.Move(tmp, Paths.SettingsFile, true);
        }
        catch { }
    }
}
