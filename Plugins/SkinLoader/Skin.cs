using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SkinLoaderApp;

// Скин — это НАБОР НАСТРОЕК наших плагинов, а не программа. В файле лежат только значения из белого списка (цвета, числа, имена
// форм из фиксированного списка, идентификатор встроенных обоев): ни путей к файлам, ни команд, ни кода. Всё, что пришло извне
// (импорт файла, потом и галерея), проходит через Skin.Parse: лишнее выбрасывается, числа загоняются в допустимые пределы.
sealed class Skin
{
    public const int Format = 1;
    public const int MaxFileBytes = 64 * 1024;

    public string Id = "";                       // номер в локальной библиотеке (в файл не пишется)
    public string Name = "Мой скин", Author = "", Description = "";
    public long Created;
    public JsonObject Parts = new();             // taskbar, color, icons, wallpaper, cursor — каждая часть уже очищена

    public IEnumerable<string> PartIds => SkinSchema.PartIds.Where(p => Parts[p] is JsonObject);
    public JsonObject? Part(string id) => Parts[id] as JsonObject;

    public string ToJson()
    {
        var root = new JsonObject
        {
            ["format"] = Format, ["name"] = Name, ["author"] = Author, ["description"] = Description, ["created"] = Created,
            ["parts"] = Parts.DeepClone(),
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    // Разбор и очистка. Фатально (исключение) только то, что вообще не скин; остальное чистится, замечания — в warnings.
    public static Skin Parse(string json, List<string>? warnings = null)
    {
        warnings ??= new();
        if (json.Length > MaxFileBytes) throw new InvalidDataException("Файл скина слишком большой");
        JsonObject root;
        try { root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = 8 }) as JsonObject ?? throw new InvalidDataException("Это не скин"); }
        catch (JsonException) { throw new InvalidDataException("Файл повреждён: это не скин"); }
        var fmt = SkinSchema.Int(root, "format");
        if (fmt == null || fmt < 1) throw new InvalidDataException("В файле нет номера формата скина");
        if (fmt > Format) throw new InvalidDataException("Скин сделан в более новой версии Skin Loader — обновите плагин");
        var skin = new Skin
        {
            Name = SkinSchema.Text(SkinSchema.Str(root, "name"), 40, "Скин без названия"),
            Author = SkinSchema.Text(SkinSchema.Str(root, "author"), 40, ""),
            Description = SkinSchema.Text(SkinSchema.Str(root, "description"), 300, ""),
            Created = Math.Max(0, SkinSchema.Long(root, "created") ?? 0),
        };
        if (root["parts"] is not JsonObject parts) throw new InvalidDataException("В скине нет настроек");
        foreach (var kv in parts)
        {
            if (!SkinSchema.PartIds.Contains(kv.Key)) { warnings.Add($"Неизвестная часть «{kv.Key}» пропущена"); continue; }
            var clean = SkinSchema.CleanPart(kv.Key, kv.Value as JsonObject, warnings);
            if (clean != null) skin.Parts[kv.Key] = clean;
        }
        if (!skin.PartIds.Any()) throw new InvalidDataException("В скине нет ни одной допустимой части");
        return skin;
    }
}

// Допустимые значения каждой части. Имена ключей — мои (camelCase), значения проверяются по спискам и пределам из самих плагинов.
static class SkinSchema
{
    public static readonly string[] PartIds = { "taskbar", "color", "icons", "wallpaper", "cursor" };
    public static readonly Dictionary<string, string> PartNames = new()
    {
        ["taskbar"] = "Панель задач", ["color"] = "Цвет окон", ["icons"] = "Значки", ["wallpaper"] = "Обои", ["cursor"] = "Курсор",
    };
    public static readonly Dictionary<string, string> PartPlugins = new()
    {
        ["taskbar"] = "Taskbar Plus", ["color"] = "Color Plus", ["icons"] = "Icon Plus", ["wallpaper"] = "Wallpaper Plus", ["cursor"] = "Cursor Plus",
    };

