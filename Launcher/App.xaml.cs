using System.Threading;
using System.Windows;

namespace BattleLauncher;

public partial class App : Application
{
    static Mutex? single;

    // true — этот запуск сделан самообновлением (старая версия сама открыла новую)
    public static bool JustUpdated;

    // Перед запуском новой версии старая отпускает замок «одно окно»: иначе новая может не успеть
    // его взять и молча закрыться, а человеку остаётся нажимать «Обновить» снова и снова.
    public static void ReleaseSingleInstance()
    {
        try { single?.ReleaseMutex(); } catch { /* замок мог быть уже отпущен */ }
        try { single?.Dispose(); } catch { }
        single = null;
    }

    // --screenshot=<папка>: служебный режим, лаунчер сам сохраняет свои окна (с выдуманными данными) в PNG и выходит.
    // Нужен, чтобы сделать честные скриншоты для Microsoft Store на настоящей Windows (см. build-windows.yml).
    public static string? ScreenshotDir;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Программа удаления из списка «Установленные приложения»
        if (e.Args.Contains("--uninstall")) { SelfInstall.Uninstall(); Shutdown(); return; }
        var un = e.Args.FirstOrDefault(a => a.StartsWith("--uninstall-native=", StringComparison.Ordinal));
        if (un != null) { NativeInstaller.UninstallById(un["--uninstall-native=".Length..]); Shutdown(); return; }

        var shot = e.Args.FirstOrDefault(a => a.StartsWith("--screenshot=", StringComparison.Ordinal));
        if (shot != null)
        {
            ScreenshotDir = shot["--screenshot=".Length..];
            AccountStore.NoDisk = true;
            base.OnStartup(e);
            return;
        }

        // Одно окно лаунчера. Короткое ожидание нужно после установки и самообновления:
        // прежний процесс ещё может секунду-другую держать замок.
        JustUpdated = e.Args.Contains("--updated");
        single = new Mutex(false, "BattleLauncher.SingleInstance");
        bool got;
        try { got = single.WaitOne(JustUpdated ? 20000 : 4000); }
        catch (AbandonedMutexException) { got = true; }
        if (!got) { Shutdown(); return; }

        // Остатки прошлой версии после самообновления — убираем при следующем запуске.
        SelfUpdate.CleanupLeftovers();

        // Скачанный файл предлагает поставить себя на компьютер (ярлыки, список приложений).
        // Из Microsoft Store это не нужно: там всё делает сам Store.
        if (!Packaged.IsPackaged)
        {
            if (SelfInstall.OfferInstall()) { Shutdown(); return; }
            SelfInstall.RefreshRegistry();
        }
        base.OnStartup(e);
    }
}
