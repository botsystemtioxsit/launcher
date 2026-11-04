using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BattleLauncher;

// Lite — облегчённая версия (окно Electron вокруг страницы, ставится из релизов репозитория приложения);
// Native — Desktop-версия (настоящая программа .exe, лежит в релизе указанного репозитория).
record AppDef(string Id, string Name, string Desc, string Repo, string ProductName, string[]? Roles,
    string? NativeExe = null, string? NativeName = null, string NativeRepo = "launcher", string NativeTag = "latest", string? Site = null, bool IsPlugin = false)
{
    // null в Roles — любой аккаунт игры, но не сессия по токену доступа (у неё нет игрока)
    public bool IsAllowed(string role) => Roles == null ? role != "token" : Roles.Contains(role);
}

// Программы, которые лаунчер не ставит сам (Linux, расширения Chrome): только кнопка «Скачать» со ссылкой на файл в релизе.
// Os — для каких систем запись: лаунчер показывает только подходящие его системе ("windows", "linux", "android").
record DownloadDef(string Id, string Name, string Desc, string Platform, string Url, string[] Os);

// Расширение для браузера: zip в релизе репозитория. Лаунчер скачивает и распаковывает его в папку,
// а человек один раз подключает эту папку в браузере («Загрузить распакованное расширение»).
record BrowserPluginDef(string Id, string Name, string Desc, string Browsers, string Repo, string Tag, string Asset);

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
    // Плагины лаунчера — отдельные программы: ставятся только по кнопке (раздел "plugins" каталога), в «Приложения» не попадают.
    public static AppDef[] Plugins { get; private set; } = DefaultPlugins();
    public static DownloadDef[] Downloads { get; private set; } = Array.Empty<DownloadDef>();
    // Плагины для браузера (раздел "browserPlugins" каталога): вкладка «Для браузера» в «Плагинах».
    public static BrowserPluginDef[] BrowserPlugins { get; private set; } = DefaultBrowserPlugins();

    static AppDef[] Defaults() => new[]
    {
        new AppDef("troll-battle", "Тролль-Баттл", "Голосовые баттлы — та же игра, что в Telegram.",
            "index.html", "Тролль-Баттл", null, "TIOXSITTrollBattle.exe", "Тролль-Баттл (Desktop)", Site: "https://botsystemtioxsit.github.io/index.html/"),
        new AppDef("battle-admin", "Ghost Shell Terminal", "Панель администратора: игроки, заявки, журнал, бэкапы, ВидеоЧат.",
            "battle-admin-bot", "БАТТЛ Админ", new[] { "moderator", "admin", "superadmin", "token" }, "TIOXSITTerminal.exe", "Ghost Shell Terminal",
            Site: "https://botsystemtioxsit.github.io/battle-admin-bot/public/admin.html"),
        new AppDef("videochat", "ВидеоЧат", "Случайные видеоразговоры. Аккаунт тот же, что в «Тролль-Баттле».",
            "videochat", "ВидеоЧат", null, "TIOXSITVideoChat.exe", "ВидеоЧат (Desktop)"),
    };

    static AppDef[] DefaultPlugins() => new[]
    {
        new AppDef("taskbar-plus", "Taskbar Plus", "Панель задач Windows по-своему: форма, острова, расположение частей, значки, автоскрытие.",
            "launcher", "Taskbar Plus", null, "TaskbarPlus.exe", "Taskbar Plus", IsPlugin: true),
        new AppDef("color-plus", "Color Plus", "Один цвет для окон, выделения и панели задач: выберите, посмотрите на предпросмотре и примените.",
            "launcher", "Color Plus", null, "ColorPlus.exe", "Color Plus", IsPlugin: true),
        new AppDef("icon-plus", "Icon Plus", "Значки Windows по-своему: один цвет для всех значков или готовый набор иконок, сайт с иконками внутри.",
            "launcher", "Icon Plus", null, "IconPlus.exe", "Icon Plus", IsPlugin: true),
        new AppDef("wallpaper-plus", "Wallpaper Plus", "Живые обои рабочего стола: видео, GIF и анимированные страницы. Каталог, свои файлы и сайты, конструктор своих обоев.",
            "launcher", "Wallpaper Plus", null, "WallpaperPlus.exe", "Wallpaper Plus", IsPlugin: true),
        new AppDef("cursor-plus", "Cursor Plus", "Свой курсор: цвет, форма, шлейф за мышью (комета, искры, звёзды…) и эффекты клика. Можно нарисовать курсор самому.",
            "launcher", "Cursor Plus", null, "CursorPlus.exe", "Cursor Plus", IsPlugin: true),
        new AppDef("dns-plus", "DNS Plus", "Защищённый DNS NextDNS для всей Windows: вставьте свой ID и нажмите «Включить». Сам сервис NextDNS (nextdns.io) бесплатный — профиль и фильтры настраиваются у них. Нужна Windows 11.",
            "launcher", "DNS Plus", null, "DnsPlus.exe", "DNS Plus", IsPlugin: true),
        new AppDef("gateway-plus", "Gateway Plus", "Личный VPN: мастер сам ставит WireGuard на ваш сервер по SSH, шлюз пускает через него весь интернет компьютера — сайты видят адрес и страну сервера. Нужен бесплатный аккаунт; только личное использование.",
            "launcher", "Gateway Plus", null, "GatewayPlus.exe", "Gateway Plus", IsPlugin: true),
        new AppDef("skin-loader", "Skin Loader", "Скины Windows: отметьте, какие плагины входят (панель задач, цвет, значки, обои, курсор), соберите скин и применяйте его одной кнопкой. Скин можно сохранить в файл и отдать другу.",
            "launcher", "Skin Loader", null, "SkinLoader.exe", "Skin Loader", IsPlugin: true),
    };

    static BrowserPluginDef[] DefaultBrowserPlugins() => new[]
    {
        new BrowserPluginDef("mask-plus", "Mask Plus",
            "Подменяет часовой пояс, язык и местоположение в браузере под выбранную страну и закрывает утечку адреса через WebRTC. IP-адрес не меняет — для него шлюз Gateway Plus.",
            "Chrome · Edge · Brave · Opera · Vivaldi", "launcher", "latest", "MaskPlus-extension.zip"),
    };

    // Плагины, убранные из лаунчера (владелец их удалил): если такой остался на компьютере, при запуске лаунчер сам его закрывает и удаляет.
    public static AppDef[] Retired { get; } = Array.Empty<AppDef>();

    public static AppDef? ById(string id) => Apps.Concat(Plugins).FirstOrDefault(a => a.Id == id);

    public static async Task Refresh(bool force = false, CancellationToken ct = default)
    {
        if (!force && DateTime.UtcNow - lastFetch < TimeSpan.FromMinutes(5)) return;
        try
        {
            var text = await Net.GetText($"https://raw.githubusercontent.com/{Owner}/launcher/main/catalog.json", ct);
            var parsed = Parse(text);
            if (parsed.Length > 0) Apps = parsed;
            Downloads = ParseDownloads(text);
            var plugins = ParsePlugins(text);
            if (plugins.Length > 0) Plugins = plugins;
            var browser = ParseBrowserPlugins(text);
            if (browser.Length > 0) BrowserPlugins = browser;
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

    // Раздел "downloads" каталога: ссылка собирается только из проверенных частей и ведёт на релиз нашего же аккаунта.
    public static DownloadDef[] ParseDownloads(string json)
    {
        var list = new List<DownloadDef>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("downloads", out var arr) || arr.ValueKind != JsonValueKind.Array) return Array.Empty<DownloadDef>();
            foreach (var d in arr.EnumerateArray())
            {
                string? S(string k) => d.ValueKind == JsonValueKind.Object && d.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var id = S("id"); var name = S("name"); var repo = S("repo"); var tag = S("tag"); var asset = S("asset");
                if (id == null || name == null || repo == null || tag == null || asset == null) continue;
                if (!Net.SafeId.IsMatch(id) || !Safe.IsMatch(repo) || !Safe.IsMatch(tag) || !Safe.IsMatch(asset)) continue;
                var desc = S("desc") ?? ""; var platform = S("platform") ?? "";
                var os = d.TryGetProperty("os", out var oa) && oa.ValueKind == JsonValueKind.Array
                    ? oa.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(x => Safe.IsMatch(x)).ToArray()
                    : Array.Empty<string>();
                list.Add(new DownloadDef(id, name[..Math.Min(name.Length, 60)], desc[..Math.Min(desc.Length, 240)], platform[..Math.Min(platform.Length, 40)],
                    $"https://github.com/{Owner}/{repo}/releases/download/{tag}/{asset}", os));
            }
        }
        catch { /* битый раздел — просто не показываем */ }
        return list.ToArray();
    }

    // Раздел "plugins" каталога: та же форма записи, что у Desktop-версии приложения — EXE + .sha256 в релизе репозитория.
    public static AppDef[] ParsePlugins(string json)
    {
        var list = new List<AppDef>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("plugins", out var arr) || arr.ValueKind != JsonValueKind.Array) return Array.Empty<AppDef>();
            foreach (var p in arr.EnumerateArray())
            {
                string? S(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var id = S(p, "id"); var name = S(p, "name");
                if (id == null || name == null || !Net.SafeId.IsMatch(id)) continue;
                p.TryGetProperty("desktop", out var desk);
                var asset = S(desk, "asset");
                if (asset == null || !Safe.IsMatch(asset) || !asset.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                var repo = S(desk, "repo") is { } r && Safe.IsMatch(r) ? r : "launcher";
                var tag = S(desk, "tag") is { } t && Safe.IsMatch(t) ? t : "latest";
                name = name[..Math.Min(name.Length, 60)];
                var desc = S(p, "desc") ?? "";
                list.Add(new AppDef(id, name, desc[..Math.Min(desc.Length, 240)], repo, name, null, asset, name, repo, tag, null, true));
            }
        }
        catch { /* битый раздел — остаются прежние плагины */ }
        return list.ToArray();
    }

    // Раздел "browserPlugins" каталога: zip с расширением в релизе репозитория нашего же аккаунта.
    public static BrowserPluginDef[] ParseBrowserPlugins(string json)
    {
        var list = new List<BrowserPluginDef>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("browserPlugins", out var arr) || arr.ValueKind != JsonValueKind.Array) return Array.Empty<BrowserPluginDef>();
            foreach (var p in arr.EnumerateArray())
            {
                string? S(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var id = S(p, "id"); var name = S(p, "name");
                if (id == null || name == null || !Net.SafeId.IsMatch(id)) continue;
                p.TryGetProperty("zip", out var zip);
                var asset = S(zip, "asset");
                if (asset == null || !Safe.IsMatch(asset) || !asset.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                var repo = S(zip, "repo") is { } r && Safe.IsMatch(r) ? r : "launcher";
                var tag = S(zip, "tag") is { } t && Safe.IsMatch(t) ? t : "latest";
                var desc = S(p, "desc") ?? ""; var browsers = S(p, "browsers") ?? "";
                list.Add(new BrowserPluginDef(id, name[..Math.Min(name.Length, 60)], desc[..Math.Min(desc.Length, 260)], browsers[..Math.Min(browsers.Length, 80)], repo, tag, asset));
            }
        }
        catch { /* битый раздел — остаётся прежний список */ }
        return list.ToArray();
    }
}
