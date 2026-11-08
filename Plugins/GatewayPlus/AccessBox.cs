using System.Windows;
using System.Windows.Controls;

namespace GatewayPlusApp;

// Блок «как войти на сервер по SSH»: адрес, порт, логин, пароль или файл ключа. Если для этого сервера доступ уже сохранён (человек сам согласился),
// вместо полей показывается одна строка «доступ сохранён» — вводить заново ничего не нужно. Общий для мастера, окна «Телефон и скорость» и окна «Сервер».
sealed class AccessBox : StackPanel
{
    readonly TextBox hostBox = new() { Width = 320, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = 253 };
    readonly TextBox portBox = new() { Text = "22", Width = 90, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = 5 };
    readonly TextBox userBox = new() { Text = "root", Width = 200, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = 32 };
    readonly PasswordBox passBox = new() { Width = 320, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = 256 };
    readonly TextBox keyFileBox = new() { IsReadOnly = true, Width = 420, HorizontalAlignment = HorizontalAlignment.Left };
    readonly PasswordBox keyPassBox = new() { Width = 320, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = 256 };
    readonly RadioButton rbPass = new() { Content = "Пароль", IsChecked = true, Margin = new Thickness(0, 0, 18, 0) };
    readonly RadioButton rbKey = new() { Content = "Файл ключа SSH" };
    readonly CheckBox remember = new() { Content = "Запомнить доступ к этому серверу на этом компьютере", IsChecked = true, Margin = new Thickness(0, 12, 0, 2) };
    readonly StackPanel hostRow = new(), manual = new(), savedPanel = new() { Visibility = Visibility.Collapsed };
    readonly TextBlock savedText = new() { TextWrapping = TextWrapping.Wrap, Foreground = Ui.Res("Green"), Margin = new Thickness(0, 8, 0, 4) };
    readonly StackPanel passPanel = new(), keyPanel = new() { Visibility = Visibility.Collapsed };
    readonly StackPanel portUser = new() { Orientation = Orientation.Horizontal };

    ServerInfo? server;
    bool useSaved, forceManual;
    readonly bool lockHost, showRemember;

    // Показывать ли галочку «запомнить»: в окне «Сервер» не нужна — там сама кнопка «Проверить и сохранить доступ».
    public AccessBox(string? host, bool lockHost = false, bool showRemember = true)
    {
        this.lockHost = lockHost; this.showRemember = showRemember;
        Children.Add(Ui.Label("Адрес сервера (IP, например 203.0.113.5)"));
        hostRow.Children.Add(hostBox);
        Children.Add(hostRow);
        var portCol = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
        portCol.Children.Add(Ui.Label("Порт SSH")); portCol.Children.Add(portBox);
        var userCol = new StackPanel();
        userCol.Children.Add(Ui.Label("Пользователь")); userCol.Children.Add(userBox);
        portUser.Children.Add(portCol); portUser.Children.Add(userCol);
        manual.Children.Add(portUser);

        manual.Children.Add(Ui.Label("Как войти на сервер"));
        var rb = new StackPanel { Orientation = Orientation.Horizontal };
        rb.Children.Add(rbPass); rb.Children.Add(rbKey);
        manual.Children.Add(rb);
        passPanel.Children.Add(Ui.Label("Пароль"));
        passPanel.Children.Add(passBox);
        var pick = new StackPanel { Orientation = Orientation.Horizontal };
        pick.Children.Add(keyFileBox);
        pick.Children.Add(Ui.Btn("Выбрать…", null, PickKey));
        keyPanel.Children.Add(Ui.Label("Файл закрытого ключа"));
        keyPanel.Children.Add(pick);
        keyPanel.Children.Add(Ui.Label("Парольная фраза ключа (если она есть)"));
        keyPanel.Children.Add(keyPassBox);
        manual.Children.Add(passPanel); manual.Children.Add(keyPanel);
        rbPass.Checked += (_, _) => { passPanel.Visibility = Visibility.Visible; keyPanel.Visibility = Visibility.Collapsed; };
        rbKey.Checked += (_, _) => { passPanel.Visibility = Visibility.Collapsed; keyPanel.Visibility = Visibility.Visible; };
        if (showRemember)
        {
            manual.Children.Add(remember);
            manual.Children.Add(Ui.Note("Тогда ставить второй протокол, переключать их и добавлять телефон можно без повторного ввода пароля. Доступ хранится зашифрованным средствами Windows (только для вашей учётной записи на этом компьютере) и никуда не отправляется. Любая программа, запущенная от вашего имени, технически может его прочитать, поэтому на чужом или общем компьютере галочку лучше снять. Забыть доступ можно в любой момент: кнопка «Сервер…» в списке серверов.", 11.5));
        }
        else manual.Children.Add(Ui.Note("Пароль или ключ шифруются средствами Windows (только для вашей учётной записи на этом компьютере) и никуда не отправляются.", 11.5));

        var other = Ui.Btn("Ввести другой доступ", null, () => { forceManual = true; Refresh(); }, first: true);
        savedPanel.Children.Add(savedText);
        savedPanel.Children.Add(other);

        Children.Add(savedPanel);
        Children.Add(manual);

        hostBox.IsReadOnly = lockHost;
        hostBox.LostFocus += (_, _) => { forceManual = false; Refresh(); };
        if (!string.IsNullOrWhiteSpace(host)) hostBox.Text = host.Trim();
        Refresh();
    }

