namespace ColorPlusApp;

// Собирает три части плагина: эффекты окон и панели, системные цвета и связь с Taskbar Plus.
sealed class Controller : IDisposable
{
    public readonly WindowEffects Effects = new();
    public readonly GlassManager Glass = new(System.Windows.Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher);
    readonly SystemColors sys = new();

    public Controller() { Effects.Glass = Glass; }

    public string? SystemError { get; private set; }

    public void Apply(ColorSettings s)
    {
        Effects.Update(s);
        TaskbarLink.Write(s);
        SystemError = sys.Apply(s);
    }

    // Всё, что менял плагин, возвращается: окна, панель, цвета.
    public void Dispose()
    {
        try { Effects.Dispose(); } catch { }
        try { Glass.Dispose(); } catch { }
        try { sys.RestoreAll(); } catch { }
        try { TaskbarLink.Write(new ColorSettings()); } catch { }
    }
}
