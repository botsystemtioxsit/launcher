using System.Windows;
using System.Windows.Controls;

namespace GatewayPlusApp;

// «Для телефона»: создаёт на вашем сервере отдельный ключ для телефона (чтобы телефон и компьютер работали ОДНОВРЕМЕННО) и показывает QR-код
// для приложения WireGuard. Либо, запасной путь, показывает QR того же профиля, что и на компьютере (тогда устройства включают по очереди).
sealed class DeviceWindow : Window
{
    readonly string profile, profileConf;
    readonly AccessBox access;
    readonly TextBox nameBox = new() { Text = "phone", Width = 220, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = 20 };
    readonly TextBox logBox = new() { IsReadOnly = true, FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("Mono"), FontSize = 12, Height = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.NoWrap, Padding = new Thickness(8) };
    readonly TextBlock msg = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    Button go = null!, tuneBtn = null!;
    CancellationTokenSource? cts;
    bool running;
    string? lastConf;
    readonly string host;

    public DeviceWindow(string profile, string conf)
    {
        this.profile = profile; profileConf = conf;
        Ui.Setup(this, "Телефон и скорость", 680, 860);
        var page = new StackPanel { Margin = new Thickness(24, 18, 24, 18) };
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };

        page.Children.Add(Ui.Title("ТЕЛЕФОН И СКОРОСТЬ"));
        page.Children.Add(Ui.Note("Плагин зайдёт на ваш сервер и создаст отдельный ключ для телефона. Тогда телефон и компьютер работают одновременно, как «один аккаунт — несколько устройств» у обычных VPN. Потом вы наведёте камеру телефона на QR-код в приложении WireGuard (Google Play / App Store). Если доступ к серверу уже сохранён, пароль вводить не нужно.", 12.5));
        if (Store.Bypass.Contains("whatsapp")) page.Children.Add(Ui.Note("WhatsApp на телефоне пойдёт мимо VPN: в QR-код добавлено исключение этого приложения (звонки лучше слышны). Поменять можно в приложении WireGuard: туннель → ✎ → «Исключённые приложения».", 12));

        host = Store.EndpointOf(profileConf);
        access = new AccessBox(host);
        page.Children.Add(access);
        page.Children.Add(Ui.Label("Название устройства (латиницей)")); page.Children.Add(nameBox);

        go = Ui.Btn("Создать ключ для телефона", "Primary", Go, first: true);
        go.Margin = new Thickness(0, 16, 0, 8);
        page.Children.Add(go);
        page.Children.Add(Ui.Label("Что происходит"));
        page.Children.Add(Ui.Box(logBox));
        page.Children.Add(msg);

        page.Children.Add(Ui.Label("VPN медленный?"));
        page.Children.Add(Ui.Note("Кнопка зайдёт на сервер по тем же данным (доступ выше), подгонит размер пакетов, включит быструю пересылку UDP (если ядро сервера умеет) и IPv6 внутри туннеля (если у сервера есть свой IPv6), и покажет, насколько быстр сам сервер. Если медленный сам сервер, VPN быстрее него не станет. Размер пакета для вашего компьютера подбирается отдельно — кнопка «Скорость и размер пакета…» в главном окне.", 12));
        tuneBtn = Ui.Btn("Проверить и ускорить сервер", null, Tune, first: true);
        page.Children.Add(tuneBtn);

        page.Children.Add(Ui.Label("Запасной путь"));
        page.Children.Add(Ui.Note("Не хотите вводить пароль от сервера? Можно показать QR-код того же профиля, что у компьютера. Тогда телефон и компьютер включайте по очереди: с одним ключом они выбивают друг друга.", 12));
        page.Children.Add(Ui.Btn("Показать QR того же профиля", null, () => new QrWindow(profile, Split.ForPhone(profileConf, Store.Bypass)) { Owner = this }.ShowDialog(), first: true));
        Closing += (_, e) =>
        {
            if (!running) return;
            if (Loc.Box(this, "Идёт добавление устройства. Прервать?", "Для телефона", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) { e.Cancel = true; return; }
            cts?.Cancel();
        };
    }

    void Log(string line) => Dispatcher.BeginInvoke(new Action(() => { logBox.AppendText(Loc.T(line) + "\n"); logBox.ScrollToEnd(); }));
    bool Ask(string text, string title, MessageBoxImage icon) => Dispatcher.Invoke(() => Loc.Box(this, text, title, MessageBoxButton.YesNo, icon) == MessageBoxResult.Yes);

