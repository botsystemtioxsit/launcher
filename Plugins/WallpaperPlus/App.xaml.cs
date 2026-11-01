using System.IO;
using System.Threading;
using System.Windows;

namespace WallpaperPlusApp;

public partial class App : Application
{
    const string MutexName = "WallpaperPlus.SingleInstance.GhostShell";
    const string ShowEventName = "WallpaperPlus.Show.GhostShell";
    const string QuitEventName = "GhostShell.Plugin.Quit.wallpaper-plus";   // имя ждёт лаунчер: "GhostShell.Plugin.Quit." + id плагина

    Mutex? mutex;
    EventWaitHandle? showEvent, quitEvent;
    Controller? controller;
    WpSettings settings = new();
    MainWindow? window;
    public static bool Quitting { get; set; }

    static string? Arg(StartupEventArgs e, string prefix) => e.Args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // служебный режим для проверки в CI: окно настроек в PNG, без показа и без обоев
        var shot = Arg(e, "--screenshot=");
        if (shot != null)
        {
            Paths.Ensure(); Library.EnsureBuiltins();
            try { Directory.CreateDirectory(shot); new MainWindow(new Controller(), WpSettings.Load()).RenderTo(shot); }
            catch (Exception ex) { File.WriteAllText(Path.Combine(shot, "wallpaper-plus-ui-error.txt"), ex.ToString()); }
            Quitting = true; Shutdown(); return;
        }

        // Одна копия: повторный запуск (из лаунчера или ярлыка) просто показывает окно уже работающей.
        mutex = new Mutex(true, MutexName, out var first);
        showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!first) { showEvent.Set(); mutex = null; Shutdown(); return; }

        Paths.Ensure();
        Library.EnsureBuiltins();
        settings = WpSettings.Load();
        var apply = Arg(e, "--apply=");
        if (apply != null) { settings.Active = apply; settings.Save(); }

        controller = new Controller();
        _ = controller.ApplyAsync(settings);   // тихо включаем сохранённые обои

        var ev = showEvent;
        new Thread(() =>
        {
            while (true)
            {
                try { ev.WaitOne(); } catch { return; }
                Dispatcher.BeginInvoke(new Action(ShowWindow));
            }
        }) { IsBackground = true }.Start();

        // Лаунчер просит выйти перед обновлением или удалением плагина: выходим, обои убираются, рабочий стол возвращается как был.
        quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        var qe = quitEvent;
        new Thread(() =>
        {
            try { qe.WaitOne(); } catch { return; }
            Dispatcher.BeginInvoke(new Action(() => { Quitting = true; Shutdown(); }));
        }) { IsBackground = true }.Start();

        var background = e.Args.Contains("--background") || apply != null;
        if (background && settings.Active.Length == 0) { Quitting = true; Shutdown(); return; }   // фоновый запуск без обоев — делать нечего
        if (!background) ShowWindow();
    }

    void ShowWindow()
    {
        if (controller == null) return;
        window ??= new MainWindow(controller, settings);
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Show();
        window.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { controller?.Dispose(); } catch { }   // окна обоев закрываются, рабочий стол возвращается как был
        try { mutex?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}
