namespace SeasonPlusApp;

// Рисование: сугробы, трава, гирлянды, украшения на окнах, частицы, большой отсчёт.
sealed partial class SeasonEngine
{
    static readonly Rgb[] LeafPal = { Rgb.Hex(0xD6481E), Rgb.Hex(0xEC7A20), Rgb.Hex(0xF2B228), Rgb.Hex(0xA65C2A), Rgb.Hex(0xC88C2C) };
    static readonly Rgb[] PetalPal = { Rgb.Hex(0xFFB7C5), Rgb.Hex(0xFFCFDA), Rgb.Hex(0xF8A0BA), Rgb.Hex(0xFFE8EE), Rgb.Hex(0xFFD634), Rgb.Hex(0xF05A8C), Rgb.Hex(0xFFFFFF), Rgb.Hex(0xDC3232) };
    static readonly Rgb[] FlowerPal = { Rgb.Hex(0xFCFCFC), Rgb.Hex(0xFF96BE), Rgb.Hex(0xFFD634), Rgb.Hex(0xDE2A2C), Rgb.Hex(0x6082EC), Rgb.Hex(0xFF8C28), Rgb.Hex(0xB068E2) };
    static readonly Rgb[] Vivid = { Rgb.Hex(0xFF3B5C), Rgb.Hex(0xFFD23F), Rgb.Hex(0x3CFF9A), Rgb.Hex(0x00D4FF), Rgb.Hex(0xA259FF), Rgb.Hex(0xFF8A3D), Rgb.Hex(0xFF2E97) };
    static readonly Rgb[][] GarlandPal =
    {
        new[] { Rgb.Hex(0xFF3B3B), Rgb.Hex(0xFFD23F), Rgb.Hex(0x3CE06A), Rgb.Hex(0x35C8FF), Rgb.Hex(0xE04DFF), Rgb.Hex(0xFF8A3D) },
        new[] { Rgb.Hex(0xFFB347), Rgb.Hex(0xFFD27F), Rgb.Hex(0xFF9A3C), Rgb.Hex(0xFFE3A3) },
        new[] { Rgb.Hex(0xBFE9FF), Rgb.Hex(0xFFFFFF), Rgb.Hex(0x9AD0FF), Rgb.Hex(0xDDEBFF) },
        new[] { Rgb.Hex(0x2E7BFF), Rgb.Hex(0x5AA0FF), Rgb.Hex(0x1F4FD8), Rgb.Hex(0x8EC1FF) },
        new[] { Rgb.Hex(0xE3262E), Rgb.Hex(0x2BB24C) },
        new[] { Rgb.Hex(0xFFD34D), Rgb.Hex(0xFFC21A), Rgb.Hex(0xFFE48A) },
    };

    float Cx(double x) => (float)(x - OX);
    float Cy(double y) => (float)(y - OY);

    public void Render(Raster r)
    {
        foreach (var sg in segs) DrawPile(r, sg);
        DrawGarland(r);
        DrawProps(r);
        DrawParts(r);
        DrawBig(r);
    }

    // ---------- сугробы, листва, лепестки ----------
    void DrawPile(Raster r, Seg sg)
    {
        var p = sg.P;
        var x0 = (int)Math.Ceiling(sg.A); var x1 = (int)Math.Floor(sg.B);
        if (p.Total > 0.4f && p.Kind >= 0)
        {
            for (var x = x0; x <= x1; x++)
            {
                var h = HtAt(p, x);
                if (h < 0.3f) continue;
                var baseY = (float)(p.Top + Dip(p, x)) + 1f - OY;
                var yTop = baseY - h;
                var xc = x - OX;
                switch (p.Kind)
                {
                    case 0:
                        r.Column(xc, yTop, baseY, new Rgb(255, 255, 255), new Rgb(190, 210, 236), 0.97f);
                        if (Hash(p.Seed, (uint)(x * 7 + (int)(t * 2))) % 331 == 0) { r.Glow(xc, yTop - 0.5f, 4.5f, new Rgb(255, 255, 255), 0.9f); r.Blend(xc, (int)yTop, 255, 255, 255, 1f); }
                        break;
                    case 3:
                    {
                        var idx = (int)(Hash(p.Seed, (uint)(x / 5)) % 5);
                        var c = LeafPal[idx].Mul(0.88f + (Hash(p.Seed, (uint)x) % 5) * 0.05f);
                        r.Column(xc, yTop, baseY, c, c.Mul(0.55f), 1f);
                        break;
                    }
                    case 1:
                        r.Column(xc, yTop, baseY, new Rgb(255, 224, 234), new Rgb(238, 168, 196), 0.95f);
                        break;
                    case 2:
                        r.Column(xc, yTop, baseY, new Rgb(252, 252, 246), new Rgb(224, 224, 212), 0.92f);
                        break;
                }
            }
        }
        if (!p.Taskbar) DrawGrass(r, sg);
    }

