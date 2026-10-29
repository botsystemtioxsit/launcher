using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace IconPlusApp;

// Всё, что плагин поменял в настройках пользователя, запоминается здесь: по этому списку «Вернуть как было» возвращает всё в точности.
sealed class RegChange
{
    public string Path { get; set; } = "";          // относительно HKEY_CURRENT_USER
    public string Name { get; set; } = "";          // "" — значение по умолчанию
    public bool Existed { get; set; }               // значение было до нас
    public string? Old { get; set; }
    public string? CreatedFrom { get; set; }        // с какого уровня ветка реестра создана нами (null — ветка уже была)
}

sealed class LnkChange
{
    public string File { get; set; } = "";
    public string OldPath { get; set; } = "";
    public int OldIndex { get; set; }
}

sealed class IconState
{
    public List<RegChange> Reg { get; set; } = new();
    public List<LnkChange> Lnk { get; set; } = new();
    public Dictionary<string, string> Sources { get; set; } = new();   // откуда брали исходный значок: ключ → «путь,номер»
    public string Applied { get; set; } = "";                           // что сейчас применено: пусто | color | pack:<имя>
}

static class Paths
{
    public static readonly string Root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BattleLauncher", "icon-plus");
    public static string Packs => System.IO.Path.Combine(Root, "packs");
    public static string Generated => System.IO.Path.Combine(Root, "generated");
    public static string StateFile => System.IO.Path.Combine(Root, "state.json");
    public static string SettingsFile => System.IO.Path.Combine(Root, "settings.json");
    public static string Downloads => System.IO.Path.Combine(Root, "downloads");
    public static string WebData => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GhostShell", "IconPlus", "webview");

    public static void Ensure() { foreach (var d in new[] { Root, Packs, Generated, Downloads }) Directory.CreateDirectory(d); }
}

static class StateStore
{
    public static IconState Load()
    {
        try { return JsonSerializer.Deserialize<IconState>(File.ReadAllText(Paths.StateFile)) ?? new IconState(); }
        catch { return new IconState(); }
    }

    public static void Save(IconState s)
    {
        try
        {
            Paths.Ensure();
            var tmp = Paths.StateFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(s));
            File.Move(tmp, Paths.StateFile, true);
        }
        catch { /* не вышло сохранить — «вернуть как было» будет знать меньше */ }
    }
}

// Запись значка в реестр текущего пользователя с запоминанием прежнего значения.
sealed class RegistryWriter
{
    readonly IconState state;
    public RegistryWriter(IconState state) { this.state = state; }

    public void Set(string path, string name, string value)
    {
        var change = state.Reg.FirstOrDefault(c => c.Path == path && c.Name == name);
        if (change == null)
        {
            change = new RegChange { Path = path, Name = name, CreatedFrom = FirstMissingPrefix(path) };
            using (var key = Registry.CurrentUser.OpenSubKey(path))
            {
                var old = key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (old is string s) { change.Existed = true; change.Old = s; }
                else if (old != null) { change.Existed = true; change.Old = old.ToString(); }
            }
            state.Reg.Add(change);
        }
        using var k = Registry.CurrentUser.CreateSubKey(path, true);
        k.SetValue(name, value, RegistryValueKind.String);
    }

    // Самая верхняя часть пути, которой ещё нет в реестре, — её мы создадим и потом уберём.
    static string? FirstMissingPrefix(string path)
    {
        var parts = path.Split('\\');
        var cur = "";
        for (var i = 0; i < parts.Length; i++)
        {
            cur = i == 0 ? parts[0] : cur + "\\" + parts[i];
            using var k = Registry.CurrentUser.OpenSubKey(cur);
            if (k == null) return cur;
        }
        return null;
    }

    public static void Revert(RegChange c)
    {
        try
        {
            if (c.Existed)
            {
                using var k = Registry.CurrentUser.CreateSubKey(c.Path, true);
                k.SetValue(c.Name, c.Old ?? "", RegistryValueKind.String);
                return;
            }
            using (var k = Registry.CurrentUser.OpenSubKey(c.Path, true)) k?.DeleteValue(c.Name, false);
            // убрать пустые ветки, созданные нами (снизу вверх до места, где мы начали)
            if (c.CreatedFrom == null) return;
            var cur = c.Path;
            while (cur.Length >= c.CreatedFrom.Length)
            {
                using (var k = Registry.CurrentUser.OpenSubKey(cur))
                {
                    if (k == null) { }
                    else if (k.ValueCount == 0 && k.SubKeyCount == 0) { }
                    else break;
                }
                var parent = cur.Contains('\\') ? cur[..cur.LastIndexOf('\\')] : "";
                var leaf = cur.Contains('\\') ? cur[(cur.LastIndexOf('\\') + 1)..] : cur;
                using (var pk = parent.Length > 0 ? Registry.CurrentUser.OpenSubKey(parent, true) : Registry.CurrentUser)
                {
                    try { pk?.DeleteSubKey(leaf, false); } catch { break; }
                }
                if (cur == c.CreatedFrom) break;
                cur = parent;
            }
        }
        catch { /* ветка занята или удалена — идём дальше */ }
    }
}
