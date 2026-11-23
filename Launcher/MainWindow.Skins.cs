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
                st.Children.Add(new TextBlock { Text = s.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                st.Children.Add(new TextBlock { Text = s.Desc, Foreground = Res("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap, MinHeight = 34, Margin = new Thickness(0, 2, 0, 8) });
                var b = new Button
                {
                    Content = current ? "✓ Выбран" : "Применить", Style = (Style)FindResource(current ? "Primary" : "Ghost"),
                    HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = !current,
                };
                var skin = s;
                b.Click += (_, _) => { Skins.Apply(skin); Render(); ShowView("skins"); Toast("Скин: " + skin.Name); };
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
}
