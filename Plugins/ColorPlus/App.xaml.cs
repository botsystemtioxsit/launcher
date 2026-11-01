using System.IO;
using System.Threading;
using System.Windows;

namespace ColorPlusApp;

public partial class App : Application
{
    const string MutexName = "ColorPlus.SingleInstance.GhostShell";
    const string ShowEventName = "ColorPlus.Show.GhostShell";
    const string QuitEventName = "GhostShell.Plugin.Quit.color-plus";   // имя ждёт лаунчер: "GhostShell.Plugin.Quit." + id плагина

    Mutex? mutex;
    EventWaitHandle? showEvent, quitEvent;
    MainWindow? window;
    bool forceBackground;
    internal static TitleEngine Engine { get; } = new();
    internal static bool QuitRequested { get; set; }

    static string? Arg(StartupEventArgs e, string prefix) => e.Args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // служебные режимы для проверки в CI (без окна)
        if (e.Args.Contains("--revert"))
        {
            // если Color Plus уже работает в фоне — просим его выйти (он вернёт цвета окон), потом возвращаем всё остальное
            try { using var q = EventWaitHandle.OpenExisting(QuitEventName); q.Set(); Thread.Sleep(3000); } catch { }
            new Applier().RevertAll();
            var st = ColorSettings.Load(); st.Applied = false; st.Save();
            Shutdown(); return;
        }
        var apply = Arg(e, "--apply=");
        if (apply != null)
        {
            var s = ColorSettings.Load();
            if (Col.IsHex(apply)) s.Color = apply.ToUpperInvariant();
            s.Windows = !e.Args.Contains("--no-windows"); s.Taskbar = !e.Args.Contains("--no-taskbar");
            var err = new Applier().Apply(s);
            s.Applied = true; s.Save();
            Applier.SetAutostart(s.Windows);
            if (err != null) { try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "color-plus-error.txt"), err); } catch { } }
            if (!s.Windows) { Thread.Sleep(2000); Shutdown(); return; }
            forceBackground = true;   // дальше работаем в фоне и красим заголовки окон
        }
        var shot = Arg(e, "--screenshot=");
        if (shot != null)
        {
            try { Directory.CreateDirectory(shot); new MainWindow(ColorSettings.Load()).RenderTo(Path.Combine(shot, "color-plus-ui.png")); }
            catch (Exception ex) { try { File.WriteAllText(Path.Combine(shot, "color-plus-ui-error.txt"), ex.ToString()); } catch { } }
            Shutdown(); return;
        }

        // Одна копия: повторный запуск (из лаунчера или ярлыка) просто показывает окно уже открытой.
        mutex = new Mutex(true, MutexName, out var first);
        showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!first) { showEvent.Set(); mutex = null; Shutdown(); return; }

        var ev = showEvent;
        new Thread(() =>
        {
            while (true)
            {
                try { ev.WaitOne(); } catch { return; }
                Dispatcher.BeginInvoke(new Action(ShowWindow));
            }
        }) { IsBackground = true }.Start();

        // Лаунчер просит выйти перед обновлением или удалением плагина: просто закрываемся (в фоне Color Plus ничего не держит)
        quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        var qe = quitEvent;
        new Thread(() =>
        {
            try { qe.WaitOne(); } catch { return; }
            Dispatcher.BeginInvoke(new Action(() => { QuitRequested = true; Engine.Stop(); Shutdown(); }));
        }) { IsBackground = true }.Start();

        TitleEngine.RecoverLeftovers();   // следы аварийно закрытого прошлого запуска
        var saved = ColorSettings.Load();
        if (saved.Applied && saved.Windows) Engine.Set(saved.Color);
        var background = forceBackground || e.Args.Contains("--background");
        if (background) { if (!Engine.Running) Shutdown(); return; }   // фоновый запуск: если красить заголовки нечего — сразу выходим
        ShowWindow();
    }

    void ShowWindow()
    {
        window ??= new MainWindow(ColorSettings.Load());
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Show();
        window.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { Engine.Stop(); } catch { }
        try { mutex?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}
