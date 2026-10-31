namespace ColorPlusApp;

// Собирает три части плагина: эффекты окон и панели, системные цвета и связь с Taskbar Plus.
sealed class Controller : IDisposable
{
    public readonly WindowEffects Effects = new();
    public readonly GlassManager Glass = new(System.Windows.Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher);
    readonly SystemColors sys = new();

    public Controller() { Effects.Glass = Glass; }

    public string? SystemError { get; private set; }

    // ---------- отчёт для разбора: после запуска, после «Применить» и после перезапуска Проводника ----------
    IntPtr lastTray;
    System.Threading.Timer? watch;

    public void StartReporting(ColorSettings s)
    {
        lastTray = Native.FindWindow("Shell_TrayWnd", null);
        watch = new System.Threading.Timer(_ =>
        {
            try
            {
                var h = Native.FindWindow("Shell_TrayWnd", null);
                if (h != IntPtr.Zero && h != lastTray) { lastTray = h; ScheduleReport(s, "Проводник перезапущен", 20); }
            }
            catch { }
        }, null, 2000, 2000);
        ScheduleReport(s, "запуск", 25);
    }

    public void ScheduleReport(ColorSettings s, string reason, int delaySeconds)
    {
        if (!s.AutoReport) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delaySeconds * 1000);
                await Report.UploadAsync(s, reason, Effects.Status);
            }
            catch { }
        });
    }

    public void Apply(ColorSettings s)
    {
        Effects.Update(s);
        TaskbarLink.Write(s);
        SystemError = sys.Apply(s);
    }

    // Всё, что менял плагин, возвращается: окна, панель, цвета.
    public void Dispose()
    {
        try { watch?.Dispose(); } catch { }
        try { Effects.Dispose(); } catch { }
        try { Glass.Dispose(); } catch { }
        try { sys.RestoreAll(); } catch { }
        try { TaskbarLink.Write(new ColorSettings()); } catch { }
    }
}
