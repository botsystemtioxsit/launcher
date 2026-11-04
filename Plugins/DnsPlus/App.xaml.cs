using System.Windows;

namespace DnsPlusApp;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
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
        new MainWindow().Show();
    }
}
