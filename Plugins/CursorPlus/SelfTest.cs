using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace CursorPlusApp;

// Самопроверка для CI: файлы курсоров, запись в Windows и возврат, снимки форм и шлейфа.
static class SelfTest
{
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extra);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);

    static string? RegValue(string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors");
        return k?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString();
    }

    // Средний цвет непрозрачных пикселей картинки.
    static string Mean(CurImg? c)
    {
        if (c == null) return "нет картинки";
        double r = 0, g = 0, b = 0; var n = 0;
        for (var i = 0; i + 3 < c.Bgra.Length; i += 4) { if (c.Bgra[i + 3] < 128) continue; b += c.Bgra[i]; g += c.Bgra[i + 1]; r += c.Bgra[i + 2]; n++; }
        return n == 0 ? "прозрачная" : $"{c.W}×{c.H}, цвет R{r / n:0} G{g / n:0} B{b / n:0}, точка {c.HotX},{c.HotY}";
    }

    public static void Run(string dir)
    {
        Directory.CreateDirectory(dir);
        var log = new StringBuilder();
        try
        {
            log.AppendLine($"Cursor Plus {typeof(SelfTest).Assembly.GetName().Version} · Windows {Environment.OSVersion.Version}");
            var s = new CursorSettings { Shape = "classic", Fill = "#FF2A9D", Size = 100, All = true, Trail = "comet" };
            s.Normalize();
            var fill = Col.Parse(s.Fill); var line = s.OutlineRgb();

            // 1. файл .cur туда и обратно
            var imgs = new List<CurImg> { Shapes.Render("classic", 32, fill, line)!, Shapes.Render("classic", 64, fill, line)! };
            var path = Path.Combine(dir, "test.cur");
            CurFile.Write(path, imgs);
            var back = SysCursors.LoadFile(path, 64);
            log.AppendLine($"файл .cur: {new FileInfo(path).Length} байт; прочитан Windows: {Mean(back)}; ожидали точку {imgs[1].HotX},{imgs[1].HotY}; исходник {Mean(imgs[1])}");

            // 2. настоящие указатели Windows до
            log.AppendLine("--- указатели Windows до применения");
            var arrow0 = SysCursors.Load(32512, 32);
            log.AppendLine($"Arrow в реестре: «{RegValue("Arrow")}»; размер указателя (SM_CXCURSOR): {Native.GetSystemMetrics(Native.SM_CXCURSOR)}; CursorBaseSize: {RegValue("CursorBaseSize")}; системная стрелка: {Mean(arrow0)}");
            foreach (var role in Roles.All) log.AppendLine($"  {role.Reg,-10} 32px: {Mean(SysCursors.Load(role.Ocr, 32))}");
            Sheets(dir, s);

            // 3. применить
            log.AppendLine("--- применяем (классика, розовый, 100%)");
            var ap = new CursorApplier();
            var err = ap.Apply(s);
            Thread.Sleep(600);
            log.AppendLine($"результат: {err ?? "ok"}; Arrow в реестре: «{RegValue("Arrow")}»; файл есть: {File.Exists(RegValue("Arrow") ?? "")}; SM_CXCURSOR: {Native.GetSystemMetrics(Native.SM_CXCURSOR)}");
            log.AppendLine($"  Arrow  32px после: {Mean(SysCursors.Load(32512, 32))}");
            log.AppendLine($"  Hand   32px после: {Mean(SysCursors.Load(32649, 32))}");
            log.AppendLine($"  IBeam  32px после: {Mean(SysCursors.Load(32513, 32))}");

            log.AppendLine("--- размер 200%");
            s.Size = 200; s.Normalize();
            err = ap.Apply(s);
            Thread.Sleep(600);
            log.AppendLine($"результат: {err ?? "ok"}; CursorBaseSize: {RegValue("CursorBaseSize")}; SM_CXCURSOR: {Native.GetSystemMetrics(Native.SM_CXCURSOR)}; Arrow 64px: {Mean(SysCursors.Load(32512, 64))}");

            // 3б. другие способы сменить размер указателя сразу (для справки: что из этого Windows принимает)
            log.AppendLine("--- пробуем другие способы сменить размер указателя сразу");
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(@"Control Panel\Cursors")) k.SetValue("CursorBaseSize", 96, RegistryValueKind.DWord);
                Native.ReloadCursors(); Thread.Sleep(400);
                log.AppendLine($"реестр 96 + SPI_SETCURSORS: SM_CXCURSOR = {Native.GetSystemMetrics(Native.SM_CXCURSOR)}");
                Native.SystemParametersInfo(0x2029, 0, (IntPtr)96, 0x03); Thread.Sleep(400);
                log.AppendLine($"SPI 0x2029 (pvParam=96): SM_CXCURSOR = {Native.GetSystemMetrics(Native.SM_CXCURSOR)}");
                Native.SystemParametersInfo(0x2029, 96, IntPtr.Zero, 0x03); Thread.Sleep(400);
                log.AppendLine($"SPI 0x2029 (uiParam=96): SM_CXCURSOR = {Native.GetSystemMetrics(Native.SM_CXCURSOR)}");
                Native.SystemParametersInfo(0x2029, 0, (IntPtr)5, 0x03); Thread.Sleep(400);
                log.AppendLine($"SPI 0x2029 (pvParam=5): SM_CXCURSOR = {Native.GetSystemMetrics(Native.SM_CXCURSOR)}");
                var t = Native.LoadImage(IntPtr.Zero, (IntPtr)32512, Native.IMAGE_CURSOR, 0, 0, 0x8040);   // LR_DEFAULTSIZE | LR_SHARED
                log.AppendLine($"системная стрелка в размере по умолчанию: {Mean(t == IntPtr.Zero ? null : SysCursors.Load(32512, Native.GetSystemMetrics(Native.SM_CXCURSOR)))}");
            }
            catch (Exception ex) { log.AppendLine("пробы размера: " + ex.Message); }

            // 4. вернуть
            err = ap.Revert();
            Thread.Sleep(600);
            log.AppendLine("--- вернули");
            log.AppendLine($"результат: {err ?? "ok"}; Arrow в реестре: «{RegValue("Arrow")}»; CursorBaseSize: {RegValue("CursorBaseSize")}; SM_CXCURSOR: {Native.GetSystemMetrics(Native.SM_CXCURSOR)}; системная стрелка: {Mean(SysCursors.Load(32512, 32))}");
            log.AppendLine($"папка с файлами убрана: {!Directory.Exists(Path.Combine(CursorSettings.Dir, "cursors"))}");
        }
        catch (Exception ex) { log.AppendLine("ОШИБКА: " + ex); }
        File.WriteAllText(Path.Combine(dir, "report.txt"), log.ToString());
    }

    // ---------- снимки форм и указателей ----------
    static void Draw(DrawingContext dc, CurImg? img, double x, double y)
    {
        if (img != null) dc.DrawImage(Img.ToSource(img), new Rect(x, y, img.W, img.H));
    }

    static void Save(DrawingVisual v, int w, int h, string file)
    {
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(v);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(file);
        enc.Save(fs);
    }

    static FormattedText Text(string t, double size, Brush b) => new(t, CultureInfo.GetCultureInfo("ru-RU"), FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, b, 1.0);

    // Таблицы: все указатели Windows до и после перекраски, и все готовые формы — на тёмном и светлом фоне.
    public static void Sheets(string dir, CursorSettings s)
    {
        var fill = Col.Parse(s.Fill); var line = s.OutlineRgb();
        var dark = new SolidColorBrush(Color.FromRgb(14, 12, 28)); var light = new SolidColorBrush(Color.FromRgb(240, 240, 245));
        // указатели Windows
        {
            const int cw = 90, rh = 80;
            var v = new DrawingVisual();
            using (var dc = v.RenderOpen())
            {
                dc.DrawRectangle(dark, null, new Rect(0, 0, 4 * cw + 150, Roles.All.Length * rh));
                dc.DrawRectangle(light, null, new Rect(2 * cw + 150, 0, 2 * cw, Roles.All.Length * rh));
                for (var i = 0; i < Roles.All.Length; i++)
                {
                    var role = Roles.All[i];
                    var y = i * rh + 8;
                    dc.DrawText(Text(role.Title, 12, Brushes.White), new Point(6, y + 20));
                    var src = SysCursors.Load(role.Ocr, 64);
                    var tint = CursorApplier.Build(role, s, new[] { 64 })?.FirstOrDefault();
                    Draw(dc, src, 150, y); Draw(dc, tint, 150 + cw, y);
                    Draw(dc, src, 150 + 2 * cw, y); Draw(dc, tint, 150 + 3 * cw, y);
                }
            }
            Save(v, 4 * cw + 150, Roles.All.Length * rh, Path.Combine(dir, "roles.png"));
        }
        // формы
        {
            var shapes = Shapes.All.Where(x => Shapes.IsProcedural(x.Id)).ToList();
            const int cw = 100, rh = 120;
            var v = new DrawingVisual();
            using (var dc = v.RenderOpen())
            {
                dc.DrawRectangle(dark, null, new Rect(0, 0, shapes.Count * cw, rh));
                dc.DrawRectangle(light, null, new Rect(0, rh, shapes.Count * cw, rh));
                for (var i = 0; i < shapes.Count; i++)
                {
                    var img = Shapes.Render(shapes[i].Id, 64, fill, line);
                    Draw(dc, img, i * cw + 18, 12); Draw(dc, img, i * cw + 18, rh + 12);
                    dc.DrawText(Text(shapes[i].Title, 12, Brushes.White), new Point(i * cw + 10, 88));
                    // маленькая метка точки клика
                    if (img != null) { dc.DrawRectangle(Brushes.Cyan, null, new Rect(i * cw + 18 + img.HotX - 1, 12 + img.HotY - 1, 3, 3)); dc.DrawRectangle(Brushes.Red, null, new Rect(i * cw + 18 + img.HotX - 1, rh + 12 + img.HotY - 1, 3, 3)); }
                }
            }
            Save(v, shapes.Count * cw, rh * 2, Path.Combine(dir, "shapes.png"));
        }
    }

    // ---------- шлейф на настоящем экране ----------
    static void Capture(string file, int x, int y, int w, int h)
    {
        var sdc = GetDC(IntPtr.Zero);
        var mdc = CreateCompatibleDC(sdc);
        var bmp = CreateCompatibleBitmap(sdc, w, h);
        var old = SelectObject(mdc, bmp);
        BitBlt(mdc, 0, 0, w, h, sdc, x, y, 0x00CC0020 | 0x40000000);   // копия экрана вместе с прозрачными окнами
        var src = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        SelectObject(mdc, old);
        Native.DeleteObject(bmp); DeleteDC(mdc); ReleaseDC(IntPtr.Zero, sdc);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(src));
        using var fs = File.Create(file);
        enc.Save(fs);
    }

    // Запускает шлейф, сам водит курсором по экрану (и при желании кликает) и снимает экран.
    public static void OverlayTest(string dir, string style, string click, string mode)
    {
        Directory.CreateDirectory(dir);
        var log = new StringBuilder();
        try
        {
            var s = new CursorSettings { Shape = "classic", Fill = "#FF2A9D", Trail = style, Click = click, TrailColorMode = mode, TrailColor = "#00F0FF", TrailColor2 = "#FF2A9D", TrailLength = 55, TrailSize = 45 };
            s.Normalize();
            App.StartOverlay(s);
            Thread.Sleep(500);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i <= 80; i++)
            {
                var t = i / 80.0;
                SetCursorPos((int)(150 + 700 * t), (int)(430 + 110 * Math.Sin(t * 6.3)));
                Thread.Sleep(12);
            }
            log.AppendLine($"курсор проведён за {sw.ElapsedMilliseconds} мс; шлейф работает: {App.OverlayRunning}");
            if (click != "none")
            {
                mouse_event(0x2, 0, 0, 0, IntPtr.Zero); Thread.Sleep(30); mouse_event(0x4, 0, 0, 0, IntPtr.Zero);
                Thread.Sleep(140);
            }
            Capture(Path.Combine(dir, $"trail-{style}-{click}-{mode}.png"), 100, 250, 840, 360);
            log.AppendLine("снимок сделан");
            App.StopOverlay();
            log.AppendLine($"шлейф остановлен: {!App.OverlayRunning}");
        }
        catch (Exception ex) { log.AppendLine("ОШИБКА: " + ex); }
        File.AppendAllText(Path.Combine(dir, "overlay-report.txt"), $"[{style}/{click}/{mode}] " + log + Environment.NewLine);
    }
}
