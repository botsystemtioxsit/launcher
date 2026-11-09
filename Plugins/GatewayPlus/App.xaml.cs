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
        Loc.Start(e.Args);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // помощник с правами администратора: ставит и снимает службу туннеля
        var helper = Arg(e, "--helper=");
        if (helper != null) { Shutdown(Helper.Main(helper.Trim('"'))); return; }

        // служебные режимы для проверки в CI (без окна)
        var shot = Arg(e, "--screenshot=");
        if (shot != null)
        {
            var demoDir = Path.Combine(Path.GetTempPath(), "gp-shot-" + Guid.NewGuid().ToString("N")[..8]);
            try
            {
                Directory.CreateDirectory(shot);
                Store.DirOverride = demoDir;   // демо-серверы не должны попасть в настоящие данные
                var de = SeedDemoServers();
                new ServerWindow(de, false).RenderTo(Path.Combine(shot, "gateway-plus-server.png"));
                new WizardWindow("203.0.113.5", "gsp", "germany-gsp").RenderTo(Path.Combine(shot, "gateway-plus-wizard-saved.png"), demo: false);
                new WizardWindow("198.51.100.7", "gsp", "finland-gsp").RenderTo(Path.Combine(shot, "gateway-plus-wizard-nosaved.png"), demo: false);
                new MainWindow(GwDemo.Account).RenderTo(Path.Combine(shot, "gateway-plus-ui.png"));
                new MainWindow(GwDemo.NoAccount).RenderTo(Path.Combine(shot, "gateway-plus-ui-noaccount.png"));
                new MainWindow(GwDemo.Blocked).RenderTo(Path.Combine(shot, "gateway-plus-ui-blocked.png"));
                new AccountWindow(false).RenderTo(Path.Combine(shot, "gateway-plus-account.png"));
                new AccountWindow(true).RenderTo(Path.Combine(shot, "gateway-plus-account-key.png"));
                new WizardWindow().RenderTo(Path.Combine(shot, "gateway-plus-wizard.png"), demo: true);
                new ProfileWindow("nl", "[Interface]\nPrivateKey = AAAA\nAddress = 10.0.0.2/32\nDNS = 1.1.1.1\n\n[Peer]\nPublicKey = BBBB\nEndpoint = 203.0.113.5:51820\nAllowedIPs = 0.0.0.0/0\n", false).RenderTo(Path.Combine(shot, "gateway-plus-profile.png"));
                new ProfileWindow("nl-gsp", "# GSP\n[Interface]\nPrivateKey = AAAA\nAddress = 10.78.0.2/32\nFEC = auto\n\n[Peer]\nPublicKey = BBBB\nEndpoint = 203.0.113.5:51821\nAllowedIPs = 0.0.0.0/0\n", false).RenderTo(Path.Combine(shot, "gateway-plus-profile-gsp.png"));
            }
            catch (Exception ex) { try { File.WriteAllText(Path.Combine(shot, "gateway-plus-ui-error.txt"), ex.ToString()); } catch { } }
            try { Store.DirOverride = null; Directory.Delete(demoDir, true); } catch { }
            Loc.WriteAudit(shot);
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

    // Два демо-сервера для снимков окон: Германия (WireGuard + GSP, доступ сохранён) и Финляндия (только WireGuard).
    static ServerRow SeedDemoServers()
    {
        const string wg = "[Interface]\nPrivateKey = AAAA\nAddress = 10.0.0.2/32\n\n[Peer]\nPublicKey = BBBB\nEndpoint = {0}:51820\nAllowedIPs = 0.0.0.0/0\n";
        const string gsp = "# GSP\n[Interface]\nPrivateKey = AAAA\nAddress = 10.78.0.2/32\nFEC = auto\n\n[Peer]\nPublicKey = BBBB\nEndpoint = {0}:51821\nAllowedIPs = 0.0.0.0/0\n";
        Store.Save("germany-wg", string.Format(wg, "203.0.113.5"));
        Store.Save("germany-gsp", string.Format(gsp, "203.0.113.5"));
        Store.Save("finland", string.Format(wg, "198.51.100.7"));
        Store.Active = "germany-gsp";
        var rows = Servers.List();
        var de = rows.First(r => r.Info.Host == "203.0.113.5");
        var fi = rows.First(r => r.Info.Host == "198.51.100.7");
        Servers.Rename(de.Info.Id, "Германия"); Servers.Rename(fi.Info.Id, "Финляндия");
        Servers.SaveAccess(de.Info.Id, new SavedAccess { Password = "demo" });
        return Servers.List().First(r => r.Info.Host == "203.0.113.5");
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
