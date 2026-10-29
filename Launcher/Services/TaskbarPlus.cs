using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace BattleLauncher;

// Taskbar Plus — первый плагин лаунчера: настройки панели задач Windows (выравнивание, поиск, размер, группировка
// кнопок, секунды в часах, автоскрытие и др.). Меняются только обычные параметры текущего пользователя (HKCU) —
// те же, что Windows хранит в «Параметры → Персонализация → Панель задач»; права администратора не нужны.
// Перед первым изменением каждого параметра запоминается исходное значение, «Вернуть как было» возвращает всё.
record TaskbarOption(string Id, string Title, string Hint, string Key, string Name, int[] Values, string[] Labels, int Default, bool Win11Only = false, bool Win10Only = false);

class TaskbarSaved
{
    // исходные значения параметров до первого изменения (null — параметра не было в реестре)
    public Dictionary<string, int?> Original { get; set; } = new();
    public bool? OriginalAutoHide { get; set; }
}

static class TaskbarPlus
{
    const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    const string Search = @"Software\Microsoft\Windows\CurrentVersion\Search";

    public static readonly TaskbarOption[] Options =
    {
        new("align", "Значки", "Где стоят значки: слева, как в Windows 10, или по центру", Advanced, "TaskbarAl", new[] { 0, 1 }, new[] { "Слева", "По центру" }, 1, Win11Only: true),
        new("search", "Поиск", "Что показывать вместо строки поиска", Search, "SearchboxTaskbarMode", new[] { 0, 1, 2 }, new[] { "Скрыть", "Значок", "Строка" }, 2),
        new("taskview", "Представление задач", "Кнопка обзора окон", Advanced, "ShowTaskViewButton", new[] { 1, 0 }, new[] { "Показать", "Скрыть" }, 1),
        new("widgets", "Виджеты", "Погода и новости слева", Advanced, "TaskbarDa", new[] { 1, 0 }, new[] { "Показать", "Скрыть" }, 1, Win11Only: true),
        new("chat", "Чат", "Кнопка «Чат» (Teams)", Advanced, "TaskbarMn", new[] { 1, 0 }, new[] { "Показать", "Скрыть" }, 1, Win11Only: true),
        new("size", "Размер панели", "Высота панели и значков", Advanced, "TaskbarSi", new[] { 0, 1, 2 }, new[] { "Малая", "Средняя", "Большая" }, 1, Win11Only: true),
        new("small", "Размер значков", "Мелкие значки на панели", Advanced, "TaskbarSmallIcons", new[] { 0, 1 }, new[] { "Обычные", "Мелкие" }, 0, Win10Only: true),
        new("combine", "Группировка кнопок", "Когда объединять окна одной программы", Advanced, "TaskbarGlomLevel", new[] { 0, 1, 2 }, new[] { "Всегда", "При нехватке места", "Никогда" }, 0),
        new("seconds", "Секунды в часах", "Показывать секунды в часах на панели", Advanced, "ShowSecondsInSystemClock", new[] { 0, 1 }, new[] { "Нет", "Да" }, 0),
    };

    // Windows 11 — сборка 22000 и выше
    public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

    public static bool Available(TaskbarOption o) => !(o.Win11Only && !IsWindows11) && !(o.Win10Only && IsWindows11);

    static readonly string SavedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BattleLauncher", "taskbar-plus.json");

    static TaskbarSaved LoadSaved()
    {
        try { return JsonSerializer.Deserialize<TaskbarSaved>(File.ReadAllText(SavedPath)) ?? new TaskbarSaved(); }
        catch { return new TaskbarSaved(); }
    }

