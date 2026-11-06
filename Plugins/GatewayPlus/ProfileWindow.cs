using System.Windows;
using System.Windows.Controls;

namespace GatewayPlusApp;

// Окно «Изменить профиль»: какой протокол использует этот сервер (WireGuard / GSP), правка текста конфига, переименование, удаление.
// Протокол задаёт сервер, а не подпись на кнопке: чтобы профиль работал по GSP, на сервере должен быть поставлен GSP. Поэтому для протокола,
// которого на этом сервере ещё нет, окно предлагает мастер установки (адрес подставляется), а если профиль уже есть — просто делает его выбранным.
sealed class ProfileWindow : Window
{
    public enum Outcome { None, Use, Install, Deleted, Saved }

    readonly string name;
    readonly string original;
    readonly string host;
    readonly TextBox nameBox = new() { Width = 260, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = 32 };
    readonly TextBox confBox = new() { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 230, FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("Mono"), FontSize = 12, Padding = new Thickness(8) };
    readonly TextBlock protoLine = new() { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
    readonly TextBlock msg = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    readonly StackPanel page = new();
    readonly ScrollViewer scroll;

    public Outcome Result { get; private set; } = Outcome.None;
    public string ResultName { get; private set; } = "";      // профиль, который надо сделать выбранным (Use / Saved)
    public string InstallProtocol { get; private set; } = ""; // "wg" или "gsp" (Install)
    public string InstallHost => host;
    public string InstallName { get; private set; } = "";     // предлагаемое имя нового профиля

    public ProfileWindow(string name, string conf, bool connected)
    {
        this.name = name; original = conf; host = Store.EndpointOf(conf);
        Ui.Setup(this, "Изменить профиль", 700, 860);
        page.Margin = new Thickness(24, 18, 24, 18);
        scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
        Content = scroll;
        nameBox.Text = name; confBox.Text = conf.Trim();
        Build(connected);
    }

    static bool IsGspText(string conf) => Gsp.IsGsp(conf);
    static string ProtoName(bool gsp) => gsp ? "GSP" : "WireGuard";

    // Профили на том же сервере (по адресу), по протоколам.
    (List<string> wg, List<string> gsp) Siblings()
    {
        var wg = new List<string>(); var gsp = new List<string>();
        foreach (var p in Store.List())
        {
            var c = Store.Load(p);
            if (c == null) continue;
            if (!string.Equals(Store.EndpointOf(c), host, StringComparison.OrdinalIgnoreCase) || host.Length == 0) continue;
            (IsGspText(c) ? gsp : wg).Add(p);
        }
        return (wg, gsp);
    }

    void Build(bool connected)
    {
        page.Children.Add(Ui.Title("ИЗМЕНИТЬ ПРОФИЛЬ"));
        var cur = IsGspText(original);
        page.Children.Add(new TextBlock { Text = $"Профиль «{name}» · протокол {ProtoName(cur)}" + (host.Length > 0 ? $" · сервер {host}" : ""), Foreground = Ui.Res("Accent2"), FontSize = 15, Margin = new Thickness(0, 6, 0, 8), TextWrapping = TextWrapping.Wrap });
        if (connected)
            page.Children.Add(Ui.Note("Шлюз сейчас включён — сначала отключите его, потом меняйте профиль.", 12.5));

        // 1. протокол
        page.Children.Add(Ui.Label("Какой протокол использовать на этом сервере"));
        var (wg, gsp) = Siblings();
        var box = new StackPanel();
        box.Children.Add(ProtoRow(false, wg, connected));
        box.Children.Add(ProtoRow(true, gsp, connected));
        box.Children.Add(Ui.Note("Протокол задаёт сервер: профиль — это ключи и порт для одного протокола. Поэтому, чтобы пользоваться GSP, его нужно поставить на ваш сервер (мастер сделает это сам по SSH; WireGuard на сервере при этом остаётся, они не мешают друг другу). Когда на сервере есть оба — между ними переключаются одной кнопкой «Использовать».", 11.5));
        page.Children.Add(Ui.Box(box));

        // 2. быстрая настройка защиты от потерь (только GSP)
        if (cur)
        {
            page.Children.Add(Ui.Label("Защита от потерь пакетов (FEC) — только GSP"));
            var fec = new ComboBox { Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
            var options = new[] { ("auto", "Авто — включается сама, когда связь теряет пакеты"), ("off", "Выключена — как WireGuard"), ("8+2", "Всегда: 8+2 (небольшой запас)"), ("8+3", "Всегда: 8+3"), ("8+4", "Всегда: 8+4 (для очень плохой связи)") };
            foreach (var (v, text) in options) fec.Items.Add(new ComboBoxItem { Content = text, Tag = v });
            var now = Store.InterfaceValue(original, "FEC");
            var idx = Array.FindIndex(options, o => o.Item1.Equals(now, StringComparison.OrdinalIgnoreCase));
            fec.SelectedIndex = idx >= 0 ? idx : 0;
            fec.SelectionChanged += (_, _) =>
            {
                if (fec.SelectedItem is ComboBoxItem it && it.Tag is string v) confBox.Text = Store.WithInterfaceValue(confBox.Text.Trim(), "FEC", v);
            };
            page.Children.Add(fec);
        }

        // 3. имя и текст конфига
        page.Children.Add(Ui.Label("Название профиля (латиницей)"));
        page.Children.Add(nameBox);
        page.Children.Add(Ui.Label("Текст конфига (для тех, кто знает, что менять: адрес сервера, MTU, DNS, FEC…)"));
        page.Children.Add(confBox);
        confBox.TextChanged += (_, _) => UpdateProtoLine();
        UpdateProtoLine();
        page.Children.Add(protoLine);

        var row = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var save = Ui.Btn("Сохранить изменения", "Primary", Save, first: true);
        save.IsEnabled = !connected;
        row.Children.Add(save);
        row.Children.Add(Ui.Btn("Закрыть", null, () => DialogResult = Result != Outcome.None));
        var del = Ui.Btn("Удалить профиль", "Danger", Delete);
        del.IsEnabled = !connected;
        row.Children.Add(del);
        page.Children.Add(row);
        page.Children.Add(msg);
    }

    void UpdateProtoLine()
    {
        var g = IsGspText(confBox.Text);
        var changed = g != IsGspText(original);
        protoLine.Text = "Протокол по тексту: " + ProtoName(g) + (changed ? " — отличается от сохранённого, см. подсказку при сохранении" : "");
        protoLine.Foreground = changed ? Ui.Res("Accent2") : Ui.Res("Muted");
    }

    UIElement ProtoRow(bool forGsp, List<string> have, bool connected)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = ProtoName(forGsp);
        var thisIsCurrent = IsGspText(original) == forGsp;
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = label + (thisIsCurrent ? "  ·  этот профиль" : ""), FontWeight = FontWeights.SemiBold });
        string note;
        Button btn;
        if (have.Count > 0)
        {
            var target = thisIsCurrent ? name : have[0];
            note = thisIsCurrent
                ? "Этот профиль уже работает по " + label + "."
                : "На этом сервере уже есть профиль «" + target + "» по " + label + ".";
            btn = Ui.Btn(thisIsCurrent ? "Выбран" : "Использовать «" + target + "»", thisIsCurrent ? null : "Primary", () =>
            {
                Result = Outcome.Use; ResultName = target; DialogResult = true;
            }, first: true);
            btn.IsEnabled = !thisIsCurrent && !connected;
        }
        else
        {
            note = host.Length == 0
                ? "Адрес сервера в профиле не найден — поставить " + label + " мастером нельзя."
                : "На сервере " + host + " " + label + " ещё не поставлен. Мастер зайдёт по SSH и поставит его, адрес подставится сам.";
            btn = Ui.Btn("Поставить на этом сервере…", null, () =>
            {
                Result = Outcome.Install; InstallProtocol = forGsp ? "gsp" : "wg";
                InstallName = Store.SafeName(name + (forGsp ? "-gsp" : "-wg"));
                DialogResult = true;
            }, first: true);
            btn.IsEnabled = host.Length > 0 && !connected;
        }
        sp.Children.Add(new TextBlock { Text = note, Foreground = Ui.Res("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 12, 0) });
        Grid.SetColumn(sp, 0); Grid.SetColumn(btn, 1);
        btn.VerticalAlignment = VerticalAlignment.Center; btn.Margin = new Thickness(0);
        grid.Children.Add(sp); grid.Children.Add(btn);
        return grid;
    }

    void Save()
    {
        msg.Foreground = Ui.Res("Accent2");
        var conf = confBox.Text.Trim() + "\n";
        var bad = Store.Validate(conf);
        if (bad != null) { msg.Text = bad; return; }
        var newName = Store.SafeName(nameBox.Text);
        if (!Store.ValidName(newName)) { msg.Text = "Название: латинские буквы, цифры, - _ . (до 32 знаков)."; return; }
        if (!newName.Equals(name, StringComparison.Ordinal) && Store.List().Contains(newName, StringComparer.OrdinalIgnoreCase)) { msg.Text = "Профиль с таким названием уже есть."; return; }
        var was = IsGspText(original); var now = IsGspText(conf);
        if (was != now)
        {
            var q = $"Протокол профиля меняется: {ProtoName(was)} → {ProtoName(now)}.\n\nЭто сработает, только если на сервере порт и ключи в этом тексте — именно для {ProtoName(now)}. Обычно для смены протокола нужно поставить его на сервер кнопкой «Поставить на этом сервере…» выше, а не править текст.\n\nВсё равно сохранить?";
            if (MessageBox.Show(this, q, "Смена протокола", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        }
        try
        {
            var wasActive = Store.Active.Equals(name, StringComparison.Ordinal);
            Store.Save(newName, conf);
            if (!newName.Equals(name, StringComparison.Ordinal)) { Store.Delete(name); if (wasActive) Store.Active = newName; }
            Result = Outcome.Saved; ResultName = newName;
            DialogResult = true;
        }
        catch (Exception ex) { msg.Text = "Не сохранилось: " + ex.Message; }
    }

    void Delete()
    {
        if (MessageBox.Show(this, $"Удалить профиль «{name}»? Сам сервер не меняется. Восстановить конфиг можно, только если у вас осталась его копия.", "Удалить профиль", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Store.Delete(name);
        if (Store.Active.Equals(name, StringComparison.Ordinal)) Store.Active = Store.List().FirstOrDefault() ?? "";
        Result = Outcome.Deleted;
        DialogResult = true;
    }

    // Служебный режим: рисует окно в PNG без показа.
    public void RenderTo(string file) => Ui.Render(this, scroll, page, file, 700);
}
