using System.Windows;
using System.Windows.Controls;

namespace WallpaperPlusApp;

sealed partial class MainWindow
{
    List<CatalogItem>? catalogItems;
    string catalogError = "";
    bool catalogLoading;

    void BuildCatalog() => RenderCatalog();

    async Task LoadCatalogAsync()
    {
        if (catalogLoading || screenshotMode) return;
        catalogLoading = true;
        RenderCatalog();
        try { catalogItems = await Catalog.FetchAsync(); catalogError = ""; }
        catch (Exception ex) { catalogError = "Каталог не загрузился: " + ex.Message; }
        catalogLoading = false;
        RenderCatalog();
    }

    void RenderCatalog()
    {
        var p = pages[1];
        p.Children.Clear();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        head.Children.Add(Btn("Обновить список", null, () => { catalogItems = null; _ = LoadCatalogAsync(); }, first: true));
        p.Children.Add(Card("Каталог обоев", new UIElement[]
        {
            head,
            Note("Список лежит в репозитории проекта, поэтому новые обои появляются без нового выпуска плагина. Скачиваются только страницы, картинки и видео из списка — программ среди них нет."),
        }));
        if (catalogLoading) { p.Children.Add(Note("Загружаю список…")); return; }
        if (catalogError.Length > 0) { p.Children.Add(Note(catalogError)); return; }
        if (catalogItems == null) { p.Children.Add(Note("Откройте вкладку, чтобы загрузить список.")); return; }
        if (catalogItems.Count == 0) { p.Children.Add(Note("В каталоге пока пусто.")); return; }
        foreach (var item in catalogItems)
        {
            var it = item;
            var installed = Catalog.Installed(it);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock { Text = it.Title, FontSize = 16, FontWeight = FontWeights.SemiBold });
            info.Children.Add(new TextBlock { Text = it.Description, Foreground = Res("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            grid.Children.Add(info);
            var b = Btn(installed ? "Установлено" : "Установить", installed ? null : "Primary", async () =>
            {
                var (w, msg) = await Catalog.InstallAsync(it, t => Dispatcher.BeginInvoke(new Action(() => Say(t))));
                Say(msg);
                if (w != null) { Activate(w.Id); }
                RenderCatalog();
            }, first: true);
            b.IsEnabled = !installed;
            Grid.SetColumn(b, 1);
            grid.Children.Add(b);
            p.Children.Add(new System.Windows.Controls.Border
            {
                Background = Res("Panel"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 12), Child = grid,
            });
        }
    }
}
