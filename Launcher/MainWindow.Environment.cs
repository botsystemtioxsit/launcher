using System.Windows;
using System.Windows.Controls;

namespace BattleLauncher;

// «Окружение»: что запускать при включении компьютера. Лаунчер стартует сам, показывает плашку подтверждения
// и экран загрузки, затем тихо запускает выбранные плагины. Изменения сохраняются сразу.
public partial class MainWindow
{
    Boot.Config envCfg = new();
    bool envLoading;

    void OpenEnv()
    {
        envCfg = Boot.Load();
        envCfg.Autostart = envCfg.Autostart && (App.ScreenshotDir != null || Boot.AutostartEnabled());
        BuildEnv();
    }

    void BuildEnv()
    {
        envLoading = true;
        EnvBody.Children.Clear();
        EnvBody.Children.Add(new TextBlock { Text = "Окружение", FontSize = 22, FontWeight = FontWeights.SemiBold });
        EnvBody.Children.Add(new TextBlock
        {
            Text = "Выберите плагины, которые должны включаться сами при старте компьютера. Лаунчер запустится вместе с Windows, покажет плашку «Нажмите подтвердить», " +
                   "затем экран загрузки — и всё включится без вашей помощи. Окна плагинов при этом не открываются.",
            Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 16, 16),
        });

        var st = new StackPanel();
        var auto = new CheckBox { Content = "Запускать лаунчер вместе с Windows и предлагать включить окружение", IsChecked = envCfg.Autostart, Margin = new Thickness(0, 0, 0, 14) };
        auto.Click += (_, _) => { envCfg.Autostart = auto.IsChecked == true; SaveEnv(); };
        st.Children.Add(auto);
        st.Children.Add(FbLabel("Что включать"));

        foreach (var def in Catalog.Plugins.Where(p => Boot.BackgroundIds.Contains(p.Id)))
        {
            var installed = App.ScreenshotDir != null || NativeInstaller.Find(def) != null;
            var cb = new CheckBox
            {
                Content = def.Name + (installed ? "" : " — не установлен (поставьте во вкладке «Плагины»)"),
                IsChecked = envCfg.Items.Contains(def.Id), IsEnabled = installed, Margin = new Thickness(0, 3, 0, 3),
            };
            var id = def.Id;
            cb.Click += (_, _) =>
            {
                envCfg.Items.Remove(id);
                if (cb.IsChecked == true) envCfg.Items.Add(id);
                SaveEnv();
            };
            st.Children.Add(cb);
        }

        st.Children.Add(new TextBlock
        {
            Text = "Здесь только плагины, которые умеют работать в фоне. Остальные (DNS Plus, Gateway Plus, Icon Plus, Skin Loader) запускаются из своего окна.",
            Foreground = Res("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 14),
        });
        var preview = new Button { Content = "Показать, как это выглядит", Style = (Style)FindResource("Ghost"), HorizontalAlignment = HorizontalAlignment.Left };
        preview.Click += (_, _) =>
        {
            var list = Boot.Resolve(envCfg);
            if (list.Count == 0) { Toast("Отметьте хотя бы один установленный плагин"); return; }
            new BootWindow(list, demo: true).Show();   // только показ: ничего не запускает; закроется само или по Esc
        };
        st.Children.Add(preview);
        EnvBody.Children.Add(FbCard(st));
        envLoading = false;
    }

    void SaveEnv()
    {
        if (envLoading || App.ScreenshotDir != null) return;
        try { Boot.Save(envCfg); Toast("Сохранено"); }
        catch (Exception ex) { Toast("Не удалось сохранить: " + ex.Message); }
    }
}
