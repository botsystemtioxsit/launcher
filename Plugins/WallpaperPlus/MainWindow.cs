using System.Windows;
using System.Windows.Controls;

namespace WallpaperPlusApp;

// Временное окно (будет заменено полным интерфейсом): нужно, чтобы проект собирался и обои можно было проверить.
sealed class MainWindow : Window
{
    readonly Controller ctl;
    readonly WpSettings settings;

    public MainWindow(Controller ctl, WpSettings settings)
    {
        this.ctl = ctl; this.settings = settings;
        Title = "Wallpaper Plus";
        Width = 800; Height = 600;
        Content = new TextBlock { Text = "Wallpaper Plus" };
    }

    public void RenderTo(string folder) { }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!App.Quitting && settings.Active.Length > 0) { e.Cancel = true; Hide(); return; }
        base.OnClosing(e);
        Application.Current.Shutdown();
    }
}
