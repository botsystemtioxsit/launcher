using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.Win32;

namespace SeasonPlusApp;

public partial class App : Application
{
    const string MutexName = "SeasonPlus.SingleInstance.GhostShell";
    const string ShowEventName = "SeasonPlus.Show.GhostShell";
    const string QuitEventName = "GhostShell.Plugin.Quit.season-plus";   // имя ждёт лаунчер: "GhostShell.Plugin.Quit." + id плагина

    static readonly SeasonOverlay overlay = new();
    Mutex? mutex;
    EventWaitHandle? showEvent, quitEvent;
    MainWindow? window;
    internal static bool QuitRequested { get; set; }
    internal static bool OverlayRunning => overlay.Running;

    static string? Arg(StartupEventArgs e, string prefix) => e.Args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];

    internal static void StartOverlay(SeasonSettings s)
    {
        if (overlay.Running) overlay.SetSettings(s); else overlay.Start(s);
    }

    internal static void StopOverlay() => overlay.Stop();
    internal static void Fireworks() => overlay.Fireworks();

    // Запуск вместе с Windows: плагин сидит в фоне, пока включён.
    internal static void SetAutostart(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (on) k.SetValue("SeasonPlus", "\"" + Environment.ProcessPath + "\" --background"); else k.DeleteValue("SeasonPlus", false);
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Loc.Start(e.Args);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // служебные режимы для проверки в CI (без окна)
        if (e.Args.Contains("--revert"))
        {
            try { using var q = EventWaitHandle.OpenExisting(QuitEventName); q.Set(); Thread.Sleep(2500); } catch { }
            var st = SeasonSettings.Load(); st.Active = false; st.Save();
            SetAutostart(false);
            Shutdown(); return;
        }
        var shot = Arg(e, "--screenshot=");
        if (shot != null)
        {
            try
            {
                Directory.CreateDirectory(shot);
                var w = new MainWindow(SeasonSettings.Load());
                w.RenderTo(Path.Combine(shot, "season-plus-ui.png"));
                SelfTest.Sheets(shot);
            }
            catch (Exception ex) { try { File.WriteAllText(Path.Combine(shot, "season-plus-ui-error.txt"), ex.ToString()); } catch { } }
            Loc.WriteAudit(shot);
            Shutdown(); return;
        }
        var self = Arg(e, "--selftest=");
        if (self != null) { SelfTest.Run(self); Shutdown(); return; }
        var ot = Arg(e, "--overlay-test=");
        if (ot != null) { SelfTest.OverlayTest(ot); Shutdown(); return; }
        if (e.Args.Contains("--apply"))
        {
            var s = SeasonSettings.Load(); s.Active = true; s.Save();
            SetAutostart(true);
        }

        // Одна копия: повторный запуск (из лаунчера или ярлыка) просто показывает окно уже открытой.
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

        // Лаунчер просит выйти перед обновлением или удалением плагина
        quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        var qe = quitEvent;
        new Thread(() =>
        {
            try { qe.WaitOne(); } catch { return; }
            Dispatcher.BeginInvoke(new Action(() => { QuitRequested = true; overlay.Stop(); Shutdown(); }));
        }) { IsBackground = true }.Start();

        var saved = SeasonSettings.Load();
        if (saved.Active) StartOverlay(saved);
        var background = e.Args.Contains("--background") || e.Args.Contains("--apply");
        if (background) { if (!overlay.Running) Shutdown(); return; }   // фоновый запуск: если выключено — сразу выходим
        ShowWindow();
    }

    void ShowWindow()
    {
        window ??= new MainWindow(SeasonSettings.Load());
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Show();
        window.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { overlay.Stop(); } catch { }
        try { mutex?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}
