using System.Windows;
using System.Windows.Controls;

namespace BattleLauncher;

// «Телефон»: установка и обновление Android-приложений проекта по кабелю (отладка по USB). Всё делает сам лаунчер:
// когда телефон подключён, он смотрит версии, спрашивает «Обновить?» и ставит приложения через очередь (менеджер обновлений).
public partial class MainWindow
{
    string? phoneSerial, phoneStatus;
    List<PhoneState> phoneStates = new();
    readonly HashSet<string> phoneAsked = new();   // для каких телефонов в этом запуске уже спрашивали — чтобы не надоедать
    bool phoneChecking, phoneAdbFetching;

    void OpenPhone()
    {
        BuildPhone();
        _ = CheckPhone(false);
    }

    // Следит за кабелем: раз в 8 секунд спрашивает adb, не подключён ли телефон. Работает, только если adb уже есть на компьютере.
    async Task PhoneWatchLoop()
    {
        await Task.Delay(TimeSpan.FromSeconds(20));
        while (true)
        {
            try
            {
                if (!phoneChecking && !ops.Snapshot().Any(v => v.State is OpState.Queued or OpState.Running))
                {
                    var adb = PhoneApps.FindAdb();
                    if (adb != null)
                    {
                        var dev = (await PhoneApps.Devices(adb, CancellationToken.None)).FirstOrDefault(d => d.State == "device");
                        if (dev != null && phoneAsked.Add(dev.Serial)) await CheckPhone(true);
                        else if (dev == null) phoneAsked.Clear();   // отключили кабель — в следующий раз спросим снова
                    }
                }
            }
            catch { /* нет связи или adb занят — попробуем в следующий раз */ }
            await Task.Delay(TimeSpan.FromSeconds(8));
        }
    }

