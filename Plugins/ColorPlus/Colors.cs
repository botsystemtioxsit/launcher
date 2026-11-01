namespace ColorPlusApp;

// Цветовая арифметика: разбор #RRGGBB, HSV и HSL, читаемый цвет текста, смешивание.
static class Col
{
    public static (int R, int G, int B) Parse(string hex)
    {
        hex = (hex ?? "").Trim().TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var v)) return (255, 42, 157);
        return ((v >> 16) & 255, (v >> 8) & 255, v & 255);
    }

    public static bool IsHex(string s) => System.Text.RegularExpressions.Regex.IsMatch(s ?? "", "^#[0-9A-Fa-f]{6}$");

    public static string Hex(int r, int g, int b) => $"#{Math.Clamp(r, 0, 255):X2}{Math.Clamp(g, 0, 255):X2}{Math.Clamp(b, 0, 255):X2}";

    public static (double H, double S, double V) ToHsv(string hex)
    {
        var (r, g, b) = Parse(hex);
        double R = r / 255.0, G = g / 255.0, B = b / 255.0;
        var max = Math.Max(R, Math.Max(G, B)); var min = Math.Min(R, Math.Min(G, B)); var d = max - min;
        double h = 0;
        if (d > 1e-9)
        {
            if (max == R) h = 60 * (((G - B) / d) % 6);
            else if (max == G) h = 60 * ((B - R) / d + 2);
            else h = 60 * ((R - G) / d + 4);
        }
        if (h < 0) h += 360;
        return (h, max <= 1e-9 ? 0 : d / max, max);
    }

    public static string FromHsv(double h, double s, double v)
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
        return Hex((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
    }

    public static (double H, double S, double L) ToHsl(string hex)
    {
        var (r, g, b) = Parse(hex);
        double R = r / 255.0, G = g / 255.0, B = b / 255.0;
        var max = Math.Max(R, Math.Max(G, B)); var min = Math.Min(R, Math.Min(G, B)); var d = max - min;
        var l = (max + min) / 2;
        double h = 0, s = 0;
        if (d > 1e-9)
        {
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == R) h = 60 * (((G - B) / d) % 6);
            else if (max == G) h = 60 * ((B - R) / d + 2);
            else h = 60 * ((R - G) / d + 4);
            if (h < 0) h += 360;
        }
        return (h, s, l);
    }

    public static (int R, int G, int B) FromHsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360; s = Math.Clamp(s, 0, 1); l = Math.Clamp(l, 0, 1);
        var c = (1 - Math.Abs(2 * l - 1)) * s; var x = c * (1 - Math.Abs(h / 60 % 2 - 1)); var m = l - c / 2;
        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return ((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
    }

    public static double Lum(string hex)
    {
        var (r, g, b) = Parse(hex);
        return (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
    }

    // Чёрный или белый — что лучше читается на этом цвете
    public static string Readable(string bg) => Lum(bg) > 0.58 ? "#111111" : "#FFFFFF";

    public static string Mix(string a, string b, double t)
    {
        var (r1, g1, b1) = Parse(a); var (r2, g2, b2) = Parse(b);
        return Hex((int)Math.Round(r1 + (r2 - r1) * t), (int)Math.Round(g1 + (g2 - g1) * t), (int)Math.Round(b1 + (b2 - b1) * t));
    }
}
