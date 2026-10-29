using System.IO;
using System.Text.Json;

namespace TaskbarPlusApp;

// Фон панели (прозрачность, размытие, цвет, тонкая линия) задаёт плагин Color Plus: он кладёт пожелание в
// color-plus-taskbar.json. Здесь файл только читается; пишет его один Color Plus.
static class ColorPlusLink
{
    public record Bg(bool Active, string Mode, string Color, int Opacity, bool HideStroke);

    static readonly string LinkPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BattleLauncher", "color-plus-taskbar.json");
    static readonly object gate = new();
    static DateTime lastWrite = DateTime.MinValue;
    static Bg cached = new(false, "default", "#101018", 60, false);

    public static Bg Current { get { Refresh(); lock (gate) return cached; } }

    // true — файл изменился с прошлого чтения (или впервые прочитан).
    public static bool Refresh()
    {
        try
        {
            var info = new FileInfo(LinkPath);
            var stamp = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue;
            lock (gate)
            {
                if (stamp == lastWrite) return false;
                lastWrite = stamp;
                if (!info.Exists) { cached = new Bg(false, "default", "#101018", 60, false); return true; }
            }
            using var doc = JsonDocument.Parse(File.ReadAllText(LinkPath));
            var r = doc.RootElement;
            string Str(string name, string def) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? def : def;
            var mode = Str("BgMode", "default");
            if (mode is not ("default" or "solid" or "clear" or "acrylic")) mode = "default";
            var color = Str("BgColor", "#101018");
            if (!System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$")) color = "#101018";
            var opacity = r.TryGetProperty("BgOpacity", out var o) && o.TryGetInt32(out var oi) ? Math.Min(100, Math.Max(0, oi)) : 60;
            var active = r.TryGetProperty("Active", out var a) && a.ValueKind == JsonValueKind.True;
            var hide = r.TryGetProperty("HideStroke", out var h) && h.ValueKind == JsonValueKind.True;
            lock (gate) cached = new Bg(active, mode, color, opacity, hide);
            return true;
        }
        catch
        {
            lock (gate) lastWrite = DateTime.MinValue; // файл как раз пишется или повреждён — прочитаем в следующий раз
            return false;
        }
    }
}