    static void SaveSaved(TaskbarSaved s)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SavedPath)!);
            var tmp = SavedPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(s));
            File.Move(tmp, SavedPath, true); // запись целиком или никак
        }
        catch { /* не вышло запомнить исходное — откат этого параметра будет недоступен */ }
    }

    public static bool HasChanges => LoadSaved() is { } s && (s.Original.Count > 0 || s.OriginalAutoHide != null);

    // ---------- реестр ----------
    static int? ReadValue(TaskbarOption o)
    {
        using var k = Registry.CurrentUser.OpenSubKey(o.Key);
        return k?.GetValue(o.Name) is int v ? v : null;
    }

    // Текущее значение параметра; если в реестре его нет — значение по умолчанию Windows.
    public static int Current(TaskbarOption o) => ReadValue(o) ?? o.Default;

    static void WriteValue(TaskbarOption o, int? value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(o.Key);
        if (value == null) k.DeleteValue(o.Name, false);
        else k.SetValue(o.Name, value.Value, RegistryValueKind.DWord);
        if (o.Name == "TaskbarGlomLevel") // вторые мониторы — тот же выбор
        {
            if (value == null) k.DeleteValue("MMTaskbarGlomLevel", false);
            else k.SetValue("MMTaskbarGlomLevel", value.Value, RegistryValueKind.DWord);
        }
    }

    // Меняет параметр. Возвращает текст ошибки или null, если всё хорошо.
    public static string? Set(TaskbarOption o, int value)
    {
        if (!o.Values.Contains(value)) return "Недопустимое значение";
        try
        {
            var saved = LoadSaved();
            if (!saved.Original.ContainsKey(o.Id)) { saved.Original[o.Id] = ReadValue(o); SaveSaved(saved); }
            WriteValue(o, value);
            Notify();
            return null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return "Windows не даёт менять этот параметр на вашей версии";
        }
        catch (Exception ex) { return ex.Message; }
    }

    // ---------- автоскрытие панели ----------
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    struct APPBARDATA { public uint cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam; }

    [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

    const uint ABM_GETSTATE = 4, ABM_SETSTATE = 10, ABS_AUTOHIDE = 1, ABS_ALWAYSONTOP = 2;

    static APPBARDATA NewAppBar() => new() { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), hWnd = FindWindow("Shell_TrayWnd", null) };

    public static bool AutoHide
    {
        get
        {
            try { var d = NewAppBar(); return ((uint)SHAppBarMessage(ABM_GETSTATE, ref d) & ABS_AUTOHIDE) != 0; }
            catch { return false; }
        }
    }

    static void SetAutoHideRaw(bool on)
    {
        var d = NewAppBar();
        d.lParam = (IntPtr)(on ? ABS_AUTOHIDE | ABS_ALWAYSONTOP : ABS_ALWAYSONTOP);
        SHAppBarMessage(ABM_SETSTATE, ref d);
    }

    public static string? SetAutoHide(bool on)
    {
        try
        {
            var saved = LoadSaved();
            if (saved.OriginalAutoHide == null) { saved.OriginalAutoHide = AutoHide; SaveSaved(saved); }
            SetAutoHideRaw(on);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    // ---------- применение и откат ----------
    // Сообщаем проводнику, что настройки панели изменились (часть параметров подхватывается сразу).
    static void Notify()
    {
        try { SendMessageTimeout((IntPtr)0xFFFF, 0x1A /* WM_SETTINGCHANGE */, UIntPtr.Zero, "TraySettings", 2 /* SMTO_ABORTIFHUNG */, 1500, out _); }
        catch { /* уведомление не обязательно */ }
    }

    // Перезапуск проводника: закрывает его окна и панель задач на пару секунд; нужен для параметров,
    // которые Windows читает только при запуске (размер, группировка, секунды у части сборок).
    public static string? RestartExplorer()
    {
        try
        {
            using (var kill = Process.Start(new ProcessStartInfo("taskkill", "/f /im explorer.exe") { CreateNoWindow = true, UseShellExecute = false }))
                kill?.WaitForExit(5000);
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    // Возвращает всё, что меняли через плагин, к исходному виду. Возвращает число восстановленных параметров.
    public static (int Restored, string? Error) Reset()
    {
        var saved = LoadSaved();
        var restored = 0;
        string? error = null;
        foreach (var o in Options)
        {
            if (!saved.Original.TryGetValue(o.Id, out var orig)) continue;
            try { WriteValue(o, orig); restored++; }
            catch (Exception ex) { error = ex.Message; }
        }
        try { if (saved.OriginalAutoHide != null) { SetAutoHideRaw(saved.OriginalAutoHide.Value); restored++; } }
        catch (Exception ex) { error = ex.Message; }
        if (error == null) { try { File.Delete(SavedPath); } catch { } }
        Notify();
        return (restored, error);
    }
}
