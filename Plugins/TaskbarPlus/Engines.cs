namespace TaskbarPlusApp;

// Два способа менять панель: обычный (вырез окна, работает и в Windows 10) и новый (движок XAML для Windows 11).
// Пока новый движок не подключился и не нашёл панель, работает обычный; как только он заработал — обычный снимается.
sealed class Engines : IDisposable
{
    readonly TaskbarEngine classic = new();
    readonly XamlEngine xaml = new();
    AppearanceSettings settings = new();

    public event Action? StatusChanged;
    public XamlEngine Xaml => xaml;

    public Engines()
    {
        classic.StatusChanged += () => StatusChanged?.Invoke();
        xaml.Changed += () => { ApplyClassic(); StatusChanged?.Invoke(); };
    }

    public string Status
    {
        get
        {
            if (settings.UseXaml && xaml.Status.Length > 0) return xaml.Status;
            return classic.Status;
        }
    }

    public void Update(AppearanceSettings s)
    {
        settings = s.Clone();
        xaml.Update(s);
        ApplyClassic();
    }

    void ApplyClassic()
    {
        var c = settings.Clone();
        c.UseXaml = false;
        if (settings.UseXaml && xaml.Ready) c.Shape = AppearanceSettings.ShapeNone; // форму уже держит новый движок
        classic.Update(c);
    }

    public void Dispose()
    {
        try { xaml.Dispose(); } catch { }
        try { classic.Dispose(); } catch { }
    }
}
