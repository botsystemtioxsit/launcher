using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GatewayPlusApp;

// Окно «Аккаунт»: создать новый (получить ключ) или войти по ключу. Тут же правила — без согласия с ними аккаунт не создаётся.
sealed class AccountWindow : Window
{
    readonly StackPanel page = new();
    readonly ScrollViewer scroll;
    readonly TextBox nameBox = new() { MaxLength = 30, Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
    readonly TextBox keyBox = new() { MaxLength = 80, FontFamily = (FontFamily)Application.Current.FindResource("Mono") };
    readonly CheckBox accept = new() { Content = "Я прочитал(а) правила и согласен(на): только личное использование, без коммерции", Margin = new Thickness(0, 10, 0, 4) };
    readonly TextBlock msg = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = Ui.Res("Accent2") };
    bool loginMode, busy;

    public Account.Info? Result { get; private set; }

    readonly bool readOnly;

    public AccountWindow(bool loginMode, bool termsOnly = false, bool readOnly = false)
    {
        this.loginMode = loginMode;
        this.readOnly = readOnly;
        Ui.Setup(this, "Аккаунт Gateway Plus", 680, 780);
        page.Margin = new Thickness(24, 18, 24, 18);
        scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
        Content = scroll;
        if (termsOnly) BuildTermsOnly(); else BuildForm();
    }

    TextBlock RulesText() => new() { Text = Terms.Text, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };

