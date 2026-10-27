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
        Title = "Ghost Shell Terminal";
        Loaded += async (_, _) => await StartAsync();
    }

    int loadToken;

    void Fail(string text, bool runtime)
    {
        Log("FAIL: " + text);
        loadToken++;
        Loading.Visibility = Visibility.Collapsed;
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
        Loading.Visibility = Visibility.Visible;
        try
        {
            if (!started)
            {
                Log("init webview");
                var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TIOXSITTerminal", "WebView2");
                // без аппаратного ускорения: на части видеокарт/виртуальных машин окно WebView2 остаётся чёрным
                var opts = new CoreWebView2EnvironmentOptions("--disable-gpu --disable-gpu-compositing");
                var env = await CoreWebView2Environment.CreateAsync(null, data, opts);
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
                cw.ProcessFailed += (_, a) =>
                {
                    Log("process failed: " + a.ProcessFailedKind);
                    Dispatcher.Invoke(() => { try { cw.Reload(); } catch { } });
                };
                cw.NavigationCompleted += async (_, a) =>
                {
                    Log("navigation completed: ok=" + a.IsSuccess + " status=" + a.WebErrorStatus);
                    if (!a.IsSuccess)
                    {
                        if (App.ScreenshotDir == null)
                            Fail("Не удалось открыть админ-панель (" + a.WebErrorStatus + "). Проверь интернет и нажми «Повторить».", false);
                        return;
                    }
                    if (App.ScreenshotDir != null) return;
                    Loading.Visibility = Visibility.Collapsed;
                    Web.Visibility = Visibility.Visible;
                    // самопроверка: если через 4 секунды страница пустая — скажем об этом, а не покажем чёрный экран
                    var token = loadToken;
                    await Task.Delay(4000);
                    if (token != loadToken) return;
                    try
                    {
                        var len = await cw.ExecuteScriptAsync("(document.body&&document.body.innerText||'').trim().length");
                        Log("page text length: " + len);
                        if (int.TryParse(len, out var n) && n < 20)
                            Fail("Панель открылась пустой. Нажми «Повторить». Если не помогает — пришли мне этот текст:\n" + LogTail(), false);
                    }
                    catch (Exception ex) { Log("selfcheck error: " + ex.Message); }
                };
                var seed = SeedScript();
                if (seed.Length > 0) await cw.AddScriptToExecuteOnDocumentCreatedAsync(seed);
                started = true;
                Log("webview ready");
            }
            if (App.ScreenshotDir != null) { Web.Visibility = Visibility.Visible; await Screenshot(App.ScreenshotDir); return; }
            loadToken++;
            Log("navigate");
            Web.CoreWebView2.Navigate(PanelUrl);
            var t = loadToken;
            // если за 30 секунд страница так и не загрузилась — не оставляем пустое окно
            _ = Task.Delay(30000).ContinueWith(_ => Dispatcher.Invoke(() =>
            {
                if (t == loadToken && Loading.Visibility == Visibility.Visible)
                    Fail("Админ-панель не загрузилась за 30 секунд. Проверь интернет и нажми «Повторить».\n" + LogTail(), false);
            }));
        }
        catch (Exception ex) when (App.ScreenshotDir != null)
        {
            try { Directory.CreateDirectory(App.ScreenshotDir); File.WriteAllText(Path.Combine(App.ScreenshotDir, "terminal-error.txt"), ex.ToString()); } catch { }
            Application.Current.Shutdown();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            Fail("Для работы Ghost Shell Terminal нужен компонент Microsoft Edge WebView2. Он бесплатный и ставится за минуту.", true);
        }
        catch (Exception ex)
        {
            Fail("Не получилось открыть админ-панель: " + ex.Message + "\n" + ex.GetType().Name, false);
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
    static readonly string LogFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TIOXSITTerminal", "log.txt");

    static void Log(string m)
    {
        try
        {
            var line = DateTime.Now.ToString("HH:mm:ss ") + m + "\n";
            if (App.ScreenshotDir != null) { Directory.CreateDirectory(App.ScreenshotDir); File.AppendAllText(Path.Combine(App.ScreenshotDir, "terminal-log.txt"), line); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
            if (File.Exists(LogFile) && new FileInfo(LogFile).Length > 100_000) File.Delete(LogFile);
            File.AppendAllText(LogFile, line);
        }
        catch { }
    }

    static string LogTail()
    {
        try { return string.Join("\n", File.ReadAllLines(LogFile).TakeLast(6)); } catch { return ""; }
    }

    async Task Screenshot(string dir)
    {
        Log("screenshot start");
        Directory.CreateDirectory(dir);
        var done = new TaskCompletionSource();
        Web.CoreWebView2.NavigationCompleted += (_, _) => done.TrySetResult();
        Web.CoreWebView2.Navigate(PanelUrl);
        await Task.WhenAny(done.Task, Task.Delay(30000));
        Log("navigated or timeout");
        await Task.Delay(6000);
        using var fs = File.Create(Path.Combine(dir, "terminal-web.png"));
        await Web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);
        Log("captured");
        Application.Current.Shutdown();
    }
}
