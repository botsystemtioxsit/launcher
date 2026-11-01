using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace ColorPlusApp;

// Применяет цвет средствами самой Windows — ничего не работает в фоне и не рисует поверх программ, поэтому игры и любые окна в безопасности:
//  • «Окна и выделение»: акцентный цвет Windows (тот же, что в Параметры → Персонализация → Цвета) + «показывать акцент на заголовках окон и рамках»
//    + цвет выделения мышкой (системные цвета подсветки). Меню «Пуск» и панель задач акцентом НЕ красятся.
//  • «Панель задач»: только пожелание для Taskbar Plus (см. TaskbarLink); окно панели Windows не заливается никогда.
// Всё прежнее запоминается до первого изменения (color-plus-originals.json) и возвращается кнопкой «Вернуть как было».
sealed class Applier
{
    const int COLOR_HIGHLIGHT = 13, COLOR_HIGHLIGHTTEXT = 14;
    const string DwmKey = @"SOFTWARE\Microsoft\Windows\DWM";
    const string AccentKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent";
    const string PersKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    const string ColorsKey = @"Control Panel\Colors";

    static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BattleLauncher", "color-plus-originals.json");

    class Originals
    {
        public bool WindowsChanged { get; set; }
        // null — значения не было в реестре
        public int? DwmAccent { get; set; }
        public int? DwmAccentInactive { get; set; }
        public int? DwmColorization { get; set; }
        public int? DwmAfterglow { get; set; }
        public int? DwmPrevalence { get; set; }
        public int? PersPrevalence { get; set; }
        public byte[]? Palette { get; set; }
        public string? RegHilight { get; set; }
        public string? RegHilightText { get; set; }
        public int SysHighlight { get; set; }
        public int SysHighlightText { get; set; }
    }

    Originals orig = Load();

