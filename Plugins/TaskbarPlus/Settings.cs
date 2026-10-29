using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TaskbarPlusApp;

// Внешний вид панели: форма, острова, размытие. Хранится в %APPDATA%\BattleLauncher\taskbar-plus-settings.json.
class AppearanceSettings
{
    public const string ShapeNone = "none", ShapePill = "pill", ShapeTab = "tab", ShapeIslands = "islands";
    public const string BackdropNone = "none", BackdropClear = "clear", BackdropBlur = "blur", BackdropAcrylic = "acrylic";

    static readonly Regex Hex = new(@"^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);
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
    public string Backdrop { get; set; } = BackdropNone;
    public int TintOpacity { get; set; } = 0;           // 0..255
    public string TintColor { get; set; } = "#000000";
    public bool BackgroundHintShown { get; set; }

    [JsonIgnore] public bool Active => Shape != ShapeNone || Backdrop != BackdropNone;

    public AppearanceSettings Clone() => (AppearanceSettings)MemberwiseClone();

    static int Clamp(int v, int min, int max) => Math.Min(max, Math.Max(min, v));

    // файл могли править руками — приводим всё к допустимому
    void Normalize()
    {
        if (Shape is not (ShapeNone or ShapePill or ShapeTab or ShapeIslands)) Shape = ShapeNone;
        if (IslandStyle is not ("float" or "tab")) IslandStyle = "float";
        if (IslandMode is not ("each" or "group")) IslandMode = "each";
        IslandGap = Clamp(IslandGap, 0, 40);
        if (Backdrop is not (BackdropNone or BackdropClear or BackdropBlur or BackdropAcrylic)) Backdrop = BackdropNone;
        if (!Hex.IsMatch(TintColor ?? "")) TintColor = "#000000";
        Radius = Clamp(Radius, 0, 40);
        SideMargin = Clamp(SideMargin, 0, 600);
        BottomGap = Clamp(BottomGap, 0, 60);
        IslandPadding = Clamp(IslandPadding, 0, 40);
        TintOpacity = Clamp(TintOpacity, 0, 255);
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

    // ABGR для вызова Windows: прозрачность + цвет подложки
    public uint TintAbgr()
    {
        var c = Hex.IsMatch(TintColor) ? TintColor : "#000000";
        var r = Convert.ToByte(c.Substring(1, 2), 16);
        var g = Convert.ToByte(c.Substring(3, 2), 16);
        var b = Convert.ToByte(c.Substring(5, 2), 16);
        return ((uint)TintOpacity << 24) | ((uint)b << 16) | ((uint)g << 8) | r;
    }
}
