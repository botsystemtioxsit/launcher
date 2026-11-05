using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GatewayPlusApp;

// Мастер «Создать свой VPN»: адрес сервера, логин и пароль (или файл ключа SSH) → плагин сам заходит на сервер, ставит WireGuard и сохраняет готовый профиль.
// Пароль нигде не сохраняется. Первое подключение к серверу — с подтверждением его отпечатка (как у ssh).
sealed class WizardWindow : Window
{
    readonly StackPanel page = new();
    readonly ScrollViewer scroll;
    readonly TextBox hostBox = new() { Width = 320, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = 253 };
    readonly TextBox portBox = new() { Text = "22", Width = 90, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = 5 };
    readonly TextBox userBox = new() { Text = "root", Width = 200, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = 32 };
    readonly PasswordBox passBox = new() { Width = 320, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = 256 };
    readonly TextBox keyFileBox = new() { IsReadOnly = true, Width = 420, HorizontalAlignment = HorizontalAlignment.Left };
    readonly PasswordBox keyPassBox = new() { Width = 320, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = 256 };
    readonly TextBox nameBox = new() { Text = "germany", Width = 220, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = 32 };
    readonly RadioButton rbPass = new() { Content = "Пароль", IsChecked = true, Margin = new Thickness(0, 0, 18, 0) };
    readonly RadioButton rbKey = new() { Content = "Файл ключа SSH" };
    readonly RadioButton rbWg = new() { Content = "WireGuard — стандарт: быстрый, проверенный годами", IsChecked = true, Margin = new Thickness(0, 0, 0, 4), GroupName = "proto" };
    readonly RadioButton rbGsp = new() { Content = "GSP — наш протокол: лучше переносит потери связи (мобильная сеть, Wi-Fi, далёкий сервер)", GroupName = "proto" };
    readonly StackPanel passPanel = new(), keyPanel = new() { Visibility = Visibility.Collapsed };
    readonly TextBox logBox = new() { IsReadOnly = true, FontFamily = (FontFamily)Application.Current.FindResource("Mono"), FontSize = 12, TextWrapping = TextWrapping.NoWrap, Height = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8) };
    readonly TextBlock msg = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    Button go = null!;
    CancellationTokenSource? cts;
    bool running, done;

    public string? SavedName { get; private set; }

    public WizardWindow()
    {
        Ui.Setup(this, "Создать свой VPN", 700, 860);
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

        page.Children.Add(Ui.Label("Адрес сервера (IP, например 203.0.113.5)"));
        page.Children.Add(hostBox);
        var pu = new StackPanel { Orientation = Orientation.Horizontal };
        var portCol = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
        portCol.Children.Add(Ui.Label("Порт SSH")); portCol.Children.Add(portBox);
        var userCol = new StackPanel();
        userCol.Children.Add(Ui.Label("Пользователь")); userCol.Children.Add(userBox);
        pu.Children.Add(portCol); pu.Children.Add(userCol);
        page.Children.Add(pu);

        page.Children.Add(Ui.Label("Как войти на сервер"));
        var rb = new StackPanel { Orientation = Orientation.Horizontal };
        rb.Children.Add(rbPass); rb.Children.Add(rbKey);
        page.Children.Add(rb);
        passPanel.Children.Add(Ui.Label("Пароль"));
        passPanel.Children.Add(passBox);
        var pick = new StackPanel { Orientation = Orientation.Horizontal };
        pick.Children.Add(keyFileBox);
        pick.Children.Add(Ui.Btn("Выбрать…", null, PickKey));
        keyPanel.Children.Add(Ui.Label("Файл закрытого ключа"));
        keyPanel.Children.Add(pick);
        keyPanel.Children.Add(Ui.Label("Парольная фраза ключа (если она есть)"));
        keyPanel.Children.Add(keyPassBox);
        page.Children.Add(passPanel); page.Children.Add(keyPanel);
        rbPass.Checked += (_, _) => { passPanel.Visibility = Visibility.Visible; keyPanel.Visibility = Visibility.Collapsed; };
        rbKey.Checked += (_, _) => { passPanel.Visibility = Visibility.Collapsed; keyPanel.Visibility = Visibility.Visible; };
        page.Children.Add(Ui.Note("Пароль нигде не сохраняется: он нужен только на время установки.", 11.5));

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

    void PickKey()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Выберите закрытый ключ SSH", Filter = "Все файлы|*.*" };
        if (dlg.ShowDialog(this) == true) keyFileBox.Text = dlg.FileName;
    }

    void Log(string line) => Dispatcher.BeginInvoke(new Action(() =>
    {
        logBox.AppendText(line + "\n");
        if (logBox.Text.Length > 60000) logBox.Text = logBox.Text[^40000..];
        logBox.ScrollToEnd();
    }));

    bool Ask(string text, string title, MessageBoxImage icon) =>
        Dispatcher.Invoke(() => MessageBox.Show(this, text, title, MessageBoxButton.YesNo, icon) == MessageBoxResult.Yes);

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
        var t = new SshTarget { Host = hostBox.Text.Trim(), Port = int.TryParse(portBox.Text.Trim(), out var p) ? p : 22, User = userBox.Text.Trim(), Protocol = rbGsp.IsChecked == true ? "gsp" : "wg" };
        if (rbKey.IsChecked == true) { t.KeyFile = keyFileBox.Text.Trim(); t.KeyPass = keyPassBox.Password; }
        else t.Password = passBox.Password;
        var bad = Provisioner.ValidateTarget(t);
        if (bad != null) { msg.Text = bad; return; }
        var name = UniqueName(Store.SafeName(nameBox.Text));
        running = true; go.IsEnabled = false; logBox.Clear();
        cts = new CancellationTokenSource();
        try
        {
            var res = await Provisioner.RunAsync(t, ConfirmKey, ConfirmReplace, Log, cts.Token);
            passBox.Clear(); keyPassBox.Clear(); t.Password = null; t.KeyPass = null;
            if (!res.Ok) { msg.Text = res.Error; return; }
            Store.Save(name, res.Conf);
            Store.Active = name;
            SavedName = name;
            msg.Foreground = Ui.Res("Green");
            msg.Text = $"Готово! Профиль «{name}» сохранён (сервер {res.Endpoint}, протокол {(res.Protocol == "gsp" ? "GSP" : "WireGuard")}). Закройте окно и нажмите «Подключить». Если не подключается — в панели хостинга откройте порт {(res.Protocol == "gsp" ? "51821" : "51820")} по протоколу UDP.";
            done = true; go.Content = "Закрыть";
        }
        catch (Exception ex) { msg.Text = "Не вышло: " + ex.Message; }
        finally { running = false; go.IsEnabled = true; }
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
        if (MessageBox.Show(this, "Установка ещё идёт. Прервать её?", "Создать свой VPN", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) { e.Cancel = true; return; }
        cts?.Cancel();
    }

    // Служебный режим: рисует окно в PNG без показа (с примером журнала).
    public void RenderTo(string file, bool demo)
    {
        if (demo)
        {
            hostBox.Text = "203.0.113.5";
            logBox.Text = "Подключаюсь к 203.0.113.5:22 как root…\nОтпечаток сервера знакомый — этот сервер уже был подтверждён раньше.\nВход выполнен.\nСистема: ubuntu 24.04.\nСтавлю WireGuard на сервер. Это занимает 1–3 минуты…\nHit:1 http://archive.ubuntu.com/ubuntu noble InRelease\nПолучаю конфиг…\nГотово: сервер настроен, конфиг получен.\n";
            msg.Foreground = Ui.Res("Green");
            msg.Text = "Готово! Профиль «germany» сохранён (сервер 203.0.113.5). Закройте окно и нажмите «Подключить».";
        }
        Ui.Render(this, scroll, page, file, 700);
    }
}
