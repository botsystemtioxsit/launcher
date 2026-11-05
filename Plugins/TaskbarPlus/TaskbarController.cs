namespace TaskbarPlusApp;

// Всё, что держит вид панели: движок в Проводнике (делает фон прозрачным и измеряет значки) + окно с плашками под панелью.
sealed class TaskbarController : IDisposable
{
    readonly XamlEngine xaml = new();
    readonly OverlayManager overlay;
    AppearanceSettings settings = new();

    public event Action? StatusChanged;
    public XamlEngine Xaml => xaml;
    public OverlayManager Overlay => overlay;

    public TaskbarController()
    {
        overlay = new OverlayManager(xaml);
        xaml.Changed += () => StatusChanged?.Invoke();
        overlay.Changed += () => StatusChanged?.Invoke();
        xaml.DebugText = overlay.DebugText;
    }

    public string Status
    {
        get
        {
            var s = xaml.Status;
            var note = overlay.Note;
            return note.Length == 0 ? s : (s.Length == 0 ? note : s + " " + char.ToUpper(note[0]) + note[1..] + ".");
        }
    }

    public void Update(AppearanceSettings s)
    {
        settings = s.Clone();
        xaml.Update(s);
        overlay.Update(s);
    }

    public void Dispose()
    {
        // сначала возвращаем родной фон панели, потом убираем плашки: так панель ни на миг не остаётся прозрачной без них
        try { xaml.Dispose(); } catch { }
        try { overlay.Dispose(); } catch { }
    }
}
