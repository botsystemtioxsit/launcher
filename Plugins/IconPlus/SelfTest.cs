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
    static string DesktopUrl => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "IconPlusTest.url");
    static string PublicUrl => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "IconPlusTestAll.url");
    static string BrokenLnk => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "IconPlusTestBroken.lnk");
    static string PublicLnk => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "IconPlusTestAll.lnk");
    static string StartAllLnk => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs", "IconPlusTestAll.lnk");

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
        if (File.Exists(DesktopUrl) && Shortcuts.ReadIcon(DesktopUrl, out var u1, out var ui1)) Add("url", u1 + "," + ui1); else Add("url", "");
        if (File.Exists(PublicUrl) && Shortcuts.ReadIcon(PublicUrl, out var u2, out var ui2)) Add("url-public", u2 + "," + ui2); else Add("url-public", "");
        if (File.Exists(BrokenLnk) && Shortcuts.ReadIcon(BrokenLnk, out var b1, out var bi1)) Add("lnk-broken", b1 + "," + bi1); else Add("lnk-broken", "");
        Add("docs-ini", DesktopIni.Read(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "desktop.ini"), "IconResource") ?? "(нет строки)");
        foreach (var x in new[] { ".ttf", ".cab", ".reg", ".msi" }) Add("assoc" + x, Native.AssocDefaultIcon(x));
        if (File.Exists(PublicLnk) && Shortcuts.ReadIcon(PublicLnk, out var p2, out var i2)) Add("lnk-public", p2 + "," + i2); else Add("lnk-public", "");
        if (File.Exists(StartAllLnk) && Shortcuts.ReadIcon(StartAllLnk, out var p3, out var i3)) Add("lnk-startall", p3 + "," + i3); else Add("lnk-startall", "");
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
        foreach (var u in new[] { DesktopUrl, PublicUrl })
        {
            try { File.WriteAllText(u, "[InternetShortcut]\r\nURL=https://example.com/\r\nIconIndex=5\r\nIconFile=C:\\Windows\\System32\\imageres.dll\r\n"); } catch { }
        }
        try
        {
            dynamic sh2 = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            var l2 = sh2.CreateShortcut(BrokenLnk);
            l2.TargetPath = @"C:\Windows\System32\notepad.exe";
            l2.IconLocation = @"C:\NoSuchFolder\nope.ico,0";   // значка нет на месте — должен сработать запасной путь
            l2.Save();
        }
        catch { }
        foreach (var path in new[] { DesktopLnk, PublicLnk, StartAllLnk })
        {
            try
            {
                dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
                var l = sh.CreateShortcut(path);
                l.TargetPath = @"C:\Windows\System32\notepad.exe";
                l.Save();
            }
            catch { }
        }
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
                Enabled = new() { "folder", "drive-fixed", "thispc", "recycle-empty", "network", "ft-text", "ft-archive", "ft-image", "ft-audio", "ft-pdf", "ft-doc", "lnk-desktop", "lnk-desktop-all", "lnk-start-all", "ft-all", "kf" },
            };
            s.Normalize();
            var res = engine.ApplyColor(s);
            log.AppendLine($"перекраска: сделано {res.Done}, не вышло {res.Failed}, записей запомнено {engine.ChangesCount}");
            foreach (var m in res.Messages.Take(20)) log.AppendLine("  " + m);
            log.AppendLine(Report.Build(res));
            // как Проводник рисует значки (для проверки прозрачности и стрелки ярлыка)
            try
            {
                void Png(string name, System.Windows.Media.Imaging.BitmapSource? img) { if (img != null) File.WriteAllBytes(Path.Combine(outDir, name), IconData.Png(img)); else log.AppendLine("не отрисовалось: " + name); }
                Png("render-docs-folder.png", ShellIcon.Render(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 128));
                Png("render-lnk-target.png", ShellIcon.RenderLnkTarget(DesktopLnk, 128));
                Png("render-url.png", ShellIcon.Render(DesktopUrl, 128));
            }
            catch (Exception ex) { log.AppendLine("отрисовка: " + ex.Message); }
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
        // ярлык приложения «Пуска» (раньше падал: «Entry point was not found»): создаётся, в нём записан идентификатор программы
        try
        {
            var lnk = Path.Combine(Box, "startapp-test.lnk");
            var ico = Path.Combine(Paths.Generated, "startapp-test.ico");
            Paths.Ensure();
            var imgs = IconData.LoadAllSizes("shell:" + StartApps.AppsFolderPath("Microsoft.Windows.Explorer"));
            if (imgs != null) IconData.WriteIco(ico, imgs);
            StartApps.CreateLink(lnk, "Microsoft.Windows.Explorer", File.Exists(ico) ? ico : @"C:\Windows\explorer.exe");
            log.AppendLine($"ярлык приложения «Пуска»: файл есть: {File.Exists(lnk)}, AUMID в нём: {Shortcuts.ReadAumid(lnk)}");
            File.Delete(lnk); try { File.Delete(ico); } catch { }
        }
        catch (Exception ex) { log.AppendLine("ярлык приложения «Пуска»: ОШИБКА " + ex.GetType().Name + ": " + ex.Message); }
        foreach (var f in new[] { PublicLnk, StartAllLnk, DesktopUrl, PublicUrl, BrokenLnk }) { try { File.Delete(f); } catch { } }
        File.WriteAllText(Path.Combine(outDir, "report.txt"), log.ToString());
    }

    static string Mean(System.Windows.Media.Imaging.BitmapSource? img)
    {
        if (img == null) return "нет значка";
        var px = IconData.Pixels(img);
        double r = 0, g = 0, b = 0; var n = 0;
        for (var i = 0; i + 3 < px.Length; i += 4) { if (px[i + 3] < 128) continue; b += px[i]; g += px[i + 1]; r += px[i + 2]; n++; }
        return n == 0 ? "прозрачный" : $"средний цвет R{r / n:0} G{g / n:0} B{b / n:0}";
    }

    // Приложения «Пуска» без ярлыков: создаём свои ярлыки, смотрим, не появились ли дубли, как Проводник рисует значок, и возвращаем всё как было.
    public static void StartAppsTest(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var log = new StringBuilder();
        try
        {
            var all = StartApps.List();
            File.WriteAllLines(Path.Combine(outDir, "apps-before.txt"), all.Select(a => a.Name + " | " + a.AppId));
            log.AppendLine($"в списке «Пуска» {all.Count} приложений, из них с «!» в идентификаторе (магазинные): {all.Count(a => a.AppId.Contains('!'))}");
            var cand = StartApps.Candidates(Array.Empty<string>(), all);
            log.AppendLine($"кандидатов на свой ярлык: {cand.Count}");
            foreach (var a in cand.Take(60)) log.AppendLine($"  {a.Name} | {a.AppId}");
            var first = cand.Take(10).ToList();
            var before = first.ToDictionary(a => a.AppId, a => ShellIcon.Render(StartApps.AppsFolderPath(a.AppId), 64));
            var engine = new IconEngine();
            var s = new IconSettings { Color = "#FF2A9D", Style = Tint.Mono, Strength = 100, Enabled = new() { "start-apps" } };
            s.Normalize();
            var res = engine.ApplyColor(s);
            log.AppendLine($"применено: сделано {res.Done}, не вышло {res.Failed}");
            log.AppendLine(Report.Build(res));
            System.Threading.Thread.Sleep(5000);
            var after = StartApps.List();
            File.WriteAllLines(Path.Combine(outDir, "apps-after.txt"), after.Select(a => a.Name + " | " + a.AppId));
            log.AppendLine($"после: в списке {after.Count} приложений (было {all.Count})");
            var created = Directory.Exists(StartApps.ProgramsFolder) ? Directory.GetFiles(StartApps.ProgramsFolder, "*.lnk").Where(f => engine.State.Created.Contains(f, StringComparer.OrdinalIgnoreCase)).ToList() : new List<string>();
            log.AppendLine($"создано ярлыков: {created.Count}");
            var i = 0;
            foreach (var a in first)
            {
                i++;
                var byId = after.Count(x => string.Equals(x.AppId, a.AppId, StringComparison.OrdinalIgnoreCase));
                var byName = after.Count(x => string.Equals(x.Name, a.Name, StringComparison.OrdinalIgnoreCase));
                var img = ShellIcon.Render(StartApps.AppsFolderPath(a.AppId), 64);
                var lnk = Path.Combine(StartApps.ProgramsFolder, StartApps.FileName(a.Name) + ".lnk");
                var info = File.Exists(lnk) ? $"ярлык есть, AUMID в ярлыке: {Shortcuts.ReadAumid(lnk)}" : "ярлыка нет";
                log.AppendLine($"[{a.Name}] записей с этим идентификатором: {byId}, с этим именем: {byName}; {info}");
                log.AppendLine($"    значок в «Пуске» (как рисует Проводник): было {Mean(before[a.AppId])} → стало {Mean(img)}");
                try
                {
                    if (before[a.AppId] != null) File.WriteAllBytes(Path.Combine(outDir, $"app{i}-before.png"), IconData.Png(before[a.AppId]!));
                    if (img != null) File.WriteAllBytes(Path.Combine(outDir, $"app{i}-after.png"), IconData.Png(img));
                }
                catch { }
            }
            var restored = engine.RestoreAll();
            log.AppendLine($"вернули: {restored} изменений, ярлыков осталось: {created.Count(File.Exists)}");
            System.Threading.Thread.Sleep(4000);
            var back = StartApps.List();
            log.AppendLine($"после возврата в списке {back.Count} приложений (было {all.Count})");
            foreach (var a in first.Take(3)) log.AppendLine($"    {a.Name}: значок после возврата {Mean(ShellIcon.Render(StartApps.AppsFolderPath(a.AppId), 64))}");
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
            log.AppendLine("не подобрано: " + string.Join(", ", Target.All.Where(t => t.Keywords.Length > 0 && !map.ContainsKey(t.Id)).Select(t => t.Id)));
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
