using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Wpf;

namespace WallpaperPlusApp;

// Большое окно редактора раскладки «Терминала»: те же обои, но панели можно двигать мышью, добавлять, настраивать и убирать.
// Данные в окне настоящие (звук, музыка, курсор, погода). Раскладка уходит в окно «Создать» сразу при каждом изменении.
sealed class EditorWindow : Window
{
    readonly WebView2 web = new();

    public EditorWindow(Window owner, Wallpaper w, WpSettings s, Dictionary<string, string> values, string background, Action<string> onLayout)
    {
        Owner = owner;
        Title = "Wallpaper Plus — редактор: " + w.Title;
        Width = Math.Min(1500, SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(900, SystemParameters.WorkArea.Height - 40);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Black;
        Content = web;
        Loaded += async (_, _) =>
        {
            try { await WallpaperView.LoadAsync(web, w, s, values, new LoadOptions { Edit = true, Background = background, OnLayout = onLayout }); }
            catch (Exception ex) { MessageBox.Show(this, "Редактор не запустился: " + ex.Message + "\n(нужен «Microsoft Edge WebView2 Runtime»)", "Wallpaper Plus"); Close(); }
        };
        Closed += (_, _) => { WallpaperView.Detach(web); try { web.Dispose(); } catch { } };
    }
}
