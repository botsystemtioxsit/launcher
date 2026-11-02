using System.IO;
using System.Text.Json.Nodes;

namespace SkinLoaderApp;

// «Собрать скин»: берёт то, что у человека выбрано сейчас в каждом плагине (из его файла настроек), и приводит к допустимому виду.
static class Capture
{
    public sealed record PartInfo(string Part, bool Installed, bool InUse, string Summary, JsonObject? Settings, string? Note);

    public static List<PartInfo> Scan()
    {
        var list = new List<PartInfo>();
        foreach (var def in Plugins.All)
        {
            var installed = Plugins.ExePath(def) != null;
            var file = Plugins.SettingsFile(def.Part);
            var has = File.Exists(file);
            var raw = Plugins.ReadSettings(def.Part);
            var warnings = new List<string>();
            JsonObject? part = has || def.Part == "taskbar" ? FromSettings(def.Part, raw, warnings) : null;
            // «используется» — плагин что-то уже применял (а не просто стоит с настройками по умолчанию)
            var inUse = def.Part switch
            {
                "taskbar" => (SkinSchema.Str(raw, "Shape") is { } sh && sh != "none") || SkinSchema.Bool(raw, "UseXaml") == true,
                "color" => SkinSchema.Bool(raw, "Applied") == true,
                "cursor" => SkinSchema.Bool(raw, "Applied") == true,
                "wallpaper" => !string.IsNullOrEmpty(SkinSchema.Str(raw, "Active")),
                "icons" => File.Exists(Path.Combine(Plugins.DataDir, "icon-plus", "state.json")),
                _ => false,
            };
            var summary = part != null ? SkinSchema.Describe(def.Part, part) : "настроек пока нет — откройте плагин и выберите";
            list.Add(new PartInfo(def.Part, installed, inUse, summary, part, warnings.Count > 0 ? string.Join("; ", warnings) : null));
        }
        return list;
    }

    // Из файла настроек плагина (PascalCase) в часть скина (camelCase) — через ту же очистку, что и для чужого файла
    static JsonObject? FromSettings(string part, JsonObject raw, List<string> warnings)
    {
        JsonObject Map(params (string To, string From)[] map)
        {
            var o = new JsonObject();
            foreach (var (to, from) in map) if (raw[from] != null) o[to] = raw[from]!.DeepClone();
            return o;
        }
        JsonObject m = part switch
        {
            "color" => Map(("color", "Color"), ("windows", "Windows"), ("taskbar", "Taskbar")),
            "icons" => Map(("color", "Color"), ("style", "Style"), ("strength", "Strength")),
            "wallpaper" => Map(("id", "Active"), ("fit", "Fit"), ("fps", "Fps")),
            "cursor" => Map(("shape", "Shape"), ("fill", "Fill"), ("outline", "Outline"), ("size", "Size"), ("all", "All"), ("trail", "Trail"), ("trailColorMode", "TrailColorMode"),
                ("trailColor", "TrailColor"), ("trailColor2", "TrailColor2"), ("trailLength", "TrailLength"), ("trailSize", "TrailSize"), ("click", "Click")),
            "taskbar" => Map(("shape", "Shape"), ("radius", "Radius"), ("sideMargin", "SideMargin"), ("bottomGap", "BottomGap"), ("islandStyle", "IslandStyle"), ("islandPadding", "IslandPadding"),
                ("islandMode", "IslandMode"), ("islandGap", "IslandGap"), ("keepEdges", "KeepEdges"), ("zoneWidgets", "ZoneWidgets"), ("zoneSystem", "ZoneSystem"), ("zoneApps", "ZoneApps"), ("edgeMargin", "EdgeMargin")),
            _ => new JsonObject(),
        };
        return SkinSchema.CleanPart(part, m, warnings);
    }

    public static Skin Build(string name, string author, string description, IEnumerable<string> parts, out List<string> warnings)
    {
        warnings = new List<string>();
        var skin = new Skin
        {
            Name = SkinSchema.Text(name, 40, "Мой скин"), Author = SkinSchema.Text(author, 40, ""), Description = SkinSchema.Text(description, 300, ""),
            Created = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        foreach (var info in Scan().Where(i => parts.Contains(i.Part)))
        {
            if (info.Settings == null) { warnings.Add($"{SkinSchema.PartNames[info.Part]}: нет настроек — пропущено"); continue; }
            skin.Parts[info.Part] = info.Settings.DeepClone();
            if (info.Note != null) warnings.Add($"{SkinSchema.PartNames[info.Part]}: {info.Note}");
        }
        if (!skin.PartIds.Any()) throw new InvalidDataException("Нечего собирать: отметьте хотя бы одну часть, у которой есть настройки.");
        return skin;
    }
}
