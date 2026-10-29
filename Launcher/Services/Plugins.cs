using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace BattleLauncher;

// Плагины лаунчера. Сейчас один вид — темы оформления: файл только с цветами, никакого кода. Список плагинов
// живёт в catalog.json (раздел "plugins"), сами файлы — в папке plugins/ репозитория launcher. Установленное
// хранится в %APPDATA%\BattleLauncher\plugins.json.
class InstalledPlugin
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public Dictionary<string, string> Colors { get; set; } = new();
}

class PluginStore
{
    static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BattleLauncher", "plugins.json");

    public List<InstalledPlugin> Installed { get; set; } = new();
    public string? ActiveTheme { get; set; }

    [JsonIgnore] public InstalledPlugin? Active => Installed.FirstOrDefault(p => p.Id == ActiveTheme);
    public InstalledPlugin? Find(string id) => Installed.FirstOrDefault(p => p.Id == id);

    public static PluginStore Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<PluginStore>(File.ReadAllText(FilePath)) ?? new PluginStore();
            // файл могли править руками — оставляем только то, что проходит ту же проверку, что и при установке
            s.Installed = s.Installed.Where(p => Net.SafeId.IsMatch(p.Id) && Plugins.ValidColors(p.Colors)).Take(50).ToList();
            if (s.ActiveTheme != null && s.Active == null) s.ActiveTheme = null;
            return s;
        }
        catch { return new PluginStore(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this));
            File.Move(tmp, FilePath, true); // запись целиком или никак
        }
        catch { /* не вышло сохранить — тема просто не запомнится */ }
    }
}

static class Plugins
{
    public const string ThemeKind = "theme";

    // имя цвета в файле темы → ключ кисти в App.xaml
    static readonly Dictionary<string, string> ResourceKey = new()
    {
        ["bg"] = "Bg", ["panel"] = "Panel", ["panel2"] = "Panel2", ["line"] = "Line", ["text"] = "Text", ["muted"] = "Muted",
        ["accent"] = "Accent", ["onAccent"] = "OnAccent", ["accent2"] = "Accent2", ["red"] = "Red", ["green"] = "Green",
    };

    // Тема по умолчанию (она же в App.xaml): возвращаем её, когда плагин выключен.
    static readonly Dictionary<string, string> Default = new()
    {
        ["bg"] = "#06050D", ["panel"] = "#0D0B1A", ["panel2"] = "#15112B", ["line"] = "#2D2060", ["text"] = "#E6F4FF", ["muted"] = "#8B86B8",
        ["accent"] = "#00F0FF", ["onAccent"] = "#001418", ["accent2"] = "#FF2A9D", ["red"] = "#FF3366", ["green"] = "#2CFF9A",
    };

    static readonly Regex Hex = new(@"^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    public static bool ValidColors(Dictionary<string, string>? colors) =>
        colors != null && ResourceKey.Keys.All(k => colors.TryGetValue(k, out var v) && Hex.IsMatch(v));

    // Разбор файла темы. null — файл не тот: чужой вид плагина, нет цвета или цвет не вида #RRGGBB.
    public static Dictionary<string, string>? ParseTheme(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("kind", out var kind) || kind.GetString() != ThemeKind) return null;
            if (!root.TryGetProperty("colors", out var cols) || cols.ValueKind != JsonValueKind.Object) return null;
            var map = new Dictionary<string, string>();
            foreach (var k in ResourceKey.Keys)
            {
                if (!cols.TryGetProperty(k, out var v) || v.ValueKind != JsonValueKind.String) return null;
                var s = v.GetString() ?? "";
                if (!Hex.IsMatch(s)) return null;
                map[k] = s;
            }
            return map;
        }
        catch { return null; }
    }

    // Скачивает и проверяет плагин. Ссылка собирается из проверенных частей и ведёт в наш репозиторий.
    public static async Task<InstalledPlugin> Install(PluginDef def, CancellationToken ct = default)
    {
        var text = await Net.GetText(def.Url, ct);
        var colors = ParseTheme(text) ?? throw new InvalidDataException("Файл плагина повреждён или не подходит");
        return new InstalledPlugin { Id = def.Id, Name = def.Name, Version = def.Version, Colors = colors };
    }

    // Применяет тему к окну на лету (в XAML кисти подключены через DynamicResource). null — тема по умолчанию.
    public static void Apply(Dictionary<string, string>? colors)
    {
        var src = ValidColors(colors) ? colors! : Default;
        var res = Application.Current.Resources;
        foreach (var (name, key) in ResourceKey)
            res[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(src[name]));
    }

    public static void ApplyActive(PluginStore store) => Apply(store.Active?.Colors);
}
