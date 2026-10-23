using System.Net.Http;
using System.IO;
using System.Windows;
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
    }

    List<AppInfo> apps = new();
    readonly Dictionary<string, (bool Installing, int? Percent)> busy = new();
    UpdateInfo? pendingUpdate;
    readonly DispatcherTimer toastTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    public MainWindow()
    {
        InitializeComponent();
        try
        {
            // в .ico несколько размеров; WPF по умолчанию берёт первый (мелкий), поэтому выбираем самый крупный
            var dec = new IconBitmapDecoder(new Uri("pack://application:,,,/Assets/icon.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var best = dec.Frames.OrderByDescending(f => f.PixelWidth).First();
            LoginLogo.Source = best;
            SideLogo.Source = best;
        }
        catch { /* без логотипа тоже работаем */ }
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
        AppsSub.Text = $"Ты вошёл как {me.Display} · {me.RoleName}\nСейчас можно скачать браузерные версии (приложение в отдельном окне). Полноценные EXE-версии появятся позже.";
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
    void SetLoginBusy(bool on, bool cancellable = true)
    {
        LoginBtn.IsEnabled = TokenBtn.IsEnabled = PairBtn.IsEnabled = !on;
        EmailBox.IsEnabled = TokenBox.IsEnabled = !on;
        LoginCancelBtn.Visibility = on && cancellable ? Visibility.Visible : Visibility.Collapsed;
        if (!on) PairCode.Visibility = Visibility.Collapsed;
    }

    void LoginFail(string text)
    {
        LoginStatus.Visibility = Visibility.Collapsed;
        LoginError.Text = text;
        LoginError.Visibility = Visibility.Visible;
        SetLoginBusy(false);
    }

    void LoginInfo(string text)
    {
        LoginError.Visibility = Visibility.Collapsed;
        LoginStatus.Text = text;
        LoginStatus.Visibility = Visibility.Visible;
    }

    void LoginDone()
    {
        EmailBox.Text = TokenBox.Text = "";
        LoginStatus.Visibility = Visibility.Collapsed;
        SetLoginBusy(false);
        Render();
    }

    CancellationToken StartLogin()
    {
        LoginError.Visibility = Visibility.Collapsed;
        LoginStatus.Visibility = Visibility.Collapsed;
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
        LoginStatus.Visibility = Visibility.Collapsed;
    }

    // ---------- навигация ----------
    void ShowView(string name)
    {
        AppsView.Visibility = name == "apps" ? Visibility.Visible : Visibility.Collapsed;
        AccountView.Visibility = name == "account" ? Visibility.Visible : Visibility.Collapsed;
        NavApps.Foreground = name == "apps" ? Res("Text") : Res("Muted");
        NavAccount.Foreground = name == "account" ? Res("Text") : Res("Muted");
        if (name == "apps") _ = LoadApps(false);
    }

    void NavApps_Click(object sender, RoutedEventArgs e) => ShowView("apps");
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
        return info;
    }

    void RenderApps()
    {
        AppList.Children.Clear();
        foreach (var a in apps) AppList.Children.Add(AppCard(a));
    }

    UIElement AppCard(AppInfo a)
    {
        var card = new Border { Background = Res("Panel"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(22), Margin = new Thickness(0, 0, 0, 14) };
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
                ? Badge("Установлено" + (a.Installed.Version != null ? " " + a.Installed.Version : "") + " (браузерная)", Res("Green"), null)
                : Badge("Не установлено", Res("Muted"), null));
            if (a.Latest != null)
                badges.Children.Add(a.UpdateAvailable ? Badge("Доступно " + a.Latest, Res("OnAccent"), Res("Accent")) : Badge("Последняя " + a.Latest, Res("Muted"), null));
        }
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
                var install = MakeButton("Скачать браузерную версию", "Primary", async () => await InstallApp(a, "Установлено"));
                install.IsEnabled = a.Latest != null;
                actions.Children.Add(install);
            }
        }
        // Справа: кнопки браузерной версии, под ними — заблокированная «EXE-версия» (нативных приложений пока нет;
        // когда будут, эта кнопка начнёт качать их)
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(right, 1);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        right.Children.Add(actions);
        if (a.Allowed)
        {
            var exe = MakeButton("🔒 Скачать EXE-версию", "Ghost", () => Task.CompletedTask);
            exe.IsEnabled = false;
            exe.FontSize = 12;
            exe.Padding = new Thickness(10, 5, 10, 5);
            exe.Margin = new Thickness(0, 8, 0, 0);
            exe.HorizontalAlignment = HorizontalAlignment.Right;
            exe.ToolTip = "Полноценная EXE-версия пока не готова. Она появится, когда мы выпустим нативные приложения.";
            ToolTipService.SetShowOnDisabled(exe, true);
            right.Children.Add(exe);
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
                new() { Def = Catalog.Apps[1], Allowed = true, Latest = "1.0.6" },
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
