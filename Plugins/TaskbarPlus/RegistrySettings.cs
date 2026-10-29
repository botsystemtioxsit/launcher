using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace TaskbarPlusApp;

// Параметры панели задач из реестра текущего пользователя — те же, что Windows хранит в
// «Параметры → Персонализация → Панель задач». Права администратора не нужны.
// Перед первым изменением параметра запоминается исходное значение, «Вернуть как было» возвращает всё.
record RegOption(string Id, string Title, string Hint, string Key, string Name, int[] Values, string[] Labels, int Default,
    bool Win11Only = false, bool Win10Only = false, bool NeedsRestart = false);

// Файл тот же, что у встроенной версии 1.12.0 лаунчера: тот, кто уже менял настройки, сможет их откатить.
class RegSaved
{
    public Dictionary<string, int?> Original { get; set; } = new();
    public bool? OriginalAutoHide { get; set; }
}

static class RegistrySettings
{
    const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    const string Search = @"Software\Microsoft\Windows\CurrentVersion\Search";

    public static readonly RegOption[] Options =
    {
        new("align", "Значки", "Где стоят значки: слева, как в Windows 10, или по центру. Справа Windows не умеет — это пока не сделано.",
            Advanced, "TaskbarAl", new[] { 0, 1 }, new[] { "Слева", "По центру" }, 1, Win11Only: true),
        new("search", "Поиск", "Что показывать вместо строки поиска",
            Search, "SearchboxTaskbarMode", new[] { 0, 1, 2 }, new[] { "Скрыть", "Значок", "Строка" }, 2),
        new("taskview", "Представление задач", "Кнопка обзора всех открытых окон",
            Advanced, "ShowTaskViewButton", new[] { 1, 0 }, new[] { "Показать", "Скрыть" }, 1),
        new("widgets", "Виджеты", "Кнопка с погодой и новостями слева на панели. Новые сборки Windows 11 защищают её от программ — тогда её можно скрыть только вручную.",
            Advanced, "TaskbarDa", new[] { 1, 0 }, new[] { "Показать", "Скрыть" }, 1, Win11Only: true),
        new("chat", "Чат", "Кнопка «Чат» (Teams); в новых сборках Windows 11 её уже нет",
            Advanced, "TaskbarMn", new[] { 1, 0 }, new[] { "Показать", "Скрыть" }, 1, Win11Only: true),
        new("size", "Размер панели", "Высота панели и значков. Появляется после перезапуска панели; в самых новых сборках Windows 11 может не работать.",
            Advanced, "TaskbarSi", new[] { 0, 1, 2 }, new[] { "Малая", "Средняя", "Большая" }, 1, Win11Only: true, NeedsRestart: true),
        new("small", "Размер значков", "Мелкие значки на панели",
            Advanced, "TaskbarSmallIcons", new[] { 0, 1 }, new[] { "Обычные", "Мелкие" }, 0, Win10Only: true, NeedsRestart: true),
        new("combine", "Группировка кнопок", "Когда объединять окна одной программы в одну кнопку",
            Advanced, "TaskbarGlomLevel", new[] { 0, 1, 2 }, new[] { "Всегда", "При нехватке места", "Никогда" }, 0, NeedsRestart: true),
        new("seconds", "Секунды в часах", "Показывать секунды в часах на панели",
            Advanced, "ShowSecondsInSystemClock", new[] { 0, 1 }, new[] { "Нет", "Да" }, 0, NeedsRestart: true),
        new("animations", "Анимации панели", "Движение панели, когда она появляется и прячется. Нужны ещё анимации самой Windows (ниже).",
            Advanced, "TaskbarAnimations", new[] { 1, 0 }, new[] { "Включены", "Выключены" }, 1),
    };

    // Windows 11 — сборка 22000 и выше
    public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

    public static bool Available(RegOption o) => !(o.Win11Only && !IsWindows11) && !(o.Win10Only && IsWindows11);

    static readonly string SavedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BattleLauncher", "taskbar-plus.json");

    static RegSaved LoadSaved()
    {
        try { return JsonSerializer.Deserialize<RegSaved>(File.ReadAllText(SavedPath)) ?? new RegSaved(); }
        catch { return new RegSaved(); }
    }

    static void SaveSaved(RegSaved s)
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

    static int? ReadValue(RegOption o)
    {
        using var k = Registry.CurrentUser.OpenSubKey(o.Key);
        return k?.GetValue(o.Name) is int v ? v : null;
    }

    // Текущее значение параметра; если в реестре его нет — значение по умолчанию Windows.
    public static int Current(RegOption o) => ReadValue(o) ?? o.Default;

    static void WriteValue(RegOption o, int? value)
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
    public static string? Set(RegOption o, int value)
    {
        if (!o.Values.Contains(value)) return "Недопустимое значение";
        try
        {
            var saved = LoadSaved();
            if (!saved.Original.ContainsKey(o.Id)) { saved.Original[o.Id] = ReadValue(o); SaveSaved(saved); }
            WriteValue(o, value);
            // Windows 11 тихо защищает часть параметров: запись «проходит», но значение не меняется — проверяем
            if (ReadValue(o) != value) return "Windows защищает этот параметр и не даёт его менять программам";
            Native.NotifySettingsChanged();
            return null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return "Windows не даёт менять этот параметр на вашей версии";
        }
        catch (Exception ex) { return ex.Message; }
    }

    public static string? SetAutoHide(bool on)
    {
        try
        {
            var saved = LoadSaved();
            if (saved.OriginalAutoHide == null) { saved.OriginalAutoHide = Native.GetAutoHide(); SaveSaved(saved); }
            Native.SetAutoHide(on);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    // Перезапуск Проводника: закрывает его окна и панель задач на пару секунд; нужен для параметров,
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

    // Возвращает всё, что меняли здесь, к исходному виду. Возвращает число восстановленных параметров.
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
        try { if (saved.OriginalAutoHide != null) { Native.SetAutoHide(saved.OriginalAutoHide.Value); restored++; } }
        catch (Exception ex) { error = ex.Message; }
        if (error == null) { try { File.Delete(SavedPath); } catch { } }
        Native.NotifySettingsChanged();
        return (restored, error);
    }

    // ---------- запуск вместе с Windows ----------
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "TaskbarPlus";

    public static bool Autostart
    {
        get { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue(RunName) is string; }
    }

    public static void SetAutostart(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue(RunName, $"\"{Environment.ProcessPath}\" --background");
        else k.DeleteValue(RunName, false);
    }
}
