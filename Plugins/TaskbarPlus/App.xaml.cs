using System.Threading;
using System.Windows;

namespace TaskbarPlusApp;

public partial class App : Application
{
    const string MutexName = "TaskbarPlus.SingleInstance.GhostShell";
    const string ShowEventName = "TaskbarPlus.Show.GhostShell";
    const string QuitEventName = "GhostShell.Plugin.Quit.taskbar-plus"; // имя ждёт лаунчер: "GhostShell.Plugin.Quit." + id плагина

    Mutex? mutex;
    EventWaitHandle? showEvent;
    EventWaitHandle? quitEvent;
    Engines? engine;
    AppearanceSettings settings = new();
    MainWindow? window;
    public static bool Quitting { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Одна копия: повторный запуск (из лаунчера или ярлыка) просто показывает окно уже работающей.
        mutex = new Mutex(true, MutexName, out var first);
        showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!first)
        {
            showEvent.Set();
            mutex = null;
            Shutdown();
            return;
        }

        var background = e.Args.Contains("--background");
        settings = AppearanceSettings.Load();
        engine = new Engines();
        // Тихо возвращаем всё, что могло остаться от прошлого запуска, и применяем сохранённый вид.
        engine.Update(settings);

        var ev = showEvent;
        new Thread(() =>
        {
            while (true)
            {
                try { ev.WaitOne(); } catch { return; }
                Dispatcher.BeginInvoke(new Action(ShowWindow));
            }
        }) { IsBackground = true }.Start();

        // Лаунчер просит выйти перед обновлением или удалением плагина: выходим и возвращаем панель как была.
        quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        var qe = quitEvent;
        new Thread(() =>
        {
            try { qe.WaitOne(); } catch { return; }
            Dispatcher.BeginInvoke(new Action(() => { Quitting = true; Shutdown(); }));
        }) { IsBackground = true }.Start();

        if (background && !settings.Active) { Quitting = true; Shutdown(); return; } // фоновый запуск без эффектов — делать нечего
        if (!background) ShowWindow();
    }

    void ShowWindow()
    {
        if (engine == null) return;
        window ??= new MainWindow(engine, settings);
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Show();
        window.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { engine?.Dispose(); } catch { }   // снимает форму с панели
        try { mutex?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}
