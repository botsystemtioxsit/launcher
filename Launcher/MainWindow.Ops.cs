using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace BattleLauncher;

// Менеджер обновлений: плашка внизу окна. Показывает, что стоит в очереди, что идёт прямо сейчас («Останавливаю плагин…»,
// «Скачиваю 40%», «Заменяю файл…», «Запускаю снова…»), чем закончилось и почему не вышло. Все установки, обновления и удаления
// идут через очередь строго по одной (OpQueue), поэтому два плагина никогда не останавливаются и не запускаются одновременно.
public partial class MainWindow
{
    OpQueue ops = null!;
    readonly DispatcherTimer opsTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    bool opsDirty, opsPinned, opsExpanded = true, opsDismissed;
    DateTime opsHideAt = DateTime.MinValue;
    int opsLastActive;

    void InitOps()
    {
        ops = new OpQueue(a => Dispatcher.BeginInvoke(a));
        // Сигналы приходят очень часто (процент скачивания) — перерисовываем не чаще раза в 150 мс
        ops.Changed += () => { opsDirty = true; if (!opsTimer.IsEnabled) opsTimer.Start(); };
        opsTimer.Tick += (_, _) => OpsTick();
    }

    void OpsTick()
    {
        var hideDue = opsHideAt != DateTime.MinValue && DateTime.Now >= opsHideAt;
        if (opsDirty || hideDue)
        {
            opsDirty = false;
            RenderOps();
            RefreshBusyCards();
        }
        var snap = ops.Snapshot();
        var active = snap.Any(v => v.State is OpState.Queued or OpState.Running);
        var waitingHide = !active && OpsDock.Visibility == Visibility.Visible && !opsPinned && snap.All(v => v.State != OpState.Failed) && DateTime.Now < opsHideAt;
        if (!active && !waitingHide) opsTimer.Stop();
    }

    // Пока что-то идёт, карточки показывают ту же фразу, что и плашка («В очереди…», «Заменяю файл…»)
    void RefreshBusyCards()
    {
        if (busy.Count == 0) return;
        if (AppsView.Visibility == Visibility.Visible) RenderApps();
        else if (PluginsView.Visibility == Visibility.Visible) { if (currentView == "ext") RenderBrowserPlugins(); else RenderPlugins(); }
    }

    // Что писать в карточке, пока приложение занято
    string BusyText(string key, (bool Installing, int? Percent) b)
    {
        var v = ops.Snapshot().FirstOrDefault(x => x.Key == key && (x.State == OpState.Queued || x.State == OpState.Running));
        if (v != null) return v.State == OpState.Queued ? "В очереди…" : v.Phase;
        return b.Installing ? "Работаю…" : $"Скачиваю {b.Percent ?? 0}%";
    }

    static string OpKind(string okText) => okText == "Обновлено" ? "Обновление" : "Установка";

    // Подписи фаз приходят из фонового потока — доставляем их в окно тем же способом, что и проценты, чтобы порядок не путался
    static Action<string> PhaseSink(OpItem item)
    {
        IProgress<string> p = new Progress<string>(item.SetPhase);   // Report у Progress<T> закрыт — вызывается через интерфейс
        return p.Report;
    }

    // Ставит действие в очередь менеджера. Пока оно ждёт или идёт, карточка занята, а повторное нажатие ничего не дублирует.
    async Task<bool> RunOp(string busyKey, string title, string kind, Action render, Func<OpItem, CancellationToken, Task<bool>> body)
    {
        if (ops.IsActive(busyKey) || busy.ContainsKey(busyKey)) return false;
        busy[busyKey] = (true, null);
        render();
        try
        {
            return await ops.Enqueue(busyKey, title, kind, async (item, ct) =>
            {
                busy[busyKey] = (false, 0);
                render();
                return await body(item, ct);
            });
        }
        finally { busy.Remove(busyKey); }
    }

    // Запись о мгновенном действии (запуск) — остаётся в списке и на несколько секунд показывает плашку
    void OpsNote(string kind, string title, bool ok, string note)
    {
        ops.Record(kind, title, ok, note);
        opsDismissed = false;
        opsHideAt = DateTime.Now.AddSeconds(12);
    }