    static Originals Load()
    {
        try { return JsonSerializer.Deserialize<Originals>(File.ReadAllText(FilePath)) ?? new Originals(); } catch { return new Originals(); }
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(orig));
        }
        catch { }
    }

    // Какой акцентный цвет сейчас в системе (для предпросмотра «как сейчас»)
    public static string CurrentAccent()
    {
        try
        {
            var v = ReadDword(DwmKey, "AccentColor");
            if (v is int a) return Col.Hex(a & 255, (a >> 8) & 255, (a >> 16) & 255);   // ABGR
        }
        catch { }
        return "#0078D4";
    }

    // Применить выбор: включённое — выставить, выключенное — вернуть. Возвращает текст ошибки или null.
    public string? Apply(ColorSettings s)
    {
        string? err = null;
        try { if (s.Windows) ApplyWindows(s.Color); else RestoreWindows(); }
        catch (Exception ex) { err = "окна: " + ex.Message; }
        try { TaskbarLink.Write(s.Taskbar, s.Color); }
        catch (Exception ex) { err = (err == null ? "" : err + "; ") + "панель: " + ex.Message; }
        return err;
    }

    public void RevertAll()
    {
        try { RestoreWindows(); } catch { }
        try { TaskbarLink.Write(false, "#101018"); } catch { }
    }

    // ---------- окна и выделение ----------
    static int Abgr(string hex) { var (r, g, b) = Col.Parse(hex); return unchecked((int)(0xFF000000u | (uint)(b << 16) | (uint)(g << 8) | (uint)r)); }
    static int Argb(string hex) { var (r, g, b) = Col.Parse(hex); return unchecked((int)(0xFF000000u | (uint)(r << 16) | (uint)(g << 8) | (uint)b)); }
    static int ColorRef(string hex) { var (r, g, b) = Col.Parse(hex); return (b << 16) | (g << 8) | r; }

    void Backup()
    {
        if (orig.WindowsChanged) return;
        orig.DwmAccent = ReadDword(DwmKey, "AccentColor");
        orig.DwmAccentInactive = ReadDword(DwmKey, "AccentColorInactive");
        orig.DwmColorization = ReadDword(DwmKey, "ColorizationColor");
        orig.DwmAfterglow = ReadDword(DwmKey, "ColorizationAfterglow");
        orig.DwmPrevalence = ReadDword(DwmKey, "ColorPrevalence");
        orig.PersPrevalence = ReadDword(PersKey, "ColorPrevalence");
        orig.Palette = ReadBinary(AccentKey, "AccentPalette");
        orig.RegHilight = ReadString(ColorsKey, "Hilight");
        orig.RegHilightText = ReadString(ColorsKey, "HilightText");
        orig.SysHighlight = Native.GetSysColor(COLOR_HIGHLIGHT);
        orig.SysHighlightText = Native.GetSysColor(COLOR_HIGHLIGHTTEXT);
        orig.WindowsChanged = true;
        Save();   // сначала запоминаем, потом меняем
    }

    // Восемь оттенков акцента: три светлее, сам цвет, три темнее, последний остаётся как в системе (так делает и окно «Цвета» в Параметрах)
    static byte[] BuildPalette(string hex, byte[]? original)
    {
        var (h, s, l) = Col.ToHsl(hex);
        double[] d = { 0.38, 0.23, 0.07, 0, -0.04, -0.13, -0.22 };
        var p = new byte[32];
        for (var i = 0; i < 7; i++)
        {
            var (r, g, b) = i == 3 ? Col.Parse(hex) : Col.FromHsl(h, s, Math.Clamp(l + d[i], 0.04, 0.96));
            p[i * 4] = (byte)r; p[i * 4 + 1] = (byte)g; p[i * 4 + 2] = (byte)b; p[i * 4 + 3] = 0xFF;
        }
        if (original != null && original.Length == 32) Array.Copy(original, 28, p, 28, 4);
        else { p[28] = 0xF7; p[29] = 0x63; p[30] = 0x0C; p[31] = 0xFF; }
        return p;
    }

    void ApplyWindows(string hex)
    {
        Backup();
        var inactive = Col.Mix(hex, "#202028", 0.55);                      // неактивные окна — потемнее
        WriteDword(DwmKey, "AccentColor", Abgr(hex));
        WriteDword(DwmKey, "AccentColorInactive", Abgr(inactive));
        WriteDword(DwmKey, "ColorizationColor", Argb(hex));
        WriteDword(DwmKey, "ColorizationAfterglow", Argb(hex));
        WriteDword(DwmKey, "ColorPrevalence", 1);                           // акцент на заголовках окон и рамках
        WriteDword(PersKey, "ColorPrevalence", 0);                          // «Пуск» и панель задач акцентом не красятся
        WriteBinary(AccentKey, "AccentPalette", BuildPalette(hex, orig.Palette));
        // выделение: подсветка нашим цветом, буквы на ней — чёрные или белые (что читается)
        var text = Col.Readable(hex);
        var (r, g, b) = Col.Parse(hex); var (tr, tg, tb) = Col.Parse(text);
        WriteString(ColorsKey, "Hilight", $"{r} {g} {b}");
        WriteString(ColorsKey, "HilightText", $"{tr} {tg} {tb}");
        Native.SetSysColors(2, new[] { COLOR_HIGHLIGHT, COLOR_HIGHLIGHTTEXT }, new[] { ColorRef(hex), ColorRef(text) });
        Broadcast(Argb(hex));
    }

    void RestoreWindows()
    {
        if (!orig.WindowsChanged) return;
        WriteDword(DwmKey, "AccentColor", orig.DwmAccent);
        WriteDword(DwmKey, "AccentColorInactive", orig.DwmAccentInactive);
        WriteDword(DwmKey, "ColorizationColor", orig.DwmColorization);
        WriteDword(DwmKey, "ColorizationAfterglow", orig.DwmAfterglow);
        WriteDword(DwmKey, "ColorPrevalence", orig.DwmPrevalence);
        WriteDword(PersKey, "ColorPrevalence", orig.PersPrevalence);
        WriteBinary(AccentKey, "AccentPalette", orig.Palette);
        WriteString(ColorsKey, "Hilight", orig.RegHilight);
        WriteString(ColorsKey, "HilightText", orig.RegHilightText);
        Native.SetSysColors(2, new[] { COLOR_HIGHLIGHT, COLOR_HIGHLIGHTTEXT }, new[] { orig.SysHighlight, orig.SysHighlightText });
        orig.WindowsChanged = false;
        Save();
        Broadcast(orig.DwmColorization ?? 0);
    }

    // ---------- реестр ----------
    static int? ReadDword(string path, string name) { using var k = Registry.CurrentUser.OpenSubKey(path); return k?.GetValue(name) is int v ? v : null; }
    static byte[]? ReadBinary(string path, string name) { using var k = Registry.CurrentUser.OpenSubKey(path); return k?.GetValue(name) as byte[]; }
    static string? ReadString(string path, string name) { using var k = Registry.CurrentUser.OpenSubKey(path); return k?.GetValue(name) as string; }

    static void WriteDword(string path, string name, int? value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(path);
        if (value is int v) k.SetValue(name, v, RegistryValueKind.DWord); else k.DeleteValue(name, false);
    }
    static void WriteBinary(string path, string name, byte[]? value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(path);
        if (value != null) k.SetValue(name, value, RegistryValueKind.Binary); else k.DeleteValue(name, false);
    }
    static void WriteString(string path, string name, string? value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(path);
        if (value != null) k.SetValue(name, value, RegistryValueKind.String); else k.DeleteValue(name, false);
    }

    // Сообщаем Windows и программам, что цвета поменялись (в фоне: окно, которое долго отвечает, не должно вешать плагин)
    static void Broadcast(int colorization)
    {
        Task.Run(() =>
        {
            try
            {
                Native.SendMessageTimeout((IntPtr)0xFFFF, 0x1A /* WM_SETTINGCHANGE */, UIntPtr.Zero, "ImmersiveColorSet", 2, 1500, out _);
                Native.PostMessage((IntPtr)0xFFFF, 0x0320 /* WM_DWMCOLORIZATIONCOLORCHANGED */, (IntPtr)colorization, IntPtr.Zero);
            }
            catch { /* уведомление не обязательно */ }
        });
    }
}
