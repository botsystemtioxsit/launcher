namespace TaskbarPlusApp;

// Два способа менять панель: обычный (вырез окна, работает и в Windows 10) и новый (движок XAML для Windows 11).
// Новый движок работает всегда, когда он возможен (Windows 11, 64-бит, не заблокирован); обычный способ — только если нового быть не может.
sealed class Engines : IDisposable
{
    readonly TaskbarEngine classic = new();
    readonly XamlEngine xaml = new();
    readonly TaskbarCrop crop = new();
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
        // Форму держит новый движок. Пока он подключается (после перезапуска Проводника, при долгом ответе), обычный способ не включаем:
        // он вырезает часы и трей, если рядом нет места, — значки пропадали. Обычный способ — только там, где нового движка быть не может.
        if (settings.UseXaml && XamlEngine.UnavailableReason == null && !xaml.Blocked) c.Shape = AppearanceSettings.ShapeNone;
        classic.Update(c);
        // Обрезка по островам — только когда движок работает и у панели есть форма с островами
        crop.SetWanted(settings.UseXaml && settings.CropToIslands && settings.Shape != AppearanceSettings.ShapeNone && xaml.Ready);
    }

    public void Dispose()
    {
        try { crop.Dispose(); } catch { }
        try { xaml.Dispose(); } catch { }
        try { classic.Dispose(); } catch { }
    }
}
