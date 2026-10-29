using System.IO;
using System.Threading;
using System.Windows;

namespace IconPlusApp;

public partial class App : Application
{
    const string MutexName = "IconPlus.SingleInstance.GhostShell";
    const string ShowEventName = "IconPlus.Show.GhostShell";
    const string QuitEventName = "GhostShell.Plugin.Quit.icon-plus";   // имя ждёт лаунчер: "GhostShell.Plugin.Quit." + id плагина

    Mutex? mutex;
    EventWaitHandle? showEvent, quitEvent;
    MainWindow? window;

    static string? Arg(StartupEventArgs e, string prefix) => e.Args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // служебные режимы для проверки в CI: ничего не показывают и сразу выходят
        var shot = Arg(e, "--screenshot=");
        if (shot != null) { RenderScreenshots(shot); Shutdown(); return; }
        var self = Arg(e, "--selftest=");
        if (self != null) { SelfTest.Run(self); Shutdown(); return; }
        var probe = Arg(e, "--probe=");
        if (probe != null) { SelfTest.Probe(probe); Shutdown(); return; }
        var packTest = Arg(e, "--pack-test=");
        if (packTest != null) { SelfTest.PackTest(packTest, Arg(e, "--out=") ?? "."); Shutdown(); return; }
        var machine = Arg(e, "--machine=");
        if (machine != null) { Shutdown(Machine.Work(machine.Trim('"'))); return; }
        var color = Arg(e, "--apply-color=");
        if (color != null) { var s = IconSettings.Load(); s.Color = color; s.Normalize(); new IconEngine().ApplyColor(s); Shutdown(); return; }
        if (e.Args.Contains("--restore")) { new IconEngine().RestoreAll(); Shutdown(); return; }

        // Одна копия: повторный запуск просто показывает окно уже открытой.
        mutex = new Mutex(true, MutexName, out var first);
        showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!first) { showEvent.Set(); mutex = null; Shutdown(); return; }

        var ev = showEvent;
        new Thread(() =>
        {
            while (true)
            {
                try { ev.WaitOne(); } catch { return; }
                Dispatcher.BeginInvoke(new Action(() => { if (window != null) { if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal; window.Activate(); } }));
            }
        }) { IsBackground = true }.Start();

        // Лаунчер просит выйти перед обновлением или удалением плагина.
        quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        var qe = quitEvent;
        new Thread(() =>
        {
            try { qe.WaitOne(); } catch { return; }
            Dispatcher.BeginInvoke(new Action(Shutdown));
        }) { IsBackground = true }.Start();

        var page = int.TryParse(Arg(e, "--tab="), out var t) ? t : 0;
        window = new MainWindow(new IconEngine(), IconSettings.Load(), page);
        window.Closed += (_, _) => Shutdown();   // значки хранятся в настройках Windows, плагину в фоне работать не нужно
        window.Show();
    }

    // Служебный режим: рисует страницы окна (без сайта) в PNG и выходит.
    static void RenderScreenshots(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var w = new MainWindow(new IconEngine(), IconSettings.Load());
            w.RenderTo(dir);
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(dir, "icon-plus-ui-error.txt"), ex.ToString()); }
    }
}
