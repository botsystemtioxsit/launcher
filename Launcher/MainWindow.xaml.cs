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
        InitOps();
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
        // Вход не обязателен: приложения и плагины открыты всем, вход живёт во вкладке «Аккаунт» и нужен только для приложений с ролью (админ-панель).
        NavAccount.Content = me == null ? "Войти" : "Аккаунт";
        AppsSub.Text = me == null
            ? "Приложения доступны без входа. Облегчённая версия — приложение в отдельном окне (Electron). Админ-панель появится после входа администратора (вкладка «Войти»)."
            : $"Ты вошёл как {me.Display} · {me.RoleName}\nОблегчённая версия — приложение в отдельном окне (Electron). Desktop-версия (EXE) — настоящая программа; кнопка включится сама, как только версия выйдет.";
        if (me == null)
        {
            RenderLogin();
            if (sessionNote != null)
            {
                LoginError.Text = sessionNote; LoginError.Visibility = Visibility.Visible; sessionNote = null;
                ShowView("account"); // объясняем, почему выбросило из сессии
            }
            else ShowView(currentView);
            return;
        }
        MeBox.Children.Clear();
        MeBox.Children.Add(Avatar(me, 52));
        var info = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = me.Display, FontWeight = FontWeights.SemiBold, FontSize = 16 });
        info.Children.Add(new TextBlock { Text = me.RoleName + (me.PlayerId.Length > 0 ? " · " + me.PlayerId : ""), Foreground = Res("Muted") });
        MeBox.Children.Add(info);
        FillAccountList(AllAccounts, removableExceptActive: true);
        ShowView(currentView);
        firstRender = false;
    }

    bool firstRender = true;

    // Роль для проверки доступа к приложению: без входа — «guest» (видны только приложения без ограничения по роли).
    static string RoleOf(Account? a) => a?.Role ?? "guest";

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
        // после входа: у кого появились приложения «по роли» (админ-панель) — сразу к ним, остальным — сведения об аккаунте
        var role = RoleOf(store.Active);
        ShowView(Catalog.Apps.Any(a => a.Roles != null && a.IsAllowed(role)) ? "apps" : "account");
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
    // Разделы: apps (приложения), style (оформление), net (сеть и защита), ext (расширения для браузера), env, feedback, account.
    string currentView = "apps";

    void ShowView(string name)
    {
        currentView = name;
        var isPlug = name is "style" or "net" or "ext";
        AppsView.Visibility = name == "apps" ? Visibility.Visible : Visibility.Collapsed;
        PluginsView.Visibility = isPlug ? Visibility.Visible : Visibility.Collapsed;
        FeedbackView.Visibility = name == "feedback" ? Visibility.Visible : Visibility.Collapsed;
        EnvView.Visibility = name == "env" ? Visibility.Visible : Visibility.Collapsed;
        NavEnv.Foreground = name == "env" ? Res("Text") : Res("Muted");
        NavFeedback.Foreground = name == "feedback" ? Res("Text") : Res("Muted");
        // «Аккаунт»: без входа показываем форму входа, со входом — сведения об аккаунте
        var signedIn = store.Active != null;
        LoginView.Visibility = name == "account" && !signedIn ? Visibility.Visible : Visibility.Collapsed;
        AccountView.Visibility = name == "account" && signedIn ? Visibility.Visible : Visibility.Collapsed;
        NavApps.Foreground = name == "apps" ? Res("Text") : Res("Muted");
        NavStyle.Foreground = name == "style" ? Res("Text") : Res("Muted");
        NavNet.Foreground = name == "net" ? Res("Text") : Res("Muted");
        NavExt.Foreground = name == "ext" ? Res("Text") : Res("Muted");
        NavAccount.Foreground = name == "account" ? Res("Text") : Res("Muted");
        if (name == "apps") _ = LoadApps(false);
        if (isPlug) SetPluginsView(name);
        if (name is "style" or "net") _ = LoadPlugins(false);
        if (name == "ext") _ = LoadBrowserPlugins(false);
        if (name == "feedback") OpenFeedback();
        if (name == "env") OpenEnv();
    }

    // ---------- плагины ----------
    // Плагины — отдельные программы (EXE в релизе лаунчера). Каждый ставится только по кнопке и удаляется отдельно.
    List<AppInfo> pluginInfos = new();

    async Task LoadPlugins(bool force)
    {
        if (App.ScreenshotDir != null) return; // в режиме скриншотов ничего не качаем
        RenderPlugins(); // сразу то, что уже известно
        await Catalog.Refresh(force);
        var list = await Task.WhenAll(Catalog.Plugins.Select(async def =>
        {
            var info = new AppInfo { Def = def, Allowed = true };
            info.Native = await Task.Run(() => NativeInstaller.Find(def));
            try { info.NativeLatest = await NativeInstaller.Latest(def, force); } catch { /* нет связи — кнопка просто не включится */ }
            info.NativeUpdate = info.Native != null && info.NativeLatest != null && info.Native.Sha != info.NativeLatest.Sha;
            return info;
        }));
        pluginInfos = list.ToList();
        RenderPlugins();
    }

    void RenderPlugins()
    {
        PluginList.Children.Clear();
        if (pluginInfos.Count == 0)
        {
            PluginList.Children.Add(new TextBlock { Text = "Загружаю список плагинов…", Foreground = Res("Muted") });
            return;
        }
        var mine = pluginInfos.Where(p => Catalog.CategoryOf(p.Def) == pluginsCategory).ToList();
        if (mine.Count == 0) { PluginList.Children.Add(new TextBlock { Text = "В этом разделе пока нет плагинов.", Foreground = Res("Muted") }); return; }
        foreach (var p in mine) PluginList.Children.Add(PluginCard(p));
    }

    UIElement PluginCard(AppInfo p)
    {
        var key = p.Def.Id + ":plugin";
        var card = new Border { Background = Res("Panel"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(22), Margin = new Thickness(0, 0, 0, 14) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        left.Children.Add(new TextBlock { Text = p.Def.Name, FontSize = 17, FontWeight = FontWeights.SemiBold });
        left.Children.Add(new TextBlock { Text = p.Def.Desc, Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 12, 8) });
        var badges = new StackPanel { Orientation = Orientation.Horizontal };
        badges.Children.Add(Badge("Плагин", Res("Muted"), null));
        if (p.Native != null)
        {
            badges.Children.Add(Badge("Установлен " + p.Native.Version, Res("Green"), null));
            if (p.NativeUpdate) badges.Children.Add(Badge("Есть обновление", Res("OnYellow"), Res("Yellow")));
        }
        else badges.Children.Add(Badge("Не установлен", Res("Muted"), null));
        left.Children.Add(badges);
        grid.Children.Add(left);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(actions, 1);
        if (busy.TryGetValue(key, out var b))
        {
            actions.Children.Add(new TextBlock { Text = BusyText(key, b), Foreground = Res("Muted"), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            actions.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = b.Percent ?? 0, IsIndeterminate = b.Installing });
        }
        else if (p.Native != null)
        {
            actions.Children.Add(MakeButton("Открыть", "Primary", () => LaunchPlugin(p)));
            if (p.NativeUpdate) actions.Children.Add(MakeButton("Обновить", null, () => InstallPlugin(p, "Обновлено")));
            actions.Children.Add(MakeButton("Удалить", "Danger", () => UninstallPlugin(p)));
        }
        else
        {
            // Кнопка всегда включена: если проверка релиза не удалась, нажатие пробует ещё раз и показывает настоящую причину
            actions.Children.Add(MakeButton("Скачать и установить", "Primary", () => InstallPlugin(p, "Установлено")));
        }
        grid.Children.Add(actions);
        card.Child = grid;
        return card;
    }

    async Task<bool> InstallPlugin(AppInfo p, string okText)
    {
        var key = p.Def.Id + ":plugin";
        var ok = await RunOp(key, p.Def.Name, OpKind(okText), RenderPlugins, async (item, ct) =>
        {
            try
            {
                item.SetPhase("Проверяю релиз…");
                var rel = await NativeInstaller.Latest(p.Def, true, ct) ?? throw new InvalidOperationException("в последнем релизе на GitHub нет файла плагина");
                var progress = new Progress<int?>(v => { busy[key] = v == null ? (true, null) : (false, v); item.SetPercent(v); RenderPlugins(); });
                var restarted = await NativeInstaller.Install(p.Def, rel, progress, ct, PhaseSink(item));
                if (restarted) item.SetNote("запущен снова");
                Toast($"{okText}: {p.Def.Name}" + (restarted ? " — запущен снова" : ""));
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                var why = BrowserInstaller.Explain(ex);
                item.Fail(why);
                Toast("Не получилось: " + why);
                return false;
            }
        });
        await LoadPlugins(true);
        return ok;
    }

    async Task UninstallPlugin(AppInfo p)
    {
        var key = p.Def.Id + ":plugin";
        if (busy.ContainsKey(key) || ops.IsActive(key) || p.Native == null) return;
        if (MessageBox.Show(this, $"Удалить «{p.Def.Name}» с этого компьютера?\n\nЕсли плагин менял панель задач, он сам вернёт её к обычному виду.", "Удаление", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await RunOp(key, p.Def.Name, "Удаление", RenderPlugins, async (item, ct) =>
        {
            try
            {
                var sink = PhaseSink(item);
                await Task.Run(() => NativeInstaller.Uninstall(p.Def, sink));
                Toast($"Удалено: {p.Def.Name}");
                return true;
            }
            catch (Exception ex) { item.Fail(ex.Message); Toast("Не получилось: " + ex.Message); return false; }
        });
        await LoadPlugins(true);
    }

    async Task LaunchPlugin(AppInfo p)
    {
        try
        {
            var found = await Task.Run(() => NativeInstaller.Find(p.Def));
            if (found == null) { Toast("Плагин не установлен"); await LoadPlugins(true); return; }
            NativeInstaller.Launch(found);
        }
        catch (Exception ex) { Toast("Не получилось запустить: " + ex.Message); }
    }

    // ---------- плагины для браузера ----------
    // Вкладка «Для браузера»: лаунчер скачивает расширение (zip из релиза) в папку, человек один раз подключает её в браузере.
    class BrowserPluginInfo
    {
        public required BrowserPluginDef Def;
        public BrowserInfo? Installed;
        public BrowserRelease? Latest;
        public bool Update;
    }

    List<BrowserPluginInfo> browserInfos = new();
    // Почему не получилось (показываем в карточке, пока человек не попробует снова) — исчезающее сообщение внизу легко пропустить
    readonly Dictionary<string, string> browserErrors = new();
    string pluginsCategory = "style";   // какой раздел плагинов показан: style | net (для вкладки «Расширения» не используется)

    // Одно и то же окно служит трём вкладкам: «Оформление» и «Сеть и защита» (плагины для компьютера по разделам) и «Расширения» (для браузера).
    void SetPluginsView(string view)
    {
        var browser = view == "ext";
        if (!browser) pluginsCategory = view;
        PluginList.Visibility = browser ? Visibility.Collapsed : Visibility.Visible;
        BrowserPluginList.Visibility = browser ? Visibility.Visible : Visibility.Collapsed;
        PluginsLaunchAllBtn.Visibility = browser ? Visibility.Collapsed : Visibility.Visible;
        PluginsTitle.Text = view switch { "net" => "Сеть и защита", "ext" => "Расширения для браузера", _ => "Оформление" };
        PluginsHint.Text = view switch
        {
            "net" => "VPN, защищённый DNS и всё, что про сеть. Каждый плагин качается отдельно и удаляется в любой момент.",
            "ext" => "Расширения для Chrome, Edge и других браузеров. Лаунчер скачивает их в отдельную папку; подключить папку в браузере нужно один раз — как, написано в карточке.",
            _ => "Панель задач, цвета, значки, обои, курсор и скины Windows. Каждый плагин качается отдельно и удаляется в любой момент.",
        };
        if (!browser) RenderPlugins();
    }

    async Task LoadBrowserPlugins(bool force)
    {
        if (App.ScreenshotDir != null) return; // в режиме скриншотов ничего не качаем
        RenderBrowserPlugins();
        await Catalog.Refresh(force);
        var list = await Task.WhenAll(Catalog.BrowserPlugins.Select(async def =>
        {
            var info = new BrowserPluginInfo { Def = def };
            info.Installed = await Task.Run(() => BrowserInstaller.Find(def));
            try
            {
                info.Latest = await BrowserInstaller.Latest(def, force);
                if (info.Latest != null) browserErrors.Remove(def.Id);
                else browserErrors[def.Id] = "В последнем релизе на GitHub пока нет файла расширения.";
            }
            catch (Exception ex) { browserErrors[def.Id] = "Не удалось проверить версию: " + BrowserInstaller.Explain(ex); }
            info.Update = info.Installed != null && info.Latest != null && info.Installed.Sha != info.Latest.Sha;
            return info;
        }));
        browserInfos = list.ToList();
        RenderBrowserPlugins();
    }

    void RenderBrowserPlugins()
    {
        BrowserPluginList.Children.Clear();
        if (browserInfos.Count == 0)
        {
            BrowserPluginList.Children.Add(new TextBlock { Text = "Загружаю список плагинов…", Foreground = Res("Muted") });
            return;
        }
        foreach (var p in browserInfos) BrowserPluginList.Children.Add(BrowserPluginCard(p));
        // Расширения, которые лаунчер сам не ставит (zip по ссылке), лежат здесь же, а не среди приложений
        foreach (var d in Catalog.Downloads.Where(x => x.Os.Contains("windows") && Catalog.IsExtension(x))) BrowserPluginList.Children.Add(DownloadCard(d));
    }

    UIElement BrowserPluginCard(BrowserPluginInfo p)
    {
        var key = p.Def.Id + ":browser";
        var card = new Border { Background = Res("Panel"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(22), Margin = new Thickness(0, 0, 0, 14) };
        var col = new StackPanel();
        col.Children.Add(new TextBlock { Text = p.Def.Name, FontSize = 17, FontWeight = FontWeights.SemiBold });
        col.Children.Add(new TextBlock { Text = p.Def.Desc, Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) });
        var badges = new WrapPanel { Orientation = Orientation.Horizontal };
        badges.Children.Add(Badge("Для браузера", Res("Muted"), null));
        if (p.Def.Browsers.Length > 0) badges.Children.Add(Badge(p.Def.Browsers, Res("Muted"), null));
        if (p.Installed != null)
        {
            badges.Children.Add(Badge("Скачан " + p.Installed.Version, Res("Green"), null));
            if (p.Update) badges.Children.Add(Badge("Есть обновление", Res("OnYellow"), Res("Yellow")));
        }
        else badges.Children.Add(Badge("Не скачан", Res("Muted"), null));
        col.Children.Add(badges);

        var actions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        if (busy.TryGetValue(key, out var b))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = BusyText(key, b), Foreground = Res("Muted"), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = b.Percent ?? 0, IsIndeterminate = b.Installing, Width = 160, Height = 6 });
            actions.Children.Add(row);
        }
        else if (p.Installed != null)
        {
            var dir = p.Installed.Dir;
            col.Children.Add(BrowserHelp(p));
            actions.Children.Add(FirstButton(MakeButton("Открыть Chrome", "Primary", () => OpenBrowserPage("chrome"))));
            actions.Children.Add(MakeButton("Открыть Edge", null, () => OpenBrowserPage("edge")));
            actions.Children.Add(MakeButton("Открыть папку", null, () => { try { BrowserInstaller.OpenFolder(p.Def); } catch (Exception ex) { Toast(ex.Message); } return Task.CompletedTask; }));
            actions.Children.Add(MakeButton("Скопировать путь", null, () => { try { Clipboard.SetText(dir); Toast("Путь скопирован"); } catch { Toast("Не получилось скопировать"); } return Task.CompletedTask; }));
            if (p.Update) actions.Children.Add(MakeButton("Обновить", null, () => InstallBrowserPlugin(p, "Обновлено")));
            actions.Children.Add(MakeButton("Удалить", "Danger", () => UninstallBrowserPlugin(p)));
        }
        else
        {
            // Кнопка всегда включена: если проверка релиза не удалась, нажатие просто пробует ещё раз и показывает настоящую причину
            actions.Children.Add(FirstButton(MakeButton("Скачать", "Primary", () => InstallBrowserPlugin(p, "Скачано"))));
            actions.Children.Add(MakeButton("Скачать файл вручную", null, () => OpenManualDownload(p.Def)));
        }
        if (!busy.ContainsKey(key) && browserErrors.TryGetValue(p.Def.Id, out var why))
        {
            col.Children.Add(new TextBlock { Text = why + (p.Installed == null ? " Нажмите «Скачать», чтобы попробовать ещё раз, или «Скачать файл вручную» — тогда распакуйте архив в любую папку и подключите её в браузере." : ""),
                Foreground = Res("Red"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        }
        col.Children.Add(actions);
        card.Child = col;
        return card;
    }

    static Button FirstButton(Button b) { b.Margin = new Thickness(0, 0, 0, 0); return b; }

    // Короткая инструкция «как подключить» — внутри карточки, пока расширение скачано
    UIElement BrowserHelp(BrowserPluginInfo p)
    {
        var box = new Border { Background = Res("Panel2"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(14, 10, 14, 12), Margin = new Thickness(0, 14, 0, 0) };
        var st = new StackPanel();
        st.Children.Add(new TextBlock { Text = "Как подключить (один раз)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        string[] steps =
        {
            "1. Нажмите «Открыть Chrome» (или «Открыть Edge») — откроется страница расширений.",
            "2. Справа вверху включите «Режим разработчика».",
            "3. Нажмите «Загрузить распакованное расширение» и выберите эту папку:",
        };
        foreach (var t in steps) st.Children.Add(new TextBlock { Text = t, Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 3) });
        st.Children.Add(new TextBlock { Text = p.Installed!.Dir, FontFamily = (FontFamily)FindResource("Mono"), FontSize = 12, Foreground = Res("Text"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 0, 0, 4) });
        st.Children.Add(new TextBlock { Text = "4. Закрепите значок маски на панели браузера (кнопка с пазлом) — там выбор страны.", Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        st.Children.Add(new TextBlock { Text = "После обновления из лаунчера на странице расширений нажмите круглую стрелку у «" + p.Def.Name + "».", Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap });
        box.Child = st;
        return box;
    }

    Task OpenBrowserPage(string kind)
    {
        try { BrowserInstaller.OpenExtensionsPage(kind); }
        catch (Exception ex) { Toast(ex.Message); }
        return Task.CompletedTask;
    }

    // Запасной путь: если скачать из самого лаунчера не вышло, архив можно взять браузером
    Task OpenManualDownload(BrowserPluginDef def)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(BrowserInstaller.ZipUrl(def)) { UseShellExecute = true }); }
        catch { Toast("Не удалось открыть браузер"); }
        return Task.CompletedTask;
    }

    async Task<bool> InstallBrowserPlugin(BrowserPluginInfo p, string okText)
    {
        var key = p.Def.Id + ":browser";
        if (ops.IsActive(key) || busy.ContainsKey(key)) return false;
        browserErrors.Remove(p.Def.Id);
        var ok = await RunOp(key, p.Def.Name + " (расширение)", OpKind(okText), RenderBrowserPlugins, async (item, ct) =>
        {
            try
            {
                item.SetPhase("Проверяю релиз…");
                var rel = await BrowserInstaller.Latest(p.Def, true) ?? throw new InvalidOperationException("в последнем релизе на GitHub пока нет файла расширения");
                var progress = new Progress<int?>(v => { busy[key] = v == null ? (true, null) : (false, v); item.SetPercent(v); RenderBrowserPlugins(); });
                await BrowserInstaller.Install(p.Def, rel, progress, ct, PhaseSink(item));
                item.SetNote("подключите папку в браузере");
                Toast($"{okText}: {p.Def.Name}. Подключите папку в браузере — инструкция в карточке.");
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                var why = BrowserInstaller.Explain(ex);
                browserErrors[p.Def.Id] = "Не получилось скачать: " + why + ".";
                item.Fail(why);
                Toast("Не получилось скачать: " + why);
                return false;
            }
        });
        var failed = browserErrors.TryGetValue(p.Def.Id, out var keep) ? keep : null;
        await LoadBrowserPlugins(true);
        if (failed != null) { browserErrors[p.Def.Id] = failed; RenderBrowserPlugins(); } // перезагрузка списка не должна затирать причину сбоя
        return ok;
    }

    async Task UninstallBrowserPlugin(BrowserPluginInfo p)
    {
        var key = p.Def.Id + ":browser";
        if (busy.ContainsKey(key) || p.Installed == null) return;
        if (MessageBox.Show(this, $"Удалить файлы «{p.Def.Name}» с этого компьютера?\n\nИз самого браузера расширение лучше убрать отдельно: страница расширений → «Удалить».", "Удаление", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            await Task.Run(() => BrowserInstaller.Uninstall(p.Def));
            Toast($"Удалено: {p.Def.Name}");
        }
        catch (Exception ex) { Toast("Не получилось: " + ex.Message); }
        await LoadBrowserPlugins(true);
    }

    void NavApps_Click(object sender, RoutedEventArgs e) => ShowView("apps");
    void NavStyle_Click(object sender, RoutedEventArgs e) => ShowView("style");
    void NavNet_Click(object sender, RoutedEventArgs e) => ShowView("net");
    void NavExt_Click(object sender, RoutedEventArgs e) => ShowView("ext");
    void NavAccount_Click(object sender, RoutedEventArgs e) => ShowView("account");
    void NavEnv_Click(object sender, RoutedEventArgs e) => ShowView("env");
    void NavFeedback_Click(object sender, RoutedEventArgs e) => ShowView("feedback");

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
        var role = RoleOf(store.Active);
        RefreshBtn.IsEnabled = false;
        await Catalog.Refresh(force); // список приложений и их версий читается из catalog.json — без обновления лаунчера
        var list = await Task.WhenAll(Catalog.Apps.Select(def => Describe(def, role, force)));
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
        // приложения, на которые у роли нет доступа (админ-панель без входа администратора), не показываем совсем
        foreach (var a in apps.Where(x => x.Allowed)) AppList.Children.Add(AppCard(a));
        var downloads = Catalog.Downloads.Where(d => d.Os.Contains("windows") && !Catalog.IsExtension(d)).ToArray();
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
                badges.Children.Add(a.UpdateAvailable ? Badge("Доступно " + a.Latest, Res("OnYellow"), Res("Yellow")) : Badge("Последняя " + a.Latest, Res("Muted"), null));
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
            actions.Children.Add(new TextBlock { Text = BusyText(a.Def.Id, b), Foreground = Res("Muted"), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
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
                exeRow.Children.Add(new TextBlock { Text = BusyText(exeKey, eb), Foreground = Res("Muted"), FontSize = 12, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
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

    // Плашка со срезанными углами: с заливкой (bg) — заметная неоновая (обновление и т. п.), без заливки — тонкий контур цвета текста.
    static FrameworkElement Badge(string text, Brush fg, Brush? bg)
    {
        var filled = bg != null;
        Brush edge = fg;
        if (!filled) { edge = fg.Clone(); edge.Opacity = 0.55; }
        var g = new Grid { Margin = new Thickness(0, 0, 8, 0), SnapsToDevicePixels = true };
        g.Children.Add(new GhostShellUi.ChamferBorder { Fill = bg ?? Res("Panel2"), Stroke = filled ? bg : edge, Thickness = 1, Cut = 6 });
        g.Children.Add(new TextBlock
        {
            Text = (filled ? "▲ " : "") + text, FontFamily = (FontFamily)Application.Current.FindResource("Mono"), FontSize = 11.5,
            FontWeight = filled ? FontWeights.Bold : FontWeights.Normal, Foreground = fg, Margin = new Thickness(11, 3, 11, 4),
        });
        if (filled && bg is SolidColorBrush sb)
            g.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = sb.Color, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.5 };
        return g;
    }

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
        var me = store.Active;
        if (me != null)
        {
            await RefreshActiveProfile();
            me = store.Active;
            if (me == null) { Render(); Toast("Аккаунт больше недоступен — войди заново."); return false; }
        }
        if (!def.IsAllowed(RoleOf(me)))
        {
            Toast(me == null ? "Для этого приложения нужен вход в аккаунт (вкладка «Войти»)." : "Для этого приложения нет доступа у твоего аккаунта.");
            await LoadApps(false);
            return false;
        }
        return true;
    }

    async Task<bool> InstallApp(AppInfo a, string okText)
    {
        var id = a.Def.Id;
        var ok = await RunOp(id, a.Def.Name, OpKind(okText), RenderApps, async (item, ct) =>
        {
            try
            {
                item.SetPhase("Проверяю доступ…");
                if (!await RequireAllowed(a.Def)) { item.Fail("нет доступа к этому приложению"); return false; }
                item.SetPhase("Проверяю релиз…");
                var release = await Releases.Latest(a.Def.Repo, true, ct);
                var progress = new Progress<int?>(p => { busy[id] = p == null ? (true, null) : (false, p); item.SetPercent(p); RenderApps(); });
                await Installer.Install(a.Def, release, progress, ct, PhaseSink(item));
                Toast($"{okText}: {a.Def.Name}");
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { item.Fail(ex.Message); Toast("Не получилось: " + ex.Message); return false; }
        });
        await LoadApps(true);
        return ok;
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
    async Task<bool> InstallNative(AppInfo a, string okText)
    {
        var key = a.Def.Id + ":exe";
        var ok = await RunOp(key, a.Def.NativeName ?? a.Def.Name, OpKind(okText), RenderApps, async (item, ct) =>
        {
            try
            {
                item.SetPhase("Проверяю доступ…");
                if (!await RequireAllowed(a.Def)) { item.Fail("нет доступа к этому приложению"); return false; }
                item.SetPhase("Проверяю релиз…");
                var rel = await NativeInstaller.Latest(a.Def, true, ct) ?? throw new InvalidOperationException("EXE-версии пока нет в последнем релизе");
                var progress = new Progress<int?>(p => { busy[key] = p == null ? (true, null) : (false, p); item.SetPercent(p); RenderApps(); });
                await NativeInstaller.Install(a.Def, rel, progress, ct, PhaseSink(item));
                Toast($"{okText}: {a.Def.NativeName}");
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { item.Fail(ex.Message); Toast("Не получилось: " + ex.Message); return false; }
        });
        await LoadApps(true);
        return ok;
    }

    async Task UninstallNative(AppInfo a)
    {
        var key = a.Def.Id + ":exe";
        if (busy.ContainsKey(key) || ops.IsActive(key) || a.Native == null) return;
        if (MessageBox.Show(this, $"Удалить «{a.Def.NativeName}» с этого компьютера?", "Удаление", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await RunOp(key, a.Def.NativeName ?? a.Def.Name, "Удаление", RenderApps, async (item, ct) =>
        {
            try
            {
                var sink = PhaseSink(item);
                await Task.Run(() => NativeInstaller.Uninstall(a.Def, sink));
                Toast($"Удалено: {a.Def.NativeName}");
                return true;
            }
            catch (Exception ex) { item.Fail(ex.Message); Toast("Не получилось: " + ex.Message); return false; }
        });
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
            ShowView("account");
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
            ShowView("apps");
            RenderApps();
        });
        // разделы плагинов: оформление и сеть (часть «установлена», у одного есть обновление)
        pluginInfos = Catalog.Plugins.Select((d, i) => new AppInfo
        {
            Def = d, Allowed = true,
            Native = i % 3 == 2 ? null : new NativeInfo("1.0." + i, "demo.exe", "aa"),
            NativeLatest = new NativeRelease("latest", "", i == 1 ? "bb" : "aa"),
            NativeUpdate = i == 1,
        }).ToList();
        await Capture(Path.Combine(dir, "launcher-7-style.png"), () => { ShowView("style"); RenderPlugins(); });
        await Capture(Path.Combine(dir, "launcher-8-net.png"), () => { ShowView("net"); RenderPlugins(); });
        await Capture(Path.Combine(dir, "launcher-3-plugins-browser.png"), () =>
        {
            var def = Catalog.BrowserPlugins[0];
            browserInfos = new List<BrowserPluginInfo>
            {
                new() { Def = def, Installed = new BrowserInfo("1.0.0", Path.Combine(BrowserInstaller.Root, def.Id), "00"), Latest = new BrowserRelease("1.0.0", "", "00") },
            };
            ShowView("ext");
            RenderBrowserPlugins();
        });
        await Capture(Path.Combine(dir, "launcher-5-env.png"), () =>
        {
            ShowView("env");
        });
        {   // экран загрузки «Окружение» (демо: никого не запускает)
            var w = new BootWindow(Catalog.Plugins.Where(p => Boot.BackgroundIds.Contains(p.Id)).Select(p => (p, new NativeInfo("1.0.0", "demo.exe", null))).ToList(), demo: true)
            { WindowState = WindowState.Normal, Width = 1100, Height = 700, Left = 0, Top = 0, Topmost = false };
            w.Show();
            await Task.Delay(4500);
            var vis = (FrameworkElement)w.Content;
            var bmp = new RenderTargetBitmap((int)vis.ActualWidth, (int)vis.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) dc.DrawRectangle(w.Background, null, new Rect(0, 0, vis.ActualWidth, vis.ActualHeight));
            bmp.Render(dv);
            bmp.Render(vis);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp));
            await using (var fs = File.Create(Path.Combine(dir, "launcher-6-boot.png"))) enc.Save(fs);
            w.Close();
        }
        await Capture(Path.Combine(dir, "launcher-4-feedback.png"), () =>
        {
            ShowView("feedback");
            SetFeedbackType("bug");
            fbText!.Text = "После включения формы «Острова» панель задач мигает при наведении мыши на часы.\nШаги: 1) включил форму 2) нажал «Применить» 3) навёл мышь на часы.";
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            fbSentBox!.Children.Clear();
            fbSentBox.Children.Add(FeedbackSentCard(new FeedbackSent("demo1", "bug", "Панель задач мигает после включения плагина…", now - 7200_000), new FeedbackState("done", "Спасибо! Исправили в версии 1.34.1 — обновите лаунчер.", now - 3600_000, false)));
            fbSentBox.Children.Add(FeedbackSentCard(new FeedbackSent("demo2", "idea", "Добавьте тёмную тему для окна плагина…", now - 86400_000), new FeedbackState("new", null, null, false)));
        });
        {   // менеджер обновлений: одно готово, одно не вышло, одно останавливается, два ждут
            ShowView("style");
            RenderPlugins();
            await OpsDemo();
            await Capture(Path.Combine(dir, "launcher-9-ops.png"), () => RenderOps());
        }
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
                var role = RoleOf(store.Active);
                {
                    await Catalog.Refresh();
                    var list = (await Task.WhenAll(Catalog.Apps.Select(def => Describe(def, role, false)))).ToList();
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