    void DrawGrass(Raster r, Seg sg)
    {
        var p = sg.P;
        for (var i = 0; i < p.N; i++)
        {
            var g = p.G[i];
            if (g < 1.2f) continue;
            var bx = p.L + i * Step;
            if (bx < sg.A || bx > sg.B) continue;
            var baseY = (float)(p.Top + Dip(p, bx)) + 1f - OY;
            var hs = Hash(p.Seed, (uint)(i * 3 + 1));
            var sway = MathF.Sin((float)t * 1.6f + (float)bx * 0.07f) * g * 0.2f;
            var green = (hs % 3) switch { 0 => new Rgb(96, 168, 64), 1 => new Rgb(70, 140, 52), _ => new Rgb(124, 192, 76) };
            var xb = Cx(bx);
            r.Line(xb, baseY, xb + sway, baseY - g, 1.6f, green, 1f);
            if (hs % 14 == 0 && g >= p.T[i] * 0.88f)
            {
                var bloom = Math.Clamp((g / p.T[i] - 0.88f) / 0.12f, 0.35f, 1f);
                DrawFlower(r, xb + sway, baseY - g, (int)(hs / 14 % 7), Season == 2 ? 1.35f : 1f, bloom);
            }
        }
    }

    void DrawFlower(Raster r, float x, float y, int kind, float sc, float bloom)
    {
        var col = FlowerPal[kind];
        var center = kind == 2 ? new Rgb(255, 140, 20) : new Rgb(255, 209, 26);
        if (Season == 2 && kind == 2)
        {
            // подсолнух
            for (var k = 0; k < 10; k++)
            {
                var a = k * 0.6283f;
                r.Ellipse(x + MathF.Cos(a) * 4.2f * bloom, y + MathF.Sin(a) * 4.2f * bloom, 2.4f * bloom, 1.2f * bloom, a, new Rgb(255, 205, 40), 1f);
            }
            r.Disc(x, y, 3.2f * bloom, new Rgb(110, 62, 24), 1f);
            return;
        }
        for (var k = 0; k < 5; k++)
        {
            var a = k * 1.2566f + 0.3f;
            r.Disc(x + MathF.Cos(a) * 2.5f * sc * bloom, y + MathF.Sin(a) * 2.5f * sc * bloom, 1.9f * sc * bloom, col, 1f);
        }
        r.Disc(x, y, 1.5f * sc * bloom, center, 1f);
    }

