using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BattleLauncher;

// Lite — облегчённая версия (окно Electron вокруг страницы, ставится из релизов репозитория приложения);
// Native — Desktop-версия (настоящая программа .exe, лежит в релизе указанного репозитория).
record AppDef(string Id, string Name, string Desc, string Repo, string ProductName, string[]? Roles,
    string? NativeExe = null, string? NativeName = null, string NativeRepo = "launcher", string NativeTag = "latest", string? Site = null)
{
    // null в Roles — любой аккаунт игры, но не сессия по токену доступа (у неё нет игрока)
    public bool IsAllowed(string role) => Roles == null ? role != "token" : Roles.Contains(role);
}

// Что умеет ставить лаунчер. Список живёт в catalog.json в репозитории launcher и читается при каждом
// запуске и обновлении списка: новое приложение или новая версия приложения (EXE, облегчённая) появляется
// без обновления самого лаунчера. Кнопка Desktop-версии, у которой в релизе ещё нет файла, остаётся
// закрытой и включается сама, как только файл появится. Встроенный список — запасной, на случай, если сети нет.
static class Catalog
{
    public const string Owner = "botsystemtioxsit";
    static readonly Regex Safe = new(@"^[A-Za-z0-9._-]{1,100}$", RegexOptions.Compiled);
    static DateTime lastFetch = DateTime.MinValue;

    public static AppDef[] Apps { get; private set; } = Defaults();

    static AppDef[] Defaults() => new[]
    {
        new AppDef("troll-battle", "Тролль-Баттл", "Голосовые баттлы — та же игра, что в Telegram.",
            "index.html", "Тролль-Баттл", null, "TIOXSITTrollBattle.exe", "Тролль-Баттл (Desktop)", Site: "https://botsystemtioxsit.github.io/index.html/"),
        new AppDef("battle-admin", "БАТТЛ Админ", "Панель администратора: игроки, заявки, журнал, бэкапы, ВидеоЧат.",
            "battle-admin-bot", "БАТТЛ Админ", new[] { "moderator", "admin", "superadmin", "token" }, "TIOXSITTerminal.exe", "Ghost Shell Terminal",
            Site: "https://botsystemtioxsit.github.io/battle-admin-bot/public/admin.html"),
        new AppDef("videochat", "ВидеоЧат", "Случайные видеоразговоры. Аккаунт тот же, что в «Тролль-Баттле».",
            "videochat", "ВидеоЧат", null, "TIOXSITVideoChat.exe", "ВидеоЧат (Desktop)"),
    };

    public static AppDef? ById(string id) => Apps.FirstOrDefault(a => a.Id == id);

    public static async Task Refresh(bool force = false, CancellationToken ct = default)
    {
        if (!force && DateTime.UtcNow - lastFetch < TimeSpan.FromMinutes(5)) return;
        try
        {
            var text = await Net.GetText($"https://raw.githubusercontent.com/{Owner}/launcher/main/catalog.json", ct);
            var parsed = Parse(text);
            if (parsed.Length > 0) Apps = parsed;
            lastFetch = DateTime.UtcNow;
        }
        catch { /* нет сети или файл повреждён — остаётся прежний список */ }
    }

    // Всё из файла проверяется: имена репозиториев/файлов — только безопасные символы, ссылки — только https.
    public static AppDef[] Parse(string json)
    {
        var list = new List<AppDef>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("apps", out var arr) || arr.ValueKind != JsonValueKind.Array) return Array.Empty<AppDef>();
        foreach (var a in arr.EnumerateArray())
        {
            try
            {
                string? S(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var id = S(a, "id");
                var name = S(a, "name");
                if (id == null || name == null || !Net.SafeId.IsMatch(id)) continue;
                a.TryGetProperty("lite", out var lite);
                a.TryGetProperty("desktop", out var desk);
                var liteRepo = S(lite, "repo");
                var product = S(lite, "productName");
                if (liteRepo == null || product == null || !Safe.IsMatch(liteRepo)) continue;
                string[]? roles = null;
                if (a.TryGetProperty("roles", out var r) && r.ValueKind == JsonValueKind.Array)
                    roles = r.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(x => Safe.IsMatch(x)).ToArray();
                var site = S(lite, "site");
                if (site != null && !site.StartsWith("https://", StringComparison.Ordinal)) site = null;
                string? exe = null, nName = null; var nRepo = "launcher"; var nTag = "latest";
                var dAsset = S(desk, "asset");
                if (dAsset != null && Safe.IsMatch(dAsset) && dAsset.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    exe = dAsset;
                    nName = S(desk, "name") is { Length: > 0 } n ? n[..Math.Min(n.Length, 60)] : Path.GetFileNameWithoutExtension(dAsset);
                    var rr = S(desk, "repo"); if (rr != null && Safe.IsMatch(rr)) nRepo = rr;
                    var tt = S(desk, "tag"); if (tt != null && Safe.IsMatch(tt)) nTag = tt;
                }
                list.Add(new AppDef(id, name[..Math.Min(name.Length, 60)], (S(a, "desc") ?? "")[..Math.Min((S(a, "desc") ?? "").Length, 200)],
                    liteRepo, product, roles, exe, nName, nRepo, nTag, site));
            }
            catch { /* одну битую запись пропускаем, остальные работают */ }
        }
        return list.ToArray();
    }
}
