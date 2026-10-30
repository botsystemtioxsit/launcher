using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarPlusApp;

// Свой стиль одного острова. Пустое значение (−1 / null) — «как у всех».
class IslandStyle
{
    public int Radius { get; set; } = -1;        // скругление, 0 — прямые углы
    public string? Color { get; set; }           // #RRGGBB
    public int Opacity { get; set; } = -1;       // 0–100
    public string? Mode { get; set; }            // default | solid | clear | acrylic
    public bool Hidden { get; set; }             // острова нет, значки лежат прямо на панели

    [JsonIgnore] public bool IsEmpty => Radius < 0 && Color == null && Opacity < 0 && Mode == null && !Hidden;
}

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

    // Новый движок для Windows 11 (плавные края, прозрачность, размытие) — см. XamlEngine.cs
    public const string BgDefault = "default", BgSolid = "solid", BgClear = "clear", BgAcrylic = "acrylic";
    public bool UseXaml { get; set; }
    public string BgMode { get; set; } = BgDefault;     // default — как в Windows | solid — свой цвет | clear — прозрачный | acrylic — размытие
    public string BgColor { get; set; } = "#202020";    // #RRGGBB
    public int BgOpacity { get; set; } = 85;            // 0–100
    public bool HideStroke { get; set; }                // убрать тонкую линию панели
    public bool XamlConsentShown { get; set; }
    public bool AutoReport { get; set; } = true;        // после подключения к панели отправить отчёт разработчику сам (версии, настройки, названия элементов; без имени пользователя и компьютера)

    // Расположение трёх групп: виджеты, системная область (часы, сеть, трей, скрытые значки — всегда одним объектом), значки приложений.
    public const string ZoneDefault = "default", ZoneLeft = "left", ZoneCenter = "center", ZoneRight = "right";
    public string ZoneWidgets { get; set; } = ZoneDefault;
    public string ZoneSystem { get; set; } = ZoneDefault;
    public string ZoneApps { get; set; } = ZoneDefault;
    public string[] LayoutOrder { get; set; } = { "widgets", "system", "apps" };   // порядок слева направо
    public int EdgeMargin { get; set; } = 12;                                       // отступ от края для групп «слева» и «справа»

    // Стили островов по ключам: "system", "widgets", "apps" (все значки сразу), "app.<hex>" (один значок).
    public Dictionary<string, IslandStyle> Islands { get; set; } = new();

    [JsonIgnore] public bool LayoutActive => ZoneWidgets != ZoneDefault || ZoneSystem != ZoneDefault || ZoneApps != ZoneDefault;

    [JsonIgnore] public bool Active => Shape != ShapeNone || UseXaml;

    public AppearanceSettings Clone()
    {
        var c = (AppearanceSettings)MemberwiseClone();
        c.LayoutOrder = (string[])LayoutOrder.Clone();
        c.Islands = Islands.ToDictionary(kv => kv.Key, kv => new IslandStyle { Radius = kv.Value.Radius, Color = kv.Value.Color, Opacity = kv.Value.Opacity, Mode = kv.Value.Mode, Hidden = kv.Value.Hidden });
        return c;
    }

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
        if (BgMode is not (BgDefault or BgSolid or BgClear or BgAcrylic)) BgMode = BgDefault;
        if (!System.Text.RegularExpressions.Regex.IsMatch(BgColor ?? "", "^#[0-9A-Fa-f]{6}$")) BgColor = "#202020";
        BgOpacity = Clamp(BgOpacity, 0, 100);

        static string Zone(string? z) => (z is ZoneLeft or ZoneCenter or ZoneRight) ? z! : ZoneDefault;
        ZoneWidgets = Zone(ZoneWidgets); ZoneSystem = Zone(ZoneSystem); ZoneApps = Zone(ZoneApps);
        var order = (LayoutOrder ?? Array.Empty<string>()).Where(g => g is "widgets" or "system" or "apps").Distinct().ToList();
        foreach (var g in new[] { "widgets", "system", "apps" }) if (!order.Contains(g)) order.Add(g);
        LayoutOrder = order.ToArray();
        EdgeMargin = Clamp(EdgeMargin, 0, 400);

        Islands ??= new();
        foreach (var k in Islands.Keys.ToList())
        {
            var st = Islands[k];
            if (st == null || st.IsEmpty) { Islands.Remove(k); continue; }
            st.Radius = st.Radius < 0 ? -1 : Clamp(st.Radius, 0, 100);
            st.Opacity = st.Opacity < 0 ? -1 : Clamp(st.Opacity, 0, 100);
            if (st.Mode is not (null or BgDefault or BgSolid or BgClear or BgAcrylic)) st.Mode = null;
            if (st.Color != null && !System.Text.RegularExpressions.Regex.IsMatch(st.Color, "^#[0-9A-Fa-f]{6}$")) st.Color = null;
        }
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