    // ---------- гирлянда ----------
    Rgb Bulb(int j, out float lit)
    {
        var pal = GarlandPal[Math.Clamp(S.GarlandColor, 0, 5)];
        lit = 1f;
        switch (S.GarlandMode)
        {
            case 0:
            {
                var f = j * 0.5f + (float)t * 1.0f;
                var i = (int)MathF.Floor(f); var fr = f - i;
                return Rgb.Lerp(pal[((i % pal.Length) + pal.Length) % pal.Length], pal[(((i + 1) % pal.Length) + pal.Length) % pal.Length], fr);
            }
            case 1:
            {
                var wave = ((j - (float)t * 6f) % 6f + 6f) % 6f;
                lit = wave < 2f ? 1f : 0.22f;
                return pal[j % pal.Length];
            }
            case 2:
                lit = 0.35f + 0.65f * (0.5f + 0.5f * MathF.Sin((float)t * (2f + j % 5) + j * 1.7f));
                return pal[j % pal.Length];
            case 3:
                return pal[j % pal.Length];
            case 4:
                lit = (((int)(t * 2) + j) % 2 == 0) ? 1f : 0.15f;
                return pal[j % pal.Length];
            default:
            {
                var f = (float)t * 0.4f;
                var i = (int)MathF.Floor(f);
                return Rgb.Lerp(pal[i % pal.Length], pal[(i + 1) % pal.Length], f - i);
            }
        }
    }

    bool GarlandOn()
    {
        if (S.Garland == 0) return false;
        if (S.Garland == 1 && Holiday == "") return false;
        return true;
    }

    void DrawGarland(Raster r)
    {
        if (!GarlandOn()) return;
        const double span = 150, gap = 22;
        foreach (var sg in segs)
        {
            var p = sg.P;
            if (S.GarlandWhere == 1 && !p.Taskbar) continue;
            var y0 = p.Top + (p.Taskbar ? 3 : 4);
            double SagAt(double x)
            {
                var k = (int)Math.Floor((x - p.L) / span);
                var u = ((x - p.L) - k * span) / span;
                var sag = 8 + Hash(p.Seed, (uint)(k + 1000)) % 6;
                return y0 + sag * 4 * u * (1 - u);
            }
            for (var x = Math.Ceiling(sg.A); x < sg.B; x += 4)
            {
                var xe = Math.Min(x + 4, sg.B);
                r.Line(Cx(x), Cy(SagAt(x)), Cx(xe), Cy(SagAt(xe)), 1.3f, new Rgb(28, 40, 32), 0.85f);
            }
            var j0 = (int)Math.Ceiling((sg.A - p.L - 11) / gap);
            for (var j = Math.Max(0, j0); ; j++)
            {
                var bx = p.L + 11 + j * gap;
                if (bx > sg.B) break;
                if (bx < sg.A) continue;
                var by = (float)SagAt(bx);
                var col = Bulb(j, out var lit);
                var x = Cx(bx); var y = Cy(by);
                DrawBulb(r, x, y, col, lit);
            }
        }
    }

    void DrawBulb(Raster r, float x, float y, Rgb col, float lit)
    {
        var off = col.Mul(0.25f);
        var c = Rgb.Lerp(off, col, lit);
        switch (S.GarlandBulb)
        {
            case 1:
                r.Glow(x, y + 4, 9f, col, 0.5f * lit);
                r.Disc(x, y + 4, 3.4f, c, 1f);
                r.Disc(x - 1f, y + 3f, 1f, new Rgb(255, 255, 255), 0.55f * lit);
                break;
            case 2:
                r.Glow(x, y + 5, 10f, col, 0.5f * lit);
                r.Line(x - 4.5f, y + 5, x + 4.5f, y + 5, 1.6f, c, 1f);
                r.Line(x, y + 0.5f, x, y + 9.5f, 1.6f, c, 1f);
                r.Line(x - 3f, y + 2f, x + 3f, y + 8f, 1.1f, c, 0.8f);
                r.Line(x + 3f, y + 2f, x - 3f, y + 8f, 1.1f, c, 0.8f);
                r.Disc(x, y + 5, 1.6f, new Rgb(255, 255, 255), 0.8f * lit);
                break;
            case 3:
                r.Glow(x, y + 8, 9f, col, 0.45f * lit);
                r.Line(x, y, x, y + 9f, 2.2f, c, 0.95f);
                r.Disc(x, y + 9f, 1.6f, c, 1f);
                break;
            default:
                r.Glow(x, y + 4.5f, 10f, col, 0.5f * lit);
                r.Ellipse(x, y + 5f, 2.6f, 3.6f, 0f, c, 1f);
                r.Rect(x - 1.4f, y, x + 1.4f, y + 2.2f, new Rgb(70, 76, 70), 1f);
                r.Disc(x - 0.8f, y + 4f, 0.9f, new Rgb(255, 255, 255), 0.5f * lit);
                break;
        }
    }

