using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace WallpaperPlusApp;

// Вкладка «Сайты»: обычный браузер внутри плагина (Microsoft Edge WebView2). Человек сам выбирает обои на сайте и нажимает «Скачать» —
// плагин ничего не запрашивает с сайтов сам и не обходит их защиту. Скачанный файл (видео, GIF, картинка, архив) сразу попадает в «Мои обои».
sealed partial class MainWindow
{
    static readonly (string Name, string Url, string Hint)[] SiteLinks =
    {
        ("Pexels · видео", "https://www.pexels.com/videos/", "бесплатные видео, свободная лицензия"),
        ("Pixabay · видео", "https://pixabay.com/videos/", "бесплатные видео, свободная лицензия"),
        ("Mixkit · видео", "https://mixkit.co/free-stock-video/", "бесплатные видео-фоны"),
        ("MoeWalls", "https://moewalls.com/", "живые обои (проверяйте лицензию)"),
    };
    static readonly string[] BadExts = { ".exe", ".msi", ".bat", ".cmd", ".scr", ".dll", ".lnk", ".ps1", ".vbs", ".js", ".jar", ".apk", ".7z", ".rar", ".pkg", ".iso" };

    WebView2? web;
    readonly TextBlock siteStatus = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6) };
    bool webStarted;

    void BuildSite()
    {
        siteStatus.Foreground = Res("Accent2");
        var sv = (ScrollViewer)host.Children[3];
        sv.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        var dock = new DockPanel();
        var head = new StackPanel();
        head.Children.Add(Note("Откройте обои на сайте и нажмите «Скачать» — плагин сам добавит файл в «Мои обои». Сайт может попросить вход или проверку — это обычная работа сайта. " +
                               "Плагин сам ничего не качает; из скачанного берутся только видео, GIF, картинки, страницы и архивы zip. Программы (.exe, .msi) он не запускает."));
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        bar.Children.Add(Btn("← Назад", null, () => { if (web?.CanGoBack == true) web.GoBack(); }, first: true));
        bar.Children.Add(Btn("Вперёд →", null, () => { if (web?.CanGoForward == true) web.GoForward(); }));
        foreach (var (name, url, hint) in SiteLinks)
        {
            var u = url;
            var b = Btn(name, null, () => Navigate(u));
            b.ToolTip = hint;
            bar.Children.Add(b);
        }
        bar.Children.Add(Btn("Открыть в обычном браузере", null, () => OpenExternal(web?.Source?.ToString() ?? SiteLinks[0].Url)));
        head.Children.Add(bar);
        head.Children.Add(siteStatus);
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);
        web = new WebView2();
        dock.Children.Add(web);
        sv.Content = dock;
    }

    // Браузер запускается при первом открытии вкладки «Сайты», а не вместе с окном
    async Task StartWeb()
    {
        if (webStarted || web == null || screenshotMode) return;
        webStarted = true;
        try
        {
            Directory.CreateDirectory(Paths.SiteWeb);
            var env = await CoreWebView2Environment.CreateAsync(null, Paths.SiteWeb);
            await web.EnsureCoreWebView2Async(env);
            web.CoreWebView2.DownloadStarting += OnDownloadStarting;
            web.CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; web.CoreWebView2.Navigate(e.Uri); };
            web.CoreWebView2.Navigate(SiteLinks[0].Url);
            siteStatus.Text = "Сайт открыт.";
        }
        catch (Exception ex)
        {
            siteStatus.Text = "Встроенный браузер не запустился (нужен «Microsoft Edge WebView2 Runtime» — он есть в Windows 11): " + ex.Message +
                              " Откройте сайт в обычном браузере, скачайте файл и перетащите его на окно плагина.";
        }
    }

    void Navigate(string url) { try { web?.CoreWebView2?.Navigate(url); } catch { } }

    static void OpenExternal(string url)
    {
        try { if (Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http")) Process.Start(new ProcessStartInfo(u.ToString()) { UseShellExecute = true }); } catch { }
    }

    void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        var fileName = SafeFileName(Path.GetFileName(e.ResultFilePath));
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var dir = Path.Combine(Paths.Downloads, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, fileName);
        e.ResultFilePath = dest;
        e.Handled = true;                                  // своё окно загрузки не показываем: ход виден в строке над сайтом
        var op = e.DownloadOperation;
        siteStatus.Text = "Скачиваю: " + fileName;
        op.BytesReceivedChanged += (_, _) =>
        {
            var got = op.BytesReceived / 1048576.0;
            var all = op.TotalBytesToReceive.HasValue && op.TotalBytesToReceive > 0 ? " из " + (op.TotalBytesToReceive.Value / 1048576.0).ToString("0.0") + " МБ" : "";
            Dispatcher.BeginInvoke(new Action(() => siteStatus.Text = "Скачиваю: " + fileName + " — " + got.ToString("0.0") + " МБ" + all));
        };
        op.StateChanged += (_, _) => Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (op.State == CoreWebView2DownloadState.Interrupted) { siteStatus.Text = "Скачивание прервано: " + op.InterruptReason; return; }
            if (op.State != CoreWebView2DownloadState.Completed) return;
            if (BadExts.Contains(ext)) { siteStatus.Text = $"Скачан файл «{fileName}» — это не обои (программа или неподдерживаемый формат). Он не запускается и не добавляется. Файл лежит здесь: {dir}"; return; }
            var (w, msg) = await Task.Run(() => Library.InstallPath(dest, "с сайта"));
            siteStatus.Text = msg;
            if (w != null) { BuildLibrary(); }
        }));
    }

    static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var s = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return s.Length > 0 ? s : "download";
    }
}
