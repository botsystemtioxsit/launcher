using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TioxsitTerminal;

// Маленькие помощники для построения интерфейса в коде. Всё, что приходит из базы, выводится только как Text.
static class Ui
{
    public static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    public static TextBlock Text(string text, double size = 14, string fg = "Text", bool bold = false, bool wrap = true) => new()
    {
        Text = text, FontSize = size, Foreground = Res(fg), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
    };

    public static Border Card(UIElement child, double bottom = 12) => new()
    {
        Background = Res("Panel"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
        Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, bottom), Child = child,
    };

    public static Button Btn(string text, string? style, Func<Task> click, double fontSize = 13)
    {
        var b = new Button { Content = text, FontSize = fontSize, Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0) };
        if (style != null) b.Style = (Style)Application.Current.FindResource(style);
        b.Click += async (_, _) => await click();
        return b;
    }

    public static Border Badge(string text, string fg = "Muted", string? bg = null) => new()
    {
        Background = Res(bg ?? "Panel2"), CornerRadius = new CornerRadius(99), Padding = new Thickness(9, 2, 9, 3), Margin = new Thickness(0, 0, 6, 0),
        Child = new TextBlock { Text = text, FontSize = 12, Foreground = Res(fg) },
    };

    public static Border Avatar(string name, double size) => new()
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = Res("Accent"),
        Child = new TextBlock
        {
            Text = (name.Trim().PadRight(2)[..2]).Trim().ToUpperInvariant(), Foreground = Res("OnAccent"), FontWeight = FontWeights.Bold, FontSize = size * 0.4,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        },
    };

    // Страница: прокручиваемая колонка с заголовком и подписью
    public static (UIElement Root, StackPanel Body) Page(string title, string? sub = null)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        stack.Children.Add(Text(title, 22, bold: true));
        if (sub != null) stack.Children.Add(new TextBlock { Text = sub, Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 16) });
        else stack.Children.Add(new Border { Height = 16 });
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack };
        return (scroll, stack);
    }

    public static TextBlock Muted(string text, double size = 13) => Text(text, size, "Muted");

    public static StackPanel Row(params UIElement[] items)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var i in items) p.Children.Add(i);
        return p;
    }

    public static WrapPanel Wrap(params UIElement[] items)
    {
        var p = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var i in items) p.Children.Add(i);
        return p;
    }

    // Плитка «число + подпись» для статистики
    public static Border Tile(string value, string label) => new()
    {
        Background = Res("Panel2"), CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 10, 10), Width = 190,
        Child = new StackPanel { Children = { Text(value, 26, bold: true), Text(label, 12, "Muted") } },
    };
}
