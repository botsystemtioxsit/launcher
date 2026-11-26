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
    static BitmapImage? SkinPreview(string id)
    {
        try
        {
            using var s = typeof(MainWindow).Assembly.GetManifestResourceStream("skin/" + id + ".jpg");
            if (s == null) return null;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = s;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    // Для скина без готовой картинки: мини-макет окна, нарисованный его же цветами.
    static UIElement MockPreview(Skin s)
    {
        Brush B(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        var g = new Grid { Height = 197, Margin = new Thickness(0, 0, 0, 8), Background = B(s.Bg) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var side = new StackPanel { Background = B(s.Panel) };
        for (int i = 0; i < 6; i++)
            side.Children.Add(new Border { Height = 8, Margin = new Thickness(10, i == 0 ? 14 : 8, 10, 0), CornerRadius = new CornerRadius(2), Background = B(i == 0 ? s.Accent : s.Muted), Opacity = i == 0 ? 1 : 0.5 });
        g.Children.Add(side);
        var main = new StackPanel { Margin = new Thickness(14, 14, 14, 0) };
        Grid.SetColumn(main, 1);
        main.Children.Add(new Border { Height = 10, Width = 90, CornerRadius = new CornerRadius(2), Background = B(s.Text), HorizontalAlignment = HorizontalAlignment.Left });
        for (int i = 0; i < 2; i++)
        {
            var card = new Border { Height = 46, Margin = new Thickness(0, 12, 0, 0), CornerRadius = new CornerRadius(Math.Min(s.CardR, 14)), Background = B(s.Panel2), BorderBrush = B(s.Line), BorderThickness = new Thickness(1) };
            var row = new Grid();
            row.Children.Add(new Border { Height = 7, Width = 70, Margin = new Thickness(10, 0, 0, 0), CornerRadius = new CornerRadius(2), Background = B(s.Text), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new Border { Height = 18, Width = 44, Margin = new Thickness(0, 0, 10, 0), CornerRadius = new CornerRadius(Math.Min(s.BtnR, 9)), Background = B(i == 0 ? s.Accent : s.Accent2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
            card.Child = row;
            main.Children.Add(card);
        }
        g.Children.Add(main);
        return new Border { ClipToBounds = true, Child = g };
    }

    void BuildSkins()
    {
        SkinsBody.Children.Clear();
        SkinsBody.Children.Add(new TextBlock { Text = "Скины", FontSize = 22, FontWeight = FontWeights.SemiBold });
        SkinsBody.Children.Add(new TextBlock
        {
            Text = "Выберите оформление лаунчера: оно применяется сразу и запоминается. Скин меняет цвета, скругления, шрифт и тени. " +
                   "Расположение меню и блоков остаётся прежним, поэтому картинки показывают задумку, а не точный вид окна.",
            Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 16, 14),
        });
        foreach (var group in Skins.All.Select(s => s.Group).Distinct())
        {
            SkinsBody.Children.Add(new TextBlock { Text = group.ToUpperInvariant(), Foreground = Res("Accent"), FontFamily = (FontFamily)FindResource("Mono"), FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 8) });
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var s in Skins.All.Where(x => x.Group == group))
            {
                var current = s.Id == Skins.Current.Id;
                var st = new StackPanel { Width = 270 };
                var img = SkinPreview(s.Id);
                if (img != null)
                    st.Children.Add(new Border { Height = 197, ClipToBounds = true, Child = new Image { Source = img, Stretch = Stretch.UniformToFill }, Margin = new Thickness(0, 0, 0, 8) });
                else
                    st.Children.Add(MockPreview(s));
                var icons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
                foreach (var k in new[] { "apps", "style", "net", "phone", "update", "play" })
                    icons.Children.Add(new Border { Margin = new Thickness(0, 0, 8, 0), Child = Icons.Make(k, 16, s, false, true) });
                st.Children.Add(icons);
                st.Children.Add(new TextBlock { Text = s.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                st.Children.Add(new TextBlock { Text = s.Desc, Foreground = Res("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap, MinHeight = 34, Margin = new Thickness(0, 2, 0, 8) });
                var b = new Button
                {
                    Content = current ? "✓ Выбран" : "Применить", Style = (Style)FindResource(current ? "Primary" : "Ghost"),
                    HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = !current,
                };
                var skin = s;
                b.Click += (_, _) => { Skins.Apply(skin); SetNavIcons(); Render(); ShowView("skins"); Toast("Скин: " + skin.Name); };
                st.Children.Add(b);
                var card = new Border
                {
                    Child = st, Padding = new Thickness(10), Margin = new Thickness(0, 0, 14, 14),
                    Background = Res("Panel"), BorderBrush = current ? Res("Accent") : Res("Line"), BorderThickness = new Thickness(current ? 2 : 1),
                }.WithSkin();
                wrap.Children.Add(card);
            }
            SkinsBody.Children.Add(wrap);
        }
    }

    // ---------- значки в меню и карточках ----------
    readonly Dictionary<Button, string> navText = new();

    // Пункты меню слева: значок + подпись. Подписи берём из разметки один раз; значки перерисовываются при смене скина.
    void SetNavIcons()
    {
        var items = new (Button Btn, string Icon)[]
        {
            (NavApps, "apps"), (NavStyle, "style"), (NavNet, "net"), (NavExt, "ext"), (NavEnv, "env"),
            (NavSkins, "skins"), (NavPhone, "phone"), (NavFeedback, "feedback"), (NavAccount, "account"),
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
