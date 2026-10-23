using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BattleLauncher;

namespace TioxsitTerminal;

record Session(Account Me, string DeviceId, bool CanBan)
{
    public bool IsSuper => Me.Role == "superadmin";
    public bool IsAdmin => Me.Role is "admin" or "superadmin";
}

// Оболочка Терминала: вход, левое меню, переключение страниц. Сами страницы — в MainWindow.*.cs.
public partial class MainWindow : Window
{
    AccountStore store = AccountStore.Load();
    Session? session;
    DispatcherTimer? timer;
    readonly DispatcherTimer toastTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    CancellationTokenSource? pairingCts;
    readonly Dictionary<string, Button> navButtons = new();

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
        try
        {
            // в .ico несколько размеров; берём самый крупный
            var dec = new IconBitmapDecoder(new Uri("pack://application:,,,/Assets/icon.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Logo.Source = dec.Frames.OrderByDescending(f => f.PixelWidth).First();
        }
        catch { /* без логотипа тоже работаем */ }
        VersionText.Text = "Версия " + AppVersion;
        toastTimer.Tick += (_, _) => { toastTimer.Stop(); ToastBox.Visibility = Visibility.Collapsed; };
        Loaded += async (_, _) => await StartAsync();
        Closing += (_, _) => { pairingCts?.Cancel(); timer?.Stop(); };
    }

    async Task StartAsync()
    {
        if (App.ScreenshotDir != null) { await RunScreenshots(App.ScreenshotDir); return; }
        await EnterAsync();
    }

    void Toast(string text)
    {
        ToastText.Text = text;
        ToastBox.Visibility = Visibility.Visible;
        toastTimer.Stop();
        toastTimer.Start();
    }

    async Task Safe(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { Toast("Не получилось: " + ex.Message); }
    }

    void Show(UIElement content) => ContentHost.Child = content;

    // ---------- вход ----------
    // Сессия общая с лаунчером (тот же файл %APPDATA%\BattleLauncher\launcher.json): вошёл там — вошёл и здесь.
    async Task EnterAsync()
    {
        timer?.Stop();
        session = null;
        var me = store.Active;
        if (me == null) { ShowSetup("Вход не найден. Войди в TIOXSIT Launcher или подтверди это устройство кодом ниже.", null); return; }
        if (me.IsToken) { ShowSetup("Вход по токену доступа в Терминале пока не поддерживается. Войди под своим аккаунтом.", null); return; }
        try
        {
            var p = await Rtdb.GetProfile(me.Id);
            if (p.Status == "banned") { store.Remove(me.Id); ShowSetup("Аккаунт заблокирован.", null); return; }
            store.Upsert(p);
            me = store.Active!;
        }
        catch (Exception ex)
        {
            ShowSetup("Нет связи с базой: " + ex.Message, "Повторить");
            return;
        }
        if (me.Role is not ("moderator" or "admin" or "superadmin"))
        {
            ShowSetup($"У аккаунта «{me.Display}» нет доступа к Терминалу (роль: {Texts.Role(me.Role)}).", null);
            return;
        }
        var deviceId = store.DeviceId();
        if (me.Role is "admin" or "superadmin")
        {
            bool trusted;
            try { trusted = await Rtdb.IsDeviceTrusted(me.Id, Rtdb.DeviceKeyOf(deviceId)); }
            catch (Exception ex) { ShowSetup("Не удалось проверить устройство: " + ex.Message, "Повторить"); return; }
            if (!trusted)
            {
                ShowSetup($"Это устройство ещё не подтверждено для аккаунта «{me.Display}». Подтверди его кодом через бота: так админ-доступ нельзя украсть вместе с файлом.", null);
                return;
            }
        }
        var canBan = me.Role == "superadmin";
        if (me.Role == "admin")
        {
            try { canBan = (await Db.Get($"users/{me.Id}/permissions/bans.json")).ValueKind == System.Text.Json.JsonValueKind.True; }
            catch { canBan = false; }
        }
        session = new Session(me, deviceId, canBan);
        BuildShell();
        Navigate("players");
    }

    void ShowSetup(string note, string? retryText)
    {
        timer?.Stop();
        Side.Visibility = Visibility.Collapsed;
        SideCol.Width = new GridLength(0);
        var stack = new StackPanel();
        stack.Children.Add(Ui.Text("TIOXSIT Terminal", 24, bold: true));
        stack.Children.Add(new TextBlock { Text = "Админ-панель проекта Тролль-Баттл", Foreground = Ui.Res("Muted"), Margin = new Thickness(0, 2, 0, 14) });
        var noteBlock = Ui.Text(note, 13, "Accent");
        noteBlock.Margin = new Thickness(0, 0, 0, 14);
        stack.Children.Add(noteBlock);

        var codeLabel = new TextBlock { FontSize = 34, FontWeight = FontWeights.ExtraBold, Foreground = Ui.Res("Accent"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 6), Visibility = Visibility.Collapsed };
        var status = Ui.Text("", 13, "Accent");
        status.Visibility = Visibility.Collapsed;
        var error = Ui.Text("", 13, "Red");
        error.Visibility = Visibility.Collapsed;
        var cancel = Ui.Btn("Отменить", "Ghost", () => { pairingCts?.Cancel(); return EnterAsync(); });
        cancel.Visibility = Visibility.Collapsed;
        var start = Ui.Btn("Войти по коду через бота", "Primary", () => Task.CompletedTask, 14);
        start.Margin = new Thickness(0, 0, 8, 8);

        void Fail(string text)
        {
            status.Visibility = Visibility.Collapsed;
            codeLabel.Visibility = Visibility.Collapsed;
            cancel.Visibility = Visibility.Collapsed;
            error.Text = text;
            error.Visibility = Visibility.Visible;
            start.IsEnabled = true;
        }

        start.Click += async (_, _) =>
        {
            pairingCts?.Cancel();
            var cts = pairingCts = new CancellationTokenSource();
            var ct = cts.Token;
            start.IsEnabled = false;
            cancel.Visibility = Visibility.Visible;
            error.Visibility = Visibility.Collapsed;
            string? code = null;
            try
            {
                var deviceKey = Rtdb.DeviceKeyOf(store.DeviceId());
                for (var attempt = 0; ; attempt++)
                {
                    code = Rtdb.RandomPairingCode();
                    try { await Rtdb.CreatePairing(code, deviceKey, ct); break; }
                    catch (InvalidOperationException) when (attempt < 3) { code = null; } // код занят — берём другой
                }
                codeLabel.Text = code;
                codeLabel.Visibility = Visibility.Visible;
                status.Text = "Открой в Telegram бота TerminalBot и отправь ему этот код сообщением — вход произойдёт сам. Код живёт 5 минут.";
                status.Visibility = Visibility.Visible;
                var deadline = DateTime.UtcNow.AddMinutes(5);
                while (DateTime.UtcNow < deadline)
                {
                    await Task.Delay(2000, ct);
                    (string? Status, string? TelegramId) r = (null, null);
                    try { r = await Rtdb.GetPairing(code!, ct); }
                    catch (HttpRequestException) { continue; }
                    if (r.Status == "approved" && r.TelegramId != null)
                    {
                        await Rtdb.RemovePairing(code!);
                        code = null;
                        var profile = await Rtdb.GetProfile(r.TelegramId, ct);
                        if (profile.Status == "banned") { Fail("Аккаунт заблокирован."); return; }
                        store.Upsert(profile, paired: true);
                        await EnterAsync();
                        return;
                    }
                    if (r.Status == "denied") { await Rtdb.RemovePairing(code!); code = null; Fail("Отклонено."); return; }
                    if (r.Status == null) { code = null; Fail("Код пропал — попробуй ещё раз."); return; }
                }
                Fail("Время ожидания истекло, попробуй ещё раз.");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Fail("Не удалось создать код: " + ex.Message); }
            finally { if (code != null) await Rtdb.RemovePairing(code); }
        };

        var fromLauncher = Ui.Btn(retryText ?? "Взять вход из лаунчера", "Ghost", async () => { store = AccountStore.Load(); await EnterAsync(); }, 14);
        fromLauncher.Margin = new Thickness(0, 0, 8, 8);
        stack.Children.Add(Ui.Wrap(start, fromLauncher));
        stack.Children.Add(codeLabel);
        stack.Children.Add(status);
        stack.Children.Add(error);
        stack.Children.Add(Ui.Wrap(cancel));

        var card = Ui.Card(stack);
        card.Width = 520;
        card.VerticalAlignment = VerticalAlignment.Center;
        card.HorizontalAlignment = HorizontalAlignment.Center;
        Show(card);
    }

    void Logout_Click(object sender, RoutedEventArgs e)
    {
        if (store.ActiveId != null) store.Remove(store.ActiveId);
        _ = EnterAsync();
    }

    // ---------- меню и страницы ----------
    void BuildShell()
    {
        var s = session!;
        Side.Visibility = Visibility.Visible;
        SideCol.Width = new GridLength(220);
        MeName.Text = s.Me.Display;
        MeRole.Text = Texts.Role(s.Me.Role);
        NavPanel.Children.Clear();
        navButtons.Clear();
        var items = new (string Key, string Title, bool Show)[]
        {
            ("players", "Игроки", true),
            ("journal", "Журнал", true),
            ("requests", "Заявки на вход", true),
            ("live", "Эфир", true),
            ("stats", "Статистика", true),
            ("staff", "Администрация", s.IsSuper),
            ("system", "Система", s.IsSuper),
        };
        foreach (var it in items.Where(i => i.Show))
        {
            var key = it.Key;
            var b = new Button { Content = it.Title, Style = (Style)FindResource("Nav"), Margin = new Thickness(0, 0, 0, 2) };
            b.Click += (_, _) => Navigate(key);
            navButtons[key] = b;
            NavPanel.Children.Add(b);
        }
    }

    void Navigate(string key)
    {
        timer?.Stop();
        timer = null;
        foreach (var kv in navButtons) kv.Value.Foreground = Ui.Res(kv.Key == key ? "Text" : "Muted");
        switch (key)
        {
            case "players": ShowPlayers(null); break;
            case "journal": ShowJournal(); break;
            case "requests": ShowRequests(); break;
            case "live": ShowLive(); break;
            case "stats": ShowStats(); break;
            case "staff": ShowStaff(); break;
            case "system": ShowSystem(); break;
        }
    }

    // Страница-заглушка «Загружаю…», которую потом заменяет настоящая
    UIElement Loading(string title)
    {
        var (root, body) = Ui.Page(title);
        body.Children.Add(Ui.Muted("Загружаю…"));
        return root;
    }
}
