using System.Windows;
using System.Windows.Controls;

namespace GatewayPlusApp;

// Окно «Сервер»: подпись в списке, проверка и сохранение доступа по SSH (чтобы не вводить пароль заново при установке второго протокола и добавлении телефона),
// «забыть доступ» и «убрать сервер из списка». Самого сервера у хостера окно не меняет.
sealed class ServerWindow : Window
{
    public enum Outcome { None, Changed, Removed }

    readonly ServerRow row;
    readonly bool connected;
    readonly AccessBox access;
    readonly TextBox nameBox = new() { Width = 300, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = 40 };
    readonly TextBox logBox = new() { IsReadOnly = true, FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("Mono"), FontSize = 12, Height = 110, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.NoWrap, Padding = new Thickness(8) };
    readonly TextBlock msg = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    readonly StackPanel page = new();
    readonly ScrollViewer scroll;
    Button test = null!, forget = null!;
    CancellationTokenSource? cts;
    bool running;

    public Outcome Result { get; private set; } = Outcome.None;

    public ServerWindow(ServerRow row, bool connected)
    {
        this.row = row; this.connected = connected;
        Ui.Setup(this, "Сервер", 700, 860);
        page.Margin = new Thickness(24, 18, 24, 18);
        scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
        Content = scroll;
        access = new AccessBox(row.Info.SshAddress, lockHost: false, showRemember: false);
        Build();
        Closing += (_, e) =>
        {
            if (!running) return;
            if (Loc.Box(this, "Идёт проверка доступа. Прервать?", "Сервер", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) { e.Cancel = true; return; }
            cts?.Cancel();
        };
    }

    static string ProtoLine(string proto, string? profile) => profile == null ? proto + ": не установлен" : proto + ": профиль «" + profile + "»";

    void Build()
    {
        var info = row.Info;
        page.Children.Add(Ui.Title("СЕРВЕР"));
        page.Children.Add(new TextBlock { Text = Servers.Label(info) + (info.Host.Length > 0 && Servers.Label(info) != info.Host ? "  ·  " + info.Host : ""), Foreground = Ui.Res("Accent2"), FontSize = 15, Margin = new Thickness(0, 6, 0, 4), TextWrapping = TextWrapping.Wrap });
        page.Children.Add(Ui.Note(ProtoLine("WireGuard", row.Wg), 12.5));
        page.Children.Add(Ui.Note(ProtoLine("GSP", row.Gsp), 12.5));
        if (row.Extra.Count > 0) page.Children.Add(Ui.Note("Ещё профили: " + string.Join(", ", row.Extra), 12.5));

        page.Children.Add(Ui.Label("Название сервера в списке"));
        nameBox.Text = info.Name;
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(nameBox);
        nameRow.Children.Add(Ui.Btn("Сохранить название", null, SaveName));
        page.Children.Add(nameRow);

        page.Children.Add(Ui.Label("Доступ к серверу по SSH"));
        page.Children.Add(Ui.Note("Нужен только для установки второго протокола, добавления телефона и «Проверить и ускорить сервер». Для обычной работы шлюза доступ не нужен. Сохранённый доступ шифруется средствами Windows и никуда не отправляется; забыть его можно здесь же.", 12));
        page.Children.Add(access);

        var accessButtons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        test = Ui.Btn("Проверить и сохранить доступ", "Primary", Test, first: true);
        forget = Ui.Btn("Забыть сохранённый доступ", null, Forget);
        forget.IsEnabled = info.HasAccess;
        accessButtons.Children.Add(test); accessButtons.Children.Add(forget);
        page.Children.Add(accessButtons);
        page.Children.Add(Ui.Label("Что происходит"));
        page.Children.Add(Ui.Box(logBox));
        page.Children.Add(msg);

        page.Children.Add(Ui.Label("Убрать сервер из списка"));
        page.Children.Add(Ui.Note("Удаляет из этого плагина все профили сервера и сохранённый доступ. Сам сервер у хостера не меняется и продолжает работать (арендовать или отключить его вы можете только у хостера). Восстановить профили можно, только если у вас осталась копия конфигов.", 12));
        var rm = Ui.Btn("Убрать сервер из списка…", "Danger", Remove, first: true);
        rm.IsEnabled = !connected;
        page.Children.Add(rm);
        if (connected) page.Children.Add(Ui.Note("Шлюз сейчас включён — сначала отключите его.", 12));
        page.Children.Add(Ui.Btn("Закрыть", null, () => DialogResult = Result != Outcome.None, first: true));
    }

    void Log(string line) => Dispatcher.BeginInvoke(new Action(() => { logBox.AppendText(Loc.T(line) + "\n"); logBox.ScrollToEnd(); }));
    bool Ask(string text, string title, MessageBoxImage icon) => Dispatcher.Invoke(() => Loc.Box(this, text, title, MessageBoxButton.YesNo, icon) == MessageBoxResult.Yes);

    bool ConfirmKey(HostKeyQuestion q)
    {
        if (q.Changed)
            return Ask($"ВНИМАНИЕ! Отпечаток сервера {q.Host} изменился с прошлого раза.\n\nЭто бывает, если сервер переустановили. Но так же выглядит и подмена сервера по дороге.\n\nНовый отпечаток:\n{q.Fingerprint}\n\nПродолжить, только если вы сами переустанавливали сервер.", "Отпечаток сервера изменился", MessageBoxImage.Warning);
        return Ask($"Первое подключение к серверу {q.Host}.\n\nОтпечаток ({q.Algorithm}):\n{q.Fingerprint}\n\nПродолжить?", "Подтвердите сервер", MessageBoxImage.Question);
    }

    void SaveName()
    {
        Servers.Rename(row.Info.Id, nameBox.Text);
        Result = Outcome.Changed;
        msg.Foreground = Ui.Res("Green"); msg.Text = "Название сохранено.";
    }

    async void Test()
    {
        if (running) return;
        msg.Foreground = Ui.Res("Accent2"); msg.Text = "";
        var t = access.Build(out var bad);
        if (t == null) { msg.Text = bad ?? "Проверьте данные для входа на сервер."; return; }
        running = true; test.IsEnabled = false; logBox.Clear();
        cts = new CancellationTokenSource();
        try
        {
            var res = await Provisioner.TestAsync(t, ConfirmKey, Log, cts.Token);
            if (!res.Ok)
            {
                if (res.AuthFailed && access.UsingSaved) { access.SwitchToManual(); msg.Text = res.Error + " Введите доступ заново."; }
                else msg.Text = res.Error;
                return;
            }
            var wasSaved = access.UsingSaved;
            access.Persist(t, row.Info.Host, "", force: true);
            Result = Outcome.Changed;
            forget.IsEnabled = true;
            msg.Foreground = Ui.Res("Green");
            msg.Text = wasSaved ? "Доступ работает." : "Доступ работает и сохранён: пароль больше вводить не придётся.";
        }
        catch (Exception ex) { msg.Text = "Не вышло: " + ex.Message; }
        finally { access.ClearSecrets(); t.Password = null; t.KeyPass = null; running = false; test.IsEnabled = true; }
    }

    void Forget()
    {
        if (Loc.Box(this, "Забыть сохранённый доступ к этому серверу? Профили останутся. Для установки второго протокола или добавления телефона пароль придётся ввести снова.", "Забыть доступ", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Servers.ForgetAccess(row.Info.Id);
        Result = Outcome.Changed;
        forget.IsEnabled = false;
        access.SetHost(access.HostText);
        msg.Foreground = Ui.Res("Green"); msg.Text = "Сохранённый доступ удалён.";
    }

    void Remove()
    {
        if (Loc.Box(this, $"Убрать сервер «{Servers.Label(row.Info)}» из списка? Будут удалены его профили ({string.Join(", ", row.All)}) и сохранённый доступ. Сам сервер у хостера не меняется.", "Убрать сервер", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        Servers.Remove(row);
        Result = Outcome.Removed;
        DialogResult = true;
    }

    // Служебный режим: рисует окно в PNG без показа.
    public void RenderTo(string file) => Ui.Render(this, scroll, page, file, 700);
}
