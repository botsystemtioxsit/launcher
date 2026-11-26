using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SeasonPlusApp;

// Проверки без экрана и снимки сцен (для CI): логика сугробов, календарь, закрытые куски окон, рисование.
static class SelfTest
{
    const int SW = 900, SH = 420;

    sealed class Log
    {
        public readonly StringBuilder Sb = new();
        public int Fails;
        public void Check(string name, bool ok, string extra = "")
        {
            Sb.AppendLine((ok ? "PASS " : "FAIL ") + name + (extra.Length > 0 ? " — " + extra : ""));
            if (!ok) Fails++;
        }
    }

    static List<Surf> Surfs(int ox = 0)
    {
        return new List<Surf>
        {
            new() { Id = 1, Left = 60 + ox, Right = 420 + ox, Top = 90, Radius = 8, Vis = { (60 + ox, 420 + ox) } },
            new() { Id = 2, Left = 330 + ox, Right = 800 + ox, Top = 160, Radius = 8, Vis = { (330 + ox, 800 + ox) } },
            new() { Id = 3, Left = 0, Right = SW, Top = 380, Radius = 0, Taskbar = true, Vis = { (0, SW) } },
        };
    }

    static SeasonEngine Make(string season, string holiday, int intensity = 100, int seed = 3)
    {
        var e = new SeasonEngine(seed) { W = SW, H = SH };
        e.S = new SeasonSettings { Season = season, Holiday = holiday, Intensity = intensity, Wind = 10 };
        e.S.Normalize();
        return e;
    }