    // ---------- украшения на окнах ----------
    void DrawProps(Raster r)
    {
        if (!S.Props || Holiday == "") return;
        foreach (var sg in segs)
        {
            var p = sg.P;
            if (p.Taskbar) continue;
            for (var k = 0; k < 2; k++)
            {
                var h = Hash(p.Seed, (uint)(500 + k));
                var px = p.L + 40 + (h % 1000) / 1000.0 * Math.Max(0, (p.R - p.L) - 120);
                if (px < sg.A + 12 || px > sg.B - 12) continue;
                var by = (float)(p.Top + Dip(p, px)) - Math.Max(0, HtAt(p, px) - 1.5f) + 1f;
                DrawProp(r, Cx(px), Cy(by), (int)((h >> 10) % 3), k);
            }
        }
    }

    void DrawProp(Raster r, float x, float y, int variant, int slot)
    {
        switch (Holiday)
        {
            case "halloween": Pumpkin(r, x, y); break;
            case "newyear":
            case "xmas":
                if ((variant + slot) % 2 == 0) Tree(r, x, y); else Gift(r, x, y, variant); break;
            case "easter": Egg(r, x, y, variant); Egg(r, x + 11, y, variant + 1); break;
            case "valentine": r.Heart(x, y - 9, 9f, new Rgb(235, 55, 90), 1f); r.Heart(x - 4, y - 14, 3.2f, new Rgb(255, 255, 255), 0.5f); break;
            case "women": Tulip(r, x, y, variant); Tulip(r, x + 8, y, variant + 1); Tulip(r, x - 8, y, variant + 2); break;
            case "maslenitsa": Pancakes(r, x, y); break;
            case "tet": Lantern(r, x, y); break;
        }
    }

    void Pumpkin(Raster r, float x, float y)
    {
        var o = new Rgb(240, 120, 20); var d = new Rgb(205, 90, 10);
        r.Ellipse(x - 7, y - 9, 7.5f, 9f, 0, d, 1f);
        r.Ellipse(x + 7, y - 9, 7.5f, 9f, 0, d, 1f);
        r.Ellipse(x, y - 9, 8.5f, 10f, 0, o, 1f);
        r.Rect(x - 1.6f, y - 22, x + 1.6f, y - 17, new Rgb(70, 120, 40), 1f);
        var flick = 0.8f + 0.2f * MathF.Sin((float)t * 7f + x);
        r.Glow(x, y - 9, 16f, new Rgb(255, 170, 40), 0.35f * flick);
        var dark = new Rgb(60, 24, 4); var lit = new Rgb(255, 222, 110);
        r.Tri(x - 7, y - 11, x - 2.5f, y - 11, x - 4.7f, y - 16, dark, 1f);
        r.Tri(x + 7, y - 11, x + 2.5f, y - 11, x + 4.7f, y - 16, dark, 1f);
        r.Tri(x - 6.2f, y - 11.4f, x - 3.3f, y - 11.4f, x - 4.7f, y - 14.6f, lit, flick);
        r.Tri(x + 6.2f, y - 11.4f, x + 3.3f, y - 11.4f, x + 4.7f, y - 14.6f, lit, flick);
        r.Line(x - 6, y - 5, x - 3, y - 3.4f, 1.6f, dark, 1f); r.Line(x - 3, y - 3.4f, x, y - 5, 1.6f, dark, 1f);
        r.Line(x, y - 5, x + 3, y - 3.4f, 1.6f, dark, 1f); r.Line(x + 3, y - 3.4f, x + 6, y - 5, 1.6f, dark, 1f);
    }

