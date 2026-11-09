using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GatewayPlusApp;

// Мастер «Создать свой VPN»: адрес сервера, логин и пароль (или файл ключа SSH) → плагин сам заходит на сервер, ставит WireGuard или GSP и сохраняет готовый профиль.
// Доступ по SSH запоминается только если человек оставил галочку (см. AccessBox): тогда второй протокол на этом же сервере ставится без повторного ввода пароля.
// Первое подключение к серверу — с подтверждением его отпечатка (как у ssh).
sealed class WizardWindow : Window
{
    readonly StackPanel page = new();
    readonly ScrollViewer scroll;
    readonly AccessBox access;
    readonly TextBox nameBox = new() { Text = "germany", Width = 220, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = 32 };
    readonly RadioButton rbWg = new() { Content = "WireGuard — стандарт: быстрый, проверенный годами", IsChecked = true, Margin = new Thickness(0, 0, 0, 4), GroupName = "proto" };
    readonly RadioButton rbGsp = new() { Content = "GSP — наш протокол: лучше переносит потери связи (мобильная сеть, Wi-Fi, далёкий сервер)", GroupName = "proto" };
    readonly TextBox logBox = new() { IsReadOnly = true, FontFamily = (FontFamily)Application.Current.FindResource("Mono"), FontSize = 12, TextWrapping = TextWrapping.NoWrap, Height = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8) };
    readonly TextBlock msg = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    Button go = null!;
    CancellationTokenSource? cts;
    bool running, done;

    public string? SavedName { get; private set; }

    // host/protocol/name — подсказки из окна «Изменить профиль»: адрес сервера, "wg" или "gsp", предлагаемое имя нового профиля.
    public WizardWindow(string? host = null, string? protocol = null, string? profileName = null)
    {
        Ui.Setup(this, "Создать свой VPN", 700, 860);
        access = new AccessBox(host, deferForm: true);
        if (protocol == "gsp") rbGsp.IsChecked = true;
        if (!string.IsNullOrWhiteSpace(profileName)) nameBox.Text = Store.SafeName(profileName);
        page.Margin = new Thickness(24, 18, 24, 18);
        scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
        Content = scroll;
        Build();
        Closing += OnClosing;
    }

    void Build()
    {
        page.Children.Add(Ui.Title("СОЗДАТЬ СВОЙ VPN"));
        page.Children.Add(Ui.Note("Мастер сам зайдёт на ваш сервер, поставит на него WireGuard или наш протокол GSP и сохранит готовый профиль — останется нажать «Подключить». Сервер мастер не покупает: арендуйте его сами (самый дешёвый подойдёт, обычно 3–5 € в месяц) в нужной стране — например, в Германии (Hetzner, Netcup, Contabo и подобные). Нужна система Ubuntu 22.04/24.04 или Debian 12. Хостер пришлёт адрес сервера, логин (обычно root) и пароль.", 12.5));

        page.Children.Add(access);

        page.Children.Add(Ui.Label("Протокол"));
        page.Children.Add(rbWg); page.Children.Add(rbGsp);
        page.Children.Add(Ui.Note("WireGuard — выбор по умолчанию. GSP — молодой протокол Ghost Shell: когда связь теряет пакеты, он восстанавливает их сам, и скорость падает заметно меньше (на хорошей связи он не быстрее WireGuard). Оба ставятся на ваш сервер по SSH, можно поставить оба — они не мешают друг другу.", 11.5));

        page.Children.Add(Ui.Label("Название профиля (латиницей)"));
        page.Children.Add(nameBox);

        go = Ui.Btn("Создать VPN на сервере", "Primary", Go, first: true);
        go.Margin = new Thickness(0, 16, 0, 8);
        page.Children.Add(go);
        page.Children.Add(Ui.Label("Что происходит"));
        page.Children.Add(Ui.Box(logBox));
        page.Children.Add(msg);
        page.Children.Add(Ui.Note("Мастер подключается к серверу по SSH, проверяет систему, передаёт скрипт, запускает его и забирает конфиг. Закрытые ключи создаются на сервере и не выводятся в журнал выше. Если у хостера есть свой файрвол, откройте в нём порт 51820 (WireGuard) или 51821 (GSP) по протоколу UDP.", 11.5));
    }

    void Log(string line) => Dispatcher.BeginInvoke(new Action(() =>
    {
        logBox.AppendText(Loc.T(line) + "\n");
        if (logBox.Text.Length > 60000) logBox.Text = logBox.Text[^40000..];
        logBox.ScrollToEnd();
    }));

    bool Ask(string text, string title, MessageBoxImage icon) =>
        Dispatcher.Invoke(() => Loc.Box(this, text, title, MessageBoxButton.YesNo, icon) == MessageBoxResult.Yes);

    bool ConfirmKey(HostKeyQuestion q)
    {
        if (q.Changed)
            return Ask($"ВНИМАНИЕ! Отпечаток сервера {q.Host} изменился с прошлого раза.\n\nЭто бывает, если сервер переустановили. Но так же выглядит и подмена сервера по дороге.\n\nНовый отпечаток:\n{q.Fingerprint}\n\nПродолжить, только если вы сами переустанавливали сервер.", "Отпечаток сервера изменился", MessageBoxImage.Warning);
        return Ask($"Первое подключение к серверу {q.Host}.\n\nОтпечаток ({q.Algorithm}):\n{q.Fingerprint}\n\nЕсли хостер показывает отпечаток в панели — сверьте. Если сверить нечем, для нового сервера это нормально: подтвердите, что это ваш сервер.\n\nПродолжить?", "Подтвердите сервер", MessageBoxImage.Question);
    }

    bool ConfirmReplace(string text) => Ask(text, "Сервер уже настроен", MessageBoxImage.Warning);

    async void Go()
    {
        if (done) { DialogResult = true; return; }
        if (running) return;
        msg.Foreground = Ui.Res("Accent2"); msg.Text = "";
        var t = access.Build(out var bad);
        if (t == null) { msg.Text = bad ?? "Проверьте данные для входа на сервер."; return; }
        t.Protocol = rbGsp.IsChecked == true ? "gsp" : "wg";
        var early = access.Server?.Host ?? t.Host;
        t.OnLoggedIn = () => { var m = Dispatcher.Invoke(() => access.EarlyMessage(t, early)); if (m != null) Log(m); };
        var name = UniqueName(Store.SafeName(nameBox.Text));
        running = true; go.IsEnabled = false; logBox.Clear();
        cts = new CancellationTokenSource();
        try
        {
            var res = await Provisioner.RunAsync(t, ConfirmKey, ConfirmReplace, Log, cts.Token);
            if (!res.Ok)
            {
                // сохранённые данные больше не подходят (сменили пароль) — показываем поля для ввода
                if (res.AuthFailed && access.UsingSaved) { access.SwitchToManual(); msg.Text = res.Error + " Введите доступ заново."; }
                else msg.Text = res.Error;
                return;
            }
            Store.Save(name, res.Conf);
            Store.Active = name;
            access.Persist(t, res.Endpoint, Servers.SuggestName(name));
            SavedName = name;
            msg.Foreground = Ui.Res("Green");
            msg.Text = $"Готово! Профиль «{name}» сохранён (сервер {res.Endpoint}, протокол {(res.Protocol == "gsp" ? "GSP" : "WireGuard")}). Закройте окно и нажмите «Подключить». Если не подключается — в панели хостинга откройте порт {(res.Protocol == "gsp" ? "51821" : "51820")} по протоколу UDP.";
            if (access.Server is { HasAccess: true }) msg.Text += " Доступ к серверу сохранён: второй протокол на нём ставится без ввода пароля.";
            done = true; go.Content = "Закрыть";
        }
        catch (Exception ex) { msg.Text = "Не вышло: " + ex.Message; }
        finally { access.ClearSecrets(); t.Password = null; t.KeyPass = null; running = false; go.IsEnabled = true; }
    }

    static string UniqueName(string name)
    {
        var have = Store.List();
        if (!have.Contains(name, StringComparer.OrdinalIgnoreCase)) return name;
        for (var i = 2; i < 100; i++) { var n = Store.SafeName(name + "-" + i); if (!have.Contains(n, StringComparer.OrdinalIgnoreCase)) return n; }
        return name;
    }

    void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!running) return;
        if (Loc.Box(this, "Установка ещё идёт. Прервать её?", "Создать свой VPN", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) { e.Cancel = true; return; }
        cts?.Cancel();
    }

    // Служебный режим: рисует окно в PNG без показа (с примером журнала).
    public void RenderTo(string file, bool demo, bool pressInstall = false)
    {
        if (pressInstall)
        {
            // как после нажатия «Создать VPN» на сервере без сохранённого доступа: форма показывается и просит пароль
            access.Build(out var ask);
            msg.Foreground = Ui.Res("Accent2"); msg.Text = ask ?? "";
        }
        if (demo)
        {
            access.SetHost("203.0.113.5");
            logBox.Text = "Подключаюсь к 203.0.113.5:22 как root…\nОтпечаток сервера знакомый — этот сервер уже был подтверждён раньше.\nВход выполнен.\nСистема: ubuntu 24.04.\nСтавлю WireGuard на сервер. Это занимает 1–3 минуты…\nHit:1 http://archive.ubuntu.com/ubuntu noble InRelease\nПолучаю конфиг…\nГотово: сервер настроен, конфиг получен.\n";
            msg.Foreground = Ui.Res("Green");
            msg.Text = "Готово! Профиль «germany» сохранён (сервер 203.0.113.5). Закройте окно и нажмите «Подключить».";
        }
        Ui.Render(this, scroll, page, file, 700);
    }
}