    bool ConfirmKey(HostKeyQuestion q)
    {
        if (q.Changed)
            return Ask($"ВНИМАНИЕ! Отпечаток сервера {q.Host} изменился с прошлого раза.\n\nЭто бывает, если сервер переустановили. Но так же выглядит и подмена сервера по дороге.\n\nНовый отпечаток:\n{q.Fingerprint}\n\nПродолжить, только если вы сами переустанавливали сервер.", "Отпечаток сервера изменился", MessageBoxImage.Warning);
        return Ask($"Первое подключение к серверу {q.Host}.\n\nОтпечаток ({q.Algorithm}):\n{q.Fingerprint}\n\nПродолжить?", "Подтвердите сервер", MessageBoxImage.Question);
    }

    async void Tune()
    {
        if (running) return;
        msg.Foreground = Ui.Res("Accent2"); msg.Text = "";
        var t = access.Build(out var bad);
        if (t == null) { msg.Text = bad ?? "Проверьте данные для входа на сервер."; return; }
        running = true; go.IsEnabled = false; tuneBtn.IsEnabled = false; logBox.Clear();
        cts = new CancellationTokenSource();
        try
        {
            var res = await DeviceAdder.TuneAsync(t, ConfirmKey, Log, cts.Token);
            if (!res.Ok) { Failed(res); return; }
            access.Persist(t, host, "");
            msg.Foreground = Ui.Res("Green");
            msg.Text = "Готово. Итог проверки — в журнале выше («ВЫВОД»). Если вы на телефоне, выключите и снова включите туннель в WireGuard.";
            // сервер включил IPv6 в туннеле — профиль на этом компьютере должен получить свой IPv6-адрес, иначе IPv6 так и останется «в тупике»
            if (res.Ipv6Prefix.Length > 0)
            {
                var cur = Store.Load(profile);
                var upd = cur == null ? null : Store.WithIpv6(cur, res.Ipv6Prefix);
                if (cur != null && upd != null && upd != cur && Store.Validate(upd) == null)
                {
                    Store.Save(profile, upd);
                    msg.Text += "\nIPv6 в туннеле включён, профиль «" + profile + "» на этом компьютере обновлён. Отключите шлюз и включите снова.";
                }
            }
        }
        catch (Exception ex) { msg.Text = "Не вышло: " + ex.Message; }
        finally { access.ClearSecrets(); t.Password = null; t.KeyPass = null; running = false; go.IsEnabled = true; tuneBtn.IsEnabled = true; }
    }

    async void Go()
    {
        if (running) return;
        if (lastConf != null) { new QrWindow(nameBox.Text.Trim(), Split.ForPhone(lastConf, Store.Bypass)) { Owner = this }.ShowDialog(); return; }
        msg.Foreground = Ui.Res("Accent2"); msg.Text = "";
        var name = nameBox.Text.Trim();
        var t = access.Build(out var bad);
        if (t == null) { msg.Text = bad ?? "Проверьте данные для входа на сервер."; return; }
        if (!DeviceAdder.ValidName(name)) { msg.Text = "Название устройства: латиницей, цифры, «-» и «_», до 20 знаков."; return; }
        running = true; go.IsEnabled = false; logBox.Clear();
        cts = new CancellationTokenSource();
        try
        {
            var res = await DeviceAdder.RunAsync(t, name, ConfirmKey, Log, cts.Token);
            if (!res.Ok) { Failed(res); return; }
            access.Persist(t, host, "");
            lastConf = res.Conf;
            msg.Foreground = Ui.Res("Green");
            msg.Text = "Готово! Откройте на телефоне WireGuard → «+» → «Сканировать QR-код» и наведите камеру на экран.";
            go.Content = "Показать QR ещё раз";
            new QrWindow(name, Split.ForPhone(res.Conf, Store.Bypass)) { Owner = this }.ShowDialog();
        }
        catch (Exception ex) { msg.Text = "Не вышло: " + ex.Message; }
        finally { access.ClearSecrets(); t.Password = null; t.KeyPass = null; running = false; go.IsEnabled = true; }
    }

    // Сервер не принял сохранённые данные (сменили пароль) — показываем поля для ввода.
    void Failed(ProvisionResult res)
    {
        if (res.AuthFailed && access.UsingSaved) { access.SwitchToManual(); msg.Text = res.Error + " Введите доступ заново."; }
        else msg.Text = res.Error;
    }
}