    void Tree(Raster r, float x, float y)
    {
        var g = new Rgb(30, 130, 64); var g2 = new Rgb(22, 104, 52);
        r.Rect(x - 2, y - 4, x + 2, y, new Rgb(96, 60, 30), 1f);
        r.Tri(x - 11, y - 4, x + 11, y - 4, x, y - 18, g2, 1f);
        r.Tri(x - 9, y - 13, x + 9, y - 13, x, y - 27, g, 1f);
        r.Tri(x - 6.5f, y - 22, x + 6.5f, y - 22, x, y - 34, g2, 1f);
        r.Disc(x, y - 35, 2.4f, new Rgb(255, 214, 70), 1f);
        r.Glow(x, y - 35, 8f, new Rgb(255, 230, 120), 0.5f * (0.7f + 0.3f * MathF.Sin((float)t * 4f)));
        for (var k = 0; k < 6; k++)
        {
            var c = Vivid[(k + (int)(t * 1.5)) % Vivid.Length];
            r.Disc(x + (k % 2 == 0 ? -1 : 1) * (2 + k), y - 8 - k * 3.6f, 1.5f, c, 1f);
        }
    }

    void Gift(Raster r, float x, float y, int variant)
    {
        var c = Vivid[variant % 3 == 0 ? 0 : variant % 3 == 1 ? 3 : 4];
        r.Rect(x - 8, y - 12, x + 8, y, c, 1f);
        r.Rect(x - 1.5f, y - 12, x + 1.5f, y, new Rgb(255, 235, 150), 1f);
        r.Rect(x - 8, y - 7, x + 8, y - 4.5f, new Rgb(255, 235, 150), 1f);
        r.Ellipse(x - 3, y - 14, 3.6f, 2.2f, -0.5f, new Rgb(255, 235, 150), 1f);
        r.Ellipse(x + 3, y - 14, 3.6f, 2.2f, 0.5f, new Rgb(255, 235, 150), 1f);
    }

    void Egg(Raster r, float x, float y, int variant)
    {
        var c = Vivid[variant % Vivid.Length];
        r.Ellipse(x, y - 7, 5f, 7f, 0, c, 1f);
        r.Line(x - 4.4f, y - 7, x + 4.4f, y - 7, 1.8f, new Rgb(255, 255, 255), 0.85f);
        r.Line(x - 3.6f, y - 3.6f, x + 3.6f, y - 3.6f, 1.2f, new Rgb(255, 255, 255), 0.7f);
        r.Disc(x - 1.6f, y - 10.5f, 1.1f, new Rgb(255, 255, 255), 0.5f);
    }

    void Tulip(Raster r, float x, float y, int variant)
    {
        var c = variant % 2 == 0 ? new Rgb(235, 60, 100) : new Rgb(255, 150, 190);
        r.Line(x, y, x, y - 13, 1.6f, new Rgb(70, 150, 60), 1f);
        r.Ellipse(x - 2.2f, y - 8, 1.2f, 3.2f, 0.5f, new Rgb(70, 150, 60), 1f);
        r.Ellipse(x, y - 16, 3.6f, 4.8f, 0, c, 1f);
        r.Tri(x - 3.4f, y - 17, x - 1.7f, y - 22, x, y - 17, c, 1f);
        r.Tri(x, y - 17, x + 1.7f, y - 22, x + 3.4f, y - 17, c, 1f);
    }

    void Pancakes(Raster r, float x, float y)
    {
        for (var k = 0; k < 3; k++) r.Ellipse(x, y - 3 - k * 4, 11, 3.6f, 0, k == 2 ? new Rgb(232, 178, 92) : new Rgb(206, 140, 62), 1f);
        r.Rect(x - 2.5f, y - 14.5f, x + 2.5f, y - 12.2f, new Rgb(255, 236, 140), 1f);
    }