    static readonly Regex Hex = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);
    // Только встроенные («b-…») и каталожные («c-…») обои: свои файлы человека (u-…) на чужом компьютере не существуют
    static readonly Regex WallpaperId = new("^[bc]-[a-z0-9][a-z0-9-]{0,40}$", RegexOptions.Compiled);

    // ---------- чтение значений из JsonObject (неверный тип = нет значения) ----------
    public static string? Str(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    public static bool? Bool(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;
    public static long? Long(JsonObject o, string k)
    {
        if (o[k] is not JsonValue v) return null;
        // число в очищенной части лежит как int, а из только что разобранного файла — как JsonElement: TryGetValue<long> для int не срабатывает,
        // поэтому пробуем все варианты (иначе очищенный скин читался бы «пустым» и применялся нулями)
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<long>(out var l)) return l;
        if (v.TryGetValue<double>(out var d) && d == Math.Floor(d) && Math.Abs(d) < 1e15) return (long)d;
        return null;
    }
    public static int? Int(JsonObject o, string k) { var l = Long(o, k); return l is >= int.MinValue and <= int.MaxValue ? (int)l.Value : null; }

    // Подпись, имя, описание: без управляющих и «невидимых» символов, в одну строку, не длиннее max
    public static string Text(string? s, int max, string fallback)
    {
        if (s == null) return fallback;
        var clean = new string(s.Where(c => !char.IsControl(c) && !(c >= '​' && c <= '‏') && !(c >= '‪' && c <= '‮') && !(c >= '⁦' && c <= '⁩')).ToArray());
        clean = Regex.Replace(clean, @"\s+", " ").Trim();
        if (clean.Length > max) clean = clean[..max].TrimEnd();
        return clean.Length == 0 ? fallback : clean;
    }

    static string Pick(JsonObject o, string k, string[] allowed, string def) => Str(o, k) is { } s && allowed.Contains(s) ? s : def;
    static int Range(JsonObject o, string k, int min, int max, int def) => Int(o, k) is { } i ? Math.Clamp(i, min, max) : def;
    static bool Flag(JsonObject o, string k, bool def) => Bool(o, k) ?? def;
    static string Color(JsonObject o, string k, string def) => Str(o, k) is { } s && Hex.IsMatch(s) ? s.ToUpperInvariant() : def;

    public static bool IsHex(string? s) => s != null && Hex.IsMatch(s);

    // Очищенная часть: всегда полный набор ключей (недостающее — значение по умолчанию), чтобы скин вёл себя одинаково везде
    public static JsonObject? CleanPart(string part, JsonObject? p, List<string> warnings)
    {
        if (p == null) { warnings.Add($"Часть «{PartNames.GetValueOrDefault(part, part)}» повреждена и пропущена"); return null; }
        switch (part)
        {
            case "color":
                return new JsonObject { ["color"] = Color(p, "color", "#FF2A9D"), ["windows"] = Flag(p, "windows", true), ["taskbar"] = Flag(p, "taskbar", true) };
            case "icons":
                return new JsonObject { ["color"] = Color(p, "color", "#00F0FF"), ["style"] = Pick(p, "style", new[] { "mono", "hue" }, "mono"), ["strength"] = Range(p, "strength", 0, 100, 100) };
            case "wallpaper":
                {
                    var id = Str(p, "id");
                    if (id == null || !WallpaperId.IsMatch(id)) { warnings.Add("Обои: нужны встроенные или каталожные — часть пропущена"); return null; }
                    return new JsonObject { ["id"] = id, ["fit"] = Pick(p, "fit", new[] { "cover", "contain", "fill" }, "cover"), ["fps"] = Range(p, "fps", 15, 60, 60) };
                }
            case "cursor":
                {
                    var shape = Str(p, "shape");
                    if (shape != null && shape.StartsWith("mine:", StringComparison.Ordinal)) { warnings.Add("Курсор: свой нарисованный курсор в скин не входит — взят «classic»"); shape = "classic"; }
                    var o = new JsonObject
                    {
                        ["shape"] = shape != null && new[] { "windows", "classic", "round", "neon", "dot", "ring", "cross", "ghost", "star" }.Contains(shape) ? shape : "classic",
                        ["fill"] = Color(p, "fill", "#FF2A9D"),
                        ["outline"] = Str(p, "outline") is { } ol && (ol == "auto" || Hex.IsMatch(ol)) ? (ol == "auto" ? "auto" : ol.ToUpperInvariant()) : "auto",
                        ["size"] = Range(p, "size", 100, 300, 100),
                        ["all"] = Flag(p, "all", true),
                        ["trail"] = Pick(p, "trail", new[] { "none", "comet", "sparks", "glow", "stars", "bubbles", "hearts", "ghost" }, "none"),
                        ["trailColorMode"] = Pick(p, "trailColorMode", new[] { "cursor", "rainbow", "two", "custom" }, "cursor"),
                        ["trailColor"] = Color(p, "trailColor", "#00F0FF"),
                        ["trailColor2"] = Color(p, "trailColor2", "#FF2A9D"),
                        ["trailLength"] = Range(p, "trailLength", 0, 100, 50),
                        ["trailSize"] = Range(p, "trailSize", 0, 100, 40),
                        ["click"] = Pick(p, "click", new[] { "none", "ring", "burst" }, "none"),
                    };
                    return o;
                }
            case "taskbar":
                {
                    var zones = new[] { "default", "left", "center", "right" };
                    return new JsonObject
                    {
                        ["shape"] = Pick(p, "shape", new[] { "none", "pill", "tab", "islands" }, "none"),
                        ["radius"] = Range(p, "radius", 0, 30, 14),
                        ["sideMargin"] = Range(p, "sideMargin", 0, 600, 0),
                        ["bottomGap"] = Range(p, "bottomGap", 0, 20, 6),
                        ["islandStyle"] = Pick(p, "islandStyle", new[] { "float", "tab" }, "float"),
                        ["islandPadding"] = Range(p, "islandPadding", 0, 40, 8),
                        ["islandMode"] = Pick(p, "islandMode", new[] { "each", "group" }, "each"),
                        ["islandGap"] = Range(p, "islandGap", 0, 40, 8),
                        ["keepEdges"] = Flag(p, "keepEdges", true),
                        ["zoneWidgets"] = Pick(p, "zoneWidgets", zones, "default"),
                        ["zoneSystem"] = Pick(p, "zoneSystem", zones, "default"),
                        ["zoneApps"] = Pick(p, "zoneApps", zones, "default"),
                        ["edgeMargin"] = Range(p, "edgeMargin", 0, 400, 12),
                    };
                }
            default:
                return null;
        }
    }

    // Одной строкой, человеческими словами: что делает часть скина (для карточек и подтверждения)
    public static string Describe(string part, JsonObject p)
    {
        string S(string k) => Str(p, k) ?? "";
        switch (part)
        {
            case "color": return $"цвет {S("color")}" + (Bool(p, "windows") == true ? ", окна и выделение" : "") + (Bool(p, "taskbar") == true ? ", острова панели" : "");
            case "icons": return $"все значки цвета {S("color")}" + (S("style") == "hue" ? " (сдвиг оттенка)" : "");
            case "wallpaper": return $"обои «{S("id")[2..]}»";
            case "cursor": return $"курсор «{S("shape")}» {S("fill")}" + (S("trail") != "none" ? $", шлейф «{S("trail")}»" : "") + (S("click") != "none" ? ", эффект клика" : "");
            case "taskbar":
                var shape = S("shape");
                var names = new Dictionary<string, string> { ["none"] = "обычная", ["pill"] = "плавающая", ["tab"] = "«вкладка»", ["islands"] = "острова" };
                return "форма панели: " + names.GetValueOrDefault(shape, shape);
            default: return part;
        }
    }
}
