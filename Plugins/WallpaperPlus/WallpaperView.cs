using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace WallpaperPlusApp;

// Что ещё нужно знать при загрузке обоев в окно: на паузе ли окно, куда отдавать раскладку из редактора, открыт ли редактор, какой фон под слоем.
sealed class LoadOptions
{
    public Func<bool>? IsPaused;
    public Action<string>? OnLayout;
    public bool Edit;
    public string? Background;      // null — как записано в обоях
}

// Загрузка обоев в окно WebView2 — одинаково для рабочего стола, предпросмотра в окне и редактора.
// Безопасность: страница обоев видит только свою папку (виртуальный адрес), чужие сайты и скачивания закрыты, разрешений у страницы нет;
// выход в интернет открывается только обоям, которым человек сам его разрешил. Данные компьютера страница получает только сообщениями (см. Telemetry.cs).
static class WallpaperView
{
    public const string Host = "wallpaper.example";
    public const string BgHost = "bg.wallpaper.example";
    public const string FgHost = "fg.wallpaper.example";

    sealed class State { public bool Online; public LoadOptions? Opts; }
    static readonly ConditionalWeakTable<CoreWebView2, State> States = new();

    static CoreWebView2Environment? hostEnv;
    static readonly SemaphoreSlim envGate = new(1, 1);

    public static async Task<CoreWebView2Environment> EnvAsync()
    {
        await envGate.WaitAsync();
        try
        {
            if (hostEnv != null) return hostEnv;
            Directory.CreateDirectory(Paths.HostWeb);
            // звук и видео запускаются сами (без щелчка), окно ничего не спрашивает
            var opts = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required --disable-features=msWebOOUI,msPdfOOUI,msSmartScreenProtection");
            hostEnv = await CoreWebView2Environment.CreateAsync(null, Paths.HostWeb, opts);
            return hostEnv;
        }
        finally { envGate.Release(); }
    }

    static bool IsOurHost(string host) => host.Equals(Host, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + Host, StringComparison.OrdinalIgnoreCase);

    public static string EntryUrl(Wallpaper w, WpSettings s, Dictionary<string, string>? valuesOverride = null, string host = Host, string extra = "")
    {
        var q = new List<string> { "fit=" + s.Fit, "fps=" + s.Fps, "muted=" + (s.Mute ? "1" : "0") };
        var withValues = valuesOverride != null ? new Wallpaper { Id = w.Id, Params = w.Params, Values = valuesOverride } : w;
        var pq = withValues.ParamQuery();
        if (pq.Length > 0) q.Add(pq);
        if (extra.Length > 0) q.Add(extra);
        string path;
        if (w.Type == "html") path = string.Join("/", w.Entry.Split('/', '\\').Select(Uri.EscapeDataString));
        else
        {
            path = "_player.html";
            q.Add("f=" + Uri.EscapeDataString(Library.MediaFile(w)));
            q.Add("t=" + w.Type);
        }
        return $"https://{host}/{path}?{string.Join("&", q)}";
    }

    // Обои под слоем (если у обоев есть слой и фон выбран и существует)
    public static Wallpaper? BackgroundOf(Wallpaper w, string? background)
    {
        var id = background ?? w.Background;
        if (!w.Overlay || string.IsNullOrEmpty(id) || id == w.Id) return null;
        return Library.Get(id);
    }

    // Настраивает окно под показ обоев (один раз) и открывает страницу
    public static async Task LoadAsync(WebView2 web, Wallpaper w, WpSettings s, Dictionary<string, string>? values = null, LoadOptions? opts = null)
    {
        var env = await EnvAsync();
        await web.EnsureCoreWebView2Async(env);
        var core = web.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsBuiltInErrorPageEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsPinchZoomEnabled = false;
        core.Settings.IsSwipeNavigationEnabled = false;
        core.IsMuted = s.Mute;
        try { web.DefaultBackgroundColor = System.Drawing.Color.Black; } catch { }

        var state = States.GetOrCreateValue(core);
        state.Opts = opts;
        // SDK для страниц (GS): подставляется в каждую страницу и слой; сама страница ничего не запрашивает, данные только приходят к ней
        await core.AddScriptToExecuteOnDocumentCreatedAsync(Res.GsJs);

        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            try
            {
                if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var u)) return;
                if (IsOurHost(u.Host)) return;
                if (u.Scheme is "data" or "blob" or "about" or "edge") return;
                if (state.Online && u.Scheme is "http" or "https" or "ws" or "wss") return;
                e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
            }
            catch { }
        };
        core.NavigationStarting += (_, e) =>
        {
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && (IsOurHost(u.Host) || u.Scheme is "about" or "data" or "blob")) return;
            e.Cancel = true;
        };
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.DownloadStarting += (_, e) => { e.Cancel = true; };
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        // раскладка из редактора «Терминала»
        core.WebMessageReceived += (_, e) =>
        {
            var cb = state.Opts?.OnLayout;
            if (cb == null) return;
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                var r = doc.RootElement;
                if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("t", out var t) || t.GetString() != "layout") return;
                if (!r.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.String) return;
                var text = v.GetString() ?? "";
                if (text.Length > 200_000) return;
                using var check = JsonDocument.Parse(text);   // раскладка должна быть настоящим JSON
                cb(text);
            }
            catch { }
        };
        core.NavigationCompleted += (_, _) => TelemetryHub.I.Resync(core);

        Navigate(web, w, s, values, opts);
    }

    // Показать (или перезагрузить) обои в уже настроенном окне: подставляет папки и подключает данные
    public static void Navigate(WebView2 web, Wallpaper w, WpSettings s, Dictionary<string, string>? values = null, LoadOptions? opts = null)
    {
        var core = web.CoreWebView2;
        if (core == null) return;
        var state = States.GetOrCreateValue(core);
        state.Opts = opts;
        var bg = BackgroundOf(w, opts?.Background);
        state.Online = w.Online || (bg?.Online ?? false);
        var extra = opts?.Edit == true ? "edit=1" : "";
        void Map(string host, string dir)
        {
            try { core.ClearVirtualHostNameToFolderMapping(host); } catch { }
            core.SetVirtualHostNameToFolderMapping(host, dir, CoreWebView2HostResourceAccessKind.Allow);
        }
        string url;
        if (bg != null)
        {
            Res.EnsureSystem();
            Map(Host, Res.SystemDir); Map(BgHost, bg.Dir); Map(FgHost, w.Dir);
            var bgUrl = EntryUrl(bg, s, null, BgHost);
            var fgUrl = EntryUrl(w, s, values, FgHost, extra.Length > 0 ? "compose=1&" + extra : "compose=1");
            url = $"https://{Host}/compose.html?bg={Uri.EscapeDataString(bgUrl)}&fg={Uri.EscapeDataString(fgUrl)}";
        }
        else
        {
            Map(Host, w.Dir);
            url = EntryUrl(w, s, values, Host, extra);
        }
        var kinds = new List<string>(w.Telemetry);
        if (bg != null) kinds.AddRange(bg.Telemetry);
        if (kinds.Count > 0) TelemetryHub.I.Attach(core, kinds, opts?.IsPaused);
        else TelemetryHub.I.Detach(core);
        core.Navigate(url);
    }

    public static void Detach(WebView2 web)
    {
        try { TelemetryHub.I.Detach(web.CoreWebView2); } catch { }
    }
}