    void Lantern(Raster r, float x, float y)
    {
        var sw = MathF.Sin((float)t * 1.4f + x) * 0.6f;
        r.Glow(x, y - 12, 18f, new Rgb(255, 120, 60), 0.35f);
        r.Ellipse(x, y - 12, 8, 10, 0, new Rgb(218, 38, 38), 1f);
        r.Rect(x - 4.5f, y - 23, x + 4.5f, y - 20.5f, new Rgb(235, 190, 60), 1f);
        r.Rect(x - 4.5f, y - 3.5f, x + 4.5f, y - 1f, new Rgb(235, 190, 60), 1f);
        r.Line(x + sw, y - 1f, x + sw, y + 5, 1.2f, new Rgb(235, 190, 60), 1f);
    }

    // ---------- частицы ----------
    void DrawParts(Raster r)
    {
        for (var i = 0; i < n; i++)
        {
            ref var p = ref ps[i];
            var x = Cx(p.X); var y = Cy(p.Y);
            if (x < -40 || x > W + 40 || y < -40 || y > H + 40) continue;
            switch (p.K)
            {
                case PK.Snow:
                    if (p.S > 2.4f) r.Glow(x, y, p.S * 2.4f, new Rgb(255, 255, 255), 0.22f);
                    r.Disc(x, y, p.S, new Rgb(255, 255, 255), 0.92f);
                    break;
                case PK.Leaf:
                {
                    var flip = 0.35f + 0.65f * MathF.Abs(MathF.Cos(p.Ph + p.Age * 2.1f));
                    var c = LeafPal[p.C % 5];
                    r.Ellipse(x, y, p.S * 1.7f, p.S * 0.95f * flip, p.Rot, c, 1f);
                    r.Line(x - MathF.Cos(p.Rot) * p.S * 1.5f, y - MathF.Sin(p.Rot) * p.S * 1.5f, x + MathF.Cos(p.Rot) * p.S * 1.5f, y + MathF.Sin(p.Rot) * p.S * 1.5f, 0.9f, c.Mul(0.6f), 0.8f);
                    break;
                }
                case PK.Petal:
                {
                    var flip = 0.4f + 0.6f * MathF.Abs(MathF.Cos(p.Ph + p.Age * 2f));
                    r.Ellipse(x, y, p.S * 1.3f, p.S * 0.75f * flip, p.Rot, PetalPal[p.C % 8], 0.95f);
                    break;
                }
                case PK.Fluff:
                    r.Glow(x, y, p.S * 3.2f, new Rgb(255, 255, 255), 0.5f);
                    for (var k = 0; k < 7; k++)
                    {
                        var a = -1.57f + (k - 3) * 0.42f;
                        r.Line(x, y, x + MathF.Cos(a) * p.S * 2.4f, y + MathF.Sin(a) * p.S * 2.4f, 0.8f, new Rgb(255, 255, 255), 0.75f);
                    }
                    r.Disc(x, y, p.S * 0.55f, new Rgb(120, 110, 90), 0.9f);
                    break;
                case PK.Rain:
                    r.Line(x, y, x - p.VX * 0.018f, y - p.VY * 0.018f, 1.2f, new Rgb(175, 205, 238), 0.55f);
                    break;
                case PK.Clump:
                {
                    var c = p.C == 3 ? LeafPal[(int)(p.Ph * 10) % 5] : p.C == 1 ? new Rgb(250, 200, 215) : new Rgb(245, 250, 255);
                    r.Disc(x, y, p.S, c, 0.95f);
                    break;
                }
                case PK.Heart:
                    r.Heart(x, y, p.S, Rgb.Lerp(new Rgb(255, 70, 110), new Rgb(255, 150, 190), p.C / 2f), 0.9f);
                    break;
                case PK.Bat:
                {
                    var flap = MathF.Sin(p.Age * 14f + p.Ph);
                    var dir = p.VX >= 0 ? 1f : -1f;
                    var c = new Rgb(22, 16, 30);
                    r.Ellipse(x, y, p.S * 0.55f, p.S * 0.9f, 0, c, 1f);
                    r.Disc(x, y - p.S * 0.8f, p.S * 0.38f, c, 1f);
                    r.Tri(x - p.S * 0.3f, y - p.S * 1.0f, x - p.S * 0.1f, y - p.S * 0.8f, x - p.S * 0.4f, y - p.S * 1.5f, c, 1f);
                    r.Tri(x + p.S * 0.3f, y - p.S * 1.0f, x + p.S * 0.1f, y - p.S * 0.8f, x + p.S * 0.4f, y - p.S * 1.5f, c, 1f);
                    for (var side = -1; side <= 1; side += 2)
                    {
                        var wx = x + side * p.S * 1.4f; var wy = y - flap * p.S * 0.8f;
                        r.Tri(x, y - p.S * 0.5f, wx + side * p.S * 0.6f, wy - p.S * 0.4f, x + side * p.S * 0.3f, y + p.S * 0.5f, c, 1f);
                        r.Tri(x + side * p.S * 0.3f, y + p.S * 0.5f, wx + side * p.S * 0.6f, wy - p.S * 0.4f, wx, wy + p.S * 0.7f, c, 1f);
                    }
                    r.Disc(x - 1.1f * dir, y - p.S * 0.85f, 0.9f, new Rgb(255, 80, 60), 1f);
                    break;
                }
                case PK.Confetti:
                {
                    var flip = 0.3f + 0.7f * MathF.Abs(MathF.Cos(p.Ph + p.Age * 4f));
                    r.Ellipse(x, y, p.S * 1.4f, p.S * 0.6f * flip, p.Rot, Vivid[p.C % 7], 1f);
                    break;
                }
                case PK.Spark:
                {
                    var c = Rgb.Hsv(p.C / 100f, 0.7f, 1f);
                    r.Line(x, y, x - p.VX * 0.03f, y - p.VY * 0.03f, p.S, c, p.A * 0.6f);
                    r.Glow(x, y, p.S * 4f, c, p.A * 0.7f);
                    r.Disc(x, y, p.S * 0.8f, new Rgb(255, 255, 255), p.A);
                    break;
                }
                case PK.Rocket:
                    r.Line(x, y, x - p.VX * 0.09f, y - p.VY * 0.09f, 2f, new Rgb(255, 200, 120), 0.8f);
                    r.Glow(x, y, 8f, new Rgb(255, 220, 160), 0.8f);
                    break;
                case PK.Splash:
                {
                    var q = p.Age / p.Life;
                    r.Ring(x, y, p.S * q, 1.1f, new Rgb(190, 215, 240), (1f - q) * 0.6f);
                    break;
                }
                case PK.Balloon:
                {
                    var c = Vivid[p.C % 7];
                    r.Line(x, y + p.S * 1.1f, x + MathF.Sin(p.Age * 2f + p.Ph) * 3f, y + p.S * 3.2f, 0.9f, new Rgb(220, 220, 230), 0.8f);
                    r.Ellipse(x, y, p.S * 0.8f, p.S, 0, c, 0.95f);
                    r.Disc(x - p.S * 0.28f, y - p.S * 0.35f, p.S * 0.18f, new Rgb(255, 255, 255), 0.55f);
                    break;
                }
                case PK.Twinkle:
                {
                    var a = MathF.Sin(p.Age / p.Life * 3.14159f);
                    r.Glow(x, y, p.S * 3f, new Rgb(255, 245, 200), a * 0.6f);
                    r.Line(x - p.S, y, x + p.S, y, 1.1f, new Rgb(255, 255, 255), a);
                    r.Line(x, y - p.S, x, y + p.S, 1.1f, new Rgb(255, 255, 255), a);
                    break;
                }
            }
        }
    }

    // ---------- отсчёт и поздравление ----------
    void DrawBig(Raster r)
    {
        var text = BigText(out var greeting);
        if (text == "") return;
        var spr = Raster.Text(text, greeting ? 96 : 230, greeting ? new Rgb(255, 224, 120) : new Rgb(255, 255, 255));
        if (spr.W == 0) return;
        var pulse = 0.82f + 0.18f * MathF.Sin((float)t * 6f);
        r.Blit(spr, (W - spr.W) / 2, (int)(H * 0.34f) - spr.H / 2, greeting ? 1f : pulse);
    }
}
