using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ColorPlusApp;

// Правило для одной программы (по имени процесса, без «.exe»).
class AppRule
{
    public string Mode { get; set; } = "inherit";   // inherit — как у всех | skip — не трогать | custom — свой способ, прозрачность и размытие
    public string? WinMode { get; set; }            // background | alpha | none; null — как у всех (только при Mode = custom)
    public int Opacity { get; set; } = 100;
    public string? Backdrop { get; set; }           // none | blur | acrylic | mica | tabbed; null — как у всех
}

// Настройки Color Plus. Хранятся в %APPDATA%\BattleLauncher\color-plus-settings.json.
class ColorSettings
{
    public const string BdNone = "none", BdBlur = "blur", BdAcrylic = "acrylic", BdMica = "mica", BdTabbed = "tabbed";
    public const string TbDefault = "default", TbClear = "clear", TbBlur = "blur", TbSolid = "solid";
    public const string WmBackground = "background", WmAlpha = "alpha", WmNone = "none";

    static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BattleLauncher", "color-plus-settings.json");
    static readonly Regex HexRe = new("^#[0-9A-Fa-f]{6}$");

    // ---- окна всех программ ----
    public bool WindowsOn { get; set; }
    // Для всех окон: alpha — окно целиком прозрачнее; none — только рамка, цвет, подложка. «Только фон» (background) — ТОЛЬКО для программ,
    // которые человек выбрал сам в списке: цветовой ключ делает прозрачные места «сквозными» для мыши, на всех окнах он ломает программы (1.1.0).
    public string WindowMode { get; set; } = WmNone;
    public int WindowOpacity { get; set; } = 90;          // 20–100: насколько окно «плотное»; меньше — сквозь него виден рабочий стол
    public string Backdrop { get; set; } = BdNone;        // что рисуется за окном
    public string Tint { get; set; } = "#101018";         // оттенок стекла (размытие и акрил)
    public int TintOpacity { get; set; } = 60;            // 0–100
    public bool ExtendFrame { get; set; }                 // растянуть эффект на всё окно (не всем программам это нравится)
    public bool DarkTitle { get; set; }                   // тёмные заголовки окон
    public string? TitleColor { get; set; }               // цвет заголовка окна (Windows 11), null — не менять
    public string? TitleTextColor { get; set; }
    public Dictionary<string, AppRule> Apps { get; set; } = new();

    // ---- панель задач ----
    public bool TaskbarOn { get; set; }
    public string TaskbarMode { get; set; } = TbDefault;  // default | clear | blur | solid
    public string TaskbarColor { get; set; } = "#101018";
    public int TaskbarOpacity { get; set; } = 60;
    public bool TaskbarHideStroke { get; set; }

    // ---- выделение и акцент ----
    public bool SelectionOn { get; set; }
    public string SelectionColor { get; set; } = "#0078D7";
    public string SelectionTextColor { get; set; } = "#FFFFFF";
    public bool AccentOn { get; set; }
    public string AccentColor { get; set; } = "#00F0FF";

    public bool BackgroundHintShown { get; set; }

    [JsonIgnore] public bool Active => WindowsOn || TaskbarOn || SelectionOn || AccentOn;

    public ColorSettings Clone()
    {
        var c = (ColorSettings)MemberwiseClone();
        c.Apps = Apps.ToDictionary(kv => kv.Key, kv => new AppRule { Mode = kv.Value.Mode, WinMode = kv.Value.WinMode, Opacity = kv.Value.Opacity, Backdrop = kv.Value.Backdrop });
        return c;
    }

    static int Clamp(int v, int min, int max) => Math.Min(max, Math.Max(min, v));
    static string Hex(string? v, string fallback) => v != null && HexRe.IsMatch(v) ? v.ToUpperInvariant() : fallback;
    static string? HexOrNull(string? v) => v != null && HexRe.IsMatch(v) ? v.ToUpperInvariant() : null;

    // файл могли править руками — приводим всё к допустимому
    void Normalize()
    {
        WindowOpacity = Clamp(WindowOpacity, 20, 100);
        TintOpacity = Clamp(TintOpacity, 0, 100);
        TaskbarOpacity = Clamp(TaskbarOpacity, 0, 100);
        if (WindowMode is not (WmAlpha or WmNone)) WindowMode = WmNone;   // «только фон» для всех окон из 1.1.0 снят: он ломал Steam и Wallpaper Engine
        if (Backdrop is not (BdNone or BdBlur or BdAcrylic or BdMica or BdTabbed)) Backdrop = BdNone;
        if (TaskbarMode is not (TbDefault or TbClear or TbBlur or TbSolid)) TaskbarMode = TbDefault;
        Tint = Hex(Tint, "#101018");
        TaskbarColor = Hex(TaskbarColor, "#101018");
        SelectionColor = Hex(SelectionColor, "#0078D7");
        SelectionTextColor = Hex(SelectionTextColor, "#FFFFFF");
        AccentColor = Hex(AccentColor, "#00F0FF");
        TitleColor = HexOrNull(TitleColor);
        TitleTextColor = HexOrNull(TitleTextColor);
        Apps ??= new();
        foreach (var k in Apps.Keys.ToList())
        {
            var r = Apps[k];
            if (r == null || r.Mode == "inherit") { Apps.Remove(k); continue; }
            if (r.Mode is not ("skip" or "custom")) { Apps.Remove(k); continue; }
            r.Opacity = Clamp(r.Opacity, 20, 100);
            if (r.WinMode is not (null or WmBackground or WmAlpha or WmNone)) r.WinMode = null;
            if (r.Backdrop is not (null or BdNone or BdBlur or BdAcrylic or BdMica or BdTabbed)) r.Backdrop = null;
        }
    }

    public static ColorSettings Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<ColorSettings>(File.ReadAllText(FilePath)) ?? new ColorSettings();
            s.Normalize();
            return s;
        }
        catch { return new ColorSettings(); }
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
}
