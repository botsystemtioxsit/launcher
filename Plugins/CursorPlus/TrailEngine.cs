namespace CursorPlusApp;

// Картинка курсора для стиля «Призраки»: пиксели BGRA, уже умноженные на прозрачность.
sealed class Sprite
{
    public int W, H, HX, HY;
    public byte[] Pm = Array.Empty<byte>();

    public static Sprite From(CurImg c)
    {
        var s = new Sprite { W = c.W, H = c.H, HX = c.HotX, HY = c.HotY, Pm = new byte[c.Bgra.Length] };
        for (var i = 0; i < c.Bgra.Length; i += 4)
        {
            var a = c.Bgra[i + 3];
            s.Pm[i] = (byte)(c.Bgra[i] * a / 255); s.Pm[i + 1] = (byte)(c.Bgra[i + 1] * a / 255); s.Pm[i + 2] = (byte)(c.Bgra[i + 2] * a / 255); s.Pm[i + 3] = a;
        }
        return s;
    }
}

// Что нужно движку шлейфа: стиль, цвета, длина, размер.
sealed class TrailSpec
{
    public string Style = "none", ColorMode = "cursor", Click = "none";
    public (int R, int G, int B) C1 = (255, 42, 157), C2 = (0, 240, 255);
    public double Life = 0.6;      // сколько секунд живёт частица
    public double Size = 8;        // радиус частицы, px
    public Sprite? Cursor;         // для «Призраков»

    public static TrailSpec From(CursorSettings s, CurImg? cursor)
    {
        var sp = new TrailSpec { Style = s.Trail, Click = s.Click, ColorMode = s.TrailColorMode };
        sp.C1 = (s.TrailColorMode is "custom" or "two") ? Col.Parse(s.TrailColor) : Col.Parse(s.Fill);
        sp.C2 = Col.Parse(s.TrailColor2);
        sp.Life = 0.15 + 1.45 * s.TrailLength / 100.0;
        sp.Size = 2.5 + 22.5 * s.TrailSize / 100.0;
        if (cursor != null) sp.Cursor = Sprite.From(cursor);
        return sp;
    }
}

// Частицы шлейфа и эффектов клика и их рисование в буфер BGRA (цвет уже умножен на прозрачность — как нужно для прозрачного окна).
sealed class TrailEngine
{
    sealed class P
    {
        public float X, Y, Vx, Vy, Ay, Age, Life, Size, Grow, Rot, Vr, Seed, R, G, B;
        public byte Kind;
        public bool Two;
    }

    const byte Soft = 0, Spark = 1, Star = 2, Ring = 3, Heart = 4, Ghost = 5, Wave = 6, Cloud = 7;

    readonly List<P> ps = new();
    readonly Random rnd = new(11);
    TrailSpec spec = new();
    double lastX = double.NaN, lastY, carry, hue;

    public void SetSpec(TrailSpec s) { spec = s; }
    public int Count => ps.Count;
    public void Reset() { ps.Clear(); lastX = double.NaN; carry = 0; }

    // ---------- рождение частиц ----------
    double StepFor() => spec.Style switch
    {
        "comet" => Math.Max(1.2, spec.Size * 0.30),
        "glow" => Math.Max(2.0, spec.Size * 0.55),
        "sparks" => 6,
        "stars" => 16,
        "bubbles" => 20,
        "hearts" => 24,
        "ghost" => 14,
        _ => 0,
    };

    // Курсор передвинулся в точку (x, y): вдоль пути рождаются частицы.
    public void Move(double x, double y)
    {
        if (double.IsNaN(lastX)) { lastX = x; lastY = y; return; }
        var dx = x - lastX; var dy = y - lastY;
        var d = Math.Sqrt(dx * dx + dy * dy);
        if (d < 0.5) return;
        var step = StepFor();
        if (step > 0)
        {
            var t = step - carry;
            var n = 0;
            while (t <= d && n < 80)
            {
                Spawn(lastX + dx * (t / d), lastY + dy * (t / d), dx / d, dy / d);
                t += step; n++;
            }
            carry = n >= 80 ? 0 : d - (t - step);
        }
        lastX = x; lastY = y;
        if (ps.Count > 900) ps.RemoveRange(0, ps.Count - 900);
    }

    void Add(byte kind, double x, double y, double life, double size, double vx = 0, double vy = 0, double ay = 0, double grow = 0, double rot = 0, double vr = 0)
    {
        var p = new P
        {
            Kind = kind, X = (float)x, Y = (float)y, Life = (float)life, Size = (float)size, Vx = (float)vx, Vy = (float)vy, Ay = (float)ay,
            Grow = (float)grow, Rot = (float)rot, Vr = (float)vr, Seed = (float)(rnd.NextDouble() * 6.283),
        };
        if (spec.ColorMode == "rainbow")
        {
            hue = (hue + 7) % 360;
            var c = Col.FromHsv(hue, 0.85, 1);
            p.R = c.R; p.G = c.G; p.B = c.B;
        }
        else
        {
            p.R = spec.C1.R; p.G = spec.C1.G; p.B = spec.C1.B;
            p.Two = spec.ColorMode == "two";
        }
        ps.Add(p);
    }

