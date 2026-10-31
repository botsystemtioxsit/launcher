using System.IO;
using System.Text.Json;

namespace WallpaperPlusApp;

// Настройки Wallpaper Plus: %APPDATA%\BattleLauncher\wallpaper-plus-settings.json
sealed class WpSettings
{
    public string Active { get; set; } = "";               // идентификатор включённых обоев; пусто — обои плагина выключены
    public bool AllMonitors { get; set; } = true;          // одни и те же обои на всех мониторах (выкл. — только на главном)
    public bool PauseOnFullscreen { get; set; } = true;    // пауза, когда идёт полноэкранная игра или видео
    public bool PauseWhenCovered { get; set; } = true;     // пауза, когда окно закрывает весь рабочий стол (обои не видны)
    public bool Mute { get; set; } = true;                 // звук у обоев с видео выключен
    public string Fit { get; set; } = "cover";             // cover — заполнить, contain — вписать, fill — растянуть
    public int Fps { get; set; } = 60;                     // ограничение частоты кадров у встроенных обоев (15–60)
    public bool Autostart { get; set; }                    // запускаться вместе с Windows
    public bool Paused { get; set; }                       // пауза вручную

    public void Normalize()
    {
        Active ??= "";
        if (Fit is not ("cover" or "contain" or "fill")) Fit = "cover";
        Fps = Math.Clamp(Fps, 15, 60);
    }

    public static WpSettings Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<WpSettings>(File.ReadAllText(Paths.SettingsFile)) ?? new WpSettings();
            s.Normalize();
            return s;
        }
        catch { return new WpSettings(); }
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

    public WpSettings Clone() => (WpSettings)MemberwiseClone();
}
