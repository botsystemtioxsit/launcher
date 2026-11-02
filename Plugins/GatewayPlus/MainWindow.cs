using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GatewayPlusApp;

// Окно Gateway Plus: один экран. Сверху — каким вас видят сайты, ниже — шлюз (профиль, «Подключить»/«Отключить»), свой сервер в Германии и пояснения.
sealed class MainWindow : Window
{
    readonly StackPanel page = new();
    readonly TextBlock status = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(5) };
    bool connected, busy;
    IpInfo.Info? ip;

    static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    public MainWindow()
    {
        Title = "Gateway Plus";
        Width = Math.Min(960, SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(900, SystemParameters.WorkArea.Height - 40);
        MinWidth = 640; MinHeight = 480;
        Background = Res("Bg"); Foreground = Res("Text");
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        page.Margin = new Thickness(28, 22, 28, 24);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
        BuildPage();
        Loaded += async (_, _) => await RefreshAsync(true);
        timer.Tick += async (_, _) => await TickAsync();
        timer.Start();
    }

    async Task TickAsync()
    {
        if (busy || !IsVisible) return;
        var name = Store.Active;
        var now = name.Length > 0 && await Task.Run(() => Wg.IsRunning(name));
        if (now != connected) await RefreshAsync(true);
    }

    async Task RefreshAsync(bool checkIp)
    {
        var name = Store.Active;
        connected = name.Length > 0 && await Task.Run(() => Wg.IsRunning(name));
        if (checkIp) ip = await IpInfo.GetAsync();
        BuildPage();
    }

    // ---------- сборка окна ----------
    void BuildPage()
    {
        page.Children.Clear();
        page.Children.Add(new TextBlock { Text = "GATEWAY PLUS", FontFamily = (FontFamily)Application.Current.FindResource("Mono"), FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Res("Accent") });
        page.Children.Add(Note("Шлюз: весь интернет с этого компьютера идёт через ваш сервер, а сайты видят его адрес и страну, а не ваши. Сервер в Германии — значит Германия."));

        // 1. как видят
        var seen = new StackPanel();
        var big = new TextBlock { FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        if (ip == null) { big.Text = "Проверяю…"; big.Foreground = Res("Muted"); }
        else if (!ip.Ok) { big.Text = "Не удалось проверить"; big.Foreground = Res("Muted"); seen.Children.Add(big); seen.Children.Add(Note(ip.Error)); }
        else { big.Text = (ip.Country.Length > 0 ? ip.Country : "?") + (ip.City.Length > 0 ? ", " + ip.City : ""); big.Foreground = connected ? Res("Green") : Res("Text"); }
        if (ip == null || ip.Ok) seen.Children.Add(big);
        if (ip != null && ip.Ok) seen.Children.Add(new TextBlock { Text = "Внешний адрес: " + ip.Ip + (ip.Zone.Length > 0 ? "  ·  часовой пояс сайта: " + ip.Zone : ""), Foreground = Res("Muted"), Margin = new Thickness(0, 4, 0, 0) });
        seen.Children.Add(new TextBlock
        {
            Text = connected ? "Шлюз включён: сайты видят адрес сервера." : "Шлюз выключен: сайты видят ваш настоящий адрес.",
            Foreground = connected ? Res("Green") : Res("Accent2"), Margin = new Thickness(0, 10, 0, 8),
        });
        seen.Children.Add(Btn("Проверить ещё раз", null, async () => { ip = null; BuildPage(); await RefreshAsync(true); }, first: true));
        page.Children.Add(Card("Сейчас вас видят так", new UIElement[] { seen }));

        // 2. шлюз
        var gw = new List<UIElement>();
        var wgPath = Wg.ExePath();
        if (wgPath == null)
        {
            gw.Add(Note("Для работы шлюза нужна программа WireGuard (официальная, wireguard.com, бесплатная). Нажмите — плагин скачает установщик, проверит его подпись и запустит; Windows спросит разрешение."));
            gw.Add(Btn("Установить WireGuard", "Primary", InstallWg, first: true));
        }
        else gw.Add(new TextBlock { Text = "WireGuard установлен.", Foreground = Res("Muted"), FontSize = 12, Margin = new Thickness(0, 0, 0, 10) });

        var profiles = Store.List();
        var active = Store.Active;
        if (profiles.Count > 0)
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 6, 0, 10) };
            foreach (var p in profiles)
            {
                var name = p;
                var b = new Button { Content = name, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 8) };
                if (name == active) b.Style = (Style)FindResource("Primary");
                b.Click += (_, _) => { if (!connected) { Store.Active = name; BuildPage(); } else status.Text = "Сначала отключите шлюз."; };
                wrap.Children.Add(b);
            }
            gw.Add(new TextBlock { Text = "Профиль (куда подключаться)", FontWeight = FontWeights.SemiBold });
            gw.Add(wrap);
        }
        else gw.Add(Note("Профилей пока нет. Добавьте конфиг: от своего сервера (см. ниже) или от VPN-сервиса, который даёт файл WireGuard."));

        var buttons = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        if (!connected)
        {
            var c = Btn("Подключить", "Primary", Connect, first: true);
            c.IsEnabled = wgPath != null && profiles.Count > 0 && active.Length > 0;
            c.Padding = new Thickness(26, 11, 26, 11); c.FontSize = 16;
            buttons.Children.Add(c);
        }
        else
        {
            var d = Btn("Отключить", "Danger", Disconnect, first: true);
            d.Padding = new Thickness(26, 11, 26, 11); d.FontSize = 16;
            buttons.Children.Add(d);
        }
        buttons.Children.Add(Btn("＋ Добавить конфиг…", null, AddProfile));
        if (profiles.Count > 0 && active.Length > 0 && !connected) buttons.Children.Add(Btn("Удалить выбранный профиль", null, () => { Store.Delete(active); Store.Active = Store.List().FirstOrDefault() ?? ""; BuildPage(); }));
        gw.Add(buttons);
        gw.Add(Note("Включение и выключение шлюза требуют разрешения администратора Windows — оно спросит каждый раз. Если связь со шлюзом оборвётся, интернет не «утечёт» мимо него: пока шлюз включён, WireGuard закрывает прямой выход."));
        page.Children.Add(Card("Шлюз", gw));

        // 3. свой сервер
        page.Children.Add(Card("Свой сервер в Германии (если сервера ещё нет)", new UIElement[]
        {
            Note("Чтобы сайты видели Германию, нужна машина в Германии — «точка выхода». Самый простой путь — арендовать маленький сервер (VPS) в Германии, обычно это несколько евро в месяц (Hetzner, Netcup, Contabo и подобные); адрес у него будет свой и постоянный. Ставьте Ubuntu 22.04/24.04 или Debian 12."),
            Note("Что делать: 1) арендуйте сервер и зайдите в его консоль (веб-консоль хостинга или SSH). 2) Войдите как root (sudo -i). 3) Нажмите «Скопировать скрипт» и вставьте его в консоль. 4) В конце он напечатает конфиг между двумя линиями — скопируйте его. 5) Здесь нажмите «Добавить конфиг…» и вставьте. 6) В панели хостинга откройте порт 51820 по протоколу UDP, если он закрыт."),
            Btn("Скопировать скрипт для сервера", null, () => { try { Clipboard.SetText(Store.ServerScript()); status.Text = "Скрипт скопирован. Вставьте его в консоль сервера."; } catch { status.Text = "Не удалось скопировать."; } }, first: true),
        }));

        // 4. границы
        page.Children.Add(Card("Что шлюз скрывает, а что нет", new UIElement[]
        {
            Note("Скрывает: ваш настоящий IP-адрес и страну от сайтов и от вашего провайдера (он видит только соединение с сервером). Не скрывает: вход в аккаунты (сайт знает, кто вы, когда вы вошли), куки и отпечаток браузера, часовой пояс и язык браузера (сайт может видеть, что они не германские), платёжные данные. Шлюз не отменяет законы и правила сервисов. Бесплатным VPN-сервисам не доверяйте: за бесплатное вы часто платите своими данными. Владелец сервера видит ваш трафик (если сайт не шифрует его) — поэтому лучше свой сервер."),
        }));

        status.Foreground = Res("Accent2"); status.FontSize = 13; status.TextWrapping = TextWrapping.Wrap; status.Margin = new Thickness(0, 4, 0, 0);
        var last = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        last.Children.Add(Btn("Вернуть как было", "Danger", RevertAll, first: true));
        page.Children.Add(last);
        page.Children.Add(status);
        page.Children.Add(Note("«Вернуть как было» выключает шлюз и убирает его следы (службу и файл конфига в ProgramData). Ваши сохранённые профили остаются."));
    }

    // ---------- действия ----------
    async void InstallWg()
    {
        if (busy) return;
        busy = true;
        try
        {
            var err = await Wg.InstallAsync(t => Dispatcher.BeginInvoke(new Action(() => status.Text = t)));
            status.Text = err ?? "WireGuard установлен.";
        }
        finally { busy = false; BuildPage(); }
    }

    void AddProfile()
    {
        var w = new ImportWindow { Owner = this };
        if (w.ShowDialog() == true) { status.Text = "Профиль «" + w.SavedName + "» сохранён."; }
        BuildPage();
    }

    async void Connect()
    {
        if (busy) return;
        var name = Store.Active;
        var conf = name.Length > 0 ? Store.Load(name) : null;
        if (conf == null) { status.Text = "Сначала добавьте конфиг."; return; }
        var bad = Store.Validate(conf);
        if (bad != null) { status.Text = bad; return; }
        busy = true;
        status.Text = "Подключаю… Windows спросит разрешение администратора.";
        string? tmpDir = null;
        try
        {
            tmpDir = Path.Combine(Path.GetTempPath(), "gpc-" + Guid.NewGuid().ToString("N")[..10]);
            Directory.CreateDirectory(tmpDir);
            var file = Path.Combine(tmpDir, name + ".conf");
            await File.WriteAllTextAsync(file, conf);
            var r = await Helper.RunAsync(new Job { Action = "connect", Name = name, Conf = file });
            status.Text = r.Message;
            await Task.Delay(r.Ok ? 2500 : 200);
        }
        catch (Exception ex) { status.Text = "Не вышло: " + ex.Message; }
        finally
        {
            try { if (tmpDir != null) Directory.Delete(tmpDir, true); } catch { }
            busy = false;
        }
        await RefreshAsync(true);
    }

    async void Disconnect()
    {
        if (busy) return;
        busy = true;
        status.Text = "Отключаю… Windows спросит разрешение администратора.";
        try
        {
            var r = await Helper.RunAsync(new Job { Action = "disconnect", Name = Store.Active });
            status.Text = r.Message;
        }
        catch (Exception ex) { status.Text = "Не вышло: " + ex.Message; }
        finally { busy = false; }
        await RefreshAsync(true);
    }

    async void RevertAll()
    {
        if (busy) return;
        busy = true;
        status.Text = "Убираю шлюз… Windows спросит разрешение администратора.";
        try
        {
            var r = Wg.ExePath() == null ? new JobResult { Ok = true, Message = "WireGuard не установлен — убирать нечего." } : await Helper.RunAsync(new Job { Action = "cleanup" });
            status.Text = r.Message;
        }
        catch (Exception ex) { status.Text = "Не вышло: " + ex.Message; }
        finally { busy = false; }
        await RefreshAsync(true);
    }

    // ---------- кирпичики интерфейса ----------
    Border Card(string title, IEnumerable<UIElement> rows)
    {
        var col = new StackPanel();
        if (title.Length > 0) col.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        foreach (var r in rows) col.Children.Add(r);
        return new Border
        {
            Background = Res("Panel"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(22), Margin = new Thickness(0, 0, 0, 14), Child = col,
        };
    }

    TextBlock Note(string text) => new()
    {
        Text = text, Foreground = Res("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10),
    };

    Button Btn(string text, string? style, Action click, bool first = false)
    {
        var b = new Button { Content = text, Margin = new Thickness(first ? 0 : 10, 0, 0, 8), Padding = new Thickness(18, 9, 18, 9), HorizontalAlignment = HorizontalAlignment.Left };
        if (style != null) b.Style = (Style)FindResource(style);
        b.Click += (_, _) => click();
        return b;
    }

    // Служебный режим: рисует окно целиком в PNG без показа
    public void RenderTo(string file)
    {
        var sv = (ScrollViewer)Content;
        sv.Content = null;
        System.Windows.Documents.TextElement.SetForeground(page, Foreground);
        System.Windows.Documents.TextElement.SetFontFamily(page, FontFamily);
        System.Windows.Documents.TextElement.SetFontSize(page, FontSize);
        page.Measure(new Size(940, double.PositiveInfinity));
        var w = 940; var h = Math.Max(200, (int)Math.Ceiling(page.DesiredSize.Height));
        page.Arrange(new Rect(0, 0, w, h));
        page.UpdateLayout();
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen()) dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));
        rtb.Render(bg);
        rtb.Render(page);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(file);
        enc.Save(fs);
        sv.Content = page;
    }
}
