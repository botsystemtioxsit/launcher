using System.Threading;
using System.Windows;

namespace BattleLauncher;

public partial class App : Application
{
    Mutex? single;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Программа удаления из списка «Установленные приложения»
        if (e.Args.Contains("--uninstall")) { SelfInstall.Uninstall(); Shutdown(); return; }

        // Одно окно лаунчера. Короткое ожидание нужно после установки и самообновления:
        // прежний процесс ещё может секунду-другую держать замок.
        single = new Mutex(false, "BattleLauncher.SingleInstance");
        bool got;
        try { got = single.WaitOne(4000); }
        catch (AbandonedMutexException) { got = true; }
        if (!got) { Shutdown(); return; }

        // Остатки прошлой версии после самообновления — убираем при следующем запуске.
        SelfUpdate.CleanupLeftovers();

        // Скачанный файл предлагает поставить себя на компьютер (ярлыки, список приложений)
        if (SelfInstall.OfferInstall()) { Shutdown(); return; }
        SelfInstall.RefreshRegistry();
        base.OnStartup(e);
    }
}
