using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SkinLoaderApp;

sealed record StepResult(string Part, bool Ok, string Message);

// Применение и откат скина. На каждую часть — один плагин: пишем в его файл настроек допустимые поля, просим работающую копию выйти
// и запускаем плагин с тем же ключом, что использует CI (--apply, --apply-color, --apply=<обои>, --background).
// Каждый плагин сам запоминает прежнее состояние, поэтому «Вернуть как было» — это его собственный откат.
static class Applier
{
    static string AppliedFile => Path.Combine(Plugins.DataDir, "skin-loader", "applied.json");

    // Что получится при применении: для каждой части — ставится ли (плагин установлен) и что именно
    public static List<(string Part, bool Available, string Text)> Plan(Skin skin)
    {
        var list = new List<(string, bool, string)>();
        foreach (var part in skin.PartIds)
        {
            var def = Plugins.ForPart(part);
            var ok = Plugins.ExePath(def) != null;
            list.Add((part, ok, SkinSchema.Describe(part, skin.Part(part)!) + (ok ? "" : $" — не установлен {def.Name}")));
        }
        return list;
    }

    public static async Task<List<StepResult>> Apply(Skin skin, IReadOnlyCollection<string> parts, Action<string>? progress = null)
    {
        var results = new List<StepResult>();
        // порядок: сначала панель (Color Plus потом красит её острова), значки — последними (они могут перезапустить Проводник)
        foreach (var part in new[] { "taskbar", "color", "wallpaper", "cursor", "icons" })
        {
            if (!parts.Contains(part) || skin.Part(part) is not { } p) continue;
            var def = Plugins.ForPart(part);
            if (Plugins.ExePath(def) == null) { results.Add(new(part, false, $"{def.Name} не установлен")); continue; }
            progress?.Invoke($"{SkinSchema.PartNames[part]}…");
            try { results.Add(await Task.Run(() => ApplyPart(part, p))); }
            catch (Exception ex) { results.Add(new(part, false, ex.Message)); }
        }
        if (results.Any(r => r.Ok))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AppliedFile)!);
                var rec = new JsonObject { ["name"] = skin.Name, ["at"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ["parts"] = new JsonArray(results.Where(r => r.Ok).Select(r => (JsonNode?)JsonValue.Create(r.Part)).ToArray()) };
                File.WriteAllText(AppliedFile, rec.ToJsonString());
            }
            catch { }
        }
        return results;
    }

    static StepResult ApplyPart(string part, JsonObject p)
    {
        var def = Plugins.ForPart(part);
        string S(string k) => SkinSchema.Str(p, k) ?? "";
        int I(string k) => SkinSchema.Int(p, k) ?? 0;
        bool B(string k) => SkinSchema.Bool(p, k) ?? false;
        switch (part)
        {
            case "taskbar":
                {
                    var shape = S("shape");
                    var values = new Dictionary<string, object>
                    {
                        ["Shape"] = shape, ["Radius"] = I("radius"), ["SideMargin"] = I("sideMargin"), ["BottomGap"] = I("bottomGap"),
                        ["IslandStyle"] = S("islandStyle"), ["IslandPadding"] = I("islandPadding"), ["IslandMode"] = S("islandMode"), ["IslandGap"] = I("islandGap"),
                        ["KeepEdges"] = B("keepEdges"), ["ZoneWidgets"] = S("zoneWidgets"), ["ZoneSystem"] = S("zoneSystem"), ["ZoneApps"] = S("zoneApps"), ["EdgeMargin"] = I("edgeMargin"),
                    };
                    var wantsEngine = shape != "none" || S("zoneWidgets") != "default" || S("zoneSystem") != "default" || S("zoneApps") != "default";
                    values["UseXaml"] = wantsEngine;
                    if (wantsEngine) values["XamlConsentShown"] = true;   // согласие человек дал в окне подтверждения Skin Loader
                    Plugins.AskQuit(def);   // сначала просим выйти работающую копию, потом пишем настройки — иначе она могла бы перезаписать их своими
                    Plugins.MergeSettings(part, values);
                    var proc = Plugins.Start(def, "--background");
                    if (!wantsEngine) return new(part, true, "панель — как в Windows");
                    return proc != null ? new(part, true, "форма панели применена (движок подключится за несколько секунд)") : new(part, false, "не удалось запустить Taskbar Plus");
                }
            case "color":
                {
                    Plugins.AskQuit(def);
                    var args = "--apply=" + S("color") + (B("windows") ? "" : " --no-windows") + (B("taskbar") ? "" : " --no-taskbar");
                    Plugins.Start(def, args);
                    var ok = Plugins.WaitFor(() => SkinSchema.Bool(Plugins.ReadSettings("color"), "Applied") == true && SkinSchema.Str(Plugins.ReadSettings("color"), "Color") == S("color"), 40000);
                    return new(part, ok, ok ? "цвет применён" : "Color Plus не подтвердил применение за 40 секунд");
                }
            case "icons":
                {
                    Plugins.MergeSettings(part, new Dictionary<string, object> { ["Style"] = S("style"), ["Strength"] = I("strength") });
                    var proc = Plugins.Start(def, "--apply-color=" + S("color"));
                    if (proc == null) return new(part, false, "не удалось запустить Icon Plus");
                    var done = proc.WaitForExit(6 * 60 * 1000);
                    return new(part, done && proc.ExitCode == 0, done ? "значки перекрашены (Проводник мог перезапуститься)" : "Icon Plus ещё работает — подождите, значки обновятся сами");
                }
            case "wallpaper":
                {
                    Plugins.AskQuit(def);
                    Plugins.MergeSettings(part, new Dictionary<string, object> { ["Fit"] = S("fit"), ["Fps"] = I("fps") });
                    Plugins.Start(def, "--apply=" + S("id"));
                    var ok = Plugins.WaitFor(() => SkinSchema.Str(Plugins.ReadSettings("wallpaper"), "Active") == S("id"), 20000);
                    return new(part, ok, ok ? "обои включены" : "Wallpaper Plus не подтвердил включение за 20 секунд");
                }
            case "cursor":
                {
                    Plugins.AskQuit(def);
                    Plugins.MergeSettings(part, new Dictionary<string, object>
                    {
                        ["Shape"] = S("shape"), ["Fill"] = S("fill"), ["Outline"] = S("outline"), ["Size"] = I("size"), ["All"] = B("all"),
                        ["Trail"] = S("trail"), ["TrailColorMode"] = S("trailColorMode"), ["TrailColor"] = S("trailColor"), ["TrailColor2"] = S("trailColor2"),
                        ["TrailLength"] = I("trailLength"), ["TrailSize"] = I("trailSize"), ["Click"] = S("click"),
                    });
                    Plugins.Start(def, "--apply");
                    var ok = Plugins.WaitFor(() => SkinSchema.Bool(Plugins.ReadSettings("cursor"), "Applied") == true, 30000);
                    return new(part, ok, ok ? "курсор применён" : "Cursor Plus не подтвердил применение за 30 секунд");
                }
            default:
                return new(part, false, "неизвестная часть");
        }
    }

    // «Вернуть как было»: каждый плагин возвращает своё. Тем, у кого нет своего ключа отката, возвращаем настройки в «ничего не менять».
    public static async Task<List<StepResult>> RevertAll(IReadOnlyCollection<string>? only = null, Action<string>? progress = null)
    {
        var results = new List<StepResult>();
        foreach (var part in new[] { "cursor", "wallpaper", "color", "taskbar", "icons" })
        {
            if (only != null && !only.Contains(part)) continue;
            var def = Plugins.ForPart(part);
            if (Plugins.ExePath(def) == null) continue;
            progress?.Invoke($"{SkinSchema.PartNames[part]}…");
            try { results.Add(await Task.Run(() => RevertPart(part, def))); }
            catch (Exception ex) { results.Add(new(part, false, ex.Message)); }
        }
        try { File.Delete(AppliedFile); } catch { }
        return results;
    }

    static StepResult RevertPart(string part, PluginDef def)
    {
        switch (part)
        {
            case "cursor":
                {
                    var p = Plugins.Start(def, "--revert");
                    var done = p != null && p.WaitForExit(60000);
                    return new(part, done, done ? "курсор возвращён" : "Cursor Plus не закончил за минуту");
                }
            case "color":
                {
                    var p = Plugins.Start(def, "--revert");
                    var done = p != null && p.WaitForExit(90000);
                    return new(part, done, done ? "цвета возвращены" : "Color Plus не закончил за 90 секунд");
                }
            case "icons":
                {
                    var p = Plugins.Start(def, "--restore");
                    var done = p != null && p.WaitForExit(6 * 60 * 1000);
                    return new(part, done, done ? "значки возвращены" : "Icon Plus ещё работает");
                }
            case "wallpaper":
                {
                    var gone = Plugins.AskQuit(def);
                    Plugins.MergeSettings(part, new Dictionary<string, object> { ["Active"] = "" });
                    return new(part, gone, gone ? "обои выключены, рабочий стол как был" : "Wallpaper Plus не закрылся");
                }
            case "taskbar":
                {
                    var gone = Plugins.AskQuit(def);
                    Plugins.MergeSettings(part, new Dictionary<string, object> { ["Shape"] = "none", ["UseXaml"] = false, ["ZoneWidgets"] = "default", ["ZoneSystem"] = "default", ["ZoneApps"] = "default" });
                    return new(part, gone, gone ? "панель — как в Windows (если что-то осталось, нажмите «Вернуть как было» в самом Taskbar Plus)" : "Taskbar Plus не закрылся");
                }
            default:
                return new(part, false, "неизвестная часть");
        }
    }

    public static string? AppliedName()
    {
        try { return (JsonNode.Parse(File.ReadAllText(AppliedFile)) as JsonObject)?["name"]?.GetValue<string>(); }
        catch { return null; }
    }
}
