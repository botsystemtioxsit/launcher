using System.IO;
using System.Text.Json;

namespace ColorPlusApp;

// Что выбрал человек: один цвет и две галочки. Файл: %APPDATA%\BattleLauncher\color-plus-settings.json
sealed class ColorSettings
{
    public string Color { get; set; } = "#FF2A9D";
    public bool Windows { get; set; } = true;      // окна (заголовки и рамки), выделение, акцент Windows
    public bool Taskbar { get; set; } = true;      // острова нашей панели (Taskbar Plus)
    public bool Applied { get; set; }              // было ли нажато «Применить»

    static readonly string PathFile = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BattleLauncher", "color-plus-settings.json");

    public static ColorSettings Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<ColorSettings>(File.ReadAllText(PathFile)) ?? new ColorSettings();
            if (!Col.IsHex(s.Color)) s.Color = "#FF2A9D";
            s.Color = s.Color.ToUpperInvariant();
            return s;
        }
        catch { return new ColorSettings(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathFile)!);
            File.WriteAllText(PathFile, JsonSerializer.Serialize(this));
        }
        catch { }
    }

    public ColorSettings Clone() => (ColorSettings)MemberwiseClone();
}
