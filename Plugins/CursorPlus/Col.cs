namespace CursorPlusApp;

// Цвета: разбор #RRGGBB, оттенок → RGB, смешивание.
static class Col
{
    public static (int R, int G, int B) Parse(string hex)
    {
        hex = (hex ?? "").Trim().TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var v)) return (255, 42, 157);
        return ((v >> 16) & 255, (v >> 8) & 255, v & 255);
    }

    public static bool IsHex(string? s) => System.Text.RegularExpressions.Regex.IsMatch(s ?? "", "^#[0-9A-Fa-f]{6}$");

    public static string Hex(int r, int g, int b) => $"#{Math.Clamp(r, 0, 255):X2}{Math.Clamp(g, 0, 255):X2}{Math.Clamp(b, 0, 255):X2}";

    // h 0–360, s и v 0–1
    public static (int R, int G, int B) FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360; s = Math.Clamp(s, 0, 1); v = Math.Clamp(v, 0, 1);
        var c = v * s; var x = c * (1 - Math.Abs(h / 60 % 2 - 1)); var m = v - c;
        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return ((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
    }

    public static (int R, int G, int B) Mix((int R, int G, int B) a, (int R, int G, int B) b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return ((int)Math.Round(a.R + (b.R - a.R) * t), (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));
    }

    public static System.Windows.Media.Color ToColor(string hex)
    {
        var (r, g, b) = Parse(hex);
        return System.Windows.Media.Color.FromRgb((byte)r, (byte)g, (byte)b);
    }
}
