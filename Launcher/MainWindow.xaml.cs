using System.Net.Http;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BattleLauncher;

// Всё, что приходит из базы или GitHub, выводится только как текст (TextBlock.Text) — база открыта
// на запись, и ничего, что можно было бы «исполнить», в интерфейс не попадает.
public partial class MainWindow : Window
{
    const int RequestTtlSeconds = 300; // как в игре и админ-панели
    readonly AccountStore store = AccountStore.Load();
    CancellationTokenSource? loginCts;

    class AppInfo
    {
        public required AppDef Def;
        public bool Allowed;
        public InstalledApp? Installed;
        public string? Latest;
        public bool UpdateAvailable;
        public string? Error;
        public NativeInfo? Native;          // установленная EXE-версия (если есть)
        public NativeRelease? NativeLatest; // EXE-версия в последнем релизе лаунчера
        public bool NativeUpdate;
    }

    List<AppInfo> apps = new();
    readonly Dictionary<string, (bool Installing, int? Percent)> busy = new();
    UpdateInfo? pendingUpdate;
    readonly DispatcherTimer toastTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    public MainWindow()
    {
        InitializeComponent();
        loginTimer.Tick += (_, _) => LoginTick();
        toastTimer.Tick += (_, _) => { toastTimer.Stop(); ToastBox.Visibility = Visibility.Collapsed; };
        VersionText.Text = "Версия " + SelfUpdate.CurrentVersion;
        Loaded += async (_, _) => await StartAsync();
        Closing += (_, _) => loginCts?.Cancel();
    }

    async Task StartAsync()
    {
        if (App.ScreenshotDir != null) { await ScreenshotRun(App.ScreenshotDir); return; }
        await RefreshActiveProfile();
        Render();
        _ = CheckSelfUpdateLoop();
        _ = CheckAppUpdatesLoop();
    }

    // ---------- аккаунты ----------
    // Перечитывает текущую сессию из базы: роль могла смениться, аккаунт — заблокироваться, токен — отозваться,
    // устройство — быть отозвано владельцем. «Нет связи» — не повод разлогинивать: остаются сохранённые данные.
    async Task RefreshActiveProfile()
    {
        var acc = store.Active;
        if (acc == null) return;
        try
        {
            if (acc.IsToken)
            {
                var token = Secrets.Unprotect(acc.TokenProtected);
                var info = token == null ? null : await Rtdb.GetAccessToken(token);
                if (token == null || info == null || (info.ExpiresAt is long exp && exp <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
                {
                    store.Remove(acc.Id);
                    sessionNote = "Токен доступа отозван или истёк.";
                    return;
                }
                store.UpsertToken(token, info.Label);
                return;
            }
            var p = await Rtdb.GetProfile(acc.Id);
            if (p.Status == "banned") { store.Remove(acc.Id); sessionNote = "Аккаунт заблокирован."; return; }
            if (acc.Paired && !await Rtdb.IsDeviceTrusted(acc.Id, Rtdb.DeviceKeyOf(store.DeviceId())))
            {
                store.Remove(acc.Id);
                sessionNote = "Владелец отозвал доступ этому устройству — войди заново.";
                return;
            }
            store.Upsert(p);
        }
        catch { /* нет сети — остаются сохранённые данные */ }
    }

    string? sessionNote; // почему нас выбросило на экран входа — показываем один раз

    void Render()
    {
        var me = store.Active;
        LoginView.Visibility = me == null ? Visibility.Visible : Visibility.Collapsed;
        MainView.Visibility = me == null ? Visibility.Collapsed : Visibility.Visible;
        if (me == null)
        {
            RenderLogin();
            if (sessionNote != null) { LoginError.Text = sessionNote; LoginError.Visibility = Visibility.Visible; sessionNote = null; }
            return;
        }
        AppsSub.Text = $"Ты вошёл как {me.Display} · {me.RoleName}\nОблегчённая версия — приложение в отдельном окне (Electron). Desktop-версия (EXE) — настоящая программа; кнопка включится сама, как только версия выйдет.";
        MeBox.Children.Clear();
        MeBox.Children.Add(Avatar(me, 52));
        var info = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = me.Display, FontWeight = FontWeights.SemiBold, FontSize = 16 });
        info.Children.Add(new TextBlock { Text = me.RoleName + (me.PlayerId.Length > 0 ? " · " + me.PlayerId : ""), Foreground = Res("Muted") });
        MeBox.Children.Add(info);
        FillAccountList(AllAccounts, removableExceptActive: true);
        ShowView("apps");
    }

    void RenderLogin()
    {
        SavedAccounts.Children.Clear();
        FillAccountList(SavedAccounts, removableExceptActive: false);
    }