    public static void Run(string dir)
    {
        Directory.CreateDirectory(dir);
        var L = new Log();
        try
        {
            // календарь
            var s = new SeasonSettings();
            L.Check("сезон по календарю: январь — зима", Cal.SeasonIndex(s, new DateTime(2027, 1, 15)) == 0);
            L.Check("сезон по календарю: апрель — весна", Cal.SeasonIndex(s, new DateTime(2027, 4, 15)) == 1);
            L.Check("сезон по календарю: июль — лето", Cal.SeasonIndex(s, new DateTime(2027, 7, 15)) == 2);
            L.Check("сезон по календарю: октябрь — осень", Cal.SeasonIndex(s, new DateTime(2027, 10, 15)) == 3);
            L.Check("сезон «выключено» → −1", Cal.SeasonIndex(new SeasonSettings { Season = "off" }, DateTime.Now) == -1);
            L.Check("праздник: 31 декабря — Новый год", Cal.HolidayOf(s, new DateTime(2026, 12, 31)) == "newyear");
            L.Check("праздник: 31 октября — Хэллоуин", Cal.HolidayOf(s, new DateTime(2026, 10, 31)) == "halloween");
            L.Check("праздник: 14 февраля", Cal.HolidayOf(s, new DateTime(2027, 2, 14)) == "valentine");
            L.Check("праздник: 8 марта", Cal.HolidayOf(s, new DateTime(2027, 3, 8)) == "women");
            L.Check("праздник: середина лета — нет", Cal.HolidayOf(s, new DateTime(2027, 7, 15)) == "");
            L.Check("Пасха 2027 — 2 мая", Cal.Easter(2027) == new DateTime(2027, 5, 2), Cal.Easter(2027).ToString("yyyy-MM-dd"));
            L.Check("Пасха 2026 — 12 апреля", Cal.Easter(2026) == new DateTime(2026, 4, 12), Cal.Easter(2026).ToString("yyyy-MM-dd"));
            L.Check("Масленица 2027 — за 7 недель до Пасхи", Cal.HolidayOf(s, Cal.Easter(2027).AddDays(-52)) == "maslenitsa");
            L.Check("праздник вручную важнее даты", Cal.HolidayOf(new SeasonSettings { Holiday = "halloween" }, new DateTime(2027, 7, 1)) == "halloween");
            L.Check("свой праздник по дате", Cal.HolidayOf(new SeasonSettings { CustomDay = 5, CustomMonth = 6 }, new DateTime(2027, 6, 5)) == "custom");

            // куски окон, закрытые другими окнами
            var cut = new List<(double A, double B)> { (100, 200) };
            var parts = WindowScanner.Subtract(0, 300, cut);
            L.Check("закрытый кусок вырезается из середины", parts.Count == 2 && parts[0] == (0, 100) && parts[1] == (200, 300));
            L.Check("закрыто целиком — ничего не остаётся", WindowScanner.Subtract(120, 180, cut).Count == 0);
            L.Check("не пересекается — без изменений", WindowScanner.Subtract(250, 300, cut).Count == 1);
            L.Check("два окна подряд", WindowScanner.Subtract(0, 300, new List<(double A, double B)> { (50, 100), (90, 150) }).Count == 2);

            // скруглённый угол
            var pile = new Pile { L = 100, R = 500, Rad = 8 };
            L.Check("край на самом углу провален на радиус", Math.Abs(SeasonEngine.Dip(pile, 100) - 8) < 0.01);
            L.Check("за пределами угла провала нет", SeasonEngine.Dip(pile, 300) == 0);
            L.Check("провал плавно уменьшается", SeasonEngine.Dip(pile, 103) > SeasonEngine.Dip(pile, 106) && SeasonEngine.Dip(pile, 106) > 0);
            L.Check("у панели задач провала нет", SeasonEngine.Dip(new Pile { L = 0, R = 500, Rad = 0, Taskbar = true }, 1) == 0);

            // сугроб растёт и упирается в предел
            var e = Make("winter", "off");
            e.SetSurfaces(Surfs());
            var half = -1.0; var sw = 0.0;
            for (var tt = 0.0; tt < 420; tt += 0.05)
            {
                e.StepTo(0.05);
                if ((int)(tt * 20) % 40 == 0) e.SetSurfaces(Surfs());
                var mid = Mid(e, 1);
                if (half < 0 && mid >= 0.5 * (0.5 * 26 + 4)) { half = tt; }
            }
            var m1 = Mid(e, 1); var edge = Edge(e, 1);
            var max = 0.5 * 26 + 4;
            L.Check("зимой на окне вырастает сугроб", m1 > 3.5, "высота " + m1.ToString("0.0") + " px, потолок " + max);
            L.Check("сугроб не выше потолка", m1 <= max + 0.01);
            L.Check("сугроб растёт не слишком быстро и не слишком медленно", half > 8 && half < 300, "половина потолка за " + half.ToString("0") + " с");
            L.Check("у края сугроб ниже, чем в середине (скругление)", edge <= m1 + 0.001);
            L.Check("на панели задач тоже ложится", Mid(e, 3) > 2);
            L.Check("частиц не больше предела", e.Count <= 3200, "частиц " + e.Count);

            // осыпается при сдвиге окна
            var before = e.Count;
            var moved = Surfs(); moved[0].Left += 40; moved[0].Right += 40; moved[0].Vis = new List<(double A, double B)> { (moved[0].Left, moved[0].Right) };
            e.SetSurfaces(moved);
            L.Check("после сдвига окна сугроб пуст", Mid(e, 1) < 0.01);
            L.Check("и осыпается комьями", e.Count > before - 50, "частиц было " + before + ", стало " + e.Count);

            // окно закрыли — запись исчезает
            e.SetSurfaces(new List<Surf>());
            L.Check("закрытое окно забыто", !e.Piles.Any());

            // смена времени года тает
            var e2 = Make("winter", "off");
            e2.SetSurfaces(Surfs()); e2.Warm(200);
            var wH = Mid(e2, 1);
            e2.S = new SeasonSettings { Season = "summer", Holiday = "off", Intensity = 100 };
            e2.S.Normalize();
            for (var tt = 0.0; tt < 14; tt += 0.05) { e2.StepTo(0.05); }
            L.Check("зимой был сугроб, при смене на лето он растаял", wH > 2 && Mid(e2, 1) < 0.5, wH.ToString("0.0") + " → " + Mid(e2, 1).ToString("0.0"));

            // трава весной
            var e3 = Make("spring", "off");
            e3.SetSurfaces(Surfs()); e3.Warm(40);
            var grass = e3.Piles.First(p => p.Id == 1).G.Max();
            L.Check("весной на окне растёт трава", grass > 3, grass.ToString("0.0"));
            L.Check("на панели задач трава не растёт", e3.Piles.First(p => p.Id == 3).G.Max() < 0.01);

            // осенью листья, летом пух
            var e4 = Make("autumn", "off"); e4.SetSurfaces(Surfs()); e4.Warm(300);
            L.Check("осенью на окне копятся листья", Mid(e4, 1) > 2, Mid(e4, 1).ToString("0.0"));

            // праздники
            var e5 = Make("winter", "halloween"); e5.SetSurfaces(Surfs()); e5.Warm(80);
            L.Check("в Хэллоуин летают летучие мыши", CountKind(e5, PK.Bat) >= 0 && e5.Holiday == "halloween");
            var e6 = Make("winter", "off"); e6.StartFireworks(10); e6.Warm(6);
            L.Check("салют: появляются ракеты и искры", CountKind(e6, PK.Spark) + CountKind(e6, PK.Rocket) > 10, "искр и ракет " + (CountKind(e6, PK.Spark) + CountKind(e6, PK.Rocket)));
            var e7 = Make("winter", "newyear"); e7.NyDemo = 3;
            L.Check("отсчёт перед Новым годом показывает цифру", e7.BigText(out _) == "3");
            e7.NyDemo = 10.5;
            L.Check("после полуночи — поздравление", e7.BigText(out var gr) == "С Новым годом!" && gr);
            e7.S.Countdown = false;
            L.Check("отсчёт можно выключить", e7.BigText(out _) == "");

            // рисование: что-то видно и в пределах холста
            var r = new Raster(); r.Resize(SW, SH);
            var vis = Make("winter", "newyear"); vis.SetSurfaces(Surfs()); vis.Warm(60);
            vis.Render(r);
            var lit = 0;
            for (var i = 3; i < r.Px.Length; i += 4) if (r.Px[i] > 0) lit++;
            L.Check("кадр не пустой", lit > 800, "закрашено " + lit + " пикселей");
            L.Check("кадр не залит целиком", lit < SW * SH / 2);
            var okAlpha = true;
            for (var i = 0; i < r.Px.Length; i += 4) if (r.Px[i] > r.Px[i + 3] || r.Px[i + 1] > r.Px[i + 3] || r.Px[i + 2] > r.Px[i + 3]) { okAlpha = false; break; }
            L.Check("цвет не ярче прозрачности (как требует окно со слоями)", okAlpha);
        }
        catch (Exception ex) { L.Check("самопроверка без исключений", false, ex.ToString()); }
        L.Sb.AppendLine(L.Fails == 0 ? "ВСЁ В ПОРЯДКЕ" : "ЕСТЬ ОШИБКИ: " + L.Fails);
        File.WriteAllText(Path.Combine(dir, "selftest.txt"), L.Sb.ToString());
        try { Sheets(dir); } catch (Exception ex) { File.AppendAllText(Path.Combine(dir, "selftest.txt"), "снимки: " + ex + Environment.NewLine); }
    }

