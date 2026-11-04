using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DnsPlusApp;

public class MainWindow : Window
{
    readonly TextBox idBox = new();
    readonly TextBlock stateText = new();
    readonly TextBlock msgText = new();
    readonly Button onBtn = new() { Content = "Включить" };
    readonly Button offBtn = new() { Content = "Выключить и вернуть как было" };
    readonly Button checkBtn = new() { Content = "Проверить" };

    static SolidColorBrush B(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    public MainWindow()
    {
        Title = "DNS Plus";
        Width = 520; Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = B("#06050D");
        Foreground = B("#E6F4FF");
        FontFamily = new FontFamily("Segoe UI");

        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(Label("DNS Plus", 24, "#00F0FF", true));
        root.Children.Add(Label("Защищённый DNS NextDNS для всей Windows. Вставьте свой ID и нажмите «Включить».", 13, "#8B86B8"));

        root.Children.Add(Label("Configuration ID (6 знаков из my.nextdns.io → Setup)", 13, "#E6F4FF", false, 18));
        idBox.FontSize = 18; idBox.Padding = new Thickness(10, 8, 10, 8); idBox.MaxLength = 80;
        idBox.Background = B("#15112B"); idBox.Foreground = B("#E6F4FF"); idBox.CaretBrush = B("#E6F4FF");
        idBox.BorderBrush = B("#2D2060");
        idBox.Text = Dns.LoadId();
        root.Children.Add(idBox);
        root.Children.Add(Label("Можно вставить и всю ссылку, например https://dns.nextdns.io/abc123 — ID возьмётся сам.", 12, "#8B86B8"));

        stateText.FontSize = 16; stateText.FontWeight = FontWeights.Bold; stateText.Margin = new Thickness(0, 20, 0, 0);
        root.Children.Add(stateText);
        msgText.TextWrapping = TextWrapping.Wrap; msgText.Margin = new Thickness(0, 6, 0, 0); msgText.Foreground = B("#E6F4FF");
        root.Children.Add(msgText);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 0, 0) };
        Style(onBtn, "#00F0FF", "#001418"); Style(offBtn, "#15112B", "#E6F4FF"); Style(checkBtn, "#15112B", "#E6F4FF");
        row.Children.Add(onBtn); row.Children.Add(checkBtn);
        root.Children.Add(row);
        offBtn.Margin = new Thickness(0, 10, 0, 0); offBtn.HorizontalAlignment = HorizontalAlignment.Left;
        root.Children.Add(offBtn);

        var card = new Border
        {
            Margin = new Thickness(0, 18, 0, 0), Padding = new Thickness(12), CornerRadius = new CornerRadius(3),
            Background = B("#0D0B1A"), BorderBrush = B("#2D2060"), BorderThickness = new Thickness(1)
        };
        var cardPanel = new StackPanel();
        cardPanel.Children.Add(Label("Откуда берётся DNS", 14, "#00F0FF", true, 0));
        cardPanel.Children.Add(Label("Мы не умеем и не делаем то, что умеет сервис NextDNS (фильтры, блокировка рекламы и угроз, родительский контроль, журнал запросов). Поэтому плагин только подключает ваш компьютер к нему. NextDNS бесплатный (есть бесплатный тариф), регистрация быстрая. Наш совет: заведите профиль на nextdns.io, настройте фильтры там и берите DNS оттуда — а DNS Plus просто включит его на этом компьютере. Мы с NextDNS не связаны.", 12, "#E6F4FF", false, 6));
        var link = new Button { Content = "Открыть nextdns.io", Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        Style(link, "#15112B", "#00F0FF");
        link.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://nextdns.io") { UseShellExecute = true }); }
            catch { msgText.Text = "Не удалось открыть браузер. Адрес: https://nextdns.io"; }
        };
        cardPanel.Children.Add(link);
        card.Child = cardPanel;
        root.Children.Add(card);

        root.Children.Add(Label("Как это работает: плагин включает встроенный в Windows 11 «DNS через HTTPS» с вашим ID. Своего DNS-клиента нет, фоном ничего не работает — настройка живёт в самой Windows. «Выключить» возвращает прежние DNS. Один запрос Windows «Разрешить?» на действие. Нужна Windows 11.", 12, "#8B86B8", false, 20));
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        onBtn.Click += async (_, _) => await Run(() => Dns.EnableAsync(idBox.Text), true);
        offBtn.Click += async (_, _) => await Run(() => Dns.DisableAsync(), false);
        checkBtn.Click += async (_, _) => { SetBusy(true); msgText.Text = "Проверяю…"; msgText.Text = await Dns.CheckAsync(); SetBusy(false); };

        Refresh();
        if (!Dns.Windows11Plus())
            msgText.Text = "Этот компьютер на Windows 10: встроенного DNS через HTTPS нет. Используйте официальное приложение NextDNS или включите «Безопасный DNS» в браузере.";
    }

    static TextBlock Label(string t, double size, string color, bool bold = false, double top = 6) => new()
    {
        Text = t, FontSize = size, Foreground = B(color), TextWrapping = TextWrapping.Wrap,
        FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Margin = new Thickness(0, top, 0, 0)
    };

    static void Style(Button b, string bg, string fg)
    {
        b.Background = B(bg); b.Foreground = B(fg); b.BorderBrush = B("#2D2060");
        b.Padding = new Thickness(18, 9, 18, 9); b.Margin = new Thickness(0, 0, 10, 0); b.Cursor = System.Windows.Input.Cursors.Hand;
    }

    void SetBusy(bool busy) { onBtn.IsEnabled = offBtn.IsEnabled = checkBtn.IsEnabled = !busy; }

    void Refresh()
    {
        var on = Dns.IsOn();
        stateText.Text = on ? "● NextDNS включён" : "○ NextDNS выключен";
        stateText.Foreground = B(on ? "#2CFF9A" : "#8B86B8");
        offBtn.IsEnabled = on;
    }

    async Task Run(Func<Task<(bool ok, string msg)>> action, bool check)
    {
        SetBusy(true);
        msgText.Text = "Выполняю… (если Windows спросит «Разрешить?» — нажмите «Да»)";
        var (ok, msg) = await action();
        Refresh();
        if (ok && check) { msgText.Text = "Включено. Проверяю…"; await Task.Delay(1500); msgText.Text = await Dns.CheckAsync(); }
        else msgText.Text = ok ? "Выключено, прежние настройки DNS возвращены." : msg;
        SetBusy(false);
        offBtn.IsEnabled = Dns.IsOn();
    }
}