    void OpsToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        opsPinned = !opsPinned;
        opsDismissed = !opsPinned;
        if (opsPinned) opsExpanded = true;
        RenderOps();
    }

    void OpsExpandBtn_Click(object sender, RoutedEventArgs e) { opsExpanded = !opsExpanded; RenderOps(); }

    void OpsCancelBtn_Click(object sender, RoutedEventArgs e)
    {
        var n = ops.CancelQueued();
        Toast(n > 0 ? $"Из очереди убрано: {n}. То, что уже идёт, доработает." : "В очереди ничего нет");
    }

    void OpsClearBtn_Click(object sender, RoutedEventArgs e)
    {
        ops.ClearFinished();
        opsPinned = false;
        opsDismissed = true;
        RenderOps();
    }

    static string Join3(IEnumerable<string> names, int total)
    {
        var list = names.Take(3).ToList();
        return string.Join(", ", list) + (total > list.Count ? $" и ещё {total - list.Count}" : "");
    }

    void RenderOps()
    {
        var snap = ops.Snapshot();
        var queued = snap.Where(v => v.State == OpState.Queued).ToList();
        var running = snap.FirstOrDefault(v => v.State == OpState.Running);
        var active = queued.Count + (running != null ? 1 : 0);
        var done = snap.Count(v => v.State == OpState.Done);
        var failed = snap.Count(v => v.State == OpState.Failed);
        var cancelled = snap.Count(v => v.State == OpState.Cancelled);

        OpsToggleBtn.Content = active > 0 ? $"☰ Очередь · {active}" : "☰ Очередь";

        // Когда всё закончилось, итог висит ещё немного (если нет ошибок), потом плашка прячется сама
        if (active > 0) { opsDismissed = false; opsHideAt = DateTime.MinValue; }
        else if (opsLastActive > 0) opsHideAt = DateTime.Now.AddSeconds(12);
        opsLastActive = active;

        var show = active > 0 || opsPinned || (!opsDismissed && snap.Length > 0 && (failed > 0 || DateTime.Now < opsHideAt));
        OpsDock.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;

        // ----- заголовок -----
        string summary, now;
        if (active > 0)
        {
            var all = active + done + failed;
            summary = $"⟳ Идёт работа · готово {done} из {all}" + (failed > 0 ? $" · не вышло: {failed}" : "");
            if (running != null)
            {
                now = $"Сейчас: {running.Title} — {running.Phase}";
                if (queued.Count > 0) now += $"\nДальше: {Join3(queued.Select(q => q.Title), queued.Count)}";
            }
            else now = $"Ждут своей очереди: {Join3(queued.Select(q => q.Title), queued.Count)}";
        }
        else if (snap.Length == 0)
        {
            summary = "Очередь пуста";
            now = "Здесь будут обновления, установки и запуски: что ждёт, что идёт сейчас и чем закончилось.";
        }
        else
        {
            summary = (failed == 0 ? "✓ Всё готово" : "Закончено") + $" · готово: {done}" + (failed > 0 ? $" · не вышло: {failed}" : "") + (cancelled > 0 ? $" · отменено: {cancelled}" : "");
            now = failed > 0 ? "Что не вышло — причина в списке ниже. Можно нажать «Обновить» на карточке ещё раз." : "";
        }
        OpsSummary.Text = summary;
        OpsSummary.Foreground = active > 0 ? Res("Accent") : failed > 0 ? Res("Red") : snap.Length == 0 ? Res("Muted") : Res("Green");
        OpsNow.Text = now;
        OpsNow.Visibility = now.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        OpsProgress.Visibility = active > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (active > 0)
        {
            var all = active + done + failed;
            var part = running?.Percent is int pc ? pc / 100.0 : 0;
            OpsProgress.Value = Math.Clamp((done + failed + part) / all * 100, 0, 100);
            OpsProgress.IsIndeterminate = false;
        }

        OpsExpandBtn.Visibility = snap.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        OpsExpandBtn.Content = opsExpanded ? "▾ Свернуть" : "▴ Подробнее";
        OpsCancelBtn.Visibility = queued.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        OpsClearBtn.Visibility = active == 0 ? Visibility.Visible : Visibility.Collapsed;

        // ----- список -----
        OpsListScroll.Visibility = opsExpanded && snap.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        OpsList.Children.Clear();
        if (!opsExpanded) return;
        foreach (var v in snap.Where(v => v.State == OpState.Running)) OpsList.Children.Add(OpsRow(v));
        foreach (var v in snap.Where(v => v.State == OpState.Queued).OrderBy(v => v.Seq)) OpsList.Children.Add(OpsRow(v));
        foreach (var v in snap.Where(v => v.State is OpState.Done or OpState.Failed or OpState.Cancelled).OrderByDescending(v => v.Seq)) OpsList.Children.Add(OpsRow(v));
    }

    UIElement OpsRow(OpView v)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var (label, brush) = v.State switch
        {
            OpState.Queued => ("В ОЧЕРЕДИ", Res("Muted")),
            OpState.Running => ("В РАБОТЕ", Res("Accent")),
            OpState.Done => ("ГОТОВО", Res("Green")),
            OpState.Failed => ("НЕ ВЫШЛО", Res("Red")),
            _ => ("ОТМЕНЕНО", Res("Muted")),
        };
        var chip = Badge(label, brush, null);
        chip.HorizontalAlignment = HorizontalAlignment.Left;
        chip.VerticalAlignment = VerticalAlignment.Top;
        g.Children.Add(chip);

        var text = new StackPanel();
        Grid.SetColumn(text, 1);
        text.Children.Add(new TextBlock { Text = $"{v.Title} · {v.Kind}", FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var detail = v.State == OpState.Failed ? (v.Error ?? "не получилось") : v.Phase;
        if (v.Finished != null && v.State is OpState.Done or OpState.Failed) detail += $" · {v.Finished:HH:mm:ss}";
        if (detail.Length > 0)
            text.Children.Add(new TextBlock
            {
                Text = detail, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Foreground = v.State == OpState.Failed ? Res("Red") : v.State == OpState.Running ? Res("Text") : Res("Muted"),
            });
        if (v.State == OpState.Running && v.Percent is int p)
            text.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = p, Height = 3, Margin = new Thickness(0, 4, 0, 0) });
        g.Children.Add(text);

        if (v.State == OpState.Queued)
        {
            var seq = v.Seq;
            var b = new Button { Content = "Убрать", Style = (Style)FindResource("Ghost"), Padding = new Thickness(10, 3, 10, 3), FontSize = 12, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, 0, 0, 0) };
            b.Click += (_, _) => ops.Cancel(seq);
            Grid.SetColumn(b, 2);
            g.Children.Add(b);
        }
        return g;
    }

    // Режим снимков экрана: выдуманная очередь (одно готово, одно не вышло, одно идёт, два ждут)
    async Task OpsDemo()
    {
        var hold = new TaskCompletionSource<bool>();
        _ = ops.Enqueue("demo:1", "Color Plus", "Обновление", (it, ct) => { it.SetPhase("Останавливаю плагин…"); it.SetNote("запущен снова"); return Task.FromResult(true); });
        _ = ops.Enqueue("demo:2", "Gateway Plus", "Обновление", (it, ct) => { it.Fail("нет связи с GitHub — проверьте интернет"); return Task.FromResult(false); });
        _ = ops.Enqueue("demo:3", "Taskbar Plus", "Обновление", async (it, ct) => { it.SetPhase("Останавливаю плагин…"); await hold.Task; return true; });
        _ = ops.Enqueue("demo:4", "Wallpaper Plus", "Обновление", (it, ct) => Task.FromResult(true));
        _ = ops.Enqueue("demo:5", "Mask Plus (расширение)", "Обновление", (it, ct) => Task.FromResult(true));
        opsPinned = true; opsExpanded = true;
        await Task.Delay(700);
        RenderOps();
    }
}
