using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace IconPlusApp;

// Вкладка «Сайт с иконками»: обычный браузер внутри плагина (Microsoft Edge WebView2). Человек сам выбирает набор и нажимает «Скачать» —
// плагин ничего не запрашивает с сайта сам (у сайта в robots.txt закрыты разделы скачивания для программ) и не обходит его защиту.
// Скачанный файл попадает не в папку «Загрузки», а сразу в плагин и добавляется как набор значков.
sealed partial class MainWindow
{
    const string SiteHome = "https://vsthemes.org/ru/";
    static readonly string[] NotPackExts = { ".exe", ".msi", ".bat", ".cmd", ".scr", ".ip", ".7tsp", ".theme", ".themepack", ".deskthemepack", ".msstyles", ".dll" };

    WebView2? web;
    readonly TextBlock siteStatus = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6) };
    bool webStarted;

    void BuildSite()
    {
        siteStatus.Foreground = Res("Accent2");
        var sv = (ScrollViewer)host.Children[2];
        sv.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        var dock = new DockPanel();
        var head = new StackPanel();
        head.Children.Add(Note("Откройте набор на сайте и нажмите «Скачать» — плагин сам добавит его во вкладку «Наборы». На сайте могут понадобиться вход или проверка — это обычная работа сайта. " +
                               "Плагин сам ничего не качает и не запускает: из скачанного берутся только файлы значков (.ico, .png). Установщики и темы (.exe, .msi, .7tsp) он не запускает."));
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        bar.Children.Add(Btn("← Назад", null, () => { if (web?.CanGoBack == true) web.GoBack(); }, first: true));
        bar.Children.Add(Btn("Вперёд →", null, () => { if (web?.CanGoForward == true) web.GoForward(); }));
        bar.Children.Add(Btn("Иконки на сайте", null, () => Navigate(SiteHome)));
        bar.Children.Add(Btn("Открыть в обычном браузере", null, () => OpenExternal(web?.Source?.ToString() ?? SiteHome)));
        bar.Children.Add(Btn("Добавить скачанное вручную…", null, PickArchive));
        head.Children.Add(bar);
        head.Children.Add(siteStatus);
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);
        web = new WebView2 { Margin = new Thickness(0, 0, 0, 0) };
        dock.Children.Add(web);
        sv.Content = dock;
        Loaded += async (_, _) => await StartWeb();
    }

    async Task StartWeb()
    {
        if (webStarted || web == null) return;
        webStarted = true;
        try
        {
            Directory.CreateDirectory(Paths.WebData);
            var env = await CoreWebView2Environment.CreateAsync(null, Paths.WebData);
            await web.EnsureCoreWebView2Async(env);
            web.CoreWebView2.DownloadStarting += OnDownloadStarting;
            web.CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; web.CoreWebView2.Navigate(e.Uri); };
            web.CoreWebView2.Navigate(SiteHome);
            siteStatus.Text = "Сайт открыт.";
        }
        catch (Exception ex)
        {
            siteStatus.Text = "Встроенный браузер не запустился (нужен «Microsoft Edge WebView2 Runtime» — он есть в Windows 11): " + ex.Message +
                              " Откройте сайт в обычном браузере, скачайте набор и добавьте его кнопкой «Добавить скачанное вручную».";
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
        op.StateChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (op.State == CoreWebView2DownloadState.Interrupted) { siteStatus.Text = "Скачивание прервано: " + op.InterruptReason; return; }
            if (op.State != CoreWebView2DownloadState.Completed) return;
            if (NotPackExts.Contains(ext))
            {
                siteStatus.Text = $"Скачан файл «{fileName}» — это не набор значков (установщик или тема). Плагин его не запускает. Файл лежит здесь: {dir}";
                return;
            }
            ImportPath(dest);
        }));
    }

    static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var s = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return s.Length > 0 ? s : "download";
    }
}
