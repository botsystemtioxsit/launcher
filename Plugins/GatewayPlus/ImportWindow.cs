using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GatewayPlusApp;

// Окно «Добавить конфиг»: вставить текст конфига WireGuard или открыть файл .conf.
sealed class ImportWindow : Window
{
    readonly TextBox nameBox = new() { Text = "germany", Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
    readonly TextBox confBox = new() { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 280, FontFamily = new FontFamily("Consolas"), FontSize = 12, Padding = new Thickness(8) };
    readonly TextBlock msg = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

    public string? SavedName { get; private set; }

    static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    public ImportWindow()
    {
        Title = "Добавить конфиг шлюза";
        Width = 640; Height = 600; MinWidth = 520; MinHeight = 480;
        Background = Res("Bg"); Foreground = Res("Text");
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var page = new StackPanel { Margin = new Thickness(24, 18, 24, 18) };
        page.Children.Add(new TextBlock { Text = "ДОБАВИТЬ КОНФИГ", FontFamily = (FontFamily)Application.Current.FindResource("Mono"), FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Res("Accent") });
        page.Children.Add(new TextBlock { Text = "Вставьте сюда текст конфига WireGuard или GSP (его печатает скрипт сервера или даёт ваш VPN-сервис) или откройте файл .conf. Конфиг хранится в зашифрованном виде и расшифровывается только вашим пользователем Windows.", Foreground = Res("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12) });
        page.Children.Add(new TextBlock { Text = "Название (латиницей, например germany)", Margin = new Thickness(0, 0, 0, 4) });
        page.Children.Add(nameBox);
        page.Children.Add(new TextBlock { Text = "Конфиг", Margin = new Thickness(0, 12, 0, 4) });
        page.Children.Add(confBox);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var open = new Button { Content = "Открыть файл…", Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0) };
        open.Click += (_, _) => OpenFile();
        var save = new Button { Content = "Сохранить", Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(0, 0, 8, 0), Style = (Style)FindResource("Primary") };
        save.Click += (_, _) => Save();
        var cancel = new Button { Content = "Отмена", Padding = new Thickness(18, 8, 18, 8) };
        cancel.Click += (_, _) => DialogResult = false;
        row.Children.Add(open); row.Children.Add(save); row.Children.Add(cancel);
        page.Children.Add(row);
        page.Children.Add(msg);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
    }

    void OpenFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Конфиг WireGuard / GSP|*.conf;*.txt|Все файлы|*.*", Title = "Выберите файл конфига" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var fi = new FileInfo(dlg.FileName);
            if (fi.Length > 20000) { msg.Text = "Файл слишком большой для конфига."; return; }
            confBox.Text = File.ReadAllText(dlg.FileName);
            nameBox.Text = Store.SafeName(Path.GetFileNameWithoutExtension(dlg.FileName));
            msg.Text = "";
        }
        catch (Exception ex) { msg.Text = "Не получилось открыть: " + ex.Message; }
    }

    void Save()
    {
        var conf = confBox.Text.Trim() + "\n";
        var bad = Store.Validate(conf);
        if (bad != null) { msg.Text = bad; return; }
        var name = Store.SafeName(nameBox.Text);
        try
        {
            Store.Save(name, conf);
            Store.Active = name;
            SavedName = name;
            DialogResult = true;
        }
        catch (Exception ex) { msg.Text = "Не сохранилось: " + ex.Message; }
    }
}
