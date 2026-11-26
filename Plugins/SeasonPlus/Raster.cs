using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SeasonPlusApp;

// Цвет в долях 0–255 (не байтами — чтобы смешивание не копило ошибку округления).
readonly record struct Rgb(float R, float G, float B)
{
    public static Rgb Hex(int v) => new((v >> 16) & 255, (v >> 8) & 255, v & 255);
    public static Rgb Lerp(Rgb a, Rgb b, float t) => new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
    public Rgb Mul(float k) => new(Math.Min(255, R * k), Math.Min(255, G * k), Math.Min(255, B * k));

    public static Rgb Hsv(float h, float s, float v)
    {
        h -= MathF.Floor(h);
        var i = (int)(h * 6); var f = h * 6 - i;
        var p = v * (1 - s); var q = v * (1 - f * s); var t = v * (1 - (1 - f) * s);
        float r, g, b;
        switch (i % 6) { case 0: r = v; g = t; b = p; break; case 1: r = q; g = v; b = p; break; case 2: r = p; g = v; b = t; break; case 3: r = p; g = q; b = v; break; case 4: r = t; g = p; b = v; break; default: r = v; g = p; b = q; break; }
        return new Rgb(r * 255, g * 255, b * 255);
    }
}

// Готовая картинка (премультиплицированный BGRA) — для надписей.
sealed class Sprite
{
    public byte[] Px = Array.Empty<byte>();
    public int W, H;
}

// Свой маленький растеризатор: холст BGRA с «предумноженной» прозрачностью — как просит UpdateLayeredWindow.
// Все фигуры сглажены. Нужен только для того, что рисует плагин: круги, овалы, отрезки, треугольники, столбики.
sealed class Raster
{
    public byte[] Px = Array.Empty<byte>();
    public int W, H;

    public void Resize(int w, int h)
    {
        if (w == W && h == H && Px.Length == w * h * 4) return;
        W = w; H = h; Px = new byte[w * h * 4];
    }

    public void Clear() => Array.Clear(Px, 0, Px.Length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Blend(int x, int y, float r, float g, float b, float a)
    {
        if ((uint)x >= (uint)W || (uint)y >= (uint)H || a <= 0.004f) return;
        if (a > 1f) a = 1f;
        var i = (y * W + x) * 4; var ia = 1f - a;
        Px[i] = (byte)Math.Min(255f, b * a + Px[i] * ia + 0.5f);
        Px[i + 1] = (byte)Math.Min(255f, g * a + Px[i + 1] * ia + 0.5f);
        Px[i + 2] = (byte)Math.Min(255f, r * a + Px[i + 2] * ia + 0.5f);
        Px[i + 3] = (byte)Math.Min(255f, 255f * a + Px[i + 3] * ia + 0.5f);
    }

    public void Disc(float cx, float cy, float r, Rgb c, float a)
    {
        int x0 = Math.Max(0, (int)MathF.Floor(cx - r - 1)), x1 = Math.Min(W - 1, (int)MathF.Ceiling(cx + r + 1));
        int y0 = Math.Max(0, (int)MathF.Floor(cy - r - 1)), y1 = Math.Min(H - 1, (int)MathF.Ceiling(cy + r + 1));
        for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                var cov = Math.Clamp(r - MathF.Sqrt(dx * dx + dy * dy) + 0.5f, 0f, 1f);
                if (cov > 0) Blend(x, y, c.R, c.G, c.B, a * cov);
            }
    }

