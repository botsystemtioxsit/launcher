using System.IO;
using System.IO.Compression;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace SkinLoaderApp;

// Проверка Skin Loader на Windows-машине GitHub (CI): SkinLoader.exe --selftest=<папка> [--apply-tests]
//  1) очистка чужих скинов (всё враждебное выбрасывается или зажимается в пределы),
//  2) запись в настройки плагина без потери остальных полей,
//  3) сбор скина из настроек плагинов, библиотека, файл .ghostskin (в том числе враждебный zip),
//  4) с ключом --apply-tests — настоящее применение и откат через установленные плагины (цвет, курсор, обои, панель задач).
static class SelfTest
{
    static string log = "";
    static int bad;
    static void Say(string t) { try { File.AppendAllText(log, t + Environment.NewLine); } catch { } }
    static void Check(string name, bool ok, string extra = "") { Say((ok ? "ok     " : "ПЛОХО  ") + name + (ok ? "" : "  → " + extra)); if (!ok) bad++; }
    static bool Throws(Action a) { try { a(); return false; } catch (InvalidDataException) { return true; } catch { return false; } }

    public static void Run(string dir)
    {
        Directory.CreateDirectory(dir);
        log = Path.Combine(dir, "report.txt");
        File.WriteAllText(log, "");
        var applyTests = Environment.GetCommandLineArgs().Contains("--apply-tests");
        try
        {
            Validation();
            var tmp = Path.Combine(dir, "data");
            Directory.CreateDirectory(tmp);
            Plugins.DataDirOverride = tmp;
            MergeAndCapture();
            LibraryTests(dir);
            Plugins.DataDirOverride = null;
            if (applyTests) ApplyTests(dir); else Say("(настоящее применение через плагины не запускалось: нет ключа --apply-tests)");
        }
        catch (Exception ex) { Say("ОШИБКА: " + ex); bad++; }
        Say(bad == 0 ? "ИТОГО: всё хорошо" : "ИТОГО: проблем " + bad);
    }

    // ---------- 1. очистка ----------
    static void Validation()
    {
        Say("=== очистка скина ===");
        var good = Skin.Parse("{\"format\":1,\"name\":\"Тест\",\"author\":\"A\",\"description\":\"d\",\"parts\":{\"color\":{\"color\":\"#ff2a9d\",\"windows\":true,\"taskbar\":false}}}");
        Check("нормальный скин читается, цвет приводится к верхнему регистру", good.Name == "Тест" && SkinSchema.Str(good.Part("color")!, "color") == "#FF2A9D" && SkinSchema.Bool(good.Part("color")!, "taskbar") == false);
        var hostile = Skin.Parse("{\"format\":1,\"name\":\"\\u202Eevil\\u0007  name\\n\",\"author\":\"" + new string('x', 500) + "\",\"parts\":{" +
            "\"cursor\":{\"shape\":\"mine:../../x\",\"fill\":\"red\",\"size\":99999,\"trailLength\":-50,\"trail\":\"<script>\",\"click\":42,\"all\":\"yes\"}," +
            "\"wallpaper\":{\"id\":\"..\\\\..\\\\Windows\\\\evil\",\"fit\":\"cover\"}," +
            "\"run\":{\"cmd\":\"calc.exe\"}," +
            "\"icons\":{\"color\":\"#00F0FF\",\"style\":\"hue\",\"strength\":150,\"path\":\"C:\\\\x.ico\"}," +
            "\"taskbar\":{\"shape\":\"pill\",\"radius\":9999,\"sideMargin\":-3,\"zoneApps\":\"up\",\"extra\":{\"deep\":1}}}}");
        var cur = hostile.Part("cursor")!;
        Check("имя: управляющие и «перевёрнутые» символы убраны", !hostile.Name.Any(c => char.IsControl(c) || c == '\u202E') && hostile.Name.Contains("evil"), hostile.Name);
        Check("автор обрезан до 40 знаков", hostile.Author.Length <= 40, hostile.Author.Length.ToString());
        Check("свой курсор (mine:) заменён на classic", SkinSchema.Str(cur, "shape") == "classic");
        Check("цвет «red» → цвет по умолчанию, размер зажат в 100–300, длина шлейфа ≥ 0",
            SkinSchema.Str(cur, "fill") == "#FF2A9D" && SkinSchema.Int(cur, "size") == 300 && SkinSchema.Int(cur, "trailLength") == 0);
        Check("неизвестные значения → по умолчанию (шлейф none, клик none, all true)", SkinSchema.Str(cur, "trail") == "none" && SkinSchema.Str(cur, "click") == "none" && SkinSchema.Bool(cur, "all") == true);
        Check("обои с путём вместо идентификатора — часть выброшена", hostile.Part("wallpaper") == null);
        Check("неизвестная часть «run» выброшена", hostile.Part("run") == null && !hostile.Parts.ContainsKey("run"));
        var ic = hostile.Part("icons")!;
        Check("значки: сила зажата в 100, лишнее поле path исчезло", SkinSchema.Int(ic, "strength") == 100 && ic["path"] == null && ic.Count == 3, ic.ToJsonString());
        var tb = hostile.Part("taskbar")!;
        Check("панель: радиус ≤ 30, отступ ≥ 0, неверная зона → default, лишнее исчезло",
            SkinSchema.Int(tb, "radius") == 30 && SkinSchema.Int(tb, "sideMargin") == 0 && SkinSchema.Str(tb, "zoneApps") == "default" && tb["extra"] == null, tb.ToJsonString());
        Check("слишком большой файл отвергается", Throws(() => Skin.Parse("{\"format\":1,\"name\":\"" + new string('a', 70000) + "\",\"parts\":{}}")));
        Check("более новый формат отвергается", Throws(() => Skin.Parse("{\"format\":99,\"parts\":{\"color\":{}}}")));
        Check("не объект / не JSON отвергается", Throws(() => Skin.Parse("[1,2]")) && Throws(() => Skin.Parse("это не json")));
        Check("скин без допустимых частей отвергается", Throws(() => Skin.Parse("{\"format\":1,\"parts\":{\"run\":{}}}")));
        Check("слишком глубокий JSON отвергается", Throws(() => Skin.Parse("{\"format\":1,\"parts\":" + string.Concat(Enumerable.Repeat("{\"a\":", 20)) + "1" + new string('}', 20) + "}")));
        var round = Skin.Parse(hostile.ToJson());
        Check("очищенный скин после записи и чтения не меняется", round.ToJson() == hostile.ToJson());
    }

