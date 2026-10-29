namespace IconPlusApp;

// Перекраска значка в один цвет, как «тема значков» на Android: форма и свет сохраняются, цвет — выбранный.
static class Tint
{
    public const string Mono = "mono", Hue = "hue";

    public static (double R, double G, double B) Parse(string hex)
    {
        var v = Convert.ToUInt32(hex.TrimStart('#'), 16);
        return (((v >> 16) & 0xFF) / 255.0, ((v >> 8) & 0xFF) / 255.0, (v & 0xFF) / 255.0);
    }

    // data — BGRA без предумножения прозрачности. strength 0–100: сколько от нового цвета (остальное — исходный значок).
    public static byte[] Apply(byte[] data, string hex, string style, int strength)
    {
        var (tr, tg, tb) = Parse(hex);
        var (th, _, _) = ToHsl(tr, tg, tb);
        var k = Math.Clamp(strength, 0, 100) / 100.0;
        var res = new byte[data.Length];
        for (var i = 0; i < data.Length; i += 4)
        {
            double b = data[i] / 255.0, g = data[i + 1] / 255.0, r = data[i + 2] / 255.0;
            var a = data[i + 3];
            double nr, ng, nb;
            if (style == Hue)
            {
                var (_, s, l) = ToHsl(r, g, b);
                (nr, ng, nb) = FromHsl(th, s, l);
            }
            else
            {
                // двухцветная схема: тёмное → тень цвета, середина → сам цвет, светлое → посветлённый цвет
                var lum = 0.299 * r + 0.587 * g + 0.114 * b;
                double dr = tr * 0.16, dg = tg * 0.16, db = tb * 0.16;
                double lr = tr + (1 - tr) * 0.78, lg = tg + (1 - tg) * 0.78, lb = tb + (1 - tb) * 0.78;
                if (lum < 0.5) { var t = lum * 2; nr = dr + (tr - dr) * t; ng = dg + (tg - dg) * t; nb = db + (tb - db) * t; }
                else { var t = (lum - 0.5) * 2; nr = tr + (lr - tr) * t; ng = tg + (lg - tg) * t; nb = tb + (lb - tb) * t; }
            }
            nr = r + (nr - r) * k; ng = g + (ng - g) * k; nb = b + (nb - b) * k;
            res[i] = (byte)Math.Round(Math.Clamp(nb, 0, 1) * 255);
            res[i + 1] = (byte)Math.Round(Math.Clamp(ng, 0, 1) * 255);
            res[i + 2] = (byte)Math.Round(Math.Clamp(nr, 0, 1) * 255);
            res[i + 3] = a;
        }
        return res;
    }

    static (double H, double S, double L) ToHsl(double r, double g, double b)
    {
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2; double h = 0, s = 0;
        var d = max - min;
        if (d > 1e-9)
        {
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h /= 6;
        }
        return (h, s, l);
    }

    static (double R, double G, double B) FromHsl(double h, double s, double l)
    {
        if (s < 1e-9) return (l, l, l);
        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;
        return (Hue2Rgb(p, q, h + 1.0 / 3), Hue2Rgb(p, q, h), Hue2Rgb(p, q, h - 1.0 / 3));
    }

    static double Hue2Rgb(double p, double q, double t)
    {
        if (t < 0) t += 1; if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 0.5) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }
}
