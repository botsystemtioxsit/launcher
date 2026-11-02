using System.IO;
using System.Threading;
using System.Windows;

namespace GatewayPlusApp;

public partial class App : Application
{
    const string MutexName = "GatewayPlus.SingleInstance.GhostShell";
    const string ShowEventName = "GatewayPlus.Show.GhostShell";
    const string QuitEventName = "GhostShell.Plugin.Quit.gateway-plus";   // имя ждёт лаунчер: "GhostShell.Plugin.Quit." + id плагина

    Mutex? mutex;
    EventWaitHandle? showEvent, quitEvent;
    MainWindow? window;

    static string? Arg(StartupEventArgs e, string prefix) => e.Args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // помощник с правами администратора: ставит и снимает службу туннеля
        var helper = Arg(e, "--helper=");
        if (helper != null) { Shutdown(Helper.Main(helper.Trim('"'))); return; }

        // служебные режимы для проверки в CI (без окна)
        var shot = Arg(e, "--screenshot=");
        if (shot != null)
        {
            try { Directory.CreateDirectory(shot); new MainWindow().RenderTo(Path.Combine(shot, "gateway-plus-ui.png")); }
            catch (Exception ex) { try { File.WriteAllText(Path.Combine(shot, "gateway-plus-ui-error.txt"), ex.ToString()); } catch { } }
            Shutdown(); return;
        }
        var self = Arg(e, "--selftest=");
        if (self != null) { SelfTest.Run(self.Trim('"')); Shutdown(); return; }

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
                Dispatcher.BeginInvoke(new Action(ShowWindow));
            }
        }) { IsBackground = true }.Start();

        // Лаунчер просит выйти перед обновлением или удалением плагина (сам шлюз — служба WireGuard, он продолжает работать)
        quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        var qe = quitEvent;
        new Thread(() =>
        {
            try { qe.WaitOne(); } catch { return; }
            Dispatcher.BeginInvoke(new Action(Shutdown));
        }) { IsBackground = true }.Start();

        ShowWindow();
    }

    void ShowWindow()
    {
        if (window == null)
        {
            window = new MainWindow();
            window.Closed += (_, _) => Shutdown();
        }
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Show();
        window.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { mutex?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}
