namespace SeasonPlusApp;

enum PK : byte { Snow, Leaf, Petal, Fluff, Rain, Clump, Heart, Bat, Confetti, Spark, Rocket, Splash, Balloon, Twinkle }

struct Part
{
    public float X, Y, VX, VY, S, Rot, Spin, Ph, Age, Life, A;
    public PK K;
    public int C;
}

// Накопленное на верхнем крае окна: сугроб/листва/лепестки (H), трава (G, её целевая высота T).
sealed class Pile
{
    public long Id;
    public double L, R, Top, Rad;
    public bool Taskbar;
    public int N;
    public float[] H = Array.Empty<float>(), G = Array.Empty<float>(), T = Array.Empty<float>();
    public int Kind = -1;     // из какого времени года то, что лежит (−1 — пусто)
    public uint Seed;
    public float Total;       // сумма высот — чтобы быстро узнать, пусто ли
}

sealed class Seg
{
    public Pile P = null!;
    public double A, B;
}

// Всё, что происходит на экране: падающие частицы, сугробы на окнах, гирлянды, украшения, салют.
// Движок не знает про Windows: окна ему сообщают списком (SetSurfaces), рисует он в Raster.
sealed partial class SeasonEngine
{
    public const int Step = 2;                 // ширина колонки сугроба, px
    public SeasonSettings S = new();
    public DateTime? Clock;                    // для проверки: подменить «сегодня»
    public int W = 1920, H = 1080, OX, OY;     // размер и начало экрана (виртуальный рабочий стол)
    public float PileGain = 6f;              // насколько быстро растёт сугроб (проверено в самопроверке)
    public int Season = 0;
    public string Holiday = "";
    public double Time => t;
    public int Count => n;
    public float FwLeft;                       // сколько секунд ещё запускать салют
    public double NyDemo = -1;                 // «проверить полночь»: секунды от начала проверки (−1 — не идёт)

    readonly Random rnd;
    Part[] ps = new Part[3200];
    int n;
    readonly Dictionary<long, Pile> piles = new();
    readonly List<Seg> segs = new();
    double t, relaxAcc, rainLeft, rockAcc, bigFw;
    float wind;
    bool nyFired;

    public SeasonEngine(int seed = 1) { rnd = new Random(seed); }

    public IEnumerable<Pile> Piles => piles.Values;
    public IReadOnlyList<Seg> Segs => segs;

