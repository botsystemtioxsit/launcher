using System.IO;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace WallpaperPlusApp;

// Загрузка обоев в окно WebView2 — одинаково для рабочего стола, предпросмотра в окне и конструктора.
// Безопасность: страница обоев видит только свою папку (виртуальный адрес), чужие сайты и скачивания закрыты, разрешений у страницы нет;
// выход в интернет открывается только обоям, которым человек сам его разрешил.
static class WallpaperView
{
    public const string Host = "wallpaper.example";

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

    public static string EntryUrl(Wallpaper w, WpSettings s, Dictionary<string, string>? valuesOverride = null)
    {
        var q = new List<string> { "fit=" + s.Fit, "fps=" + s.Fps, "muted=" + (s.Mute ? "1" : "0") };
        var withValues = valuesOverride != null ? new Wallpaper { Id = w.Id, Params = w.Params, Values = valuesOverride } : w;
        var pq = withValues.ParamQuery();
        if (pq.Length > 0) q.Add(pq);
        string path;
        if (w.Type == "html") path = string.Join("/", w.Entry.Split('/', '\\').Select(Uri.EscapeDataString));
        else
        {
            path = "_player.html";
            q.Add("f=" + Uri.EscapeDataString(Library.MediaFile(w)));
            q.Add("t=" + w.Type);
        }
        return $"https://{Host}/{path}?{string.Join("&", q)}";
    }

    // Настраивает окно под показ обоев и открывает страницу
    public static async Task LoadAsync(WebView2 web, Wallpaper w, WpSettings s, Dictionary<string, string>? values = null)
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

        core.SetVirtualHostNameToFolderMapping(Host, w.Dir, CoreWebView2HostResourceAccessKind.Allow);
        var online = w.Online;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            try
            {
                if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var u)) return;
                if (u.Host.Equals(Host, StringComparison.OrdinalIgnoreCase)) return;
                if (u.Scheme is "data" or "blob" or "about" or "edge") return;
                if (online && u.Scheme is "http" or "https" or "ws" or "wss") return;
                e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
            }
            catch { }
        };
        core.NavigationStarting += (_, e) =>
        {
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && (u.Host.Equals(Host, StringComparison.OrdinalIgnoreCase) || u.Scheme is "about" or "data" or "blob")) return;
            e.Cancel = true;
        };
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.DownloadStarting += (_, e) => { e.Cancel = true; };
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.Navigate(EntryUrl(w, s, values));
    }
}
