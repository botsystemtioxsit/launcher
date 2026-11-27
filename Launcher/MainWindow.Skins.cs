using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BattleLauncher;

// «Скины»: выбор оформления лаунчера. Скин меняет цвета, скругления, шрифт и тени; устройство окна (меню слева, блоки) остаётся прежним.
// Картинки — концепты: так оформление задумано; в самом лаунчере оно получается близким, но не пиксель в пиксель.
public partial class MainWindow
{
    // ---------- значки в меню и карточках ----------
    readonly Dictionary<Button, string> navText = new();

    // Пункты меню слева: значок + подпись. Подписи берём из разметки один раз; значки перерисовываются при смене скина.
    void SetNavIcons()
    {
        var items = new (Button Btn, string Icon)[]
        {
            (NavApps, "apps"), (NavStyle, "style"), (NavNet, "net"), (NavExt, "ext"), (NavEnv, "env"),
            (NavPhone, "phone"), (NavFeedback, "feedback"), (NavAccount, "account"),
        };
        foreach (var (btn, icon) in items)
        {
            if (!navText.TryGetValue(btn, out var text)) navText[btn] = text = btn.Content as string ?? "";
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Border { Width = 22, Margin = new Thickness(0, 0, 10, 0), Child = Icons.Make(icon, 16, null, true, false), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            btn.Content = row;
        }
    }

    // Строка заголовка карточки: значок слева от названия
    UIElement TitleRow(string title, string icon, double fontSize = 17)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Border { Margin = new Thickness(0, 0, 12, 0), Child = Icons.Make(icon, 20, null, false, true), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = title, FontSize = fontSize, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }
}
