using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace WallpaperPlusApp;

sealed partial class MainWindow
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "WallpaperPlus";

    static bool Autostart
    {
        get { try { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue(RunName) != null; } catch { return false; } }
    }

    static void SetAutostart(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue(RunName, "\"" + Environment.ProcessPath + "\" --background");
        else k.DeleteValue(RunName, false);
    }

    void BuildSettings()
    {
        var p = pages[4];
        p.Children.Clear();
        var rows = new List<UIElement>
        {
            Row("Пауза вручную", "Обои замирают и не нагружают компьютер, пока вы не включите их снова",
                Seg(new[] { "Работают", "Пауза" }, settings.Paused ? 1 : 0, i => { settings.Paused = i == 1; ApplySettings(); BuildSettings(); })),
            Row("Мониторы", "Одни и те же обои на всех мониторах или только на главном",
                Seg(new[] { "Все", "Только главный" }, settings.AllMonitors ? 0 : 1, i => { settings.AllMonitors = i == 0; ApplySettings(); BuildSettings(); })),
            Row("Как вписать обои", "Заполнить — без полос, но края могут обрезаться; вписать — видна вся картинка; растянуть — на весь экран без сохранения пропорций",
                Seg(new[] { "Заполнить", "Вписать", "Растянуть" }, settings.Fit == "cover" ? 0 : settings.Fit == "contain" ? 1 : 2, i => { settings.Fit = new[] { "cover", "contain", "fill" }[i]; ApplySettings(); BuildSettings(); })),
            Row("Звук", "У обоев с видео звук по умолчанию выключен",
                Seg(new[] { "Выкл", "Вкл" }, settings.Mute ? 0 : 1, i => { settings.Mute = i == 0; ApplySettings(); BuildSettings(); })),
        };
        rows.Add(SliderRow("Кадров в секунду", "Ограничение для встроенных обоев-страниц: меньше — экономнее для видеокарты и батареи (видео идёт со своей частотой)", 15, 60, 5, settings.Fps, "", v => { settings.Fps = (int)Math.Round(v); settings.Save(); fpsTimer.Stop(); fpsTimer.Start(); }));
        p.Children.Add(Card("Показ", rows));

        p.Children.Add(Card("Экономия", new UIElement[]
        {
            Row("Пауза при игре или видео на весь экран", "Когда запущена полноэкранная игра, презентация или экран заблокирован, обои замирают",
                Seg(new[] { "Выкл", "Вкл" }, settings.PauseOnFullscreen ? 1 : 0, i => { settings.PauseOnFullscreen = i == 1; ApplySettings(); BuildSettings(); })),
            Row("Пауза, когда рабочий стол закрыт окном", "Если окно развёрнуто на весь монитор, обои под ним не видны и не рисуются",
                Seg(new[] { "Выкл", "Вкл" }, settings.PauseWhenCovered ? 1 : 0, i => { settings.PauseWhenCovered = i == 1; ApplySettings(); BuildSettings(); })),
        }));

        // данные компьютера для обоев «Терминал»
        var cityBox = new TextBox { Text = settings.WeatherQuery.Length > 0 ? settings.WeatherQuery : settings.WeatherName, Width = 220, Padding = new Thickness(6, 4, 6, 4), VerticalContentAlignment = VerticalAlignment.Center };
        var cityStatus = new TextBlock { Foreground = Res("Accent2"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), Text = settings.WeatherName.Length > 0 ? "Город: " + settings.WeatherName : "Город не выбран — погода в терминале не показывается." };
        var cityRow = new StackPanel { Orientation = Orientation.Horizontal };
        cityRow.Children.Add(cityBox);
        cityRow.Children.Add(Btn("Найти", "Primary", () => FindCity(cityBox.Text, cityStatus)));
        p.Children.Add(Card("Терминал: данные компьютера", new UIElement[]
        {
            Note("Обои «Терминал» показывают время, погоду, звук, что играет, курсор и журнал событий. Ничего не записывается на диск и не отправляется: " +
                 "единственный запрос в интернет — погода у бесплатного сервиса Open-Meteo, и в нём только название города."),
            Row("Город для погоды", "Например: Москва", cityRow),
            cityStatus,
            Row("Звук и музыка", "Уровень громкости системы, полосы-«эквалайзер» и что сейчас играет. Звук не записывается: из него берутся только высоты полос.",
                Seg(new[] { "Выкл", "Вкл" }, settings.TelAudio ? 1 : 0, i => { settings.TelAudio = i == 1; ApplySettings(); BuildSettings(); })),
            Row("Журнал событий", "Какие программы открыты и закрыты, смена окна, сеть, громкость, нагрузка. Только названия программ, без заголовков окон.",
                Seg(new[] { "Выкл", "Вкл" }, settings.TelJournal ? 1 : 0, i => { settings.TelJournal = i == 1; ApplySettings(); BuildSettings(); })),
        }));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(Btn("Выключить обои", "Danger", () => { Activate(""); }, first: true));
        buttons.Children.Add(Btn("Выйти", null, () => { App.Quitting = true; Close(); }));
        p.Children.Add(Card("Система", new UIElement[]
        {
            Row("Запускать вместе с Windows", "Обои появятся после входа в систему, окно открываться не будет",
                Seg(new[] { "Нет", "Да" }, Autostart ? 1 : 0, i =>
                {
                    try { SetAutostart(i == 1); settings.Autostart = i == 1; settings.Save(); Say(i == 1 ? "Автозапуск включён" : "Автозапуск выключен"); }
                    catch (Exception ex) { Say("Не получилось: " + ex.Message); }
                    BuildSettings();
                })),
            buttons,
            Note("Системные обои Windows плагин не меняет: его обои — отдельное окно за значками. «Выключить» или «Выйти» — и рабочий стол такой, как был. " +
                 "Если запущен Wallpaper Engine или Lively, выключите их: две программы с живыми обоями мешают друг другу."),
        }));
    }

    async void FindCity(string query, TextBlock status)
    {
        status.Text = "Ищу…";
        try
        {
            var r = await Weather.FindAsync(query);
            if (r == null) { status.Text = "Такой город не найден. Попробуйте написать иначе."; return; }
            settings.WeatherQuery = query.Trim(); settings.WeatherName = r.Value.Label; settings.WeatherLat = r.Value.Lat; settings.WeatherLon = r.Value.Lon;
            ApplySettings();
            status.Text = "Город: " + r.Value.Label;
        }
        catch (Exception ex) { status.Text = "Нет связи с сервисом погоды: " + ex.Message; }
    }

    readonly System.Windows.Threading.DispatcherTimer fpsTimer = CreateFpsTimer();
    static System.Windows.Threading.DispatcherTimer CreateFpsTimer() => new() { Interval = TimeSpan.FromMilliseconds(600) };
}