    void FillAccountList(Panel target, bool removableExceptActive)
    {
        target.Children.Clear();
        foreach (var a in store.Accounts)
        {
            var acc = a;
            var row = new Border
            {
                Background = Res("Panel2"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 8),
                BorderThickness = new Thickness(1), BorderBrush = acc.Id == store.ActiveId ? Res("Accent") : Res("Line"), Cursor = Cursors.Hand,
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var av = Avatar(acc, 34);
            Grid.SetColumn(av, 0);
            var meta = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            meta.Children.Add(new TextBlock { Text = acc.Display, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            meta.Children.Add(new TextBlock { Text = acc.RoleName + (acc.PlayerId.Length > 0 ? " · " + acc.PlayerId : ""), Foreground = Res("Muted"), FontSize = 12 });
            Grid.SetColumn(meta, 1);
            grid.Children.Add(av);
            grid.Children.Add(meta);
            if (!removableExceptActive || acc.Id != store.ActiveId)
            {
                var x = new Button { Content = "×", Style = (Style)FindResource("Ghost"), BorderThickness = new Thickness(0), Padding = new Thickness(8, 0, 8, 0), ToolTip = "Убрать из списка" };
                x.Click += (_, e) => { e.Handled = true; store.Remove(acc.Id); Render(); };
                Grid.SetColumn(x, 2);
                grid.Children.Add(x);
            }
            row.Child = grid;
            row.MouseLeftButtonUp += async (_, _) =>
            {
                if (acc.Id == store.ActiveId) return;
                store.ActiveId = acc.Id; store.Save();
                await RefreshActiveProfile();
                Render();
            };
            target.Children.Add(row);
        }
    }

    static Border Avatar(Account a, double size) => new()
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = Res("Accent"),
        Child = new TextBlock { Text = a.Initials, Foreground = Res("OnAccent"), FontWeight = FontWeights.Bold, FontSize = size * 0.4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
    };

    static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    // ---------- вход ----------
    // Три способа, как в админ-панели (Telegram-вход есть только внутри самого Telegram, на компьютере его нет):
    // заявка по почте, токен доступа, код устройства. За раз идёт один.
    readonly DispatcherTimer loginTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    DateTime loginDeadline;

    void SetLoginBusy(bool on, bool cancellable = true)
    {
        LoginBtn.IsEnabled = TokenBtn.IsEnabled = PairBtn.IsEnabled = !on;
        EmailBox.IsEnabled = TokenBox.IsEnabled = !on;
        LoginCancelBtn.Visibility = on && cancellable ? Visibility.Visible : Visibility.Collapsed;
        if (on)
        {
            // сразу, как нажали кнопку: плашка со «крутилкой» наверху, чтобы было видно, что процесс пошёл
            LoginStatus.Text = "Отправляю запрос…";
            LoginCountdown.Text = "";
            LoginBusyBox.Visibility = Visibility.Visible;
            LoginView.ScrollToTop();
        }
        else
        {
            PairCode.Visibility = Visibility.Collapsed;
            LoginBusyBox.Visibility = Visibility.Collapsed;
            loginTimer.Stop();
        }
    }

    void LoginTick()
    {
        var left = loginDeadline - DateTime.UtcNow;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        LoginCountdown.Text = $"Ждём подтверждения · осталось {(int)left.TotalMinutes}:{left.Seconds:00}";
    }

    void LoginFail(string text)
    {
        LoginBusyBox.Visibility = Visibility.Collapsed;
        loginTimer.Stop();
        LoginError.Text = text;
        LoginError.Visibility = Visibility.Visible;
        SetLoginBusy(false);
    }

    void LoginInfo(string text)
    {
        LoginError.Visibility = Visibility.Collapsed;
        LoginStatus.Text = text;
        LoginBusyBox.Visibility = Visibility.Visible;
        LoginView.ScrollToTop();
        loginDeadline = DateTime.UtcNow.AddSeconds(RequestTtlSeconds);
        LoginTick();
        loginTimer.Start();
    }

    void LoginDone()
    {
        EmailBox.Text = TokenBox.Text = "";
        LoginBusyBox.Visibility = Visibility.Collapsed;
        loginTimer.Stop();
        SetLoginBusy(false);
        Render();
    }

    CancellationToken StartLogin()
    {
        LoginError.Visibility = Visibility.Collapsed;
        LoginBusyBox.Visibility = Visibility.Collapsed;
        loginTimer.Stop();
        loginCts?.Cancel();
        loginCts = new CancellationTokenSource();
        return loginCts.Token;
    }

    void EmailBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && LoginBtn.IsEnabled) LoginBtn_Click(sender, e); }
    void TokenBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && TokenBtn.IsEnabled) TokenBtn_Click(sender, e); }

    // --- 1. заявка по почте ---
    async void LoginBtn_Click(object sender, RoutedEventArgs e)
    {
        var email = EmailBox.Text.Trim();
        if (!Rtdb.IsValidEmail(email)) { LoginFail("Впиши email целиком."); return; }
        var ct = StartLogin();
        SetLoginBusy(true);
        string? targetId = null, requestId = null;
        try
        {
            targetId = await Rtdb.FindAccountByEmail(email, ct);
            if (targetId == null) { LoginFail("Эта почта не привязана ни к одному аккаунту. Привяжи её в игре: Настройки → Аккаунт."); return; }
            requestId = await Rtdb.CreateLoginRequest(targetId, ct);
            LoginInfo("⏳ Заявка отправлена. Открой в Telegram бота TerminalBot и нажми «Разрешить» в его сообщении (можно и в игре: Сообщество → Запросы, или в админ-панели: Аккаунт). Ждём 5 минут.");

            var deadline = DateTime.UtcNow.AddSeconds(RequestTtlSeconds);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(2000, ct);
                string? status = null;
                try { status = await Rtdb.GetLoginRequestStatus(targetId, requestId, ct); }
                catch (HttpRequestException) { /* сеть моргнула — спросим снова */ }
                if (status == "approved")
                {
                    await Rtdb.RemoveLoginRequest(targetId, requestId); // заявка одноразовая — убираем за собой
                    requestId = null;
                    var profile = await Rtdb.GetProfile(targetId, ct);
                    if (profile.Status == "banned") { LoginFail("Аккаунт заблокирован."); return; }
                    store.Upsert(profile, paired: false);
                    LoginDone();
                    return;
                }
                if (status == "denied")
                {
                    await Rtdb.RemoveLoginRequest(targetId, requestId);
                    requestId = null;
                    LoginFail("Вход отклонён на другом устройстве.");
                    return;
                }
            }
            LoginFail("Время ожидания истекло, попробуй ещё раз.");
        }
        catch (OperationCanceledException) { /* нажали «Отменить» или закрыли окно */ }
        catch (Exception ex) { LoginFail("Не удалось отправить заявку: " + ex.Message); }
        finally
        {
            // заявка, которую никто не подтвердил, не должна висеть в базе
            if (targetId != null && requestId != null) await Rtdb.RemoveLoginRequest(targetId, requestId);
        }
    }

    // --- 2. токен доступа ---
    async void TokenBtn_Click(object sender, RoutedEventArgs e)
    {
        var token = TokenBox.Text.Trim();
        if (!Net.SafeId.IsMatch(token)) { LoginFail("Впиши токен целиком."); return; }
        var ct = StartLogin();
        SetLoginBusy(true, cancellable: false);
        try
        {
            var info = await Rtdb.GetAccessToken(token, ct);
            if (info == null) { LoginFail("Токен не найден или уже отозван Супердоступом."); return; }
            if (info.ExpiresAt is long exp && exp <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) { LoginFail("Срок действия токена истёк — попроси у Супердоступа новый."); return; }
            store.UpsertToken(token, info.Label);
            LoginDone();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { LoginFail("Не удалось проверить токен: " + ex.Message); }
    }

    // --- 3. код устройства ---
    async void PairBtn_Click(object sender, RoutedEventArgs e)
    {
        var ct = StartLogin();
        SetLoginBusy(true);
        string? code = null;
        try
        {
            var deviceKey = Rtdb.DeviceKeyOf(store.DeviceId());
            for (var attempt = 0; ; attempt++)
            {
                code = Rtdb.RandomPairingCode();
                try { await Rtdb.CreatePairing(code, deviceKey, ct); break; }
                catch (InvalidOperationException) when (attempt < 3) { code = null; } // код уже занят — берём другой
            }
            PairCode.Text = code;
            PairCode.Visibility = Visibility.Visible;
            LoginInfo("Открой в Telegram бота TerminalBot и отправь ему этот код сообщением — вход произойдёт сам. Код живёт 5 минут.");

            var deadline = DateTime.UtcNow.AddSeconds(RequestTtlSeconds);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(2000, ct);
                (string? Status, string? TelegramId) r = (null, null);
                try { r = await Rtdb.GetPairing(code, ct); }
                catch (HttpRequestException) { continue; }
                if (r.Status == "approved" && r.TelegramId != null)
                {
                    await Rtdb.RemovePairing(code);
                    code = null;
                    var profile = await Rtdb.GetProfile(r.TelegramId, ct);
                    if (profile.Status == "banned") { LoginFail("Аккаунт заблокирован."); return; }
                    store.Upsert(profile, paired: true);
                    LoginDone();
                    return;
                }
                if (r.Status == "denied") { await Rtdb.RemovePairing(code); code = null; LoginFail("Отклонено."); return; }
                if (r.Status == null) { code = null; LoginFail("Код пропал — попробуй ещё раз."); return; }
            }
            LoginFail("Время ожидания истекло, попробуй ещё раз.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { LoginFail("Не удалось создать код: " + ex.Message); }
        finally
        {
            if (code != null) await Rtdb.RemovePairing(code);
        }
    }

    void LoginCancelBtn_Click(object sender, RoutedEventArgs e)
    {
        loginCts?.Cancel();
        SetLoginBusy(false);
        LoginBusyBox.Visibility = Visibility.Collapsed;
    }

    // ---------- навигация ----------
    string currentView = "apps";

    void ShowView(string name)
    {
        currentView = name;
        AppsView.Visibility = name == "apps" ? Visibility.Visible : Visibility.Collapsed;
        PluginsView.Visibility = name == "plugins" ? Visibility.Visible : Visibility.Collapsed;
        AccountView.Visibility = name == "account" ? Visibility.Visible : Visibility.Collapsed;
        NavApps.Foreground = name == "apps" ? Res("Text") : Res("Muted");
        NavPlugins.Foreground = name == "plugins" ? Res("Text") : Res("Muted");
        NavAccount.Foreground = name == "account" ? Res("Text") : Res("Muted");
        if (name == "apps") _ = LoadApps(false);
        if (name == "plugins") RenderPlugins();
    }

    // ---------- плагины ----------
    void RenderPlugins()
    {
        PluginList.Children.Clear();
        PluginList.Children.Add(TaskbarPlusCard());
    }

    // Taskbar Plus — настройки панели задач Windows (Services/TaskbarPlus.cs).
    UIElement TaskbarPlusCard()
    {
        var card = new Border { Background = Res("Panel"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(22), Margin = new Thickness(0, 0, 0, 14) };
        var col = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new TextBlock { Text = "Taskbar Plus", FontSize = 17, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var badge = Badge("Плагин · Windows " + (TaskbarPlus.IsWindows11 ? "11" : "10"), Res("Muted"), null);
        badge.Margin = new Thickness(10, 0, 0, 0);
        badge.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(badge);
        col.Children.Add(head);
        col.Children.Add(new TextBlock
        {
            Text = "Настройки панели задач Windows: где стоят значки, поиск, размер, группировка кнопок, секунды в часах, автоскрытие. " +
                   "Меняются обычные параметры вашей учётной записи, права администратора не нужны. Изменения применяются сразу; " +
                   "«Вернуть как было» отменяет всё, что вы поменяли здесь.",
            Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 14),
        });

        foreach (var o in TaskbarPlus.Options.Where(TaskbarPlus.Available))
        {
            var opt = o;
            var current = TaskbarPlus.Current(opt);
            col.Children.Add(TaskbarRow(opt.Title, opt.Hint, opt.Labels, Array.IndexOf(opt.Values, current), i =>
            {
                var err = TaskbarPlus.Set(opt, opt.Values[i]);
                Toast(err ?? "Применено: " + opt.Title + " — " + opt.Labels[i]);
                RenderPlugins();
            }));
        }
        col.Children.Add(TaskbarRow("Автоскрытие панели", "Панель прячется, пока курсор не у края экрана", new[] { "Выкл", "Вкл" }, TaskbarPlus.AutoHide ? 1 : 0, i =>
        {
            var err = TaskbarPlus.SetAutoHide(i == 1);
            Toast(err ?? (i == 1 ? "Автоскрытие включено" : "Автоскрытие выключено"));
            RenderPlugins();
        }));

        col.Children.Add(new TextBlock
        {
            Text = "Если размер, группировка или секунды не поменялись сразу, перезапустите панель задач.",
            Foreground = Res("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 8),
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var restart = MakeButton("Перезапустить панель задач", null, () =>
        {
            if (MessageBox.Show("Панель задач и окна Проводника закроются на пару секунд и откроются снова. Продолжить?", "Ghost Shell Launcher", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return Task.CompletedTask;
            var err = TaskbarPlus.RestartExplorer();
            Toast(err ?? "Панель задач перезапущена");
            return Task.CompletedTask;
        });
        restart.Margin = new Thickness(0);
        buttons.Children.Add(restart);
        buttons.Children.Add(MakeButton("Вернуть как было", "Danger", () =>
        {
            var (restored, error) = TaskbarPlus.Reset();
            Toast(error != null ? "Вернулось не всё: " + error : restored == 0 ? "Вы ещё ничего не меняли" : $"Возвращено параметров: {restored}");
            RenderPlugins();
            return Task.CompletedTask;
        }));
        col.Children.Add(buttons);
        card.Child = col;
        return card;
    }

    // Строка настройки: слева название и подсказка, справа переключатель из нескольких кнопок (выбранная подсвечена).
    UIElement TaskbarRow(string title, string hint, string[] labels, int selected, Action<int> choose)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = hint, Foreground = Res("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap });
        row.Children.Add(text);
        var seg = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(seg, 1);
        for (var i = 0; i < labels.Length; i++)
        {
            var idx = i;
            var b = MakeButton(labels[i], i == selected ? "Primary" : null, () => { if (idx != selected) choose(idx); return Task.CompletedTask; });
            b.Padding = new Thickness(10, 5, 10, 5);
            b.Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0);
            seg.Children.Add(b);
        }
        row.Children.Add(seg);
        return row;
    }

    void NavApps_Click(object sender, RoutedEventArgs e) => ShowView("apps");
    void NavPlugins_Click(object sender, RoutedEventArgs e) => ShowView("plugins");
    void NavAccount_Click(object sender, RoutedEventArgs e) => ShowView("account");

    void AddAccount_Click(object sender, RoutedEventArgs e) { store.ActiveId = null; store.Save(); Render(); }

    void Logout_Click(object sender, RoutedEventArgs e)
    {
        if (store.ActiveId != null) store.Remove(store.ActiveId);
        Render();
    }

    void Toast(string text)
    {
        ToastText.Text = text;
        ToastBox.Visibility = Visibility.Visible;
        toastTimer.Stop();
        toastTimer.Start();
    }

    // ---------- приложения ----------
    async void RefreshBtn_Click(object sender, RoutedEventArgs e) => await LoadApps(true);

    async Task LoadApps(bool force)
    {
        if (App.ScreenshotDir != null) return; // в режиме скриншотов список приложений задан вручную
        var me = store.Active;
        if (me == null) return;
        RefreshBtn.IsEnabled = false;
        await Catalog.Refresh(force); // список приложений и их версий читается из catalog.json — без обновления лаунчера
        var list = await Task.WhenAll(Catalog.Apps.Select(def => Describe(def, me.Role, force)));
        apps = list.ToList();
        RefreshBtn.IsEnabled = true;
        RenderApps();
    }

    static async Task<AppInfo> Describe(AppDef def, string role, bool force)
    {
        var info = new AppInfo { Def = def, Allowed = def.IsAllowed(role) };
        info.Installed = await Task.Run(() => Installer.FindInstalled(def.ProductName));
        try { info.Latest = (await Releases.Latest(def.Repo, force)).Version; }
        catch { info.Error = "Не удалось узнать последнюю версию (нет связи с GitHub?)"; }
        if (info.Installed?.Version != null && info.Latest != null)
            info.UpdateAvailable = Net.CompareVersions(info.Latest, info.Installed.Version) > 0;
        if (def.NativeExe != null)
        {
            info.Native = await Task.Run(() => NativeInstaller.Find(def));
            try { info.NativeLatest = await NativeInstaller.Latest(def, force); } catch { /* нет связи — кнопка просто не включится */ }
            info.NativeUpdate = info.Native != null && info.NativeLatest != null && info.Native.Sha != info.NativeLatest.Sha;
        }
        return info;
    }

    void RenderApps()
    {
        AppList.Children.Clear();
        foreach (var a in apps) AppList.Children.Add(AppCard(a));
        var downloads = Catalog.Downloads.Where(d => d.Os.Contains("windows")).ToArray();
        if (downloads.Length > 0)
        {
            AppList.Children.Add(new TextBlock { Text = "ДОПОЛНИТЕЛЬНО", FontFamily = (FontFamily)FindResource("Mono"), FontWeight = FontWeights.Bold, Foreground = Res("Accent2"), Margin = new Thickness(0, 14, 0, 4) });
            AppList.Children.Add(new TextBlock { Text = "Эти программы лаунчер сам не ставит — он даёт файл для скачивания.", Foreground = Res("Muted"), Margin = new Thickness(0, 0, 0, 10) });
            foreach (var d in downloads) AppList.Children.Add(DownloadCard(d));
        }
    }

    UIElement DownloadCard(DownloadDef d)
    {
        var card = new Border { Background = Res("Panel"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(22), Margin = new Thickness(0, 0, 0, 14) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        left.Children.Add(new TextBlock { Text = d.Name, FontSize = 17, FontWeight = FontWeights.SemiBold });
        left.Children.Add(new TextBlock { Text = d.Desc, Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 12, 8) });
        left.Children.Add(Badge(d.Platform, Res("Muted"), null));
        grid.Children.Add(left);
        var btn = MakeButton("Скачать", "Primary", () =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(d.Url) { UseShellExecute = true }); }
            catch { Toast("Не удалось открыть браузер"); }
            return Task.CompletedTask;
        });
        btn.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(btn, 1);
        grid.Children.Add(btn);
        card.Child = grid;
        return card;
    }

    UIElement AppCard(AppInfo a)
    {
        var card = new Border { Background = Res("Panel"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(22), Margin = new Thickness(0, 0, 0, 14) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();
        left.Children.Add(new TextBlock { Text = a.Def.Name, FontSize = 17, FontWeight = FontWeights.SemiBold });
        left.Children.Add(new TextBlock { Text = a.Def.Desc, Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 12, 8) });
        var badges = new StackPanel { Orientation = Orientation.Horizontal };
        if (!a.Allowed) badges.Children.Add(Badge("Нет доступа для твоей роли", Res("Muted"), null));
        else
        {
            badges.Children.Add(a.Installed != null
                ? Badge("Установлено" + (a.Installed.Version != null ? " " + a.Installed.Version : "") + " (облегчённая)", Res("Green"), null)
                : Badge("Не установлено", Res("Muted"), null));
            if (a.Latest != null)
                badges.Children.Add(a.UpdateAvailable ? Badge("Доступно " + a.Latest, Res("OnAccent"), Res("Accent")) : Badge("Последняя " + a.Latest, Res("Muted"), null));
        }
        if (a.Allowed && a.Native != null)
            badges.Children.Add(Badge("Desktop установлен " + a.Native.Version + (a.NativeUpdate ? " · есть обновление" : ""), Res("Green"), null));
        left.Children.Add(badges);
        if (a.Error != null) left.Children.Add(new TextBlock { Text = a.Error, Foreground = Res("Red"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
        grid.Children.Add(left);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(actions, 1);
        if (busy.TryGetValue(a.Def.Id, out var b))
        {
            actions.Children.Add(new TextBlock { Text = b.Installing ? "Работаю…" : $"Скачиваю {b.Percent ?? 0}%", Foreground = Res("Muted"), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            actions.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = b.Percent ?? 0, IsIndeterminate = b.Installing });
        }
        else if (a.Allowed)
        {
            if (a.Installed != null)
            {
                actions.Children.Add(MakeButton("Запустить", "Primary", async () => await Launch(a)));
                if (a.UpdateAvailable) actions.Children.Add(MakeButton("Обновить", null, async () => await InstallApp(a, "Обновлено")));
                actions.Children.Add(MakeButton("Удалить", "Danger", async () => await Uninstall(a)));
            }
            else
            {
                var install = MakeButton("Скачать облегчённую версию", "Primary", async () => await InstallApp(a, "Установлено"));
                install.IsEnabled = a.Latest != null;
                actions.Children.Add(install);
            }
        }
        // Справа: кнопки браузерной версии, под ними — кнопки EXE-версии (настоящая программа без браузера).
        // EXE-версия пока есть только у админ-панели («Терминал»); у остальных кнопка заблокирована.
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(right, 1);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        right.Children.Add(actions);
        if (a.Allowed)
        {
            var exeRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var exeKey = a.Def.Id + ":exe";
            Button Small(string text, string? style, Func<Task> click)
            {
                var b2 = MakeButton(text, style, click);
                b2.FontSize = 12;
                b2.Padding = new Thickness(10, 5, 10, 5);
                return b2;
            }
            if (a.Def.NativeExe == null)
            {
                var locked = Small("🔒 Скачать Desktop-версию (EXE)", "Ghost", () => Task.CompletedTask);
                locked.IsEnabled = false;
                locked.ToolTip = "Desktop-версии этого приложения нет в каталоге.";
                ToolTipService.SetShowOnDisabled(locked, true);
                exeRow.Children.Add(locked);
            }
            else if (busy.TryGetValue(exeKey, out var eb))
            {
                exeRow.Children.Add(new TextBlock { Text = eb.Installing ? "Работаю…" : $"Скачиваю Desktop {eb.Percent ?? 0}%", Foreground = Res("Muted"), FontSize = 12, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
                exeRow.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = eb.Percent ?? 0, IsIndeterminate = eb.Installing });
            }
            else if (a.Native != null)
            {
                exeRow.Children.Add(Small("Запустить Desktop", "Primary", () => LaunchNative(a)));
                if (a.NativeUpdate) exeRow.Children.Add(Small("Обновить Desktop", null, () => InstallNative(a, "Обновлено")));
                exeRow.Children.Add(Small("Удалить Desktop", "Danger", () => UninstallNative(a)));
            }
            else
            {
                var dl = Small("Скачать Desktop-версию (EXE)", null, () => InstallNative(a, "Установлено"));
                dl.IsEnabled = a.NativeLatest != null;
                if (a.NativeLatest == null)
                {
                    dl.ToolTip = "Desktop-версия ещё не вышла (или нет связи с GitHub). Кнопка включится сама, когда она появится — обновлять лаунчер не нужно.";
                    ToolTipService.SetShowOnDisabled(dl, true);
                }
                exeRow.Children.Add(dl);
            }
            right.Children.Add(exeRow);
        }
        grid.Children.Add(right);
        card.Child = grid;
        return card;
    }

    static Border Badge(string text, Brush fg, Brush? bg) => new()
    {
        Background = bg ?? Res("Panel2"), CornerRadius = new CornerRadius(99), Padding = new Thickness(9, 2, 9, 3), Margin = new Thickness(0, 0, 6, 0),
        Child = new TextBlock { Text = text, FontSize = 12, Foreground = fg },
    };

    Button MakeButton(string text, string? style, Func<Task> click)
    {
        var b = new Button { Content = text, Margin = new Thickness(8, 0, 0, 0) };
        if (style != null) b.Style = (Style)FindResource(style);
        b.Click += async (_, _) => await click();
        return b;
    }

    // Роль проверяем заново прямо перед действием: сохранённая в файле могла устареть.
    async Task<bool> RequireAllowed(AppDef def)
    {
        await RefreshActiveProfile();
        var me = store.Active;
        if (me == null) { Render(); Toast("Аккаунт больше недоступен — войди заново."); return false; }
        if (!def.IsAllowed(me.Role)) { Toast("Для этого приложения нет доступа у твоего аккаунта."); await LoadApps(false); return false; }
        return true;
    }

    async Task InstallApp(AppInfo a, string okText)
    {
        var id = a.Def.Id;
        if (busy.ContainsKey(id)) return;
        busy[id] = (false, 0);
        RenderApps();
        try
        {
            if (!await RequireAllowed(a.Def)) return;
            var release = await Releases.Latest(a.Def.Repo, true);
            var progress = new Progress<int?>(p => { busy[id] = p == null ? (true, null) : (false, p); RenderApps(); });
            await Installer.Install(a.Def, release, progress);
            Toast($"{okText}: {a.Def.Name}");
        }
        catch (Exception ex) { Toast("Не получилось: " + ex.Message); }
        finally { busy.Remove(id); await LoadApps(true); }
    }

    async Task Uninstall(AppInfo a)
    {
        var id = a.Def.Id;
        if (busy.ContainsKey(id) || a.Installed == null) return;
        if (MessageBox.Show(this, $"Удалить «{a.Def.Name}» с этого компьютера?", "Удаление", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        busy[id] = (true, null);
        RenderApps();
        try
        {
            await Installer.Uninstall(a.Installed);
            Toast($"Удалено: {a.Def.Name}");
        }
        catch (Exception ex) { Toast("Не получилось: " + ex.Message); }
        finally { busy.Remove(id); await LoadApps(true); }
    }

    // ---------- EXE-версия ----------
    async Task InstallNative(AppInfo a, string okText)
    {
        var key = a.Def.Id + ":exe";
        if (busy.ContainsKey(key)) return;
        busy[key] = (false, 0);
        RenderApps();
        try
        {
            if (!await RequireAllowed(a.Def)) return;
            var rel = await NativeInstaller.Latest(a.Def, true) ?? throw new InvalidOperationException("EXE-версии пока нет в последнем релизе");
            var progress = new Progress<int?>(p => { busy[key] = p == null ? (true, null) : (false, p); RenderApps(); });
            await NativeInstaller.Install(a.Def, rel, progress);
            Toast($"{okText}: {a.Def.NativeName}");
        }
        catch (Exception ex) { Toast("Не получилось: " + ex.Message); }
        finally { busy.Remove(key); await LoadApps(true); }
    }

    async Task UninstallNative(AppInfo a)
    {
        var key = a.Def.Id + ":exe";
        if (busy.ContainsKey(key) || a.Native == null) return;
        if (MessageBox.Show(this, $"Удалить «{a.Def.NativeName}» с этого компьютера?", "Удаление", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            await Task.Run(() => NativeInstaller.Uninstall(a.Def));
            Toast($"Удалено: {a.Def.NativeName}");
        }
        catch (Exception ex) { Toast("Не получилось: " + ex.Message); }
        await LoadApps(true);
    }

    async Task LaunchNative(AppInfo a)
    {
        try
        {
            if (!await RequireAllowed(a.Def)) return;
            var found = await Task.Run(() => NativeInstaller.Find(a.Def));
            if (found == null) { Toast("EXE-версия не установлена"); await LoadApps(true); return; }
            NativeInstaller.Launch(found);
        }
        catch (Exception ex) { Toast("Не получилось запустить: " + ex.Message); }
    }

    async Task Launch(AppInfo a)
    {
        try
        {
            if (!await RequireAllowed(a.Def)) return;
            var found = await Task.Run(() => Installer.FindInstalled(a.Def.ProductName));
            if (found == null) { Toast("Приложение не установлено"); await LoadApps(true); return; }
            Installer.Launch(found.Exe);
        }
        catch (Exception ex) { Toast("Не получилось запустить: " + ex.Message); }
    }

    // ---------- служебный режим: скриншоты для Microsoft Store ----------
    // Показывает настоящие окна лаунчера с выдуманными данными и сохраняет их в PNG. Сети не касается.
    async Task ScreenshotRun(string dir)
    {
        Directory.CreateDirectory(dir);
        Width = 1616; Height = 939;
        await Capture(Path.Combine(dir, "launcher-1-login.png"), () =>
        {
            store.Accounts.Clear(); store.ActiveId = null; Render();
            LoginView.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            ((FrameworkElement)LoginView.Content).LayoutTransform = new ScaleTransform(0.82, 0.82);
        });
        await Capture(Path.Combine(dir, "launcher-2-apps.png"), () =>
        {
            store.Upsert(new Profile("100200300", "Alex", "superadmin", "active", "PLR-7K3M9Q"));
            apps = new List<AppInfo>
            {
                new() { Def = Catalog.Apps[0], Allowed = true, Installed = new InstalledApp("1.0.4", "demo.exe", null), Latest = "1.0.5", UpdateAvailable = true },
                new() { Def = Catalog.Apps[1], Allowed = true, Latest = "1.0.6", NativeLatest = new NativeRelease("1.6.0", "", "") },
                new() { Def = Catalog.Apps[2], Allowed = true, Latest = "1.0.2" },
            };
            Render();
            RenderApps();
        });
        Application.Current.Shutdown();
    }

    async Task Capture(string path, Action prepare)
    {
        prepare();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        await Task.Delay(500);
        // Снимаем окно в натуральном размере, но с удвоенным разрешением (k = 2): так картинка получается чёткой
        // и без растяжения, а размер окна на экране сборочной машины не важен.
        UpdateLayout();
        var content = (FrameworkElement)Content;
        double w = content.ActualWidth, h = content.ActualHeight;
        const double k = 2;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));
            // Viewbox задан явно: иначе кисть растягивает «видимое содержимое» на весь прямоугольник
            dc.DrawRectangle(new VisualBrush(content) { Viewbox = new Rect(0, 0, w, h), ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill }, null, new Rect(0, 0, w, h));
        }
        var bmp = new RenderTargetBitmap((int)(w * k), (int)(h * k), 96 * k, 96 * k, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        await using var fs = File.Create(path);
        enc.Save(fs);
    }

    // ---------- самообновление ----------
    async Task CheckSelfUpdateLoop()
    {
        if (App.ScreenshotDir != null) return;
        while (true)
        {
            try
            {
                var u = await SelfUpdate.Check();
                if (u != null && u.Version != pendingUpdate?.Version)
                {
                    pendingUpdate = u;
                    UpdateText.Text = $"Вышел лаунчер {u.Version}";
                    UpdateBar.Visibility = Visibility.Visible;
                }
            }
            catch { /* нет связи — попробуем через час */ }
            await Task.Delay(TimeSpan.FromHours(1));
        }
    }

    // ---------- уведомления о новых версиях приложений ----------
    // Раз в полчаса, пока лаунчер открыт: вышла новая версия установленного приложения или появилась Desktop-версия,
    // которой раньше не было. Показываем сообщение и мигаем значком в панели задач, если окно свёрнуто.
    readonly HashSet<string> notified = new();
    readonly Dictionary<string, bool> nativeSeen = new();

    [StructLayout(LayoutKind.Sequential)]
    struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }
    [DllImport("user32.dll")] static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    void FlashTaskbar()
    {
        try
        {
            var fi = new FLASHWINFO { hwnd = new WindowInteropHelper(this).Handle, dwFlags = 3 | 12, uCount = 5, dwTimeout = 0 };
            fi.cbSize = (uint)Marshal.SizeOf(fi);
            FlashWindowEx(ref fi);
        }
        catch { /* мигание — только приятное дополнение */ }
    }

    async Task CheckAppUpdatesLoop()
    {
        if (App.ScreenshotDir != null) return;
        await Task.Delay(TimeSpan.FromMinutes(1));
        while (true)
        {
            try
            {
                var me = store.Active;
                if (me != null)
                {
                    await Catalog.Refresh();
                    var list = (await Task.WhenAll(Catalog.Apps.Select(def => Describe(def, me.Role, false)))).ToList();
                    var news = new List<string>();
                    foreach (var a in list.Where(x => x.Allowed))
                    {
                        if (a.Installed != null && a.UpdateAvailable && notified.Add($"{a.Def.Id}:{a.Latest}")) news.Add($"{a.Def.Name} {a.Latest}");
                        if (a.Native != null && a.NativeUpdate && a.NativeLatest != null && notified.Add($"{a.Def.Id}:exe:{a.NativeLatest.Sha}")) news.Add($"{a.Def.NativeName} (Desktop)");
                        var has = a.NativeLatest != null;
                        if (nativeSeen.TryGetValue(a.Def.Id, out var had) && !had && has && a.Native == null) news.Add($"Появилась Desktop-версия: {a.Def.Name}");
                        nativeSeen[a.Def.Id] = has;
                    }
                    apps = list;
                    if (AppsView.Visibility == Visibility.Visible && busy.Count == 0) RenderApps();
                    if (news.Count > 0)
                    {
                        Toast("Новое: " + string.Join(", ", news) + ". Открой «Приложения» и обнови.");
                        FlashTaskbar();
                    }
                }
            }
            catch { /* нет связи — попробуем через полчаса */ }
            await Task.Delay(TimeSpan.FromMinutes(30));
        }
    }

    async void UpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (pendingUpdate == null) return;
        UpdateBtn.IsEnabled = false;
        try
        {
            var progress = new Progress<int?>(p => UpdateText.Text = $"Скачиваю лаунчер {pendingUpdate.Version}… {p ?? 0}%");
            await SelfUpdate.Apply(pendingUpdate, progress);
        }
        catch (Exception ex)
        {
            UpdateText.Text = $"Вышел лаунчер {pendingUpdate.Version}";
            UpdateBtn.IsEnabled = true;
            Toast("Не удалось обновить лаунчер: " + ex.Message);
        }
    }
}
