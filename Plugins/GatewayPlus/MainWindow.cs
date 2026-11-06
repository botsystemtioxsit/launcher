using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GatewayPlusApp;

// Что показывать в служебном режиме --screenshot (чтобы окно можно было снять без сети и аккаунта)
enum GwDemo { None, Account, NoAccount, Blocked }

// Окно Gateway Plus: один экран. Сверху — каким вас видят сайты, ниже — шлюз (профиль, «Подключить»/«Отключить»), свой сервер в Германии и пояснения.
sealed class MainWindow : Window
{
    readonly StackPanel page = new();
    readonly TextBlock status = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(5) };
    bool connected, busy;
    IpInfo.Info? ip;
    Account.Info? account;
    Cloud.Access? access;
    DateTime lastAccessCheck = DateTime.MinValue;
    bool keyShown;
    TextBlock? gspLine;   // строка состояния GSP (потери, защита), обновляется раз в 5 секунд

    // Что показывать вместо настоящих данных в служебном режиме --screenshot (чтобы окно можно было снять без сети и аккаунта)
    readonly GwDemo demo;
    bool Blocked => access?.Blocked ?? false;

    static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    // Протокол профиля: у GSP своя служба Windows, у WireGuard — служба самого WireGuard
    static bool IsGspProfile(string name)
    {
        var c = name.Length > 0 ? Store.Load(name) : null;
        return c != null && Gsp.IsGsp(c);
    }

    static bool RunningNow(string name) => name.Length > 0 && (IsGspProfile(name) ? GspClient.IsRunning(name) : Wg.IsRunning(name));

    public MainWindow(GwDemo demo = GwDemo.None)
    {
        this.demo = demo;
        Title = "Gateway Plus";
        Width = Math.Min(960, SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(900, SystemParameters.WorkArea.Height - 40);
        MinWidth = 640; MinHeight = 480;
        Background = Res("Bg"); Foreground = Res("Text");
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        page.Margin = new Thickness(28, 22, 28, 24);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
        if (demo == GwDemo.None) account = Account.Load();
        else
        {
            if (demo != GwDemo.NoAccount) account = new Account.Info { Key = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567", Name = "Alex", TermsV = Terms.Version };
            if (demo == GwDemo.Blocked) access = new Cloud.Access(true, "использование в коммерческих целях", false);
            else if (demo == GwDemo.Account) access = new Cloud.Access(false, "", false);
        }
        BuildPage();
        Loaded += async (_, _) =>
        {
            if (demo != GwDemo.None) return;
            await CheckAccessAsync(true);
            await RefreshAsync(true);
        };
        timer.Tick += async (_, _) => await TickAsync();
        timer.Start();
    }

    async Task TickAsync()
    {
        if (busy || !IsVisible) return;
        var name = Store.Active;
        var now = name.Length > 0 && await Task.Run(() => RunningNow(name));
        if (now != connected) await RefreshAsync(true);
        else if (now && gspLine != null)
        {
            var st = await Task.Run(() => GspClient.ReadStatus(name));
            gspLine.Text = GspClient.Describe(st);
        }
        // блокировку проверяем раз в 10 минут; если шлюз включён, а доступ закрыли — отключаем (одно окно разрешения Windows)
        if (demo == GwDemo.None && account != null && DateTime.UtcNow - lastAccessCheck > TimeSpan.FromMinutes(10))
        {
            await CheckAccessAsync(false);
            if (connected && Blocked) { status.Text = "Доступ закрыт владельцем проекта — отключаю шлюз."; Disconnect(); }
        }
    }

    async Task CheckAccessAsync(bool heartbeat)
    {
        lastAccessCheck = DateTime.UtcNow;
        var dev = Account.DeviceHash();
        access = await Cloud.CheckAccessAsync(account?.Id, dev);
        if (heartbeat && account != null && !access.Blocked) _ = Cloud.HeartbeatAsync(account.Id, dev, Ui.Version, Store.List().Count, account.TermsV, account.Name);
        BuildPage();
    }

    // Перед включением шлюза и мастером: есть ли аккаунт, приняты ли (новые) правила, не закрыт ли доступ. «Отключить» и «Вернуть как было» этого не требуют.
    async Task<bool> EnsureAllowedAsync()
    {
        if (account == null) { status.Text = "Сначала создайте аккаунт Gateway Plus (карточка «Аккаунт» вверху) или войдите по ключу."; return false; }
        if (account.TermsV < Terms.Version)
        {
            var w = new AccountWindow(false, termsOnly: true) { Owner = this };
            if (w.ShowDialog() != true) { status.Text = "Без согласия с обновлёнными правилами шлюз и мастер недоступны."; return false; }
            account.TermsV = Terms.Version;
            Account.Save(account);
            _ = Cloud.HeartbeatAsync(account.Id, Account.DeviceHash(), Ui.Version, Store.List().Count, account.TermsV, account.Name);
        }
        await CheckAccessAsync(false);
        if (Blocked) { status.Text = "Доступ к Gateway Plus закрыт владельцем проекта."; return false; }
        return true;
    }

    async void OpenAccount(bool login)
    {
        var w = new AccountWindow(login) { Owner = this };
        w.ShowDialog();
        account = Account.Load();
        keyShown = false;
        await CheckAccessAsync(true);
    }

    void Logout()
    {
        if (Loc.Box(this, "Выйти из аккаунта на этом компьютере?\n\nКлюч нужен, чтобы войти снова — убедитесь, что он у вас сохранён. Без аккаунта шлюз и мастер не включаются (уже работающий шлюз продолжит работать, его можно отключить).", "Выйти из аккаунта", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Account.Clear(); account = null; access = null; keyShown = false;
        BuildPage();
    }

    Border AccountCard()
    {
        var rows = new List<UIElement>();
        if (account == null)
        {
            rows.Add(Note("Для шлюза и мастера нужен бесплатный аккаунт Gateway Plus. Без пароля и почты: вам выдаётся ключ, он и есть вход. Аккаунт нужен, чтобы действовали правила — только личное использование, без коммерции; владелец проекта может закрыть доступ тем, кто их нарушает."));
            var b = new WrapPanel();
            b.Children.Add(Btn("Создать аккаунт", "Primary", () => OpenAccount(false), first: true));
            b.Children.Add(Btn("У меня есть ключ", null, () => OpenAccount(true)));
            rows.Add(b);
        }
        else
        {
            rows.Add(new TextBlock { Text = account.Name + "  ·  номер " + account.Id[..8] + "…", FontSize = 16, FontWeight = FontWeights.SemiBold });
            string st; Brush col;
            if (Blocked) { st = "Доступ закрыт" + (access!.Offline ? " (по последним сведениям; нет связи с базой)" : "") + "."; col = Res("Red"); }
            else if (account.TermsV < Terms.Version) { st = "Правила обновились — при следующем включении шлюза их нужно принять."; col = Res("Accent2"); }
            else if (access == null) { st = "Проверяю…"; col = Res("Muted"); }
            else if (access.Offline) { st = "Нет связи с базой — проверим позже. Правила приняты."; col = Res("Muted"); }
            else { st = "Доступ открыт · правила приняты (версия " + account.TermsV + ")"; col = Res("Green"); }
            rows.Add(new TextBlock { Text = st, Foreground = col, Margin = new Thickness(0, 4, 0, 8), TextWrapping = TextWrapping.Wrap });
            if (keyShown)
            {
                rows.Add(new TextBox { Text = Account.Format(account.Key), IsReadOnly = true, FontFamily = (FontFamily)Application.Current.FindResource("Mono"), FontSize = 15, Margin = new Thickness(0, 0, 0, 4) });
                rows.Add(Note("Никому не показывайте ключ: кто его знает, тот хозяин аккаунта."));
            }
            var b = new WrapPanel();
            b.Children.Add(Btn(keyShown ? "Скрыть ключ" : "Показать ключ", null, () => { keyShown = !keyShown; BuildPage(); }, first: true));
            if (keyShown) b.Children.Add(Btn("Скопировать ключ", null, () => { try { Clipboard.SetText(Account.Format(account.Key)); status.Text = "Ключ скопирован."; } catch { status.Text = "Не удалось скопировать."; } }));
            b.Children.Add(Btn("Правила", null, () => new AccountWindow(false, termsOnly: true, readOnly: true) { Owner = this }.ShowDialog()));
            b.Children.Add(Btn("Выйти из аккаунта", "Danger", Logout));
            rows.Add(b);
        }
        return Card("Аккаунт Gateway Plus", rows);
    }

    Border BlockedCard() => Card("Доступ закрыт", new UIElement[]
    {
        new TextBlock { Text = "Владелец проекта закрыл доступ к Gateway Plus: " + (access?.Reason ?? ""), Foreground = Res("Red"), TextWrapping = TextWrapping.Wrap, FontSize = 15 },
        Note("Включить шлюз и создать новый сервер сейчас нельзя. Отключить уже включённый шлюз и нажать «Вернуть как было» можно всегда. Если это ошибка — напишите нам: вкладка «Написать нам» в Ghost Shell Launcher."),
    });

    async Task RefreshAsync(bool checkIp)
    {
        var name = Store.Active;
        connected = name.Length > 0 && await Task.Run(() => RunningNow(name));
        if (checkIp) ip = await IpInfo.GetAsync();
        BuildPage();
    }

    // ---------- сборка окна ----------
    void BuildPage()
    {
        page.Children.Clear();
        page.Children.Add(new TextBlock { Text = "GATEWAY PLUS", FontFamily = (FontFamily)Application.Current.FindResource("Mono"), FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Res("Accent") });
        page.Children.Add(Note("Шлюз: весь интернет с этого компьютера идёт через ваш сервер, а сайты видят его адрес и страну, а не ваши. Сервер в Германии — значит Германия."));

        page.Children.Add(AccountCard());
        if (Blocked) page.Children.Add(BlockedCard());

        // 1. как видят
        var seen = new StackPanel();
        var big = new TextBlock { FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        if (ip == null) { big.Text = "Проверяю…"; big.Foreground = Res("Muted"); }
        else if (!ip.Ok) { big.Text = "Не удалось проверить"; big.Foreground = Res("Muted"); seen.Children.Add(big); seen.Children.Add(Note(ip.Error)); }
        else { big.Text = (ip.Country.Length > 0 ? ip.Country : "?") + (ip.City.Length > 0 ? ", " + ip.City : ""); big.Foreground = connected ? Res("Green") : Res("Text"); }
        if (ip == null || ip.Ok) seen.Children.Add(big);
        if (ip != null && ip.Ok) seen.Children.Add(new TextBlock { Text = "Внешний адрес: " + ip.Ip + (ip.Zone.Length > 0 ? "  ·  часовой пояс сайта: " + ip.Zone : ""), Foreground = Res("Muted"), Margin = new Thickness(0, 4, 0, 0) });
        seen.Children.Add(new TextBlock
        {
            Text = connected ? "Шлюз включён: сайты видят адрес сервера." : "Шлюз выключен: сайты видят ваш настоящий адрес.",
            Foreground = connected ? Res("Green") : Res("Accent2"), Margin = new Thickness(0, 10, 0, 8),
        });
        seen.Children.Add(Btn("Проверить ещё раз", null, async () => { ip = null; BuildPage(); await RefreshAsync(true); }, first: true));
        page.Children.Add(Card("Сейчас вас видят так", new UIElement[] { seen }));

        // 2. шлюз
        var gw = new List<UIElement>();
        var wgPath = Wg.ExePath();
        var profiles = Store.List();
        var active = Store.Active;
        var activeGsp = active.Length > 0 && IsGspProfile(active);
        gspLine = null;
        if (wgPath == null && !activeGsp)
        {
            gw.Add(Note("Для профилей WireGuard нужна программа WireGuard (официальная, wireguard.com, бесплатная). Нажмите — плагин скачает установщик, проверит его подпись и запустит; Windows спросит разрешение. Для профилей GSP она не нужна."));
            gw.Add(Btn("Установить WireGuard", "Primary", InstallWg, first: true));
        }
        else if (wgPath != null) gw.Add(new TextBlock { Text = "WireGuard установлен.", Foreground = Res("Muted"), FontSize = 12, Margin = new Thickness(0, 0, 0, 10) });
        if (profiles.Count > 0)
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 6, 0, 10) };
            foreach (var p in profiles)
            {
                var name = p;
                var b = new Button { Content = name + (IsGspProfile(name) ? "  ·  GSP" : ""), Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 8) };
                if (name == active) b.Style = (Style)FindResource("Primary");
                b.Click += (_, _) => { if (!connected) { Store.Active = name; BuildPage(); } else status.Text = "Сначала отключите шлюз."; };
                wrap.Children.Add(b);
            }
            gw.Add(new TextBlock { Text = "Профиль (куда подключаться)", FontWeight = FontWeights.SemiBold });
            gw.Add(wrap);
        }
        else gw.Add(Note("Профилей пока нет. Добавьте конфиг: от своего сервера (см. ниже) или от VPN-сервиса, который даёт файл WireGuard."));

        // какой протокол выбран и работает ли он сейчас
        if (active.Length > 0)
        {
            var proto = activeGsp ? "GSP" : "WireGuard";
            gw.Add(new TextBlock
            {
                Text = connected ? $"Сейчас работает: {proto} · профиль «{active}»" : $"Выбран профиль «{active}» ({proto}) — шлюз выключен",
                Foreground = connected ? Res("Green") : Res("Muted"), FontWeight = connected ? FontWeights.SemiBold : FontWeights.Normal,
                FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8),
            });
        }
        // как получить профиль GSP, пока его нет
        if (profiles.Count > 0 && !profiles.Any(IsGspProfile))
            gw.Add(Note("Протокол выбирается профилем: «· GSP» на кнопке — наш протокол (лучше переносит потери связи), остальные профили — WireGuard. Чтобы появился профиль GSP, создайте его мастером ниже («Создать мой VPN на сервере…», в окне мастера выберите «GSP»); WireGuard на сервере при этом не трогается. Переключать профиль можно, пока шлюз выключен."));

        var buttons = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        if (!connected)
        {
            var c = Btn("Подключить", "Primary", Connect, first: true);
            c.IsEnabled = (activeGsp ? GspClient.Available : wgPath != null) && profiles.Count > 0 && active.Length > 0 && account != null && !Blocked;
            c.Padding = new Thickness(26, 11, 26, 11); c.FontSize = 16;
            buttons.Children.Add(c);
        }
        else
        {
            var d = Btn("Отключить", "Danger", Disconnect, first: true);
            d.Padding = new Thickness(26, 11, 26, 11); d.FontSize = 16;
            buttons.Children.Add(d);
        }
        buttons.Children.Add(Btn("＋ Добавить конфиг…", null, AddProfile));
        if (profiles.Count > 0 && active.Length > 0) buttons.Children.Add(Btn("Изменить профиль / протокол…", null, () => EditProfile(active, connected)));
        if (profiles.Count > 0 && active.Length > 0 && !connected) buttons.Children.Add(Btn("Удалить выбранный профиль", null, () => { Store.Delete(active); Store.Active = Store.List().FirstOrDefault() ?? ""; BuildPage(); }));
        if (profiles.Count > 0 && active.Length > 0 && !activeGsp) buttons.Children.Add(Btn("Скорость и размер пакета…", null, () => ShowSpeed(active, connected)));
        if (profiles.Count > 0 && active.Length > 0 && !activeGsp) buttons.Children.Add(Btn("Телефон и ускорение сервера…", null, () => ShowPhoneQr(active)));
        gw.Add(buttons);
        if (profiles.Count > 0)
        {
            var wa = new CheckBox { Content = "Не пускать WhatsApp через шлюз (звонки идут напрямую — так они слышны лучше)", IsChecked = Store.Bypass.Contains("whatsapp"), Margin = new Thickness(0, 6, 0, 2) };
            wa.Click += (_, _) =>
            {
                var cur = Store.Bypass.Where(x => x != "whatsapp").ToList();
                if (wa.IsChecked == true) cur.Add("whatsapp");
                Store.Bypass = cur.ToArray();
                status.Text = connected ? "Изменение вступит в силу при следующем подключении: отключите шлюз и включите снова." : "Сохранено: вступит в силу при подключении.";
            };
            gw.Add(wa);
            gw.Add(Note("Исключаются адреса серверов WhatsApp. Это сети компании Meta, поэтому Facebook и Instagram тоже пойдут напрямую, а WhatsApp увидит ваш настоящий адрес. Пока исключение включено, WireGuard не закрывает «прямой выход» при обрыве шлюза (эта защита работает только когда в туннель идёт всё). Список адресов обновляется сам раз в неделю."));
        }
        if (activeGsp)
        {
            if (connected)
            {
                gspLine = new TextBlock { Text = GspClient.Describe(GspClient.ReadStatus(active)), Foreground = Res("Green"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8) };
                gw.Add(gspLine);
            }
            if (!GspClient.Available) gw.Add(Note("В этой сборке плагина нет программы GSP — обновите Gateway Plus из лаунчера."));
            gw.Add(Note("GSP — наш собственный протокол: на ненадёжной связи (мобильная сеть, Wi-Fi, далёкий сервер) он сам включает защиту от потерь пакетов, и скорость падает меньше, чем у WireGuard. На хорошей связи он не быстрее WireGuard. Протокол молодой: проверен тестами и на стенде, но не годами, как WireGuard. Включение и выключение требуют разрешения администратора Windows. Пока служба шлюза работает, весь интернет идёт через сервер; если служба остановится аварийно, интернет пойдёт напрямую (отдельной защиты от утечки при аварии пока нет — если она критична, берите WireGuard)."));
        }
        else gw.Add(Note("Включение и выключение шлюза требуют разрешения администратора Windows — оно спросит каждый раз. Если связь со шлюзом оборвётся, интернет не «утечёт» мимо него: пока шлюз включён, WireGuard закрывает прямой выход."));
        page.Children.Add(Card("Шлюз", gw));

        // 3. свой сервер: мастер и ручной путь
        var wizBtn = Btn("Создать мой VPN на сервере…", "Primary", () => OpenWizard(), first: true);
        wizBtn.IsEnabled = account != null && !Blocked;
        page.Children.Add(Card("Создать свой VPN", new UIElement[]
        {
            Note("Своя точка выхода — это ваш собственный сервер (VPS) в нужной стране: адрес у него свой и постоянный, трафик видите только вы. Мастер сам зайдёт на сервер по SSH (нужны адрес, логин и пароль от хостера), поставит WireGuard (стандарт) или наш протокол GSP (лучше переносит потери связи) и сохранит готовый профиль. Подойдёт самый дешёвый сервер с Ubuntu 22.04/24.04 или Debian 12 (обычно 3–5 € в месяц; Hetzner, Netcup, Contabo и подобные)."),
            wizBtn,
            Note(account == null ? "Для мастера нужен аккаунт Gateway Plus — карточка «Аккаунт» вверху." : "Для личного использования. Продавать доступ к своему серверу или шлюзу нельзя (правила)."),
        }));
        var manual = new WrapPanel();
        manual.Children.Add(Btn("Скопировать скрипт для сервера", null, async () => { if (!await EnsureAllowedAsync()) return; try { Clipboard.SetText(Store.ServerScript()); status.Text = "Скрипт скопирован. Вставьте его в консоль сервера."; } catch { status.Text = "Не удалось скопировать."; } }, first: true));
        manual.Children.Add(Btn("Скопировать скрипт GSP для сервера", null, async () => { if (!await EnsureAllowedAsync()) return; try { Clipboard.SetText(Store.GspServerScript()); status.Text = "Скрипт GSP скопирован. Вставьте его в консоль сервера; порт для хостера — 51821/udp."; } catch { status.Text = "Не удалось скопировать."; } }));
        page.Children.Add(Card("Вручную (если мастер не подходит)", new UIElement[]
        {
            Note("Если нельзя зайти по SSH или сервер уже настроен: 1) зайдите в консоль сервера как root (sudo -i). 2) Нажмите «Скопировать скрипт» (WireGuard) или «Скопировать скрипт GSP» и вставьте его в консоль. 3) В конце он напечатает конфиг между двумя линиями — скопируйте его. 4) Здесь нажмите «Добавить конфиг…» и вставьте. 5) В панели хостинга откройте порт 51820 (WireGuard) или 51821 (GSP) по протоколу UDP, если он закрыт."),
            manual,
        }));

        // 4. границы
        page.Children.Add(Card("Что шлюз скрывает, а что нет", new UIElement[]
        {
            Note("Скрывает: ваш настоящий IP-адрес и страну от сайтов и от вашего провайдера (он видит только соединение с сервером). Не скрывает: вход в аккаунты (сайт знает, кто вы, когда вы вошли), куки и отпечаток браузера, часовой пояс и язык браузера (сайт может видеть, что они не германские), платёжные данные. Шлюз не отменяет законы и правила сервисов. Бесплатным VPN-сервисам не доверяйте: за бесплатное вы часто платите своими данными. Владелец сервера видит ваш трафик (если сайт не шифрует его) — поэтому лучше свой сервер."),
        }));

        status.Foreground = Res("Accent2"); status.FontSize = 13; status.TextWrapping = TextWrapping.Wrap; status.Margin = new Thickness(0, 4, 0, 0);
        var last = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        last.Children.Add(Btn("Вернуть как было", "Danger", RevertAll, first: true));
        page.Children.Add(last);
        page.Children.Add(status);
        page.Children.Add(Note("«Вернуть как было» выключает шлюз и убирает его следы (службу и файл конфига в ProgramData). Ваши сохранённые профили остаются."));
    }

    // ---------- действия ----------
    async void InstallWg()
    {
        if (busy) return;
        busy = true;
        try
        {
            var err = await Wg.InstallAsync(t => Dispatcher.BeginInvoke(new Action(() => status.Text = t)));
            status.Text = err ?? "WireGuard установлен.";
        }
        finally { busy = false; BuildPage(); }
    }

    void ShowSpeed(string name, bool connected)
    {
        var conf = Store.Load(name);
        if (conf == null) { status.Text = "Профиль не прочитался."; return; }
        new SpeedWindow(name, conf, connected) { Owner = this }.ShowDialog();
    }

    void ShowPhoneQr(string name)
    {
        var conf = Store.Load(name);
        if (conf == null) { status.Text = "Профиль не прочитался."; return; }
        new DeviceWindow(name, conf) { Owner = this }.ShowDialog();
    }

    void AddProfile()
    {
        var w = new ImportWindow { Owner = this };
        if (w.ShowDialog() == true) { status.Text = "Профиль «" + w.SavedName + "» сохранён."; }
        BuildPage();
    }

    // Окно «Изменить профиль»: выбор протокола для сервера, правка текста, переименование.
    void EditProfile(string name, bool connected)
    {
        var conf = Store.Load(name);
        if (conf == null) { status.Text = "Профиль не прочитался."; return; }
        var w = new ProfileWindow(name, conf, connected) { Owner = this };
        w.ShowDialog();
        switch (w.Result)
        {
            case ProfileWindow.Outcome.Use:
                Store.Active = w.ResultName;
                status.Text = "Выбран профиль «" + w.ResultName + "» (" + (IsGspProfile(w.ResultName) ? "GSP" : "WireGuard") + "). Нажмите «Подключить».";
                BuildPage();
                break;
            case ProfileWindow.Outcome.Saved:
                status.Text = "Профиль «" + w.ResultName + "» сохранён.";
                BuildPage();
                break;
            case ProfileWindow.Outcome.Deleted:
                status.Text = "Профиль «" + name + "» удалён.";
                BuildPage();
                break;
            case ProfileWindow.Outcome.Install:
                OpenWizard(w.InstallHost, w.InstallProtocol, w.InstallName);
                break;
        }
    }

    async void OpenWizard(string? host = null, string? protocol = null, string? profileName = null)
    {
        if (busy) return;
        if (!await EnsureAllowedAsync()) return;
        var w = new WizardWindow(host, protocol, profileName) { Owner = this };
        w.ShowDialog();
        if (w.SavedName != null) status.Text = "Профиль «" + w.SavedName + "» сохранён. Нажмите «Подключить».";
        if (account != null) _ = Cloud.HeartbeatAsync(account.Id, Account.DeviceHash(), Ui.Version, Store.List().Count, account.TermsV, account.Name);
        BuildPage();
    }

    async void Connect()
    {
        if (busy) return;
        if (!await EnsureAllowedAsync()) return;
        var name = Store.Active;
        var conf = name.Length > 0 ? Store.Load(name) : null;
        if (conf == null) { status.Text = "Сначала добавьте конфиг."; return; }
        var bad = Store.Validate(conf);
        if (bad != null) { status.Text = bad; return; }
        var gspProfile = Gsp.IsGsp(conf);
        busy = true;
        status.Text = "Подключаю… Windows спросит разрешение администратора.";
        string? tmpDir = null;
        try
        {
            tmpDir = Path.Combine(Path.GetTempPath(), "gpc-" + Guid.NewGuid().ToString("N")[..10]);
            Directory.CreateDirectory(tmpDir);
            var file = Path.Combine(tmpDir, name + ".conf");
            // «Мимо шлюза» (WhatsApp): вместо 0.0.0.0/0 службе уходит «всё, кроме сетей сервиса и адреса сервера»; сохранённый профиль не меняется
            var bypassOn = false;
            var bypass = Store.Bypass;
            if (bypass.Length > 0)
            {
                await Split.RefreshIfStaleAsync();
                var eps = await Split.ResolveAsync(Store.EndpointOf(conf));
                var split = Split.Apply(conf, bypass, eps);
                if (split != conf && Store.Validate(split) == null) { conf = split; bypassOn = true; }
            }
            await File.WriteAllTextAsync(file, conf);
            var r = await Helper.RunAsync(new Job { Action = gspProfile ? "gsp-connect" : "connect", Name = name, Conf = file });
            status.Text = r.Message + (r.Ok && bypassOn ? " WhatsApp идёт мимо шлюза." : "");
            await Task.Delay(r.Ok ? 2500 : 200);
        }
        catch (Exception ex) { status.Text = "Не вышло: " + ex.Message; }
        finally
        {
            try { if (tmpDir != null) Directory.Delete(tmpDir, true); } catch { }
            busy = false;
        }
        await RefreshAsync(true);
    }

    async void Disconnect()
    {
        if (busy) return;
        busy = true;
        status.Text = "Отключаю… Windows спросит разрешение администратора.";
        try
        {
            var r = await Helper.RunAsync(new Job { Action = IsGspProfile(Store.Active) ? "gsp-disconnect" : "disconnect", Name = Store.Active });
            status.Text = r.Message;
        }
        catch (Exception ex) { status.Text = "Не вышло: " + ex.Message; }
        finally { busy = false; }
        await RefreshAsync(true);
    }

    async void RevertAll()
    {
        if (busy) return;
        busy = true;
        status.Text = "Убираю шлюз… Windows спросит разрешение администратора.";
        try
        {
            var r = (Wg.ExePath() == null && !Directory.Exists(Helper.WorkDir)) ? new JobResult { Ok = true, Message = "Убирать нечего." } : await Helper.RunAsync(new Job { Action = "cleanup" });
            status.Text = r.Message;
        }
        catch (Exception ex) { status.Text = "Не вышло: " + ex.Message; }
        finally { busy = false; }
        await RefreshAsync(true);
    }

    // ---------- кирпичики интерфейса ----------
    Border Card(string title, IEnumerable<UIElement> rows)
    {
        var col = new StackPanel();
        if (title.Length > 0) col.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        foreach (var r in rows) col.Children.Add(r);
        return new Border
        {
            Background = Res("Panel"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(22), Margin = new Thickness(0, 0, 0, 14), Child = col,
        };
    }

    TextBlock Note(string text) => new()
    {
        Text = text, Foreground = Res("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10),
    };

    Button Btn(string text, string? style, Action click, bool first = false)
    {
        var b = new Button { Content = text, Margin = new Thickness(first ? 0 : 10, 0, 0, 8), Padding = new Thickness(18, 9, 18, 9), HorizontalAlignment = HorizontalAlignment.Left };
        if (style != null) b.Style = (Style)FindResource(style);
        b.Click += (_, _) => click();
        return b;
    }

    // Служебный режим: рисует окно целиком в PNG без показа
    public void RenderTo(string file)
    {
        var sv = (ScrollViewer)Content;
        sv.Content = null;
        System.Windows.Documents.TextElement.SetForeground(page, Foreground);
        System.Windows.Documents.TextElement.SetFontFamily(page, FontFamily);
        System.Windows.Documents.TextElement.SetFontSize(page, FontSize);
        Loc.ApplyTree(page);   // служебный снимок: окно не показано, переводим вручную
        page.Measure(new Size(940, double.PositiveInfinity));
        var w = 940; var h = Math.Max(200, (int)Math.Ceiling(page.DesiredSize.Height));
        page.Arrange(new Rect(0, 0, w, h));
        page.UpdateLayout();
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen()) dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));
        rtb.Render(bg);
        rtb.Render(page);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(file);
        enc.Save(fs);
        sv.Content = page;
    }
}