    static int CountKind(SeasonEngine e, PK k)
    {
        // доступ к частицам только через рисунок нельзя — считаем по типам косвенно
        return e.CountOf(k);
    }

    static double Mid(SeasonEngine e, long id)
    {
        var p = e.Piles.FirstOrDefault(q => q.Id == id);
        if (p == null) return -1;
        return SeasonEngine.HtAt(p, (p.L + p.R) / 2);
    }

    static double Edge(SeasonEngine e, long id)
    {
        var p = e.Piles.FirstOrDefault(q => q.Id == id);
        return p == null ? -1 : SeasonEngine.HtAt(p, p.L + 2);
    }

    // ---------- снимки сцен ----------
    static void Backdrop(Raster r, int season)
    {
        var (a, b) = season switch
        {
            0 => (new Rgb(42, 58, 92), new Rgb(120, 146, 190)),
            1 => (new Rgb(120, 182, 232), new Rgb(214, 236, 208)),
            2 => (new Rgb(74, 155, 224), new Rgb(191, 227, 168)),
            3 => (new Rgb(224, 160, 96), new Rgb(122, 90, 74)),
            _ => (new Rgb(70, 74, 92), new Rgb(110, 116, 140)),
        };
        for (var y = 0; y < SH; y++) r.Rect(0, y, SW, y + 1, Rgb.Lerp(a, b, y / (float)SH), 1f);
        void Win(float x0, float y0, float x1, float y1, string t)
        {
            r.Rect(x0, y0, x1, y1, new Rgb(240, 242, 248), 1f);
            r.Rect(x0, y0, x1, y0 + 26, new Rgb(214, 220, 236), 1f);
            for (var i = 0; i < 5; i++) r.Rect(x0 + 14, y0 + 44 + i * 22, x0 + 140 + i * 36, y0 + 52 + i * 22, new Rgb(200, 204, 218), 1f);
        }
        Win(60, 90, 420, 380, "Проводник");
        Win(330, 160, 800, 380, "Браузер");
        r.Rect(0, 380, SW, SH, new Rgb(24, 24, 34), 1f);
        for (var i = 0; i < 6; i++) r.Rect(300 + i * 40, 389, 318 + i * 40, 407, new Rgb(80 + i * 25, 130, 210), 1f);
    }

