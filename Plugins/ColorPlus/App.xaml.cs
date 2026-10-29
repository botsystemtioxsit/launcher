using System.IO;
using System.Threading;
using System.Windows;

namespace ColorPlusApp;

public partial class App : Application
{
    const string MutexName = "ColorPlus.SingleInstance.GhostShell";
    const string ShowEventName = "ColorPlus.Show.GhostShell";
    const string QuitEventName = "GhostShell.Plugin.Quit.color-plus"; // имя ждёт лаунчер: "GhostShell.Plugin.Quit." + id плагина

    Mutex? mutex;
    EventWaitHandle? showEvent;
    EventWaitHandle? quitEvent;
    Controller? controller;
    ColorSettings settings = new();
    MainWindow? window;
    public static bool Quitting { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var shot = e.Args.FirstOrDefault(a => a.StartsWith("--screenshot="));
        if (shot != null) { RenderScreenshots(shot["--screenshot=".Length..]); Quitting = true; Shutdown(); return; }

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
        settings = ColorSettings.Load();
        controller = new Controller();
        controller.Apply(settings); // тихо применяем сохранённое (и возвращаем то, что мог оставить аварийно закрытый прошлый запуск)

        var ev = showEvent;
        new Thread(() =>
        {
            while (true)
            {
                try { ev.WaitOne(); } catch { return; }
                Dispatcher.BeginInvoke(new Action(ShowWindow));
            }
        }) { IsBackground = true }.Start();

        // Лаунчер просит выйти перед обновлением или удалением плагина: выходим и возвращаем всё как было.
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

    // Служебный режим для CI: рисует окно настроек с выдуманными настройками в PNG и выходит (ничего не применяя).
    static void RenderScreenshots(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var s = new ColorSettings
            {
                WindowsOn = true, Backdrop = ColorSettings.BdAcrylic, TitleColor = "#101018", TitleTextColor = "#F2F2F2",
                TaskbarOn = true, TaskbarMode = ColorSettings.TbBlur, SelectionOn = true, AccentOn = true,
            };
            s.Apps["notepad"] = new AppRule { Mode = "custom", Opacity = 70, Backdrop = ColorSettings.BdMica };
            s.Apps["chrome"] = new AppRule { Mode = "skip" };
            var w = new MainWindow(new Controller(), s);
            w.RenderTo(Path.Combine(dir, "color-plus-ui.png"));
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(dir, "color-plus-ui-error.txt"), ex.ToString()); }
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
        try { controller?.Dispose(); } catch { }   // возвращает окна, панель и цвета как было
        try { mutex?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}
