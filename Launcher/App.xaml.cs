using System.Threading;
using System.Windows;

namespace BattleLauncher;

public partial class App : Application
{
    Mutex? single;

    // --screenshot=<папка>: служебный режим, лаунчер сам сохраняет свои окна (с выдуманными данными) в PNG и выходит.
    // Нужен, чтобы сделать честные скриншоты для Microsoft Store на настоящей Windows (см. build-windows.yml).
    public static string? ScreenshotDir;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Программа удаления из списка «Установленные приложения»
        if (e.Args.Contains("--uninstall")) { SelfInstall.Uninstall(); Shutdown(); return; }
        var un = e.Args.FirstOrDefault(a => a.StartsWith("--uninstall-native=", StringComparison.Ordinal));
        if (un != null) { NativeInstaller.UninstallById(un["--uninstall-native=".Length..]); Shutdown(); return; }

        // --selftest-browser=<папка>: служебная проверка установщика расширений для браузера (Windows-раннер GitHub)
        var bt = e.Args.FirstOrDefault(a => a.StartsWith("--selftest-browser=", StringComparison.Ordinal));
        if (bt != null) { Task.Run(() => BrowserInstaller.SelfTest(bt["--selftest-browser=".Length..])).GetAwaiter().GetResult(); Shutdown(); return; }

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
        single = new Mutex(false, "BattleLauncher.SingleInstance");
        bool got;
        try { got = single.WaitOne(4000); }
        catch (AbandonedMutexException) { got = true; }
        if (!got) { Shutdown(); return; }

        // Остатки прошлой версии после самообновления — убираем при следующем запуске.
        SelfUpdate.CleanupLeftovers();
        _ = Task.Run(() => NativeInstaller.RemoveRetired());   // убранные плагины (Color Plus) удаляются сами

        // Скачанный файл предлагает поставить себя на компьютер (ярлыки, список приложений).
        // Из Microsoft Store это не нужно: там всё делает сам Store.
        if (!Packaged.IsPackaged)
        {
            if (SelfInstall.OfferInstall()) { Shutdown(); return; }
            SelfInstall.RefreshRegistry();
        }

        // --boot: запуск вместе с Windows. Показываем плашку «Окружение» вместо главного окна и выходим.
        if (e.Args.Contains(Boot.Arg))
        {
            var cfg = Boot.Load();
            var list = Boot.Resolve(cfg);
            if (!cfg.Autostart || list.Count == 0) { Shutdown(); return; }
            StartupUri = null!;
            base.OnStartup(e);
            var w = new BootWindow(list);
            w.Closed += (_, _) => Shutdown();
            w.Show();
            return;
        }
        base.OnStartup(e);
    }
}