    UIElement RulesBox() => Ui.Box(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = RulesText() }, 190);

    void BuildForm()
    {
        page.Children.Clear();
        page.Children.Add(Ui.Title("АККАУНТ GATEWAY PLUS"));
        page.Children.Add(Ui.Note("Бесплатный. Без пароля и почты: вам выдаётся ключ, он и есть вход. Аккаунт нужен, чтобы действовали правила (только личное использование) — владелец проекта может закрыть доступ тем, кто их нарушает."));

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        var bNew = Ui.Btn("Новый аккаунт", loginMode ? null : "Primary", () => { loginMode = false; BuildForm(); }, first: true);
        var bKey = Ui.Btn("У меня есть ключ", loginMode ? "Primary" : null, () => { loginMode = true; BuildForm(); });
        tabs.Children.Add(bNew); tabs.Children.Add(bKey);
        page.Children.Add(tabs);

        if (loginMode)
        {
            page.Children.Add(Ui.Label("Ключ аккаунта (GW-XXXX-XXXX-…)"));
            page.Children.Add(keyBox);
            page.Children.Add(Ui.Label("Имя (нужно, только если аккаунта в базе уже нет — тогда он создастся заново)"));
        }
        else page.Children.Add(Ui.Label("Имя аккаунта (до 30 знаков)"));
        page.Children.Add(nameBox);
        page.Children.Add(Ui.Note("Имя видит только владелец проекта. Не пишите настоящее имя — придумайте любое."));

        page.Children.Add(Ui.Label("Правила"));
        page.Children.Add(RulesBox());
        page.Children.Add(accept);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        row.Children.Add(Ui.Btn(loginMode ? "Войти" : "Создать аккаунт", "Primary", Submit, first: true));
        row.Children.Add(Ui.Btn("Отмена", null, () => DialogResult = false));
        page.Children.Add(row);
        page.Children.Add(msg);
    }

    // Только правила (когда они обновились): согласиться или отказаться.
    void BuildTermsOnly()
    {
        page.Children.Clear();
        page.Children.Add(Ui.Title("ПРАВИЛА GATEWAY PLUS"));
        if (readOnly)
        {
            page.Children.Add(Ui.Note("Версия правил: " + Terms.Version + ". Только личное использование; коммерческое использование — причина для бана."));
            page.Children.Add(RulesBox());
            page.Children.Add(Ui.Btn("Закрыть", "Primary", () => DialogResult = true, first: true));
            return;
        }
        page.Children.Add(Ui.Note("Правила обновились. Чтобы продолжить пользоваться шлюзом и мастером, прочитайте и согласитесь."));
        page.Children.Add(RulesBox());
        page.Children.Add(accept);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        row.Children.Add(Ui.Btn("Согласен(на)", "Primary", () =>
        {
            if (accept.IsChecked != true) { msg.Text = "Отметьте, что вы прочитали правила и согласны."; return; }
            DialogResult = true;
        }, first: true));
        row.Children.Add(Ui.Btn("Отказаться", null, () => DialogResult = false));
        page.Children.Add(row);
        page.Children.Add(msg);
    }

    async void Submit()
    {
        if (busy) return;
        msg.Foreground = Ui.Res("Accent2"); msg.Text = "";
        var name = Account.CleanName(nameBox.Text);
        string? raw = null;
        if (loginMode)
        {
            raw = Account.Normalize(keyBox.Text);
            if (raw == null) { msg.Text = "Ключ не похож на настоящий: он выглядит как GW-ABCD-EFGH-… (32 буквы и цифры)."; return; }
        }
        else if (name.Length == 0) { msg.Text = "Введите имя аккаунта."; return; }
        if (accept.IsChecked != true) { msg.Text = "Отметьте, что вы прочитали правила и согласны с ними."; return; }

        busy = true;
        msg.Foreground = Ui.Res("Muted"); msg.Text = "Подключаюсь к базе…";
        try
        {
            var dev = Account.DeviceHash();
            var servers = Store.List().Count;
            if (loginMode)
            {
                var id = Account.IdOf(raw!);
                var remote = await Cloud.GetAccountAsync(id);
                if (remote == null)
                {
                    if (name.Length == 0) { msg.Foreground = Ui.Res("Accent2"); msg.Text = "Аккаунта с таким ключом в базе нет. Если ключ верный и вы хотите создать аккаунт заново, введите имя."; return; }
                    await Cloud.RegisterAsync(id, name, dev, Ui.Version, servers);
                }
                else if (name.Length == 0) name = Account.CleanName(remote.Name);
                if (name.Length == 0) name = "Gateway";
                var info = new Account.Info { Key = raw!, Name = name, TermsV = Terms.Version };
                Account.Save(info);
                await Cloud.HeartbeatAsync(id, dev, Ui.Version, servers, Terms.Version);
                Result = info;
                DialogResult = true;
            }
            else
            {
                var acc = await Cloud.CheckAccessAsync(null, dev);
                if (acc.Blocked && !acc.Offline) { msg.Foreground = Ui.Res("Accent2"); msg.Text = "Этот компьютер заблокирован владельцем проекта: " + acc.Reason; return; }
                raw = Account.NewKey();
                await Cloud.RegisterAsync(Account.IdOf(raw), name, dev, Ui.Version, servers);
                var info = new Account.Info { Key = raw, Name = name, TermsV = Terms.Version };
                Account.Save(info);
                Result = info;
                ShowKey(info);
            }
        }
        catch (Exception ex) { msg.Foreground = Ui.Res("Accent2"); msg.Text = "Не получилось: " + ex.Message; }
        finally { busy = false; }
    }

    // Ключ показывается один раз (потом — кнопкой «Показать ключ» в окне плагина).
    void ShowKey(Account.Info info)
    {
        page.Children.Clear();
        page.Children.Add(Ui.Title("АККАУНТ СОЗДАН"));
        page.Children.Add(Ui.Note("Это ключ вашего аккаунта. Он нужен, чтобы войти на другом компьютере или после переустановки Windows. Запишите его и никому не показывайте: кто знает ключ, тот и хозяин аккаунта. Потеряете — аккаунт не восстановить (заведёте новый).", 13));
        var key = new TextBox { Text = Account.Format(info.Key), IsReadOnly = true, FontFamily = (FontFamily)Application.Current.FindResource("Mono"), FontSize = 17, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 10) };
        page.Children.Add(key);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Ui.Btn("Скопировать", null, () => { try { Clipboard.SetText(Account.Format(info.Key)); msg.Foreground = Ui.Res("Green"); msg.Text = "Ключ скопирован."; } catch { msg.Text = "Не удалось скопировать."; } }, first: true));
        row.Children.Add(Ui.Btn("Сохранить в файл…", null, () =>
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = "gateway-plus-key.txt", Filter = "Текст|*.txt" };
            if (dlg.ShowDialog(this) != true) return;
            try { File.WriteAllText(dlg.FileName, "Ключ аккаунта Gateway Plus (" + info.Name + "):\n" + Account.Format(info.Key) + "\n\nНикому не показывайте этот файл.\n"); msg.Foreground = Ui.Res("Green"); msg.Text = "Сохранено."; }
            catch (Exception ex) { msg.Foreground = Ui.Res("Accent2"); msg.Text = "Не сохранилось: " + ex.Message; }
        }));
        page.Children.Add(row);
        var saved = new CheckBox { Content = "Я сохранил(а) ключ в надёжном месте", Margin = new Thickness(0, 12, 0, 8) };
        var done = Ui.Btn("Готово", "Primary", () => DialogResult = true, first: true);
        done.IsEnabled = false;
        saved.Checked += (_, _) => done.IsEnabled = true;
        saved.Unchecked += (_, _) => done.IsEnabled = false;
        page.Children.Add(saved);
        page.Children.Add(done);
        page.Children.Add(msg);
    }

    // Служебный режим: рисует окно в PNG без показа.
    public void RenderTo(string file) => Ui.Render(this, scroll, page, file, 680);
}