    // ---------- 2–3. запись в настройки плагина и сбор ----------
    static void MergeAndCapture()
    {
        Say("=== настройки плагинов ===");
        var file = Plugins.SettingsFile("cursor");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "{\"Shape\":\"round\",\"Fill\":\"#112233\",\"Applied\":true,\"UnknownField\":{\"keep\":\"me\"},\"Size\":150}");
        Plugins.MergeSettings("cursor", new Dictionary<string, object> { ["Fill"] = "#00FF88", ["TrailLength"] = 70, ["All"] = false });
        var raw = Plugins.ReadSettings("cursor");
        Check("запись меняет только перечисленные поля", SkinSchema.Str(raw, "Fill") == "#00FF88" && SkinSchema.Int(raw, "TrailLength") == 70 && SkinSchema.Bool(raw, "All") == false);
        Check("остальные поля (в том числе неизвестные) сохраняются", SkinSchema.Str(raw, "Shape") == "round" && SkinSchema.Int(raw, "Size") == 150 && raw["UnknownField"] is JsonObject u && SkinSchema.Str(u, "keep") == "me");
        Check("файл не оставляет временных .tmp", !File.Exists(file + ".tmp"));

        File.WriteAllText(Plugins.SettingsFile("color"), "{\"Color\":\"#FF2A9D\",\"Windows\":true,\"Taskbar\":false,\"Applied\":true}");
        File.WriteAllText(Plugins.SettingsFile("wallpaper"), "{\"Active\":\"u-my-own-video\",\"Fit\":\"cover\",\"Fps\":60}");
        var scan = Capture.Scan();
        var color = scan.First(s => s.Part == "color");
        Check("сбор: цвет читается из настроек, «используется» по Applied", color.Settings != null && SkinSchema.Str(color.Settings, "color") == "#FF2A9D" && SkinSchema.Bool(color.Settings, "taskbar") == false && color.InUse);
        var curI = scan.First(s => s.Part == "cursor");
        Check("сбор: курсор — поля переложены в имена скина", curI.Settings != null && SkinSchema.Str(curI.Settings, "fill") == "#00FF88" && SkinSchema.Int(curI.Settings, "size") == 150 && SkinSchema.Int(curI.Settings, "trailLength") == 70, curI.Settings?.ToJsonString() ?? "null");
        var wp = scan.First(s => s.Part == "wallpaper");
        Check("сбор: свои обои (u-…) в скин не берутся", wp.Settings == null);
        Check("сбор: у плагина без настроек части нет", scan.First(s => s.Part == "icons").Settings == null);
        var skin = Capture.Build("Сборка", "Автор", "Описание", new[] { "color", "cursor", "wallpaper" }, out var warn);
        Check("сборка: войдёт цвет и курсор, обои пропущены с замечанием", skin.Parts.ContainsKey("color") && skin.Parts.ContainsKey("cursor") && !skin.Parts.ContainsKey("wallpaper") && warn.Count > 0, string.Join("; ", warn));
        Check("сборка из пустого набора отвергается", Throws(() => Capture.Build("x", "", "", new[] { "icons" }, out _)));
    }

    // ---------- 4. библиотека и файл скина ----------
    static void LibraryTests(string dir)
    {
        Say("=== библиотека и файл .ghostskin ===");
        var skin = Skin.Parse("{\"format\":1,\"name\":\"Библиотека\",\"author\":\"A\",\"parts\":{\"color\":{\"color\":\"#00F0FF\"},\"cursor\":{\"shape\":\"neon\",\"fill\":\"#00F0FF\"}}}");
        var id = Library.Save(skin, null);
        var listed = Library.List().FirstOrDefault(s => s.Skin.Id == id);
        Check("сохранённый скин виден в списке", listed != null && listed.Skin.Name == "Библиотека");
        var file = Path.Combine(dir, "export" + Library.Extension);
        Library.Export(skin, null, file);
        var imp = Library.Import(file);
        Check("файл .ghostskin: экспорт → импорт даёт тот же скин", imp.Skin.ToJson() == Skin.Parse(skin.ToJson()).ToJson());
        Library.Delete(id);
        Check("удаление убирает скин из библиотеки", Library.List().All(s => s.Skin.Id != id));

        // враждебные файлы
        string Zip(string name, params (string Name, byte[] Data)[] entries)
        {
            var p = Path.Combine(dir, name);
            if (File.Exists(p)) File.Delete(p);
            using var z = ZipFile.Open(p, ZipArchiveMode.Create);
            foreach (var (n, d) in entries) { var e = z.CreateEntry(n); using var s = e.Open(); s.Write(d, 0, d.Length); }
            return p;
        }
        var okJson = System.Text.Encoding.UTF8.GetBytes(skin.ToJson());
        var warnings = new List<string>();
        var hostile = Library.Import(Zip("hostile.zip", ("skin.json", okJson), ("../evil.exe", new byte[] { 77, 90 }), ("run.bat", System.Text.Encoding.ASCII.GetBytes("calc")), ("preview.jpg", new byte[] { 1, 2, 3, 4 })), warnings);
        Check("враждебный zip: берётся только skin.json, лишние файлы и битое превью игнорируются", hostile.Skin.Name == "Библиотека" && hostile.Preview == null && warnings.Count >= 2, string.Join("; ", warnings));
        Check("zip без skin.json отвергается", Throws(() => Library.Import(Zip("nojson.zip", ("readme.txt", new byte[] { 65 })))));
        Check("zip со слишком многими вложениями отвергается", Throws(() => Library.Import(Zip("many.zip", Enumerable.Range(0, 20).Select(i => ("f" + i + ".txt", new byte[] { 65 })).ToArray()))));
        Check("не архив отвергается", Throws(() => { var p = Path.Combine(dir, "junk.ghostskin"); File.WriteAllText(p, "это не zip"); Library.Import(p); }));
    }

    // ---------- 5. настоящее применение ----------
    static void ApplyTests(string dir)
    {
        Say("=== применение через настоящие плагины ===");
        foreach (var d in Plugins.All) Say($"  {d.Name}: " + (Plugins.ExePath(d) ?? "не установлен"));

        string? ArrowCursor() => Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors")?.GetValue("Arrow") as string;

        // курсор
        if (Plugins.ExePath(Plugins.ForPart("cursor")) != null)
        {
            var before = ArrowCursor();
            var skin = Skin.Parse("{\"format\":1,\"name\":\"t\",\"parts\":{\"cursor\":{\"shape\":\"neon\",\"fill\":\"#00FF88\",\"trail\":\"none\"}}}");
            var res = Applier.Apply(skin, new[] { "cursor" }).GetAwaiter().GetResult();
            Check("курсор: применён (плагин подтвердил)", res.All(r => r.Ok), string.Join("; ", res.Select(r => r.Message)));
            var after = ArrowCursor();
            Check("курсор: в реестре стоит курсор Cursor Plus", after != null && after.Contains("cursor-plus", StringComparison.OrdinalIgnoreCase), after ?? "null");
            var st = Plugins.ReadSettings("cursor");
            Check("курсор: в настройках плагина цвет из скина", SkinSchema.Str(st, "Fill") == "#00FF88" && SkinSchema.Str(st, "Shape") == "neon");
            var back = Applier.RevertAll(new[] { "cursor" }).GetAwaiter().GetResult();
            Check("курсор: откат прошёл", back.All(r => r.Ok), string.Join("; ", back.Select(r => r.Message)));
            var restored = ArrowCursor();
            Check("курсор: в реестре снова прежний курсор", restored == before || (restored != null && !restored.Contains("cursor-plus", StringComparison.OrdinalIgnoreCase)), restored ?? "null");
        }
        else Say("  курсор: плагина нет — пропущено");

        // цвет (без окон и панели: проверяем запись и подтверждение, не красим чужие окна раннера)
        if (Plugins.ExePath(Plugins.ForPart("color")) != null)
        {
            var skin = Skin.Parse("{\"format\":1,\"name\":\"t\",\"parts\":{\"color\":{\"color\":\"#00FF88\",\"windows\":false,\"taskbar\":false}}}");
            var res = Applier.Apply(skin, new[] { "color" }).GetAwaiter().GetResult();
            Check("цвет: применён (плагин подтвердил)", res.All(r => r.Ok), string.Join("; ", res.Select(r => r.Message)));
            var st = Plugins.ReadSettings("color");
            Check("цвет: в настройках плагина цвет из скина", SkinSchema.Str(st, "Color") == "#00FF88" && SkinSchema.Bool(st, "Windows") == false);
            var back = Applier.RevertAll(new[] { "color" }).GetAwaiter().GetResult();
            Check("цвет: откат прошёл", back.All(r => r.Ok), string.Join("; ", back.Select(r => r.Message)));
            Check("цвет: после отката «Applied» снят", SkinSchema.Bool(Plugins.ReadSettings("color"), "Applied") == false);
        }
        else Say("  цвет: плагина нет — пропущено");

        // обои
        if (Plugins.ExePath(Plugins.ForPart("wallpaper")) != null)
        {
            var skin = Skin.Parse("{\"format\":1,\"name\":\"t\",\"parts\":{\"wallpaper\":{\"id\":\"b-neon-grid\",\"fit\":\"contain\",\"fps\":30}}}");
            var res = Applier.Apply(skin, new[] { "wallpaper" }).GetAwaiter().GetResult();
            Check("обои: включены (плагин подтвердил)", res.All(r => r.Ok), string.Join("; ", res.Select(r => r.Message)));
            var st = Plugins.ReadSettings("wallpaper");
            Check("обои: в настройках плагина id, вписывание и частота из скина", SkinSchema.Str(st, "Active") == "b-neon-grid" && SkinSchema.Str(st, "Fit") == "contain" && SkinSchema.Int(st, "Fps") == 30, st.ToJsonString());
            var back = Applier.RevertAll(new[] { "wallpaper" }).GetAwaiter().GetResult();
            Check("обои: откат прошёл, плагин закрылся", back.All(r => r.Ok), string.Join("; ", back.Select(r => r.Message)));
            Check("обои: в настройках снова пусто", string.IsNullOrEmpty(SkinSchema.Str(Plugins.ReadSettings("wallpaper"), "Active")));
        }
        else Say("  обои: плагина нет — пропущено");

        // панель задач: записываем и запускаем; снимки и подключение движка проверяет отдельный сценарий taskbar
        if (Plugins.ExePath(Plugins.ForPart("taskbar")) != null)
        {
            var skin = Skin.Parse("{\"format\":1,\"name\":\"t\",\"parts\":{\"taskbar\":{\"shape\":\"pill\",\"radius\":18,\"sideMargin\":120}}}");
            var res = Applier.Apply(skin, new[] { "taskbar" }).GetAwaiter().GetResult();
            Check("панель: применена (плагин запущен)", res.All(r => r.Ok), string.Join("; ", res.Select(r => r.Message)));
            var st = Plugins.ReadSettings("taskbar");
            Check("панель: в настройках форма, радиус и отступ из скина, движок включён",
                SkinSchema.Str(st, "Shape") == "pill" && SkinSchema.Int(st, "Radius") == 18 && SkinSchema.Int(st, "SideMargin") == 120 && SkinSchema.Bool(st, "UseXaml") == true, st.ToJsonString());
            Thread.Sleep(4000);
            var back = Applier.RevertAll(new[] { "taskbar" }).GetAwaiter().GetResult();
            Check("панель: откат прошёл, плагин закрылся", back.All(r => r.Ok), string.Join("; ", back.Select(r => r.Message)));
            var st2 = Plugins.ReadSettings("taskbar");
            Check("панель: в настройках снова «как в Windows»", SkinSchema.Str(st2, "Shape") == "none" && SkinSchema.Bool(st2, "UseXaml") == false, st2.ToJsonString());
        }
        else Say("  панель: плагина нет — пропущено");
        Say("  значки: настоящее применение здесь не запускается (долго и перезапускает Проводник) — его проверяет сценарий icons");
    }
}
