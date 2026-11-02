using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.Win32;

namespace CursorPlusApp;

public partial class App : Application
{
    const string MutexName = "CursorPlus.SingleInstance.GhostShell";
    const string ShowEventName = "CursorPlus.Show.GhostShell";
    const string QuitEventName = "GhostShell.Plugin.Quit.cursor-plus";   // имя ждёт лаунчер: "GhostShell.Plugin.Quit." + id плагина

    static readonly TrailOverlay overlay = new();
    Mutex? mutex;
    EventWaitHandle? showEvent, quitEvent;
    MainWindow? window;
    bool forceBackground;
    internal static bool QuitRequested { get; set; }
    internal static bool OverlayRunning => overlay.Running;

    static string? Arg(StartupEventArgs e, string prefix) => e.Args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];

    // Шлейф и эффекты клика: прозрачное окно поверх экрана, пока плагин работает.
    internal static void StartOverlay(CursorSettings s)
    {
        if (!s.WantsOverlay) { overlay.Stop(); return; }
        var arrow = CursorApplier.Build(Roles.Get("Arrow"), s, new[] { CursorApplier.BaseSize(s) })?.FirstOrDefault();
        overlay.Start(TrailSpec.From(s, arrow));
    }

    internal static void StopOverlay() => overlay.Stop();

    // Запуск вместе с Windows нужен только для шлейфа (сам курсор записан в Windows и работает без плагина).
    internal static void SetAutostart(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (on) k.SetValue("CursorPlus", "\"" + Environment.ProcessPath + "\" --background"); else k.DeleteValue("CursorPlus", false);
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // служебные режимы для проверки в CI (без окна)
        if (e.Args.Contains("--revert"))
        {
            try { using var q = EventWaitHandle.OpenExisting(QuitEventName); q.Set(); Thread.Sleep(2500); } catch { }
            new CursorApplier().Revert();
            var st = CursorSettings.Load(); st.Applied = false; st.OverlayOn = false; st.Save();
            SetAutostart(false);
            Shutdown(); return;
        }
        var shot = Arg(e, "--screenshot=");
        if (shot != null)
        {
            try
            {
                Directory.CreateDirectory(shot);
                var s = CursorSettings.Load();
                new MainWindow(s).RenderTo(Path.Combine(shot, "cursor-plus-ui.png"));
                var ed = new EditorWindow(); ed.Demo(); ed.RenderTo(Path.Combine(shot, "cursor-plus-editor.png"));
                SelfTest.Sheets(shot, s);
            }
            catch (Exception ex) { try { File.WriteAllText(Path.Combine(shot, "cursor-plus-ui-error.txt"), ex.ToString()); } catch { } }
            Shutdown(); return;
        }
        var self = Arg(e, "--selftest=");
        if (self != null) { SelfTest.Run(self); Shutdown(); return; }
        var ot = Arg(e, "--overlay-test=");
        if (ot != null) { SelfTest.OverlayTest(ot, Arg(e, "--style=") ?? "comet", Arg(e, "--click=") ?? "none", Arg(e, "--mode=") ?? "cursor"); Shutdown(); return; }
        if (e.Args.Contains("--apply"))
        {
            var s = CursorSettings.Load();
            var err = new CursorApplier().Apply(s);
            s.Applied = true; s.OverlayOn = s.WantsOverlay; s.Save();
            SetAutostart(s.WantsOverlay);
            if (err != null) { try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "cursor-plus-error.txt"), err); } catch { } }
            if (!s.WantsOverlay) { Shutdown(); return; }
            forceBackground = true;
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

        var saved = CursorSettings.Load();
        if (saved.OverlayOn && saved.WantsOverlay) StartOverlay(saved);
        var background = forceBackground || e.Args.Contains("--background");
        if (background) { if (!overlay.Running) Shutdown(); return; }   // фоновый запуск: если шлейфа нет — сразу выходим
        ShowWindow();
    }

    void ShowWindow()
    {
        window ??= new MainWindow(CursorSettings.Load());
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