    double Rnd() => rnd.NextDouble();

    void Spawn(double x, double y, double ux, double uy)
    {
        var life = spec.Life; var size = spec.Size;
        switch (spec.Style)
        {
            case "comet":
                Add(Soft, x, y, life, size);
                break;
            case "glow":
                Add(Cloud, x, y, life * 1.2, size * 1.6, grow: size * 0.8);
                break;
            case "sparks":
                for (var i = 0; i < 1 + rnd.Next(2); i++)
                {
                    var ang = Rnd() * Math.PI * 2; var sp = 30 + Rnd() * 150;
                    Add(Spark, x, y, life * (0.6 + Rnd() * 0.7), size * 0.34 * (0.7 + Rnd() * 0.8) + 1.2,
                        Math.Cos(ang) * sp - ux * 25, Math.Sin(ang) * sp - uy * 25, 260);
                }
                break;
            case "stars":
                Add(Star, x + (Rnd() - 0.5) * size, y + (Rnd() - 0.5) * size, life * (0.8 + Rnd() * 0.5), size * (1.1 + Rnd() * 0.9),
                    (Rnd() - 0.5) * 30, 20 + Rnd() * 30, 0, 0, Rnd() * Math.PI, (Rnd() - 0.5) * 4);
                break;
            case "bubbles":
                Add(Ring, x + (Rnd() - 0.5) * size, y + (Rnd() - 0.5) * size, life * (1.1 + Rnd() * 0.8), size * (0.5 + Rnd() * 0.7),
                    (Rnd() - 0.5) * 30, -(20 + Rnd() * 35));
                break;
            case "hearts":
                Add(Heart, x + (Rnd() - 0.5) * size, y + (Rnd() - 0.5) * size, life * (1.0 + Rnd() * 0.7), size * (0.7 + Rnd() * 0.6),
                    (Rnd() - 0.5) * 24, -(25 + Rnd() * 30));
                break;
            case "ghost":
                if (spec.Cursor != null) Add(Ghost, x, y, life * 0.8, 1);
                break;
        }
    }

    // Нажатие кнопки мыши.
    public void Click(double x, double y)
    {
        if (spec.Click == "ring")
        {
            Add(Wave, x, y, 0.6, 6, grow: 150);
            Add(Wave, x, y, 0.85, 3, grow: 90);
        }
        else if (spec.Click == "burst")
        {
            for (var i = 0; i < 18; i++)
            {
                var ang = i * Math.PI * 2 / 18 + Rnd() * 0.3; var sp = 110 + Rnd() * 170;
                Add(Spark, x, y, 0.5 + Rnd() * 0.3, 2.2 + Rnd() * 2, Math.Cos(ang) * sp, Math.Sin(ang) * sp, 140);
            }
        }
    }

    // ---------- время ----------
    public void Step(double dt)
    {
        var f = (float)dt;
        var w = 0;
        for (var i = 0; i < ps.Count; i++)
        {
            var p = ps[i];
            p.Age += f;
            if (p.Age >= p.Life) continue;
            p.Vy += p.Ay * f; p.X += p.Vx * f; p.Y += p.Vy * f; p.Rot += p.Vr * f; p.Size += p.Grow * f;
            ps[w++] = p;
        }
        if (w < ps.Count) ps.RemoveRange(w, ps.Count - w);
    }

    float Extent(P p) => p.Kind switch
    {
        Ghost => spec.Cursor == null ? 0 : Math.Max(spec.Cursor.W, spec.Cursor.H),
        Soft => p.Size * 2.5f + 2,
        Cloud => p.Size * 1.2f + 2,
        Wave => p.Size + 8,
        Spark => p.Size * 3.3f + 3,
        Star => p.Size * 1.6f + 2,
        Ring => p.Size * 1.6f + 3,
        Heart => p.Size * 1.6f + 2,
        _ => p.Size * 2 + 4,
    };