    public string HostText => hostBox.Text.Trim();
    public bool UsingSaved => useSaved;
    public ServerInfo? Server => server;

    public void SetHost(string host) { hostBox.Text = host; forceManual = false; Refresh(); }

    void PickKey()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Выберите закрытый ключ SSH", Filter = "Все файлы|*.*" };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true) keyFileBox.Text = dlg.FileName;
    }

    // По адресу ищем сервер с сохранённым доступом: если он есть, поля прячем.
    void Refresh()
    {
        var host = HostText;
        server = host.Length > 0 ? Servers.Find(host) : null;
        SavedAccess? sa = server is { HasAccess: true } && !forceManual ? Servers.LoadAccess(server.Id) : null;
        useSaved = sa != null && server != null;
        if (useSaved)
        {
            // в поле остаётся адрес, по которому заходим по SSH (он может отличаться от адреса в профиле)
            if (!lockHost && server!.SshAddress.Length > 0) hostBox.Text = server.SshAddress;
            portBox.Text = server!.SshPort.ToString(); userBox.Text = server.SshUser;
            savedText.Text = Servers.SavedSentence(server, sa!);
        }
        savedPanel.Visibility = useSaved ? Visibility.Visible : Visibility.Collapsed;
        manual.Visibility = useSaved ? Visibility.Collapsed : Visibility.Visible;
    }

    // После отказа сервера принять сохранённые данные (поменяли пароль): показываем поля.
    public void SwitchToManual()
    {
        forceManual = true; Refresh();
    }

    // Данные для входа: сохранённые или введённые. null и текст проблемы, если их нельзя использовать.
    public SshTarget? Build(out string? error)
    {
        error = null;
        if (useSaved && server != null)
        {
            var saved = Servers.Target(server, out var problem);
            if (saved == null) { error = problem; forceManual = true; Refresh(); return null; }
            return saved;
        }
        var t = new SshTarget { Host = hostBox.Text.Trim(), Port = int.TryParse(portBox.Text.Trim(), out var p) ? p : 22, User = userBox.Text.Trim() };
        if (rbKey.IsChecked == true) { t.KeyFile = keyFileBox.Text.Trim(); t.KeyPass = keyPassBox.Password; }
        else t.Password = passBox.Password;
        error = Provisioner.ValidateTarget(t);
        return error == null ? t : null;
    }

    // После успешного входа: запомнить сервер и (если человек так выбрал) сохранить доступ. force — для кнопки «Проверить и сохранить».
    public ServerInfo? Persist(SshTarget used, string endpointHost, string suggestedName, bool force = false)
    {
        if (endpointHost.Length == 0) return null;
        if (useSaved && server != null) return server;
        var s = Servers.Remember(endpointHost, used.Host, used.Port, used.User, suggestedName);
        if (force || (showRemember && remember.IsChecked == true))
        {
            try
            {
                Servers.SaveAccess(s.Id, new SavedAccess { Password = used.Password, KeyFile = used.KeyFile, KeyPass = used.KeyPass });
                s.HasAccess = true;
            }
            catch { }
        }
        server = s;
        return s;
    }

    public void ClearSecrets() { passBox.Clear(); keyPassBox.Clear(); }
}
