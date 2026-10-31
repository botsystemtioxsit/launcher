using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace ColorPlusApp;

// Цвет выделения (то, чем подсвечивается выделенный мышкой текст и строки) и акцентный цвет Windows.
// Всё, что поменяли, запоминается в color-plus-originals.json и возвращается при выключении, выходе и при следующем запуске
// плагина (если прошлый раз он закрылся аварийно).
//  • «Цвет выделения» — системные цвета COLOR_HIGHLIGHT и COLOR_HIGHLIGHTTEXT (SetSysColors). Их используют обычные программы
//    Windows; современные (браузеры, приложения из магазина) часто берут свои цвета или акцентный. Действует до выхода из системы.
//  • «Акцентный цвет» — тот же, что выбирается в Параметры → Персонализация → Цвета: им подсвечиваются выделенные строки в
//    Проводнике, Пуск, кнопки в современных приложениях. Часть окон подхватывает его только после перезапуска программы.
sealed class SystemColors
{
    const int COLOR_HIGHLIGHT = 13, COLOR_HIGHLIGHTTEXT = 14;
    const string DwmKey = @"SOFTWARE\Microsoft\Windows\DWM";
    const string AccentKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent";

    static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BattleLauncher", "color-plus-originals.json");

    class Originals
    {
        public bool SelectionChanged { get; set; }
        public int Highlight { get; set; }
        public int HighlightText { get; set; }
        public bool AccentChanged { get; set; }
        // null — значения не было в реестре
        public int? DwmAccentColor { get; set; }
        public int? DwmColorization { get; set; }
        public int? DwmAfterglow { get; set; }
        public int? MenuAccent { get; set; }
        public int? StartAccent { get; set; }
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

    // Применить настройки: включённое — выставить, выключенное — вернуть. Возвращает текст ошибки или null.
    public string? Apply(ColorSettings s)
    {
        string? err = null;
        try { ApplySelection(s); } catch (Exception ex) { err = "выделение: " + ex.Message; }
        try { ApplyAccent(s); } catch (Exception ex) { err = (err == null ? "" : err + "; ") + "акцент: " + ex.Message; }
        return err;
    }

    public void RestoreAll()
    {
        try { RestoreSelection(); } catch { }
        try { RestoreAccent(); } catch { }
    }

    // ---------- выделение ----------
    static int ToColorRef(string hex) => (int)Native.ColorRef(hex);

    void ApplySelection(ColorSettings s)
    {
        if (!s.SelectionOn) { RestoreSelection(); return; }
        if (!orig.SelectionChanged)
        {
            orig.Highlight = Native.GetSysColor(COLOR_HIGHLIGHT);
            orig.HighlightText = Native.GetSysColor(COLOR_HIGHLIGHTTEXT);
            orig.SelectionChanged = true;
            Save(); // сначала запоминаем, потом меняем
        }
        // «Авто» — цвет букв остаётся прежним (оригинал Windows): подсветка меняется, а текст на ней всегда читается
        var text = s.SelectionTextAuto ? orig.HighlightText : ToColorRef(s.SelectionTextColor);
        Native.SetSysColors(2, new[] { COLOR_HIGHLIGHT, COLOR_HIGHLIGHTTEXT }, new[] { ToColorRef(s.SelectionColor), text });
    }

    void RestoreSelection()
    {
        if (!orig.SelectionChanged) return;
        Native.SetSysColors(2, new[] { COLOR_HIGHLIGHT, COLOR_HIGHLIGHTTEXT }, new[] { orig.Highlight, orig.HighlightText });
        orig.SelectionChanged = false;
        Save();
    }

    // ---------- акцентный цвет ----------
    static int? ReadDword(string path, string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(path);
        return k?.GetValue(name) is int v ? v : null;
    }

    static void WriteDword(string path, string name, int? value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(path);
        if (value is int v) k.SetValue(name, v, RegistryValueKind.DWord);
        else k.DeleteValue(name, false);
    }

    static int Abgr(string hex) => unchecked((int)(0xFF000000u | Native.ColorRef(hex)));   // AccentColor, AccentColorMenu, StartColorMenu
    static int Argb(string hex)
    {
        var rgb = Convert.ToUInt32(hex.TrimStart('#'), 16);
        return unchecked((int)(0xFF000000u | rgb));                                         // ColorizationColor
    }

    void ApplyAccent(ColorSettings s)
    {
        if (!s.AccentOn) { RestoreAccent(); return; }
        if (!orig.AccentChanged)
        {
            orig.DwmAccentColor = ReadDword(DwmKey, "AccentColor");
            orig.DwmColorization = ReadDword(DwmKey, "ColorizationColor");
            orig.DwmAfterglow = ReadDword(DwmKey, "ColorizationAfterglow");
            orig.MenuAccent = ReadDword(AccentKey, "AccentColorMenu");
            orig.StartAccent = ReadDword(AccentKey, "StartColorMenu");
            orig.AccentChanged = true;
            Save();
        }
        WriteDword(DwmKey, "AccentColor", Abgr(s.AccentColor));
        WriteDword(DwmKey, "ColorizationColor", Argb(s.AccentColor));
        WriteDword(DwmKey, "ColorizationAfterglow", Argb(s.AccentColor));
        // Цвет меню «Пуск» и панели задач (при «Показывать акцентный цвет на панели») НЕ меняем и возвращаем прежний, даже если прошлая версия его сменила:
        // панель красится только своими настройками, а не через акцент Windows — иначе она розовела после каждого перезапуска Проводника.
        WriteDword(AccentKey, "AccentColorMenu", orig.MenuAccent);
        WriteDword(AccentKey, "StartColorMenu", orig.StartAccent);
        Broadcast(Argb(s.AccentColor));
    }

    void RestoreAccent()
    {
        if (!orig.AccentChanged) return;
        WriteDword(DwmKey, "AccentColor", orig.DwmAccentColor);
        WriteDword(DwmKey, "ColorizationColor", orig.DwmColorization);
        WriteDword(DwmKey, "ColorizationAfterglow", orig.DwmAfterglow);
        WriteDword(AccentKey, "AccentColorMenu", orig.MenuAccent);
        WriteDword(AccentKey, "StartColorMenu", orig.StartAccent);
        orig.AccentChanged = false;
        Save();
        Broadcast(orig.DwmColorization ?? 0);
    }

    // Сообщаем Windows и программам, что цвета поменялись (в фоне: окна, которые долго отвечают, не должны вешать плагин).
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
