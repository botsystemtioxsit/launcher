using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IconPlusApp;

// Самопроверка для CI (запускается ключами --selftest / --probe / --pack-test): настоящие значки Проводника до, во время и после перекраски.
static class SelfTest
{
    static string Box => Path.Combine(Path.GetTempPath(), "IconPlusTest");
    static string DesktopLnk => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "IconPlusTest.lnk");

    // Где Проводник сейчас берёт значки тестовых объектов. Запускается отдельным процессом — чтобы ничего не оставалось в памяти.
    public static void Probe(string file)
    {
        var sb = new StringBuilder();
        void Add(string k, string v) => sb.AppendLine(k + "=" + v);
        Add("folder", Native.IconLocation(Path.Combine(Box, "sub"), false, true));
        Add("drive-C", Native.IconLocation(@"C:\", false));
        Add("thispc", Native.IconLocationOfShellItem("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"));
        Add("recycle", Native.IconLocationOfShellItem("::{645FF040-5081-101B-9F08-00AA002F954E}"));
        Add("network", Native.IconLocationOfShellItem("::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}"));
        foreach (var f in new[] { "a.txt", "b.zip", "c.png", "d.mp3", "e.pdf", "f.docx" }) Add(f, Native.AssocDefaultIcon(Path.GetExtension(f)));
        if (File.Exists(DesktopLnk) && Shortcuts.ReadIcon(DesktopLnk, out var p, out var i)) Add("lnk", p + "," + i); else Add("lnk", "");
        File.WriteAllText(file, sb.ToString());
    }

    static Dictionary<string, string> RunProbe(string outDir, string name)
    {
        var file = Path.Combine(outDir, "probe-" + name + ".txt");
        using var pr = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--probe=\"" + file + "\"") { UseShellExecute = false })!;
        pr.WaitForExit(60000);
        var map = new Dictionary<string, string>();
        if (File.Exists(file))
            foreach (var line in File.ReadAllLines(file)) { var k = line.IndexOf('='); if (k > 0) map[line[..k]] = line[(k + 1)..]; }
        return map;
    }

    static void PrepareBox()
    {
        if (Directory.Exists(Box)) Directory.Delete(Box, true);
        Directory.CreateDirectory(Path.Combine(Box, "sub"));
        foreach (var f in new[] { "a.txt", "b.zip", "c.png", "d.mp3", "e.pdf", "f.docx" }) File.WriteAllText(Path.Combine(Box, f), "x");
        try
        {
            dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            var l = sh.CreateShortcut(DesktopLnk);
            l.TargetPath = @"C:\Windows\System32\notepad.exe";
            l.Save();
        }
        catch { }
    }

    static void Compare(StringBuilder log, string title, Dictionary<string, string> a, Dictionary<string, string> b, bool expectChange)
    {
        log.AppendLine("--- " + title);
        foreach (var k in a.Keys)
        {
            b.TryGetValue(k, out var bv);
            var same = string.Equals(a[k], bv, StringComparison.OrdinalIgnoreCase);
            var verdict = expectChange ? (same ? "НЕ ИЗМЕНИЛОСЬ" : "изменилось") : (same ? "как было" : "ОТЛИЧАЕТСЯ");
            log.AppendLine($"{k,-9} {verdict,-14} было: {a[k]}   стало: {bv}");
        }
    }

    public static void Run(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var log = new StringBuilder();
        try
        {
            PrepareBox();
            var before = RunProbe(outDir, "before");
            var engine = new IconEngine();
            var s = new IconSettings
            {
                Color = "#FF2A9D", Style = Tint.Mono, Strength = 100,
                Enabled = new() { "folder", "drive-fixed", "thispc", "recycle-empty", "network", "ft-text", "ft-archive", "ft-image", "ft-audio", "ft-pdf", "ft-doc", "lnk-desktop" },
            };
            s.Normalize();
            var res = engine.ApplyColor(s);
            log.AppendLine($"перекраска: сделано {res.Done}, не вышло {res.Failed}, записей запомнено {engine.ChangesCount}");
            foreach (var m in res.Messages.Take(20)) log.AppendLine("  " + m);
            try { WritePreviews(engine, s, Path.Combine(outDir, "previews.png")); log.AppendLine("превью: previews.png"); } catch (Exception ex) { log.AppendLine("превью не вышли: " + ex.Message); }
            System.Threading.Thread.Sleep(1500);
            var after = RunProbe(outDir, "after");
            Compare(log, "после перекраски (ожидаем изменения)", before, after, true);
            var restored = engine.RestoreAll();
            log.AppendLine($"вернули: {restored} изменений, осталось запомнено {engine.ChangesCount}");
            System.Threading.Thread.Sleep(1500);
            var back = RunProbe(outDir, "restored");
            Compare(log, "после «вернуть как было» (ожидаем как было)", before, back, false);
        }
        catch (Exception ex) { log.AppendLine("ОШИБКА: " + ex); }
        File.WriteAllText(Path.Combine(outDir, "report.txt"), log.ToString());
    }

    // Набор значков: ввоз архива, подбор по именам, применение и возврат.
    public static void PackTest(string archive, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var log = new StringBuilder();
        var tag = Path.GetExtension(archive).TrimStart('.');
        try
        {
            PrepareBox();
            var before = RunProbe(outDir, "pack-" + tag + "-before");
            var name = Packs.Import(archive, null);
            var pack = Packs.List().First(p => p.Name == name);
            log.AppendLine($"набор «{name}»: значков {pack.Icons.Count}");
            var map = Packs.Suggest(pack);
            foreach (var kv in map.OrderBy(k => k.Key)) log.AppendLine($"  {kv.Key,-16} ← {Path.GetFileName(kv.Value)}");
            log.AppendLine("не подобрано: " + string.Join(", ", Target.All.Where(t => t.Kind != TargetKind.Shortcuts && !map.ContainsKey(t.Id)).Select(t => t.Id)));
            var engine = new IconEngine();
            var res = engine.ApplyPack(map);
            log.AppendLine($"применено: {res.Done}, не вышло {res.Failed}");
            System.Threading.Thread.Sleep(1500);
            var after = RunProbe(outDir, "pack-" + tag + "-after");
            Compare(log, "с набором", before, after, true);
            engine.RestoreAll();
            System.Threading.Thread.Sleep(1500);
            Compare(log, "после возврата", before, RunProbe(outDir, "pack-" + tag + "-restored"), false);
            Packs.Delete(name);
        }
        catch (Exception ex) { log.AppendLine("ОШИБКА: " + ex); }
        File.WriteAllText(Path.Combine(outDir, "pack-" + tag + ".txt"), log.ToString());
    }

    // Картинка «до → после» для нескольких целей.
    static void WritePreviews(IconEngine engine, IconSettings s, string file)
    {
        var ids = new[] { "folder", "drive-fixed", "thispc", "recycle-empty", "network", "ft-text", "ft-archive", "ft-image", "ft-audio", "ft-pdf", "ft-doc" };
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(13, 11, 26)), null, new Rect(0, 0, 220 * 6, 90 * 2 + 10));
            for (var i = 0; i < ids.Length; i++)
            {
                var t = Target.ById(ids[i])!;
                var (b, a) = engine.Preview(t, s);
                var x = (i % 6) * 220 + 10; var y = (i / 6) * 90 + 8;
                if (b != null) dc.DrawImage(b, new Rect(x, y, 48, 48));
                if (a != null) dc.DrawImage(a, new Rect(x + 90, y, 48, 48));
                var ft = new FormattedText(t.Title, CultureInfo.GetCultureInfo("ru-RU"), FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.White, 1.0);
                dc.DrawText(ft, new Point(x, y + 56));
            }
        }
        var rtb = new RenderTargetBitmap(220 * 6, 90 * 2 + 10, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(file);
        enc.Save(fs);
    }
}