    // Мягкое свечение: яркое в середине, к краю затухает.
    public void Glow(float cx, float cy, float r, Rgb c, float a)
    {
        int x0 = Math.Max(0, (int)MathF.Floor(cx - r)), x1 = Math.Min(W - 1, (int)MathF.Ceiling(cx + r));
        int y0 = Math.Max(0, (int)MathF.Floor(cy - r)), y1 = Math.Min(H - 1, (int)MathF.Ceiling(cy + r));
        for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                var t = 1f - MathF.Sqrt(dx * dx + dy * dy) / r;
                if (t > 0) Blend(x, y, c.R, c.G, c.B, a * t * t);
            }
    }

    public void Ring(float cx, float cy, float r, float w, Rgb c, float a)
    {
        var o = r + w;
        int x0 = Math.Max(0, (int)MathF.Floor(cx - o - 1)), x1 = Math.Min(W - 1, (int)MathF.Ceiling(cx + o + 1));
        int y0 = Math.Max(0, (int)MathF.Floor(cy - o - 1)), y1 = Math.Min(H - 1, (int)MathF.Ceiling(cy + o + 1));
        for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                var d = MathF.Abs(MathF.Sqrt(dx * dx + dy * dy) - r);
                var cov = Math.Clamp(w / 2f - d + 0.5f, 0f, 1f);
                if (cov > 0) Blend(x, y, c.R, c.G, c.B, a * cov);
            }
    }

    // Овал, повёрнутый на ang радиан.
    public void Ellipse(float cx, float cy, float rx, float ry, float ang, Rgb c, float a)
    {
        if (rx < 0.3f || ry < 0.3f) { Disc(cx, cy, Math.Max(rx, ry), c, a * 0.6f); return; }
        var big = Math.Max(rx, ry) + 1.5f;
        int x0 = Math.Max(0, (int)MathF.Floor(cx - big)), x1 = Math.Min(W - 1, (int)MathF.Ceiling(cx + big));
        int y0 = Math.Max(0, (int)MathF.Floor(cy - big)), y1 = Math.Min(H - 1, (int)MathF.Ceiling(cy + big));
        float cs = MathF.Cos(ang), sn = MathF.Sin(ang), small = Math.Min(rx, ry);
        for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                float lx = dx * cs + dy * sn, ly = -dx * sn + dy * cs;
                var q = MathF.Sqrt(lx * lx / (rx * rx) + ly * ly / (ry * ry));
                var cov = Math.Clamp((1f - q) * small + 0.5f, 0f, 1f);
                if (cov > 0) Blend(x, y, c.R, c.G, c.B, a * cov);
            }
    }

    // Отрезок с круглыми концами толщиной w.
    public void Line(float x1, float y1, float x2, float y2, float w, Rgb c, float a)
    {
        var r = Math.Max(0.5f, w / 2f);
        int xa = Math.Max(0, (int)MathF.Floor(Math.Min(x1, x2) - r - 1)), xb = Math.Min(W - 1, (int)MathF.Ceiling(Math.Max(x1, x2) + r + 1));
        int ya = Math.Max(0, (int)MathF.Floor(Math.Min(y1, y2) - r - 1)), yb = Math.Min(H - 1, (int)MathF.Ceiling(Math.Max(y1, y2) + r + 1));
        float vx = x2 - x1, vy = y2 - y1, len2 = vx * vx + vy * vy;
        for (var y = ya; y <= yb; y++)
            for (var x = xa; x <= xb; x++)
            {
                float px = x + 0.5f - x1, py = y + 0.5f - y1;
                var t = len2 < 1e-6f ? 0f : Math.Clamp((px * vx + py * vy) / len2, 0f, 1f);
                float dx = px - vx * t, dy = py - vy * t;
                var cov = Math.Clamp(r - MathF.Sqrt(dx * dx + dy * dy) + 0.5f, 0f, 1f);
                if (cov > 0) Blend(x, y, c.R, c.G, c.B, a * cov);
            }
    }

    // Закрашенный прямоугольник (границы дробные — края сглажены).
    public void Rect(float x0, float y0, float x1, float y1, Rgb c, float a)
    {
        int xa = Math.Max(0, (int)MathF.Floor(x0)), xb = Math.Min(W - 1, (int)MathF.Ceiling(x1) - 1);
        int ya = Math.Max(0, (int)MathF.Floor(y0)), yb = Math.Min(H - 1, (int)MathF.Ceiling(y1) - 1);
        for (var y = ya; y <= yb; y++)
        {
            var cy = Math.Clamp(Math.Min(y + 1f, y1) - Math.Max(y, y0), 0f, 1f);
            for (var x = xa; x <= xb; x++)
            {
                var cx = Math.Clamp(Math.Min(x + 1f, x1) - Math.Max(x, x0), 0f, 1f);
                Blend(x, y, c.R, c.G, c.B, a * cx * cy);
            }
        }
    }

    public void Tri(float x1, float y1, float x2, float y2, float x3, float y3, Rgb c, float a)
    {
        int xa = Math.Max(0, (int)MathF.Floor(Math.Min(x1, Math.Min(x2, x3)))), xb = Math.Min(W - 1, (int)MathF.Ceiling(Math.Max(x1, Math.Max(x2, x3))));
        int ya = Math.Max(0, (int)MathF.Floor(Math.Min(y1, Math.Min(y2, y3)))), yb = Math.Min(H - 1, (int)MathF.Ceiling(Math.Max(y1, Math.Max(y2, y3))));
        var den = (y2 - y3) * (x1 - x3) + (x3 - x2) * (y1 - y3);
        if (MathF.Abs(den) < 1e-6f) return;
        for (var y = ya; y <= yb; y++)
            for (var x = xa; x <= xb; x++)
            {
                var cov = 0;
                for (var sy = 0; sy < 2; sy++)
                    for (var sx = 0; sx < 2; sx++)
                    {
                        float px = x + 0.25f + sx * 0.5f, py = y + 0.25f + sy * 0.5f;
                        var l1 = ((y2 - y3) * (px - x3) + (x3 - x2) * (py - y3)) / den;
                        var l2 = ((y3 - y1) * (px - x3) + (x1 - x3) * (py - y3)) / den;
                        if (l1 >= 0 && l2 >= 0 && l1 + l2 <= 1) cov++;
                    }
                if (cov > 0) Blend(x, y, c.R, c.G, c.B, a * cov / 4f);
            }
    }

    // Сердечко с центром в (cx, cy), высота около 2·s.
    public void Heart(float cx, float cy, float s, Rgb c, float a)
    {
        var big = s * 1.5f + 1;
        int x0 = Math.Max(0, (int)MathF.Floor(cx - big)), x1 = Math.Min(W - 1, (int)MathF.Ceiling(cx + big));
        int y0 = Math.Max(0, (int)MathF.Floor(cy - big)), y1 = Math.Min(H - 1, (int)MathF.Ceiling(cy + big));
        for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                var cov = 0;
                for (var sy = 0; sy < 2; sy++)
                    for (var sx = 0; sx < 2; sx++)
                    {
                        var u = (x + 0.25f + sx * 0.5f - cx) / s * 0.8f;
                        var v = -(y + 0.25f + sy * 0.5f - cy) / s * 0.8f - 0.15f;
                        var t = u * u + v * v - 1f;
                        if (t * t * t - u * u * v * v * v <= 0) cov++;
                    }
                if (cov > 0) Blend(x, y, c.R, c.G, c.B, a * cov / 4f);
            }
    }

    // Столбик из одной колонки пикселей с плавным переходом цвета сверху вниз. yTop — дробный: верхний пиксель получает частичную прозрачность.
    public void Column(int x, float yTop, float yBot, Rgb top, Rgb bot, float a)
    {
        if ((uint)x >= (uint)W || yBot <= yTop) return;
        var ya = Math.Max(0, (int)MathF.Floor(yTop)); var yb = Math.Min(H - 1, (int)MathF.Ceiling(yBot) - 1);
        var span = Math.Max(0.001f, yBot - yTop);
        for (var y = ya; y <= yb; y++)
        {
            var t = Math.Clamp((y + 0.5f - yTop) / span, 0f, 1f);
            var cov = Math.Clamp(Math.Min(y + 1f, yBot) - Math.Max(y, yTop), 0f, 1f);
            var cc = Rgb.Lerp(top, bot, t);
            Blend(x, y, cc.R, cc.G, cc.B, a * cov);
        }
    }

    public void Blit(Sprite s, int dx, int dy, float a)
    {
        for (var y = 0; y < s.H; y++)
        {
            var ty = dy + y; if ((uint)ty >= (uint)H) continue;
            for (var x = 0; x < s.W; x++)
            {
                var tx = dx + x; if ((uint)tx >= (uint)W) continue;
                var si = (y * s.W + x) * 4;
                var sa = s.Px[si + 3]; if (sa == 0) continue;
                var di = (ty * W + tx) * 4; var ia = 1f - sa / 255f * a;
                Px[di] = (byte)Math.Min(255f, s.Px[si] * a + Px[di] * ia);
                Px[di + 1] = (byte)Math.Min(255f, s.Px[si + 1] * a + Px[di + 1] * ia);
                Px[di + 2] = (byte)Math.Min(255f, s.Px[si + 2] * a + Px[di + 2] * ia);
                Px[di + 3] = (byte)Math.Min(255f, sa * a + Px[di + 3] * ia);
            }
        }
    }

    // Надпись: рисуется средствами WPF один раз и запоминается (нужен поток STA).
    static readonly Dictionary<string, Sprite> textCache = new();

    public static Sprite Text(string text, double size, Rgb c)
    {
        var key = text + "|" + size.ToString(CultureInfo.InvariantCulture) + "|" + (int)c.R + "," + (int)c.G + "," + (int)c.B;
        lock (textCache)
        {
            if (textCache.TryGetValue(key, out var hit)) return hit;
            var spr = new Sprite();
            try
            {
                var tf = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
                var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, tf, size, new SolidColorBrush(System.Windows.Media.Color.FromRgb((byte)c.R, (byte)c.G, (byte)c.B)), 1.0);
                var sh = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, tf, size, new SolidColorBrush(System.Windows.Media.Color.FromArgb(150, 0, 0, 0)), 1.0);
                var w = (int)Math.Ceiling(ft.WidthIncludingTrailingWhitespace) + 12; var h = (int)Math.Ceiling(ft.Height) + 12;
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawText(sh, new System.Windows.Point(8, 8));
                    dc.DrawText(ft, new System.Windows.Point(5, 5));
                }
                var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                spr.W = w; spr.H = h; spr.Px = new byte[w * h * 4];
                rtb.CopyPixels(spr.Px, w * 4, 0);
            }
            catch { spr.W = 0; spr.H = 0; spr.Px = Array.Empty<byte>(); }
            if (textCache.Count > 60) textCache.Clear();
            textCache[key] = spr;
            return spr;
        }
    }

    public BitmapSource ToBitmap()
    {
        var wb = new WriteableBitmap(W, H, 96, 96, PixelFormats.Pbgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, W, H), Px, W * 4, 0);
        wb.Freeze();
        return wb;
    }
}