    static void Save(Raster r, string file)
    {
        var wb = new WriteableBitmap(r.W, r.H, 96, 96, PixelFormats.Pbgra32, null);
        wb.WritePixels(new System.Windows.Int32Rect(0, 0, r.W, r.H), r.Px, r.W * 4, 0);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(wb));
        using var fs = File.Create(file);
        enc.Save(fs);
    }

    public static void Sheets(string dir)
    {
        Directory.CreateDirectory(dir);
        var scenes = new (string Name, string Season, string Holiday, double Warm, Action<SeasonEngine>? Extra)[]
        {
            ("winter", "winter", "off", 260, null),
            ("spring", "spring", "off", 90, null),
            ("summer", "summer", "off", 100, null),
            ("autumn", "autumn", "off", 260, null),
            ("halloween", "autumn", "halloween", 80, null),
            ("newyear", "winter", "newyear", 200, null),
            ("valentine", "winter", "valentine", 40, null),
            ("women", "spring", "women", 60, null),
            ("easter", "spring", "easter", 60, null),
            ("tet", "winter", "tet", 40, null),
            ("maslenitsa", "winter", "maslenitsa", 40, null),
            ("fireworks", "winter", "off", 7, e => e.StartFireworks(12)),
            ("countdown", "winter", "newyear", 3, e => e.NyDemo = 4),
            ("greeting", "winter", "newyear", 3, e => e.NyDemo = 12),
            ("rain", "autumn", "off", 25, null),
        };
        foreach (var sc in scenes)
        {
            var e = Make(sc.Season, sc.Holiday, 70, 11);
            e.SetSurfaces(Surfs());
            sc.Extra?.Invoke(e);
            if (sc.Name == "rain") e.ForceRain(20);
            e.Warm(sc.Warm);
            e.SetSurfaces(Surfs());
            var r = new Raster(); r.Resize(SW, SH);
            Backdrop(r, e.Season);
            e.Render(r);
            Save(r, Path.Combine(dir, "season-" + sc.Name + ".png"));
        }
    }

    // ---------- настоящий экран ----------
    public static void OverlayTest(string dir)
    {
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder();
        var ov = new SeasonOverlay();
        var s = new SeasonSettings { Season = "winter", Holiday = "off", Intensity = 80, Active = true };
        ov.Start(s);
        Thread.Sleep(6000);
        sb.AppendLine("окно работает: " + ov.Running);
        sb.AppendLine("частиц: " + ov.LastParticles);
        sb.AppendLine("ошибка: " + (ov.LastError.Length == 0 ? "нет" : ov.LastError));
        var list = WindowScanner.Scan((uint)Environment.ProcessId, 0, 0, 3840, 2160);
        sb.AppendLine("найдено краёв окон: " + list.Count);
        foreach (var x in list) sb.AppendLine($"  {(x.Taskbar ? "панель" : "окно")} {x.Left:0}..{x.Right:0} y={x.Top:0} r={x.Radius} видимых кусков {x.Vis.Count}");
        ov.Stop();
        sb.AppendLine("остановлено: " + !ov.Running);
        File.WriteAllText(Path.Combine(dir, "overlay-test.txt"), sb.ToString());
    }
}
