using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;

namespace GatewayPlusApp;

/// <summary>
/// QR-код выбранного профиля для телефона: в приложении WireGuard (Android/iPhone) «+» → «Сканировать QR-код».
/// Внутри QR — весь конфиг, в том числе закрытый ключ: показывается только на экране, никуда не сохраняется и не отправляется.
/// Один и тот же профиль на двух устройствах сразу не работает (WireGuard опознаёт устройство по ключу), поэтому подключайте по очереди.
/// </summary>
sealed class QrWindow : Window
{
    public QrWindow(string profile, string conf)
    {
        Title = "Для телефона — " + profile;
        Width = 460; Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Ui.Res("Bg") as Brush ?? Brushes.Black;
        ResizeMode = ResizeMode.NoResize;

        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(new TextBlock { Text = "Профиль «" + profile + "» для телефона", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Ui.Res("Accent") as Brush ?? Brushes.Cyan, TextWrapping = TextWrapping.Wrap });

        BitmapImage? img = null;
        string? err = null;
        try
        {
            using var gen = new QRCodeGenerator();
            using var data = gen.CreateQrCode(conf, QRCodeGenerator.ECCLevel.L);
            var png = new PngByteQRCode(data).GetGraphic(8);
            img = new BitmapImage();
            using (var ms = new MemoryStream(png))
            {
                img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.StreamSource = ms; img.EndInit();
            }
            img.Freeze();
        }
        catch (Exception ex) { err = "Не получилось сделать QR-код: " + ex.Message; }

        if (img != null)
            root.Children.Add(new Border { Background = Brushes.White, Padding = new Thickness(10), Margin = new Thickness(0, 14, 0, 10), HorizontalAlignment = HorizontalAlignment.Center, Child = new Image { Source = img, Width = 320, Height = 320, SnapsToDevicePixels = true } });
        else
            root.Children.Add(new TextBlock { Text = err, Foreground = Ui.Res("Accent2") as Brush ?? Brushes.Red, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 10) });

        root.Children.Add(Note("1. На телефоне установите приложение WireGuard (Google Play или App Store).\n2. Нажмите «+» → «Сканировать QR-код» и наведите камеру на экран.\n3. Включите туннель."));
        root.Children.Add(Note("Важно: в этом QR-коде ваш закрытый ключ. Никому не показывайте и не фотографируйте его. Профиль работает на одном устройстве за раз: телефон и компьютер включайте по очереди, иначе они будут выбивать друг друга."));

        var close = new Button { Content = "Закрыть", Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(26, 9, 26, 9), HorizontalAlignment = HorizontalAlignment.Left };
        close.Click += (_, _) => Close();
        root.Children.Add(close);
        Content = root;
    }

    static TextBlock Note(string t) => new()
    {
        Text = t, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
        Foreground = Ui.Res("Muted") as Brush ?? Brushes.Gray,
    };
}
