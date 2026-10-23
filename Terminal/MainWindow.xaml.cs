using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using BattleLauncher;

namespace TioxsitTerminal;

// Терминал показывает ту же самую админ-панель, что и сайт (один в один): своя страница внутри окна на движке Edge (WebView2).
// Обновляется сама вместе с сайтом. Вход общий с лаунчером: при первом запуске переносим аккаунт и это устройство в страницу.
public partial class MainWindow : Window
{
    const string PanelUrl = "https://botsystemtioxsit.github.io/battle-admin-bot/public/admin.html";
    const string RuntimeUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    public static string AppVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            var plus = v.IndexOf('+');
            return plus > 0 ? v[..plus] : v;
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        Title = "БАТТЛ — Админ-панель";
        Loaded += async (_, _) => await StartAsync();
    }

    void Fail(string text, bool runtime)
    {
        Web.Visibility = Visibility.Hidden;
        ProblemText.Text = text;
        RuntimeBtn.Visibility = runtime ? Visibility.Visible : Visibility.Collapsed;
        Problem.Visibility = Visibility.Visible;
    }

    async void Retry_Click(object sender, RoutedEventArgs e) => await StartAsync();

    void Runtime_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(RuntimeUrl) { UseShellExecute = true });

    bool started;

    async Task StartAsync()
    {
        Problem.Visibility = Visibility.Collapsed;
        try
        {
            if (!started)
            {
                var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TIOXSITTerminal", "WebView2");
                var env = await CoreWebView2Environment.CreateAsync(null, data);
                await Web.EnsureCoreWebView2Async(env);
                var cw = Web.CoreWebView2;
                cw.Settings.AreDevToolsEnabled = false;
                cw.Settings.IsStatusBarEnabled = false;
                cw.Settings.AreBrowserAcceleratorKeysEnabled = true;
                // чужие ссылки — в обычном браузере, не внутри админ-панели
                cw.NewWindowRequested += (_, a) => { a.Handled = true; OpenOutside(a.Uri); };
                cw.NavigationStarting += (_, a) =>
                {
                    if (!Uri.TryCreate(a.Uri, UriKind.Absolute, out var u)) { a.Cancel = true; return; }
                    if (u.Host != "botsystemtioxsit.github.io" && u.Scheme != "about" && u.Scheme != "data") { a.Cancel = true; OpenOutside(a.Uri); }
                };
                cw.NavigationCompleted += (_, a) =>
                {
                    if (!a.IsSuccess && a.WebErrorStatus is CoreWebView2WebErrorStatus.CannotConnect or CoreWebView2WebErrorStatus.HostNameNotResolved
                        or CoreWebView2WebErrorStatus.ConnectionAborted or CoreWebView2WebErrorStatus.Disconnected or CoreWebView2WebErrorStatus.Timeout)
                        Fail("Нет связи — не удалось открыть админ-панель. Проверь интернет и нажми «Повторить».", false);
                };
                await cw.AddScriptToExecuteOnDocumentCreatedAsync(SeedScript());
                started = true;
            }
            Web.Visibility = Visibility.Visible;
            if (App.ScreenshotDir != null) { await Screenshot(App.ScreenshotDir); return; }
            Web.CoreWebView2.Navigate(PanelUrl);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            Fail("Для работы Терминала нужен компонент Microsoft Edge WebView2. Он бесплатный и ставится за минуту.", true);
        }
        catch (Exception ex)
        {
            Fail("Не получилось открыть админ-панель: " + ex.Message, false);
        }
    }

    static void OpenOutside(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http"))
        {
            try { Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true }); } catch { }
        }
    }

    // Один раз переносим вход из лаунчера: id аккаунта и deviceId этого устройства. Дальше страница живёт сама
    // (выйти / сменить аккаунт можно как в обычной панели), поэтому повторно не подставляем.
    static string SeedScript()
    {
        try
        {
            var store = AccountStore.Load();
            var me = store.Active;
            if (me == null || me.IsToken || App.ScreenshotDir != null) return "";
            var js = $"try{{if(!localStorage.getItem('tioxsitSeeded')){{localStorage.setItem('tioxsitSeeded','1');" +
                     $"if(!localStorage.getItem('battleTelegramId')){{localStorage.setItem('battleTelegramId',{JsonSerializer.Serialize(me.Id)});" +
                     $"if(!localStorage.getItem('battleDeviceSecret'))localStorage.setItem('battleDeviceSecret',{JsonSerializer.Serialize(store.DeviceId())});}}}}}}catch(e){{}}";
            return "if(location.hostname==='botsystemtioxsit.github.io'){" + js + "}";
        }
        catch { return ""; }
    }

    // служебный режим --screenshot: открыть страницу и сохранить окно в PNG (на CI)
    async Task Screenshot(string dir)
    {
        Directory.CreateDirectory(dir);
        var done = new TaskCompletionSource();
        Web.CoreWebView2.NavigationCompleted += (_, _) => done.TrySetResult();
        Web.CoreWebView2.Navigate(PanelUrl);
        await Task.WhenAny(done.Task, Task.Delay(30000));
        await Task.Delay(6000);
        using var fs = File.Create(Path.Combine(dir, "terminal-web.png"));
        await Web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);
        Application.Current.Shutdown();
    }
}