    // Прямоугольник, в котором есть что рисовать. false — частиц нет.
    public bool Bounds(out int x0, out int y0, out int x1, out int y1)
    {
        x0 = y0 = x1 = y1 = 0;
        if (ps.Count == 0) return false;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var p in ps)
        {
            var e = Extent(p);
            if (p.X - e < minX) minX = p.X - e; if (p.Y - e < minY) minY = p.Y - e;
            if (p.X + e > maxX) maxX = p.X + e; if (p.Y + e > maxY) maxY = p.Y + e;
            if (p.Kind == Ghost && spec.Cursor != null) { minX = Math.Min(minX, p.X - spec.Cursor.HX - 2); minY = Math.Min(minY, p.Y - spec.Cursor.HY - 2); maxX = Math.Max(maxX, p.X - spec.Cursor.HX + spec.Cursor.W + 2); maxY = Math.Max(maxY, p.Y - spec.Cursor.HY + spec.Cursor.H + 2); }
        }
        x0 = (int)Math.Floor(minX); y0 = (int)Math.Floor(minY); x1 = (int)Math.Ceiling(maxX); y1 = (int)Math.Ceiling(maxY);
        return x1 > x0 && y1 > y0;
    }

    // ---------- рисование ----------
    public void Render(byte[] buf, int w, int h, double ox, double oy)
    {
        Array.Clear(buf, 0, Math.Min(buf.Length, w * h * 4));
        foreach (var p in ps) Draw(buf, w, h, p, ox, oy);
    }

    static void Put(byte[] b, int w, int h, int x, int y, float r, float g, float bl, float a)
    {
        if (x < 0 || y < 0 || x >= w || y >= h || a <= 0.004f) return;
        if (a > 1) a = 1;
        var i = (y * w + x) * 4;
        var inv = 1 - a;
        b[i] = (byte)Math.Min(255, bl * a + b[i] * inv + 0.5f);
        b[i + 1] = (byte)Math.Min(255, g * a + b[i + 1] * inv + 0.5f);
        b[i + 2] = (byte)Math.Min(255, r * a + b[i + 2] * inv + 0.5f);
        b[i + 3] = (byte)Math.Min(255, a * 255 + b[i + 3] * inv + 0.5f);
    }

    // мягкий круг: непрозрачность падает к краю (pw больше — мягче)
    static void Disc(byte[] b, int w, int h, float cx, float cy, float rad, float a, float r, float g, float bl, float pw)
    {
        if (rad < 0.4f || a <= 0.004f) return;
        int x0 = (int)Math.Floor(cx - rad), x1 = (int)Math.Ceiling(cx + rad), y0 = (int)Math.Floor(cy - rad), y1 = (int)Math.Ceiling(cy + rad);
        var r2 = rad * rad;
        for (var y = Math.Max(0, y0); y <= Math.Min(h - 1, y1); y++)
            for (var x = Math.Max(0, x0); x <= Math.Min(w - 1, x1); x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                var d2 = (dx * dx + dy * dy) / r2;
                if (d2 >= 1) continue;
                var f = 1 - d2;
                Put(b, w, h, x, y, r, g, bl, a * (pw == 1 ? f : (float)Math.Pow(f, pw)));
            }
    }

    static void RingShape(byte[] b, int w, int h, float cx, float cy, float rad, float th, float a, float r, float g, float bl)
    {
        var e = rad + th + 1;
        for (var y = Math.Max(0, (int)(cy - e)); y <= Math.Min(h - 1, (int)(cy + e)); y++)
            for (var x = Math.Max(0, (int)(cx - e)); x <= Math.Min(w - 1, (int)(cx + e)); x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                var d = (float)Math.Sqrt(dx * dx + dy * dy);
                var f = 1 - Math.Abs(d - rad) / th;
                if (f > 0) Put(b, w, h, x, y, r, g, bl, a * Math.Min(1, f * 1.6f));
            }
    }

    static void Star4(byte[] b, int w, int h, float cx, float cy, float rad, float rot, float a, float r, float g, float bl)
    {
        float cs = (float)Math.Cos(-rot), sn = (float)Math.Sin(-rot);
        var e = rad * 1.5f;
        for (var y = Math.Max(0, (int)(cy - e)); y <= Math.Min(h - 1, (int)(cy + e)); y++)
            for (var x = Math.Max(0, (int)(cx - e)); x <= Math.Min(w - 1, (int)(cx + e)); x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                float rx = (dx * cs - dy * sn) / rad, ry = (dx * sn + dy * cs) / rad;
                var v = 1 - ((float)Math.Pow(Math.Abs(rx), 0.5) + (float)Math.Pow(Math.Abs(ry), 0.5));
                if (v > 0) Put(b, w, h, x, y, r, g, bl, a * (float)Math.Pow(Math.Min(1, v * 2.2f), 0.8));
            }
    }

    static void HeartShape(byte[] b, int w, int h, float cx, float cy, float rad, float a, float r, float g, float bl)
    {
        var e = rad * 1.4f;
        for (var y = Math.Max(0, (int)(cy - e)); y <= Math.Min(h - 1, (int)(cy + e)); y++)
            for (var x = Math.Max(0, (int)(cx - e)); x <= Math.Min(w - 1, (int)(cx + e)); x++)
            {
                var hx = (x + 0.5f - cx) / rad * 1.15f; var hy = -(y + 0.5f - cy) / rad * 1.15f + 0.15f;
                var q = hx * hx + hy * hy - 1;
                var v = q * q * q - hx * hx * hy * hy * hy;
                if (v < 0) Put(b, w, h, x, y, r, g, bl, a * Math.Min(1, -v * 14 + 0.35f));
            }
    }

    void Blit(byte[] b, int w, int h, Sprite s, int x0, int y0, float alpha)
    {
        for (var sy = 0; sy < s.H; sy++)
        {
            var y = y0 + sy;
            if (y < 0 || y >= h) continue;
            for (var sx = 0; sx < s.W; sx++)
            {
                var x = x0 + sx;
                if (x < 0 || x >= w) continue;
                var i = (sy * s.W + sx) * 4;
                var sa = s.Pm[i + 3] / 255f * alpha;
                if (sa <= 0.004f) continue;
                var d = (y * w + x) * 4; var inv = 1 - sa;
                b[d] = (byte)Math.Min(255, s.Pm[i] * alpha + b[d] * inv + 0.5f);
                b[d + 1] = (byte)Math.Min(255, s.Pm[i + 1] * alpha + b[d + 1] * inv + 0.5f);
                b[d + 2] = (byte)Math.Min(255, s.Pm[i + 2] * alpha + b[d + 2] * inv + 0.5f);
                b[d + 3] = (byte)Math.Min(255, sa * 255 + b[d + 3] * inv + 0.5f);
            }
        }
    }

    void Draw(byte[] b, int w, int h, P p, double ox, double oy)
    {
        var u = p.Age / p.Life;
        var cx = (float)(p.X - ox); var cy = (float)(p.Y - oy);
        float r = p.R, g = p.G, bl = p.B;
        if (p.Two) { var m = Col.Mix(spec.C1, spec.C2, u); r = m.R; g = m.G; bl = m.B; }
        float wr = r + (255 - r) * 0.55f, wg = g + (255 - g) * 0.55f, wb = bl + (255 - bl) * 0.55f;   // светлая сердцевина
        switch (p.Kind)
        {
            case Soft:
            {
                var a = (float)Math.Pow(1 - u, 1.6);
                var rad = Math.Max(0.7f, p.Size * (1 - 0.8f * u));
                Disc(b, w, h, cx, cy, rad * 2.3f, 0.2f * a, r, g, bl, 2f);
                Disc(b, w, h, cx, cy, rad, 0.95f * a, r, g, bl, 1.2f);
                Disc(b, w, h, cx, cy, rad * 0.45f, 0.7f * a, wr, wg, wb, 1f);
                break;
            }
            case Cloud:
                Disc(b, w, h, cx, cy, p.Size, 0.32f * (float)Math.Pow(1 - u, 1.3), r, g, bl, 1.6f);
                break;
            case Spark:
            {
                var a = (1 - u) * (0.75f + 0.25f * (float)Math.Sin(p.Age * 40 + p.Seed));
                Disc(b, w, h, cx, cy, p.Size * 3.2f, 0.3f * a, r, g, bl, 2f);
                Disc(b, w, h, cx, cy, p.Size, a, wr, wg, wb, 0.5f);
                break;
            }
            case Star:
            {
                var a = (float)Math.Pow(1 - u, 0.8) * (0.65f + 0.35f * (float)Math.Sin(p.Age * 12 + p.Seed));
                var rad = p.Size * (1 - 0.4f * u);
                Star4(b, w, h, cx, cy, rad, p.Rot, a, r, g, bl);
                Disc(b, w, h, cx, cy, rad * 0.5f, 0.45f * a, wr, wg, wb, 1.2f);
                break;
            }
            case Ring:
            {
                var a = 0.85f * (float)Math.Pow(1 - u, 1.2);
                var rad = p.Size * (0.7f + 0.5f * u);
                RingShape(b, w, h, cx, cy, rad, Math.Max(1.6f, rad * 0.26f), Math.Min(1f, a * 1.15f), r, g, bl);
                Disc(b, w, h, cx - rad * 0.35f, cy - rad * 0.35f, Math.Max(1f, rad * 0.22f), 0.55f * a, 255, 255, 255, 1f);
                break;
            }
            case Heart:
                HeartShape(b, w, h, cx, cy, p.Size * (1 - 0.3f * u), (float)Math.Pow(1 - u, 0.9) * 0.95f, r, g, bl);
                break;
            case Wave:
                RingShape(b, w, h, cx, cy, p.Size, 2.6f, (float)Math.Pow(1 - u, 1.2), r, g, bl);
                break;
            case Ghost:
                if (spec.Cursor != null) Blit(b, w, h, spec.Cursor, (int)Math.Round(cx - spec.Cursor.HX), (int)Math.Round(cy - spec.Cursor.HY), 0.6f * (float)Math.Pow(1 - u, 1.1));
                break;
        }
    }
}