    float F(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
    static float Fr(float v) => v - MathF.Floor(v);
    public static uint Hash(uint a, uint b)
    {
        var h = a * 374761393u + b * 668265263u;
        h = (h ^ (h >> 13)) * 1274126177u;
        return h ^ (h >> 16);
    }

    public void Reset()
    {
        n = 0; piles.Clear(); segs.Clear(); t = 0; rainLeft = 0; FwLeft = 0; NyDemo = -1; nyFired = false;
    }

    // ---------- окна ----------
    public void SetSurfaces(List<Surf> list)
    {
        var seen = new HashSet<long>();
        segs.Clear();
        foreach (var s in list)
        {
            seen.Add(s.Id);
            if (!piles.TryGetValue(s.Id, out var p)) { p = NewPile(s); piles[s.Id] = p; }
            else if (Math.Abs(p.L - s.Left) > 2.5 || Math.Abs(p.R - s.Right) > 2.5 || Math.Abs(p.Top - s.Top) > 2.5)
            {
                Shed(p);                     // окно двинули или растянули — всё осыпается
                p = NewPile(s); piles[s.Id] = p;
            }
            p.Rad = s.Radius; p.Taskbar = s.Taskbar;
            foreach (var v in s.Vis)
                if (v.B - v.A >= 6) segs.Add(new Seg { P = p, A = v.A, B = v.B });
        }
        List<long>? gone = null;
        foreach (var k in piles.Keys) if (!seen.Contains(k)) (gone ??= new List<long>()).Add(k);
        if (gone != null) foreach (var k in gone) { Shed(piles[k]); piles.Remove(k); }
    }

    Pile NewPile(Surf s)
    {
        var p = new Pile { Id = s.Id, L = s.Left, R = s.Right, Top = s.Top, Rad = s.Radius, Taskbar = s.Taskbar, Seed = Hash((uint)s.Id, 77u) };
        p.N = Math.Max(2, (int)((s.Right - s.Left) / Step) + 2);
        p.H = new float[p.N]; p.G = new float[p.N]; p.T = new float[p.N];
        for (var i = 0; i < p.N; i++) p.T[i] = 5f + Hash(p.Seed, (uint)i) % 17;
        return p;
    }

    // Сугроб осыпается комьями.
    void Shed(Pile p)
    {
        if (p.Total < 2f || p.Kind < 0) return;
        var spawned = 0;
        for (var i = 0; i < p.N && spawned < 80; i += 3)
        {
            var h = p.H[i];
            if (h < 1.2f) continue;
            if (n >= ps.Length - 20) break;
            var x = (float)(p.L + i * Step);
            ref var c = ref ps[n++];
            c = new Part { K = PK.Clump, X = x, Y = (float)(p.Top - Dip(p, x) - h), VX = F(-35, 35), VY = F(0, 40), S = MathF.Min(h, 10f) * 0.4f + 1.4f, C = p.Kind, Ph = F(0, 6.28f), Life = 4f };
            spawned++;
        }
    }

    // Провал верхнего края у скруглённых углов Windows 11.
    public static double Dip(Pile p, double x)
    {
        if (p.Taskbar || p.Rad <= 0) return 0;
        var d = Math.Min(x - p.L, p.R - x);
        if (d >= p.Rad) return 0;
        if (d < 0) d = 0;
        var q = p.Rad - d;
        return p.Rad - Math.Sqrt(Math.Max(0, p.Rad * p.Rad - q * q));
    }

    public static float HtAt(Pile p, double x)
    {
        var f = (x - p.L) / Step;
        var i = (int)Math.Floor(f);
        if (i < 0) return p.H.Length > 0 ? p.H[0] : 0;
        if (i >= p.N - 1) return p.H[p.N - 1];
        var t = (float)(f - i);
        return p.H[i] + (p.H[i + 1] - p.H[i]) * t;
    }

    float MaxH(Pile p) => (S.PileHeight / 100f * 26f + 4f) * (p.Taskbar ? 0.8f : 1f);

    void Add(Pile p, double x, float amt)
    {
        var c = (int)((x - p.L) / Step);
        var max = MaxH(p);
        ReadOnlySpan<float> w = stackalloc float[] { 0.1f, 0.2f, 0.4f, 0.2f, 0.1f };
        for (var k = -2; k <= 2; k++)
        {
            var i = c + k;
            if ((uint)i >= (uint)p.N) continue;
            var nv = Math.Min(max, p.H[i] + amt * w[k + 2] * 2.2f);
            p.Total += nv - p.H[i];
            p.H[i] = nv;
        }
        p.Kind = Season;
    }

    // ---------- шаг ----------
    public void StepTo(double dt)
    {
        dt = Math.Min(Math.Max(dt, 0), 0.05);
        t += dt;
        var now = Clock ?? DateTime.Now;
        Season = Cal.SeasonIndex(S, now);
        Holiday = Cal.HolidayOf(S, now);
        var gust = (float)(Math.Sin(t * 0.37) * Math.Sin(t * 0.113 + 1.3));
        wind = S.Wind / 100f * 90f + gust * (Season == 3 ? 55f : 32f) * (0.4f + Math.Abs(S.Wind) / 200f);

        Spawn(dt);
        UpdateParts(dt);
        UpdatePiles(dt);
        UpdateFireworks(dt, now);
    }

    float K => S.Intensity / 100f;
    float Area => W * (float)H / 1e6f;

    void Emit(PK k, float x, float y)
    {
        if (n >= ps.Length) return;
        var sz = S.Size / 100f;
        var p = new Part { K = k, X = x, Y = y, Ph = F(0, 6.28f), Life = 99f };
        switch (k)
        {
            case PK.Snow: p.S = F(1.2f, 3.2f) * sz; p.VY = F(42, 95) * (0.6f + p.S / 4f); break;
            case PK.Leaf: p.S = F(3.2f, 5.4f) * sz; p.VY = F(48, 90); p.Spin = F(-2.2f, 2.2f); p.C = rnd.Next(5); p.Rot = F(0, 6.28f); break;
            case PK.Petal: p.S = F(2.4f, 3.8f) * sz; p.VY = F(26, 55); p.Spin = F(-1.8f, 1.8f); p.C = rnd.Next(4); p.Rot = F(0, 6.28f); break;
            case PK.Fluff: p.S = F(1.6f, 2.6f) * sz; p.VY = F(7, 22); break;
            case PK.Rain: p.S = 1f; p.VY = F(620, 820); p.VX = wind * 2.2f + 40f; break;
            case PK.Heart: p.S = F(3.5f, 6.5f) * sz; p.VY = -F(38, 70); p.C = rnd.Next(3); break;
            case PK.Confetti: p.S = F(2.6f, 4f) * sz; p.VY = F(55, 115); p.Spin = F(-5, 5); p.Rot = F(0, 6.28f); p.C = rnd.Next(7); break;
            case PK.Balloon: p.S = F(10f, 15f) * sz; p.VY = -F(30, 55); p.C = rnd.Next(7); break;
            case PK.Twinkle: p.S = F(2f, 5f); p.Life = F(1.2f, 3.2f); break;
        }
        ps[n++] = p;
    }

    // Праздничный дождь частиц по кодам праздников.
    void Spawn(double dt)
    {
        if (Season < 0 && Holiday == "") return;
        var area = Area; var k = K; var sp = S.Speed / 100f;
        float xr() => F(OX - 120, OX + W + 120);
        void Rate(ref double acc, float perSec, Action make)
        {
            acc += perSec * dt;
            var c = 0;
            while (acc >= 1 && c++ < 40) { make(); acc -= 1; }
            if (acc > 50) acc = 0;
        }
        switch (Season)
        {
            case 0: Rate(ref sn, 10f * area * k * sp, () => Emit(PK.Snow, xr(), OY - 10)); break;
            case 1:
                Rate(ref sn, 5f * area * k * sp, () => Emit(PK.Petal, xr(), OY - 10));
                break;
            case 2: Rate(ref sn, 3.2f * area * k * sp, () => Emit(PK.Fluff, xr(), OY - 10)); break;
            case 3: Rate(ref sn, 4.5f * area * k * sp, () => Emit(PK.Leaf, xr(), OY - 10)); break;
        }
        // дожди весной и осенью: на 10–20 секунд, примерно раз в минуту
        if (Season == 1 || Season == 3)
        {
            if (rainLeft <= 0 && rnd.NextDouble() < dt / 70) rainLeft = F(10, 20);
            if (rainLeft > 0) { rainLeft -= dt; Rate(ref rn, 130f * area * k, () => Emit(PK.Rain, xr(), OY - 10)); }
        }
        else rainLeft = 0;

        switch (Holiday)
        {
            case "halloween": Rate(ref hl, 0.22f * area * k, SpawnBat); break;
            case "valentine": Rate(ref hl, 3f * area * k * sp, () => Emit(PK.Heart, xr(), OY + H + 10)); break;
            case "women": Rate(ref hl, 3f * area * k * sp, () => { Emit(PK.Petal, xr(), OY - 10); ref var q = ref ps[n - 1]; q.C = 4 + rnd.Next(3); }); break;
            case "tet": Rate(ref hl, 3f * area * k * sp, () => { Emit(PK.Petal, xr(), OY - 10); ref var q = ref ps[n - 1]; q.C = rnd.Next(2) == 0 ? 0 : 7; }); break;
            case "maslenitsa": Rate(ref hl, 3.5f * area * k * sp, () => Emit(PK.Confetti, xr(), OY - 10)); break;
            case "xmas": Rate(ref hl, 4f * area * k * sp, () => Emit(PK.Twinkle, F(OX, OX + W), F(OY, OY + H * 0.8f))); break;
            case "custom":
                if (S.CustomFx == 0) Rate(ref hl, 0.35f * area * k, () => Emit(PK.Balloon, xr(), OY + H + 20));
                else if (S.CustomFx == 1) Rate(ref hl, 4f * area * k * sp, () => Emit(PK.Confetti, xr(), OY - 10));
                else if (S.CustomFx == 2) Rate(ref hl, 3.5f * area * k * sp, () => { Emit(PK.Petal, xr(), OY - 10); ref var q = ref ps[n - 1]; q.C = 4 + rnd.Next(3); });
                else if (FwLeft < 1) FwLeft = 3;
                break;
        }
    }

    double sn, rn, hl;

    void SpawnBat()
    {
        if (n >= ps.Length) return;
        var fromLeft = rnd.Next(2) == 0;
        ps[n++] = new Part { K = PK.Bat, X = fromLeft ? OX - 30 : OX + W + 30, Y = OY + F(0.08f, 0.6f) * H, VX = (fromLeft ? 1 : -1) * F(80, 150), S = F(6f, 10f), Ph = F(0, 6.28f), Life = 99f };
    }

    // ---------- частицы ----------
    void UpdateParts(double dt)
    {
        var f = (float)dt; var sp = S.Speed / 100f; var ease = MathF.Min(1f, f * 2f);
        float minX = OX - 260, maxX = OX + W + 260, maxY = OY + H + 30;
        for (var i = 0; i < n; i++)
        {
            ref var p = ref ps[i];
            p.Age += f;
            float ox = p.X, oy = p.Y;
            var dead = false;
            switch (p.K)
            {
                case PK.Snow:
                    p.VX += (wind * 0.6f + MathF.Sin(p.Age * 0.9f + p.Ph) * 16f - p.VX) * ease;
                    p.X += p.VX * sp * f; p.Y += p.VY * sp * f;
                    dead = TryLand(ref p, ox, oy);
                    break;
                case PK.Leaf:
                    p.VX += (wind * 1.1f + MathF.Sin(p.Age * 1.6f + p.Ph) * 45f - p.VX) * ease;
                    p.X += p.VX * sp * f; p.Y += (p.VY + MathF.Sin(p.Age * 2.3f + p.Ph) * 18f) * sp * f; p.Rot += p.Spin * f;
                    dead = TryLand(ref p, ox, oy);
                    break;
                case PK.Petal:
                    p.VX += (wind * 0.9f + MathF.Sin(p.Age * 1.1f + p.Ph) * 30f - p.VX) * ease;
                    p.X += p.VX * sp * f; p.Y += p.VY * sp * f; p.Rot += p.Spin * f;
                    dead = TryLand(ref p, ox, oy);
                    break;
                case PK.Fluff:
                    p.VX += (wind * 0.7f + MathF.Sin(p.Age * 0.5f + p.Ph) * 12f - p.VX) * ease;
                    p.X += p.VX * sp * f; p.Y += (p.VY + MathF.Sin(p.Age * 0.8f + p.Ph) * 6f) * sp * f;
                    dead = TryLand(ref p, ox, oy);
                    break;
                case PK.Rain:
                    p.VX += (wind * 2.2f + 40f - p.VX) * ease;
                    p.X += p.VX * f; p.Y += p.VY * f;
                    dead = TryLand(ref p, ox, oy);
                    break;
                case PK.Clump:
                    p.VY += 700f * f; p.X += p.VX * f; p.Y += p.VY * f;
                    dead = TryLand(ref p, ox, oy);
                    break;
                case PK.Heart:
                    p.VX += (wind * 0.4f + MathF.Sin(p.Age * 1.3f + p.Ph) * 14f - p.VX) * ease;
                    p.X += p.VX * f; p.Y += p.VY * sp * f;
                    dead = p.Y < OY - 30;
                    break;
                case PK.Bat:
                    p.X += p.VX * f; p.Y += MathF.Sin(p.Age * 3f + p.Ph) * 32f * f;
                    dead = p.X < OX - 80 || p.X > OX + W + 80;
                    break;
                case PK.Confetti:
                    p.VX += (wind * 0.8f + MathF.Sin(p.Age * 2.5f + p.Ph) * 40f - p.VX) * ease;
                    p.X += p.VX * f; p.Y += p.VY * sp * f; p.Rot += p.Spin * f;
                    dead = TryLand(ref p, ox, oy);
                    break;
                case PK.Spark:
                    p.VY += 170f * f; p.VX *= 1f - 0.6f * f; p.X += p.VX * f; p.Y += p.VY * f;
                    p.A = Math.Clamp(1f - p.Age / p.Life, 0f, 1f); dead = p.Age >= p.Life;
                    break;
                case PK.Rocket:
                    p.Y += p.VY * f; p.X += p.VX * f;
                    if (p.Age >= p.Life) { Explode(p.X, p.Y, p.C); dead = true; }
                    break;
                case PK.Splash:
                    dead = p.Age >= p.Life;
                    break;
                case PK.Balloon:
                    p.VX += (wind * 0.3f + MathF.Sin(p.Age * 0.9f + p.Ph) * 18f - p.VX) * ease;
                    p.X += p.VX * f; p.Y += p.VY * sp * f;
                    dead = p.Y < OY - 60;
                    break;
                case PK.Twinkle:
                    dead = p.Age >= p.Life;
                    break;
            }
            if (!dead && p.K != PK.Twinkle && p.K != PK.Splash && (p.X < minX || p.X > maxX || p.Y > maxY)) dead = true;
            if (dead) { ps[i] = ps[--n]; i--; }
        }
    }

    // Попал ли на край окна. Возвращает true — частица кончилась.
    bool TryLand(ref Part p, float ox, float oy)
    {
        for (var i = 0; i < segs.Count; i++)
        {
            var sg = segs[i];
            if (p.X < sg.A || p.X > sg.B) continue;
            var pile = sg.P;
            var top = (float)(pile.Top - Dip(pile, p.X) - HtAt(pile, p.X));
            if (!(oy <= top && p.Y > top)) continue;
            var grows = S.Piles && Season >= 0 && (!pile.Taskbar || S.TaskbarPile) && (pile.Kind < 0 || pile.Kind == Season);
            switch (p.K)
            {
                case PK.Snow: if (grows && Season == 0) Add(pile, p.X, PileGain * 0.55f); return true;
                case PK.Leaf: if (grows && Season == 3) Add(pile, p.X, PileGain * 1.1f); return true;
                case PK.Petal: if (grows && Season == 1) Add(pile, p.X, PileGain * 0.7f); return true;
                case PK.Fluff: if (grows && Season == 2 && rnd.Next(3) == 0) Add(pile, p.X, PileGain * 0.35f); return true;
                case PK.Clump: if (grows) Add(pile, p.X, PileGain * 0.4f); return true;
                case PK.Rain:
                    if (n < ps.Length) ps[n++] = new Part { K = PK.Splash, X = p.X, Y = top, Life = 0.28f, S = F(3f, 5.5f) };
                    return true;
                case PK.Confetti: return true;
            }
            return true;
        }
        return false;
    }

    // ---------- сугробы ----------
    void UpdatePiles(double dt)
    {
        var f = (float)dt;
        relaxAcc += dt;
        var relax = relaxAcc >= 0.1; if (relax) relaxAcc = 0;
        var growGrass = (Season == 1 || Season == 2) && S.Piles;
        foreach (var p in piles.Values)
        {
            var melting = !S.Piles || (p.Kind >= 0 && p.Kind != Season);
            if (melting && p.Total > 0)
            {
                var tot = 0f;
                for (var i = 0; i < p.N; i++) { var v = p.H[i] - f * 9f; if (v < 0) v = 0; p.H[i] = v; tot += v; }
                p.Total = tot;
                if (tot < 0.5f) { Array.Clear(p.H, 0, p.N); p.Total = 0; p.Kind = -1; }
            }
            else if (relax && p.Total > 0)
            {
                for (var i = 0; i < p.N - 1; i++)
                {
                    var d = p.H[i] - p.H[i + 1];
                    if (MathF.Abs(d) > 1.5f) { var mv = (MathF.Abs(d) - 1.5f) * 0.35f * MathF.Sign(d); p.H[i] -= mv; p.H[i + 1] += mv; }
                }
            }
            // трава: растёт весной и летом, осенью и зимой вянет
            var tg = 0f;
            for (var i = 0; i < p.N; i++)
            {
                var g = p.G[i];
                if (growGrass && (!p.Taskbar)) g = MathF.Min(p.T[i], g + f * (1.4f + (p.T[i] % 3) * 0.3f));
                else if (g > 0) g = MathF.Max(0, g - f * 5f);
                p.G[i] = g; tg += g;
            }
        }
    }

    // ---------- салют ----------
    void Explode(float x, float y, int hue)
    {
        var count = 56 + rnd.Next(28);
        var ring = rnd.Next(10) < 4;
        var h0 = hue / 100f;
        var two = rnd.Next(3) == 0;
        for (var i = 0; i < count && n < ps.Length; i++)
        {
            var a = ring ? i * 6.2832f / count : F(0, 6.2832f);
            var sp = ring ? 190f : F(60, 250);
            var c = (int)((h0 + (two && i % 2 == 0 ? 0.5f : 0f) + F(-0.03f, 0.03f)) * 100f);
            ps[n++] = new Part { K = PK.Spark, X = x, Y = y, VX = MathF.Cos(a) * sp, VY = MathF.Sin(a) * sp, S = F(1.4f, 2.6f), Life = F(1.1f, 1.9f), C = c, A = 1f };
        }
    }

    void Launch()
    {
        if (n >= ps.Length - 120) return;
        var x = OX + W * F(0.12f, 0.88f); var ty = OY + H * F(0.12f, 0.5f);
        var flight = F(0.9f, 1.4f);
        ps[n++] = new Part { K = PK.Rocket, X = x, Y = OY + H, VX = F(-25, 25), VY = -((OY + H) - ty) / flight, S = 1.8f, Life = flight, C = rnd.Next(100) };
    }

    // Праздничная ночь: в 00:00 салют; в последние 10 секунд года — большой отсчёт.
    void UpdateFireworks(double dt, DateTime now)
    {
        if (FwLeft > 0)
        {
            FwLeft -= (float)dt; rockAcc += dt;
            if (rockAcc >= 0.55) { rockAcc = 0; Launch(); if (rnd.Next(3) == 0) Launch(); }
        }
        if (NyDemo >= 0)
        {
            NyDemo += dt;
            if (NyDemo >= 10 && !nyFired) { nyFired = true; if (S.NyFireworks) FwLeft = 25; }
            if (NyDemo > 40) { NyDemo = -1; nyFired = false; }
            return;
        }
        if (Holiday == "newyear" && now.Month == 1 && now.Day == 1 && now.Hour == 0 && now.Minute == 0 && now.Second < 40)
        {
            if (!nyFired) { nyFired = true; if (S.NyFireworks) FwLeft = 25; }
        }
        else if (!(Holiday == "newyear" && now.Month == 12 && now.Day == 31 && now.Hour == 23 && now.Minute == 59 && now.Second >= 50)) nyFired = false;
    }

    public void StartFireworks(float seconds = 14) => FwLeft = Math.Max(FwLeft, seconds);
    public void ForceRain(float seconds) => rainLeft = seconds;
    public int CountOf(PK k) { var c = 0; for (var i = 0; i < n; i++) if (ps[i].K == k) c++; return c; }

    // Что показывать посреди экрана: "" или текст отсчёта/поздравления.
    public string BigText(out bool greeting)
    {
        greeting = false;
        if (!S.Countdown || Holiday != "newyear") return "";
        if (NyDemo >= 0)
        {
            if (NyDemo < 10) return Math.Max(1, (int)Math.Ceiling(10 - NyDemo)).ToString();
            if (NyDemo < 30) { greeting = true; return "С Новым годом!"; }
            return "";
        }
        var now = Clock ?? DateTime.Now;
        if (now.Month == 12 && now.Day == 31 && now.Hour == 23 && now.Minute == 59 && now.Second >= 50) return (60 - now.Second).ToString();
        if (now.Month == 1 && now.Day == 1 && now.Hour == 0 && now.Minute == 0 && now.Second < 20) { greeting = true; return "С Новым годом!"; }
        return "";
    }

    // Прогнать «время вперёд», чтобы на экране сразу было что-то падающее.
    public void Warm(double seconds, double stepSec = 0.05)
    {
        for (var tt = 0.0; tt < seconds; tt += stepSec) StepTo(stepSec);
    }
}
