using System.IO;
using System.Text.Json;

namespace CursorPlusApp;

// Что выбрал человек. Файл: %APPDATA%\BattleLauncher\cursor-plus\settings.json
sealed class CursorSettings
{
    // Курсор
    public string Shape { get; set; } = "classic";        // windows (как в Windows, только цвет) | classic | round | neon | dot | ring | cross | ghost | star | mine:<id>
    public string Fill { get; set; } = "#FF2A9D";         // основной цвет
    public string Outline { get; set; } = "auto";         // auto (тёмная обводка) | #RRGGBB
    public int Size { get; set; } = 100;                  // 100–300 %
    public bool All { get; set; } = true;                 // перекрасить и остальные указатели (рука, текст, размеры…)

    // Шлейф
    public string Trail { get; set; } = "comet";          // none | comet | sparks | glow | stars | bubbles | hearts | ghost
    public string TrailColorMode { get; set; } = "cursor";   // cursor (цвет курсора) | rainbow | two | custom
    public string TrailColor { get; set; } = "#00F0FF";
    public string TrailColor2 { get; set; } = "#FF2A9D";
    public int TrailLength { get; set; } = 50;            // 0–100
    public int TrailSize { get; set; } = 40;              // 0–100

    // Клик
    public string Click { get; set; } = "none";           // none | ring | burst

    public bool Applied { get; set; }                     // курсор записан в Windows
    public bool OverlayOn { get; set; }                   // шлейф и клики включены (плагин остаётся в фоне)

    static readonly string[] Shapes = { "windows", "classic", "round", "neon", "dot", "ring", "cross", "ghost", "star" };
    static readonly string[] Trails = { "none", "comet", "sparks", "glow", "stars", "bubbles", "hearts", "ghost" };
    static readonly string[] Modes = { "cursor", "rainbow", "two", "custom" };
    static readonly string[] Clicks = { "none", "ring", "burst" };

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BattleLauncher", "cursor-plus");
    static string PathFile => Path.Combine(Dir, "settings.json");

    public void Normalize()
    {
        if (!(Shapes.Contains(Shape) || (Shape.StartsWith("mine:", StringComparison.Ordinal) && MineStore.IsId(Shape[5..])))) Shape = "classic";
        if (!Col.IsHex(Fill)) Fill = "#FF2A9D";
        Fill = Fill.ToUpperInvariant();
        if (Outline != "auto" && !Col.IsHex(Outline)) Outline = "auto";
        Size = Math.Clamp(Size, 100, 300);
        if (!Trails.Contains(Trail)) Trail = "none";
        if (!Modes.Contains(TrailColorMode)) TrailColorMode = "cursor";
        if (!Col.IsHex(TrailColor)) TrailColor = "#00F0FF";
        if (!Col.IsHex(TrailColor2)) TrailColor2 = "#FF2A9D";
        TrailColor = TrailColor.ToUpperInvariant(); TrailColor2 = TrailColor2.ToUpperInvariant();
        TrailLength = Math.Clamp(TrailLength, 0, 100);
        TrailSize = Math.Clamp(TrailSize, 0, 100);
        if (!Clicks.Contains(Click)) Click = "none";
    }

    public static CursorSettings Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<CursorSettings>(File.ReadAllText(PathFile)) ?? new CursorSettings();
            s.Normalize();
            return s;
        }
        catch { return new CursorSettings(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = PathFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this));
            File.Move(tmp, PathFile, true);
        }
        catch { }
    }

    public CursorSettings Clone() => (CursorSettings)MemberwiseClone();

    // Нужно ли держать плагин в фоне: шлейф или эффект клика выбран.
    public bool WantsOverlay => Trail != "none" || Click != "none";

    // Цвет обводки курсора: выбранный или тёмный оттенок основного цвета.
    public (int R, int G, int B) OutlineRgb()
    {
        if (Outline != "auto") return Col.Parse(Outline);
        var f = Col.Parse(Fill);
        return Col.Mix(f, (8, 6, 16), 0.88);
    }
}
