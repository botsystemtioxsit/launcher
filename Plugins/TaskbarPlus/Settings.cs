using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarPlusApp;

// Внешний вид панели: форма и острова. Хранится в %APPDATA%\BattleLauncher\taskbar-plus-settings.json.
class AppearanceSettings
{
    public const string ShapeNone = "none", ShapePill = "pill", ShapeTab = "tab", ShapeIslands = "islands";

    static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BattleLauncher", "taskbar-plus-settings.json");

    public string Shape { get; set; } = ShapeNone;      // none | pill | tab | islands
    public int Radius { get; set; } = 14;               // скругление, px при 100% масштаба
    public int SideMargin { get; set; } = 0;            // отступ слева и справа (плавающая панель и «вкладка»)
    public int BottomGap { get; set; } = 6;             // зазор под панелью (плавающая панель, плавающие острова)
    public string IslandStyle { get; set; } = "float";  // float — отдельные «пилюли»; tab — вкладки, прижатые к низу
    public int IslandPadding { get; set; } = 8;         // поля вокруг группы значков
    public string IslandMode { get; set; } = "each";    // each — остров вокруг каждого значка; group — вокруг группы соседних значков
    public int IslandGap { get; set; } = 8;             // зазор между соседними островами (режим «каждый значок»)
    public bool KeepEdges { get; set; } = true;         // у плавающей панели и «вкладки»: часы, трей и виджеты остаются отдельными островами, а не обрезаются
    public bool BackgroundHintShown { get; set; }

    [JsonIgnore] public bool Active => Shape != ShapeNone;

    public AppearanceSettings Clone() => (AppearanceSettings)MemberwiseClone();

    static int Clamp(int v, int min, int max) => Math.Min(max, Math.Max(min, v));

    // файл могли править руками — приводим всё к допустимому
    void Normalize()
    {
        if (Shape is not (ShapeNone or ShapePill or ShapeTab or ShapeIslands)) Shape = ShapeNone;
        if (IslandStyle is not ("float" or "tab")) IslandStyle = "float";
        if (IslandMode is not ("each" or "group")) IslandMode = "each";
        IslandGap = Clamp(IslandGap, 0, 40);
        Radius = Clamp(Radius, 0, 30);
        SideMargin = Clamp(SideMargin, 0, 600);
        BottomGap = Clamp(BottomGap, 0, 20);
        IslandPadding = Clamp(IslandPadding, 0, 40);
    }

    public static AppearanceSettings Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<AppearanceSettings>(File.ReadAllText(FilePath)) ?? new AppearanceSettings();
            s.Normalize();
            return s;
        }
        catch { return new AppearanceSettings(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this));
            File.Move(tmp, FilePath, true); // запись целиком или никак
        }
        catch { /* не вышло сохранить — настройки не запомнятся */ }
    }

    public static void Delete() { try { File.Delete(FilePath); } catch { } }
}
