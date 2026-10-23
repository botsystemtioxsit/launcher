using System.Threading;
using System.Windows;
using BattleLauncher;

namespace TioxsitTerminal;

public partial class App : Application
{
    Mutex? single;

    // --screenshot=<папка>: служебный режим — Терминал сам сохраняет свои окна (с выдуманными данными, без сети) в PNG и выходит.
    public static string? ScreenshotDir;

    protected override void OnStartup(StartupEventArgs e)
    {
        var shot = e.Args.FirstOrDefault(a => a.StartsWith("--screenshot=", StringComparison.Ordinal));
        if (shot != null)
        {
            ScreenshotDir = shot["--screenshot=".Length..];
            AccountStore.NoDisk = true;
            base.OnStartup(e);
            return;
        }

        // Одно окно Терминала
        single = new Mutex(false, "TioxsitTerminal.SingleInstance");
        bool got;
        try { got = single.WaitOne(1500); }
        catch (AbandonedMutexException) { got = true; }
        if (!got) { Shutdown(); return; }
        base.OnStartup(e);
    }
}