    // Читает, что подключено и какие версии. auto = пришло само при подключении кабеля: тогда задаём вопрос «Обновить?».
    async Task CheckPhone(bool auto)
    {
        if (phoneChecking) return;
        phoneChecking = true;
        try
        {
            var adb = PhoneApps.FindAdb();
            if (adb == null) { phoneStatus = null; phoneSerial = null; phoneStates = new(); if (currentView == "phone") BuildPhone(); return; }
            phoneStatus = "Ищу телефон…";
            if (currentView == "phone") BuildPhone();
            var devs = await PhoneApps.Devices(adb, CancellationToken.None);
            var ready = devs.FirstOrDefault(d => d.State == "device");
            if (ready == null)
            {
                phoneSerial = null; phoneStates = new();
                phoneStatus = devs.Any(d => d.State == "unauthorized")
                    ? "Телефон виден, но на нём нужно нажать «Разрешить отладку по USB»."
                    : "Телефон по кабелю не найден. Подключите его и включите «Отладку по USB» (Параметры разработчика).";
                if (currentView == "phone") BuildPhone();
                return;
            }
            phoneSerial = ready.Serial;
            phoneStatus = "Проверяю версии…";
            if (currentView == "phone") BuildPhone();
            phoneStates = await PhoneApps.Check(adb, ready.Serial, CancellationToken.None);
            phoneStatus = "Телефон подключён.";
            if (currentView == "phone") BuildPhone();
            if (auto)
            {
                var upd = phoneStates.Where(s => s.IsInstalled && s.HasUpdate).ToList();
                if (upd.Count > 0)
                {
                    var text = "К компьютеру подключён телефон. Есть обновления:\n\n" +
                               string.Join("\n", upd.Select(s => $"• {s.App.Name}: {s.Installed} → {s.Latest}")) +
                               "\n\nОбновить по кабелю?";
                    if (Loc.Box(this, text, "Телефон", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                        foreach (var s in upd) _ = UpdatePhoneApp(s);
                }
            }
        }
        catch (Exception ex)
        {
            phoneStatus = "Не получилось проверить телефон: " + ex.Message;
            if (currentView == "phone") BuildPhone();
        }
        finally { phoneChecking = false; }
    }

    Task UpdatePhoneApp(PhoneState s)
    {
        var key = "phone:" + s.App.Id;
        var verb = s.IsInstalled ? "Обновление" : "Установка";
        return RunOp(key, s.App.Name + " (телефон)", verb, () => { if (currentView == "phone") BuildPhone(); }, async (item, ct) =>
        {
            var adb = PhoneApps.FindAdb();
            var serial = phoneSerial;
            if (adb == null || serial == null) { item.Fail("телефон не подключён"); return false; }
            try
            {
                item.SetPhase("Проверяю телефон…");
                var devs = await PhoneApps.Devices(adb, ct);
                if (!devs.Any(d => d.Serial == serial && d.State == "device")) { item.Fail("телефон отключился или не разрешил отладку"); return false; }
                var progress = new Progress<int?>(p => item.SetPercent(p));
                var apk = await PhoneApps.DownloadApk(s.App, progress, ct);
                item.SetPhase("Ставлю на телефон…");
                var r = await PhoneApps.Install(adb, serial, apk, ct);
                if (!r.Ok && r.SignatureMismatch)
                {
                    var yes = await Dispatcher.InvokeAsync(() => Loc.Box(this,
                        $"«{s.App.Name}» на телефоне подписано другим ключом (старая сборка), поверх новая не ставится.\n\nУдалить старое и поставить новое? Настройки этого приложения на телефоне пропадут.",
                        "Телефон", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes);
                    if (!yes) { item.Fail("отменено: подпись на телефоне другая"); return false; }
                    item.SetPhase("Удаляю старое…");
                    await PhoneApps.Uninstall(adb, serial, s.App, ct);
                    item.SetPhase("Ставлю на телефон…");
                    r = await PhoneApps.Install(adb, serial, apk, ct);
                }
                if (!r.Ok) { item.Fail(r.Text); Toast("Не получилось: " + r.Text); return false; }
                item.SetNote("установлено на телефон");
                Toast($"На телефоне: {s.App.Name} {s.Latest}");
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { item.Fail(ex.Message); Toast("Не получилось: " + ex.Message); return false; }
        });
    }

    void BuildPhone()
    {
        PhoneBody.Children.Clear();
        PhoneBody.Children.Add(new TextBlock { Text = "Телефон", FontSize = 22, FontWeight = FontWeights.SemiBold });
        PhoneBody.Children.Add(new TextBlock
        {
            Text = "Android-приложения проекта ставятся и обновляются на телефоне по кабелю — без возни с файлами. Подключите телефон, включите «Отладку по USB» " +
                   "(Параметры разработчика) и нажмите «Разрешить» на экране телефона. Когда выйдет новая версия, лаунчер сам спросит «Обновить?».",
            Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 16, 16),
        });

        var st = new StackPanel();
        var adb = PhoneApps.FindAdb();
        if (adb == null)
        {
            st.Children.Add(new TextBlock
            {
                Text = "Для работы с телефоном по кабелю нужна маленькая программа adb от Google (около 7 МБ). Лаунчер скачает её сам с официального сайта Google, один раз.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
            });
            var get = new Button { Content = phoneAdbFetching ? "Скачиваю…" : "Скачать adb", Style = (Style)FindResource("Primary"), HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = !phoneAdbFetching };
            get.Click += async (_, _) =>
            {
                phoneAdbFetching = true; BuildPhone();
                try { await PhoneApps.FetchAdb(null, CancellationToken.None); Toast("adb скачан"); }
                catch (Exception ex) { Toast("Не получилось скачать adb: " + ex.Message); }
                phoneAdbFetching = false;
                BuildPhone();
                _ = CheckPhone(false);
            };
            st.Children.Add(get);
        }
        else
        {
            st.Children.Add(new TextBlock { Text = phoneStatus ?? "Нажмите «Проверить телефон».", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            var check = new Button { Content = "Проверить телефон", Style = (Style)FindResource("Ghost"), Margin = new Thickness(0, 0, 8, 0), IsEnabled = !phoneChecking };
            check.Click += (_, _) => _ = CheckPhone(false);
            row.Children.Add(check);
            var upd = phoneStates.Where(x => x.HasUpdate && !ops.IsActive("phone:" + x.App.Id)).ToList();
            if (phoneSerial != null && upd.Count > 0)
            {
                var all = new Button { Content = $"Обновить всё ({upd.Count})", Style = (Style)FindResource("Primary") };
                all.Click += (_, _) => { foreach (var s in upd) _ = UpdatePhoneApp(s); };
                row.Children.Add(all);
            }
            st.Children.Add(row);

            var states = phoneStates.Count > 0 ? phoneStates : PhoneApps.All.Select(a => new PhoneState(a, "", "")).ToList();
            foreach (var s in states)
            {
                var line = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
                line.Children.Add(new TextBlock { Text = s.App.Name, FontWeight = FontWeights.SemiBold });
                var info = phoneSerial == null ? "телефон не подключён"
                    : !s.IsInstalled ? $"не установлено{(s.Latest.Length > 0 ? ", доступна " + s.Latest : "")}"
                    : s.HasUpdate ? $"на телефоне {s.Installed}, вышла {s.Latest}"
                    : $"{s.Installed} — самая свежая";
                line.Children.Add(new TextBlock { Text = info, Foreground = Res("Muted"), FontSize = 12 });
                if (phoneSerial != null && s.HasUpdate)
                {
                    var busyNow = ops.IsActive("phone:" + s.App.Id);
                    var b = new Button
                    {
                        Content = busyNow ? "Идёт…" : s.IsInstalled ? "Обновить" : "Установить",
                        Style = (Style)FindResource("Ghost"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0), IsEnabled = !busyNow,
                    };
                    var st2 = s;
                    b.Click += (_, _) => _ = UpdatePhoneApp(st2);
                    line.Children.Add(b);
                }
                st.Children.Add(line);
            }
            st.Children.Add(new TextBlock
            {
                Text = "Если приложение на телефоне подписано другим ключом (старая сборка), лаунчер спросит, можно ли удалить его и поставить заново: его настройки на телефоне пропадут.",
                Foreground = Res("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0),
            });
        }
        PhoneBody.Children.Add(FbCard(st));
    }
}
