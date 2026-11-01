using System.Diagnostics;
using System.Windows.Threading;

namespace WallpaperPlusApp;

// Держит обои на рабочем столе: создаёт окна по мониторам, следит за Проводником (перезапуск), ставит на паузу, когда обои не видны.
sealed class Controller : IDisposable
{
    readonly List<HostWindow> hosts = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    WpSettings settings = new();
    string signature = "";
    bool busy;

    public string Status { get; private set; } = "";
    public event Action? Changed;

    public Controller()
    {
        timer.Tick += async (_, _) => { try { await TickAsync(); } catch (Exception ex) { Paths.Log("такт: " + ex.Message); } };
        timer.Start();
    }

    public bool Running => hosts.Count > 0;

    static string Sign(WpSettings s, Wallpaper w, List<Native.Monitor> monitors) =>
        $"{w.Id}|{w.Background}|{BgSign(w)}|{w.Values.Count}|{string.Join(",", w.Values.OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value))}|{s.Fit}|{s.Fps}|{s.Mute}|{s.AllMonitors}|{w.Online}|" +
        string.Join(";", monitors.Select(m => $"{m.Rect.Left},{m.Rect.Top},{m.Rect.Width},{m.Rect.Height}"));

    // Фон под слоем: если его значения поменялись, окна пересоздаются
    static string BgSign(Wallpaper w)
    {
        var bg = WallpaperView.BackgroundOf(w, null);
        return bg == null ? "" : bg.Id + ":" + string.Join(",", bg.Values.OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value)) + bg.Online;
    }

    // Применить настройки: включить, сменить или убрать обои. Повторный вызов с теми же настройками ничего не пересоздаёт.
    public async Task ApplyAsync(WpSettings s)
    {
        settings = s.Clone();
        TelemetryHub.I.SetConfig(settings);   // настройки данных для «Терминала» (звук, журнал, город для погоды) действуют сразу, без пересоздания окон
        if (busy) return;
        busy = true;
        try
        {
            var w = Library.Get(settings.Active);
            if (w == null)
            {
                StopAll();
                SetStatus(settings.Active.Length == 0 ? "Обои выключены." : "Выбранные обои не найдены.");
                return;
            }
            var monitors = Native.Monitors();
            if (!settings.AllMonitors) monitors = monitors.Take(1).ToList();
            var sig = Sign(settings, w, monitors);
            if (sig == signature && hosts.Count == monitors.Count && hosts.All(h => h.IsAttached)) { foreach (var h in hosts) h.SetMuted(settings.Mute); return; }

            StopAll();
            signature = sig;
            foreach (var m in monitors)
            {
                var host = new HostWindow(m);
                hosts.Add(host);
                host.Show();
                await host.LoadAsync(w, settings);
            }
            Paths.Log($"обои «{w.Title}» ({w.Id}) показаны на мониторах: {hosts.Count}; рабочий стол:\n{Desktop.Dump()}");
            SetStatus($"Обои «{w.Title}» на рабочем столе.");
        }
        catch (Exception ex)
        {
            Paths.Log("включение обоев: " + ex);
            StopAll();
            SetStatus("Обои не запустились: " + ex.Message + " (нужен «Microsoft Edge WebView2 Runtime» — он есть в Windows 11).");
        }
        finally { busy = false; }
    }

    void StopAll()
    {
        foreach (var h in hosts) h.Release();
        hosts.Clear();
        signature = "";
        Desktop.Repaint();
    }

    static readonly string[] OtherEngines = { "wallpaper32", "wallpaper64", "lively", "livelywpf" };

    async Task TickAsync()
    {
        if (hosts.Count == 0 || busy) return;
        // Проводник перезапустился — окна рабочего стола пропали: поднимаем обои заново
        if (hosts.Any(h => !h.IsAttached))
        {
            Paths.Log("рабочий стол изменился (перезапуск Проводника?) — обои поднимаются заново");
            signature = "";
            await ApplyAsync(settings);
            return;
        }
        var fullscreen = settings.PauseOnFullscreen && Native.FullscreenAppRunning();
        foreach (var h in hosts)
        {
            var covered = settings.PauseWhenCovered && Native.DesktopCovered(h.Monitor.Rect);
            await h.SetPausedAsync(settings.Paused || fullscreen || covered);
        }
        var other = OtherEngines.FirstOrDefault(n => { try { return Process.GetProcessesByName(n).Length > 0; } catch { return false; } });
        var text = hosts.All(h => h.Paused) ? "Пауза: обои не видны (полноэкранное окно) или поставлены на паузу." : $"Обои «{Library.Get(settings.Active)?.Title}» на рабочем столе.";
        if (other != null) text += " Внимание: запущена другая программа с живыми обоями (" + other + ") — выключите её, иначе обои будут мигать.";
        SetStatus(text);
    }

    void SetStatus(string text)
    {
        if (Status == text) return;
        Status = text;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        timer.Stop();
        StopAll();
    }
}
