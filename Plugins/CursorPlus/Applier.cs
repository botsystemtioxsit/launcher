using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace CursorPlusApp;

// Записывает курсоры в настройки Windows (HKCU\Control Panel\Cursors) и возвращает всё как было.
// Файлы .cur кладутся в %APPDATA%\BattleLauncher\cursor-plus\cursors\<метка>\; прежние значения реестра запоминаются в originals.json.
sealed class CursorApplier
{
    const string Key = @"Control Panel\Cursors";

    sealed class Originals
    {
        public Dictionary<string, string?> Values { get; set; } = new();   // имя → прежнее значение (null — значения не было)
        public Dictionary<string, string> Kinds { get; set; } = new();     // имя → String | ExpandString | DWord
    }

    static string OriginalsFile => Path.Combine(CursorSettings.Dir, "originals.json");
    static string CursorsDir => Path.Combine(CursorSettings.Dir, "cursors");

    static IEnumerable<string> Names() => Roles.All.Select(r => r.Reg).Append("CursorBaseSize");

    // Прежние значения запоминаем один раз — до первого изменения.
    static void Backup()
    {
        if (File.Exists(OriginalsFile)) return;
        var o = new Originals();
        using var k = Registry.CurrentUser.OpenSubKey(Key);
        foreach (var name in Names())
        {
            var v = k?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (v == null) { o.Values[name] = null; continue; }
            var kind = k!.GetValueKind(name);
            o.Kinds[name] = kind.ToString();
            o.Values[name] = v.ToString();
        }
        Directory.CreateDirectory(CursorSettings.Dir);
        File.WriteAllText(OriginalsFile, JsonSerializer.Serialize(o));
    }

    public static int BaseSize(CursorSettings s) => Math.Clamp((int)Math.Round(32 * s.Size / 100.0 / 16.0) * 16, 32, 256);

    // Картинки одного указателя на все размеры. null — такой указатель менять не нужно.
    public static List<CurImg>? Build(Role role, CursorSettings s, int[]? sizes = null)
    {
        var fill = Col.Parse(s.Fill);
        var line = s.OutlineRgb();
        sizes ??= Shapes.Sizes;
        var own = s.Shape.StartsWith("mine:", StringComparison.Ordinal);
        var custom = Shapes.IsProcedural(s.Shape) || own;
        var main = role.Reg == "Arrow" || (own && (role.Reg is "Hand" or "UpArrow"));
        var list = new List<CurImg>();
        foreach (var px in sizes)
        {
            CurImg? img = null;
            if (main && custom) img = own ? MineStore.Render(s.Shape[5..], px) : Shapes.Render(s.Shape, px, fill, line);
            if (img == null)
            {
                if (!main && !s.All) return null;
                var sys = SysCursors.Load(role.Ocr, px);
                if (sys == null) continue;
                img = SysCursors.Tint(sys, fill, line, role.Dark);
            }
            list.Add(img);
        }
        return list.Count == 0 ? null : list;
    }

    // Применить. Возвращает текст ошибки или null.
    public string? Apply(CursorSettings s)
    {
        try
        {
            Backup();
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var dir = Path.Combine(CursorsDir, stamp);
            Directory.CreateDirectory(dir);
            var written = new Dictionary<string, string>();
            var problems = new List<string>();
            foreach (var role in Roles.All)
            {
                try
                {
                    var imgs = Build(role, s);
                    if (imgs == null) continue;
                    var path = Path.Combine(dir, role.Reg + ".cur");
                    CurFile.Write(path, imgs);
                    written[role.Reg] = path;
                }
                catch (Exception ex) { problems.Add(role.Reg + ": " + ex.Message); }
            }
            using (var k = Registry.CurrentUser.CreateSubKey(Key))
            {
                foreach (var (name, path) in written) k.SetValue(name, path, RegistryValueKind.ExpandString);
                k.SetValue("CursorBaseSize", BaseSize(s), RegistryValueKind.DWord);
            }
            using (var a = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Accessibility"))
                a.SetValue("CursorSize", (BaseSize(s) - 32) / 16 + 1, RegistryValueKind.DWord);
            Native.ReloadCursors();
            CleanOld(dir);
            return problems.Count == 0 ? null : string.Join("; ", problems.Take(3));
        }
        catch (Exception ex) { return ex.Message; }
    }

    // Вернуть прежние указатели.
    public string? Revert()
    {
        try
        {
            if (File.Exists(OriginalsFile))
            {
                var o = JsonSerializer.Deserialize<Originals>(File.ReadAllText(OriginalsFile)) ?? new Originals();
                using (var k = Registry.CurrentUser.CreateSubKey(Key))
                {
                    foreach (var name in Names())
                    {
                        if (!o.Values.TryGetValue(name, out var v) || v == null) { k.DeleteValue(name, false); continue; }
                        o.Kinds.TryGetValue(name, out var kind);
                        if (kind == "DWord" && int.TryParse(v, out var n)) k.SetValue(name, n, RegistryValueKind.DWord);
                        else k.SetValue(name, v, kind == "ExpandString" ? RegistryValueKind.ExpandString : RegistryValueKind.String);
                    }
                }
                try { using var a = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Accessibility"); var b = o.Values.TryGetValue("CursorBaseSize", out var bs) && int.TryParse(bs, out var bn) ? bn : 32; a.SetValue("CursorSize", Math.Clamp((b - 32) / 16 + 1, 1, 15), RegistryValueKind.DWord); } catch { }
                Native.ReloadCursors();
                File.Delete(OriginalsFile);
            }
            try { if (Directory.Exists(CursorsDir)) Directory.Delete(CursorsDir, true); } catch { }
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    static void CleanOld(string keep)
    {
        try
        {
            foreach (var d in Directory.GetDirectories(CursorsDir))
                if (!string.Equals(d, keep, StringComparison.OrdinalIgnoreCase)) { try { Directory.Delete(d, true); } catch { } }
        }
        catch { }
    }
}
