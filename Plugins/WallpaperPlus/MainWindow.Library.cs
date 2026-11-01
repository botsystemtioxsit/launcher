using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace WallpaperPlusApp;

sealed partial class MainWindow
{
    void BuildLibrary()
    {
        var p = pages[0];
        p.Children.Clear();

        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        head.Children.Add(Btn("Добавить файл или архив…", "Primary", PickFiles, first: true));
        head.Children.Add(Btn("Добавить папку…", null, PickFolder));
        head.Children.Add(Btn("Выключить обои", "Danger", () => { Activate(""); }));
        p.Children.Add(Card("Мои обои", new UIElement[]
        {
            head,
            Note("Перетащите файлы прямо на окно: видео (mp4, webm), GIF, картинки, страницы (html) и архивы zip — в том числе обои из Lively. " +
                 "Установщики и программы (.exe, .msi, .bat) не добавляются и не запускаются. Обои Wallpaper Engine (.pkg, scene) не поддерживаются."),
        }));

        var list = Library.List();
        foreach (var w in list) p.Children.Add(WallpaperCard(w));
        if (list.Count == 0) p.Children.Add(Note("Пока нет ни одних обоев."));
    }

    Brush CoverBrush(Wallpaper w)
    {
        var b = SHA1.HashData(Encoding.UTF8.GetBytes(w.Id));
        Color Hsv(double h, double s, double v)
        {
            h = (h % 360 + 360) % 360; var c = v * s; var x = c * (1 - Math.Abs(h / 60 % 2 - 1)); var m = v - c;
            double r, g, bl;
            if (h < 60) { r = c; g = x; bl = 0; }
            else if (h < 120) { r = x; g = c; bl = 0; }
            else if (h < 180) { r = 0; g = c; bl = x; }
            else if (h < 240) { r = 0; g = x; bl = c; }
            else if (h < 300) { r = x; g = 0; bl = c; }
            else { r = c; g = 0; bl = x; }
            return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((bl + m) * 255));
        }
        var h1 = b[0] / 255.0 * 360;
        return new LinearGradientBrush(Hsv(h1, 0.85, 0.95), Hsv(h1 + 70 + b[1] % 60, 0.8, 0.45), 45);
    }

    Border WallpaperCard(Wallpaper w)
    {
        var active = settings.Active == w.Id;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var cover = new Border { Width = 128, Height = 72, CornerRadius = new CornerRadius(4), Background = CoverBrush(w), Margin = new Thickness(0, 0, 16, 0) };
        cover.Child = new TextBlock
        {
            Text = w.Type switch { "video" => "▶", "gif" => "GIF", "image" => "▣", _ => "✦" }, Foreground = Brushes.White, FontSize = 22, FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        grid.Children.Add(cover);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new TextBlock { Text = w.Title, FontSize = 16, FontWeight = FontWeights.SemiBold });
        if (active) title.Children.Add(new TextBlock { Text = "  ● на рабочем столе", Foreground = Res("Green"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        info.Children.Add(title);
        if (w.Description.Length > 0 && w.Type == "html") info.Children.Add(new TextBlock { Text = w.Description, Foreground = Res("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        info.Children.Add(new TextBlock { Text = $"{w.TypeLabel} · {SizeText(w.SizeBytes)} · {w.Source}" + (w.Online ? " · интернет разрешён" : ""), Foreground = Res("Muted"), FontSize = 11, Margin = new Thickness(0, 4, 0, 0) });
        if (w.Telemetry.Count > 0)
        {
            var names = new Dictionary<string, string> { ["sys"] = "процессор и память", ["audio"] = "звук", ["media"] = "что играет", ["input"] = "курсор", ["journal"] = "журнал событий", ["weather"] = "погода" };
            var bg = WallpaperView.BackgroundOf(w, null);
            info.Children.Add(new TextBlock { Text = "Использует: " + string.Join(", ", w.Telemetry.Select(k => names.TryGetValue(k, out var n) ? n : k)) + (bg != null ? " · под ним: " + bg.Title : ""), Foreground = Res("Muted"), FontSize = 11, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
        }
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);

        var btns = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        btns.Children.Add(Btn(active ? "Выключить" : "Включить", active ? null : "Primary", () => Activate(active ? "" : w.Id), first: true));
        if (w.Params.Count > 0) btns.Children.Add(Btn(w.Builtin ? "Сделать свои на основе…" : "Настроить", null, () => StartCreate(w)));
        if (w.Type == "html" && !w.Builtin) btns.Children.Add(Btn(w.Online ? "Закрыть интернет" : "Разрешить интернет", null, () => ToggleOnline(w)));
        if (!w.Builtin) btns.Children.Add(Btn("Удалить", "Danger", () => DeleteWallpaper(w)));
        foreach (UIElement b in btns.Children) if (b is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 0, 6);
        Grid.SetColumn(btns, 2);
        grid.Children.Add(btns);

        return new Border
        {
            Background = Res("Panel"), BorderBrush = active ? Res("Accent") : Res("Line"), BorderThickness = new Thickness(active ? 2 : 1), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 12), Child = grid,
        };
    }

    void ToggleOnline(Wallpaper w)
    {
        if (!w.Online && MessageBox.Show(this, "Разрешить этим обоям выходить в интернет?\n\nСтраница обоев сможет обращаться к сайтам (например, за погодой или картинками). " +
                "Разрешайте только обоям, которым доверяете.", "Wallpaper Plus", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        w.Online = !w.Online;
        Library.Save(w);
        if (settings.Active == w.Id) _ = ctl.ApplyAsync(settings);
        BuildLibrary();
    }

    void DeleteWallpaper(Wallpaper w)
    {
        if (MessageBox.Show(this, $"Удалить обои «{w.Title}» из библиотеки? Файлы будут удалены.", "Wallpaper Plus", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (settings.Active == w.Id) { settings.Active = ""; settings.Save(); _ = ctl.ApplyAsync(settings); }
        Library.Delete(w.Id);
        Say("Удалено: " + w.Title);
        BuildLibrary();
    }

    void PickFiles()
    {
        var dlg = new OpenFileDialog { Title = "Выберите обои", Multiselect = true, Filter = Library.PickerFilter };
        if (dlg.ShowDialog(this) == true) _ = InstallFilesAsync(dlg.FileNames);
    }

    void PickFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Папка с обоями (в ней index.html или видео)" };
        if (dlg.ShowDialog(this) == true) _ = InstallFilesAsync(new[] { dlg.FolderName });
    }
}
