using System.Threading;
using System.Windows;

namespace BattleLauncher;

public partial class App : Application
{
    Mutex? single;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Одно окно лаунчера. Короткое ожидание нужно после самообновления:
        // старый процесс ещё может секунду-другую держать замок.
        single = new Mutex(false, "BattleLauncher.SingleInstance");
        bool got;
        try { got = single.WaitOne(4000); }
        catch (AbandonedMutexException) { got = true; }
        if (!got) { Shutdown(); return; }

        // Остатки прошлой версии после самообновления — убираем при следующем запуске.
        SelfUpdate.CleanupLeftovers();
        base.OnStartup(e);
    }
}
