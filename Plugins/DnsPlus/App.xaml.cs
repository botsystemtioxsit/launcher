using System.Windows;

namespace DnsPlusApp;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Loc.Start(e.Args);
        // служебный режим: окно в PNG без показа и список непереведённых надписей
        var shot = e.Args.FirstOrDefault(a => a.StartsWith("--screenshot=", StringComparison.OrdinalIgnoreCase));
        if (shot != null)
        {
            var dir = shot.Substring("--screenshot=".Length).Trim('"');
            try { System.IO.Directory.CreateDirectory(dir); new MainWindow().RenderTo(System.IO.Path.Combine(dir, "dns-plus-ui.png")); }
            catch (Exception ex) { try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "dns-plus-ui-error.txt"), ex.ToString()); } catch { } }
            Loc.WriteAudit(dir);
            Shutdown();
            return;
        }
        // служебный режим для проверки на настоящей Windows (см. SelfTest.cs): окна нет, итог — файл report.txt и код выхода
        var st = e.Args.FirstOrDefault(a => a.StartsWith("--selftest=", StringComparison.OrdinalIgnoreCase));
        if (st != null)
        {
            int code;
            try { code = await SelfTest.Run(st.Substring("--selftest=".Length)); }
            catch (Exception ex)
            {
                try { System.IO.Directory.CreateDirectory(st.Substring("--selftest=".Length)); System.IO.File.WriteAllText(System.IO.Path.Combine(st.Substring("--selftest=".Length), "report.txt"), "Самопроверка упала: " + ex); } catch { }
                code = 2;
            }
            Shutdown(code);
            return;
        }
        // Лаунчер просит выйти перед обновлением или удалением плагина (имя ждёт лаунчер: "GhostShell.Plugin.Quit." + id плагина):
        // без этого обновление при открытом окне упиралось в «Закрой DNS Plus и повтори». DNS Plus ничего не держит в фоне — просто закрываемся.
        quitEvent = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, "GhostShell.Plugin.Quit.dns-plus");
        var qe = quitEvent;
        new System.Threading.Thread(() =>
        {
            try { qe.WaitOne(); } catch { return; }
            Dispatcher.BeginInvoke(new Action(() => Shutdown()));
        }) { IsBackground = true }.Start();
        new MainWindow().Show();
    }

    System.Threading.EventWaitHandle? quitEvent;
}
