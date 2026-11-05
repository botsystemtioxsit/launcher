using System.Windows;
using System.Windows.Controls;

namespace GatewayPlusApp;

// «Скорость и размер пакета»: быстрый замер скорости и подбор MTU профиля. Всё делается на этом компьютере, пароль от сервера не нужен.
sealed class SpeedWindow : Window
{
    readonly string profile;
    string conf;
    readonly bool connected;
    readonly TextBlock speedOut = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    readonly TextBlock mtuOut = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    Button speedBtn = null!, mtuBtn = null!, applyBtn = null!;
    CancellationTokenSource? cts;
    bool running;
    int recommended;

    public SpeedWindow(string profile, string conf, bool connected)
    {
        this.profile = profile; this.conf = conf; this.connected = connected;
        Ui.Setup(this, "Скорость и размер пакета", 640, 700);
        var page = new StackPanel { Margin = new Thickness(24, 18, 24, 18) };
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };

        page.Children.Add(Ui.Title("СКОРОСТЬ И РАЗМЕР ПАКЕТА"));
        page.Children.Add(Ui.Note("Здесь можно понять, почему VPN медленный, и поправить то, что зависит от вашего компьютера и вашего интернета. Пароль от сервера не нужен.", 12.5));

        page.Children.Add(Ui.Label("1. Скорость сейчас"));
        page.Children.Add(Ui.Note("Меряет скорость так, как идёт ваш интернет прямо сейчас: если шлюз включён — через него, если выключен — напрямую. Нажмите дважды (с VPN и без) и сравните: разница — это цена VPN. Если и с VPN, и без него скорость маленькая, дело не в VPN, а в вашем интернете или тарифе сервера.", 12));
        speedBtn = Ui.Btn("Проверить скорость", "Primary", MeasureSpeed, first: true);
        page.Children.Add(speedBtn);
        page.Children.Add(speedOut);

        page.Children.Add(Ui.Label("2. Размер пакета (MTU)"));
        page.Children.Add(Ui.Note("Если пакеты больше, чем пролезает через ваше подключение, они дробятся, и интернет тормозит. Так бывает у многих провайдеров (DS-Lite, PPPoE, мобильный интернет). Плагин проверит, какой размер проходит целиком, и подберёт значение для профиля «" + profile + "». Сейчас в профиле: " + MtuText(conf) + ".", 12));
        mtuBtn = Ui.Btn("Подобрать размер пакета", null, ProbeMtu, first: true);
        page.Children.Add(mtuBtn);
        page.Children.Add(mtuOut);
        applyBtn = Ui.Btn("Применить к профилю", "Primary", ApplyMtu, first: true);
        applyBtn.Margin = new Thickness(0, 10, 0, 0);
        applyBtn.Visibility = Visibility.Collapsed;
        page.Children.Add(applyBtn);

        Closing += (_, _) => cts?.Cancel();
    }

    static string MtuText(string c) { var m = Store.MtuOf(c); return m > 0 ? "MTU " + m : "не задан (WireGuard возьмёт 1420)"; }

    void Busy(bool on) { running = on; speedBtn.IsEnabled = mtuBtn.IsEnabled = !on; }

    async void MeasureSpeed()
    {
        if (running) return;
        Busy(true);
        speedOut.Foreground = Ui.Res("Accent2"); speedOut.Text = "";
        cts = new CancellationTokenSource();
        try
        {
            var r = await Speed.MeasureAsync(s => Dispatcher.BeginInvoke(new Action(() => speedOut.Text = s)), cts.Token);
            speedOut.Foreground = Ui.Res("Green");
            speedOut.Text = $"Скачивание: {r.DownMbps:0} Мбит/с\nОтдача: {r.UpMbps:0} Мбит/с\nЗадержка: {r.PingMs} мс\nСайты видят вас так: {r.Where}\n(замер примерный, делался через " + (connected ? "включённый шлюз)" : "обычное подключение либо через шлюз, если он включён вне окна)");
        }
        catch (OperationCanceledException) { speedOut.Text = "Отменено."; }
        catch (Exception ex) { speedOut.Foreground = Ui.Res("Red"); speedOut.Text = "Не вышло: " + ex.Message; }
        finally { Busy(false); }
    }

    async void ProbeMtu()
    {
        if (running) return;
        Busy(true);
        applyBtn.Visibility = Visibility.Collapsed;
        mtuOut.Foreground = Ui.Res("Accent2"); mtuOut.Text = "Проверяю, какие пакеты проходят целиком…";
        cts = new CancellationTokenSource();
        try
        {
            var host = Store.EndpointOf(conf);
            var r = await Speed.ProbePathMtuAsync(host, !connected, cts.Token);
            if (!r.Ok) { mtuOut.Foreground = Ui.Res("Red"); mtuOut.Text = r.Note; return; }
            recommended = Speed.RecommendedWgMtu(r.PathMtu, host);
            var cur = Store.MtuOf(conf);
            mtuOut.Foreground = Ui.Res("Green");
            mtuOut.Text = $"Ваше подключение пропускает пакеты до {r.PathMtu} байт ({r.Via}).\nДля WireGuard подходит MTU {recommended}. В профиле сейчас: {MtuText(conf)}.";
            if (cur == recommended) { mtuOut.Text += "\nЭто уже стоит — менять ничего не нужно."; return; }
            if (cur > 0 && cur < recommended) mtuOut.Text += "\nСейчас значение меньше подходящего — ничего не ломается, но можно чуть быстрее.";
            if (cur > recommended || cur == 0) mtuOut.Text += "\nСейчас значение больше, чем проходит у вас, — пакеты дробятся. Стоит поправить.";
            applyBtn.Content = $"Применить MTU {recommended} к профилю";
            applyBtn.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) { mtuOut.Text = "Отменено."; }
        catch (Exception ex) { mtuOut.Foreground = Ui.Res("Red"); mtuOut.Text = "Не вышло: " + ex.Message; }
        finally { Busy(false); }
    }

    void ApplyMtu()
    {
        if (recommended < 1280) return;
        try
        {
            conf = Store.WithInterfaceValue(conf, "MTU", recommended.ToString());
            var bad = Store.Validate(conf);
            if (bad != null) { mtuOut.Foreground = Ui.Res("Red"); mtuOut.Text = "Не получилось: " + bad; return; }
            Store.Save(profile, conf);
            applyBtn.Visibility = Visibility.Collapsed;
            mtuOut.Foreground = Ui.Res("Green");
            mtuOut.Text = $"Готово: в профиле «{profile}» теперь MTU {recommended}." + (connected ? "\nОтключите шлюз и включите снова — новое значение вступит в силу при следующем подключении." : "\nПри следующем подключении оно начнёт работать.");
        }
        catch (Exception ex) { mtuOut.Foreground = Ui.Res("Red"); mtuOut.Text = "Не получилось сохранить: " + ex.Message; }
    }
}
