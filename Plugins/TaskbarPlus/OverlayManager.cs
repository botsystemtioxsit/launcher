using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace TaskbarPlusApp;

// Следит за панелью задач и держит под ней окна с плашками (OverlayWindow). Всё идёт в потоке окна плагина, 25 раз в секунду:
// это дёшево — тяжёлое (разбор координат, построение плашек) повторяется только когда что-то поменялось.
sealed class OverlayManager : IDisposable
{
    readonly XamlEngine engine;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    readonly Dictionary<IntPtr, OverlayWindow> windows = new();
    readonly Dictionary<IntPtr, string> notes = new();      // почему плашки на этой панели не показаны (для отчёта и окна настроек)
    readonly Dictionary<IntPtr, string> planKeys = new();
    readonly Dictionary<IntPtr, List<RoundRect>> plans = new();

    AppearanceSettings settings = new();
    GeometryFile? geo;
    DateTime geoStamp = DateTime.MinValue;
    DateTime geoCheckedAt = DateTime.MinValue;
    long settingsVersion;
    string lastNote = "";

    public event Action? Changed;
    public string Note { get; private set; } = "";

    public OverlayManager(XamlEngine engine)
    {
        this.engine = engine;
        timer.Tick += (_, _) => Tick();
    }

    public void Update(AppearanceSettings s)
    {
        settings = s.Clone();
        settingsVersion++;
        if (s.Shape != AppearanceSettings.ShapeNone && XamlEngine.UnavailableReason == null) timer.Start();
        else { timer.Stop(); HideAll(); SetNote(""); }
    }

    void SetNote(string text)
    {
        if (text == lastNote) return;
        lastNote = text;
        Note = text;
        Changed?.Invoke();
    }

    // ---------- что показать ----------
    static List<IntPtr> TaskbarWindows()
    {
        var list = new List<IntPtr>();
        var main = Native.FindWindow("Shell_TrayWnd", null);
        if (main != IntPtr.Zero) list.Add(main);
        var h = IntPtr.Zero;
        while ((h = Native.FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero) list.Add(h);
        return list;
    }

    void RefreshGeometry()
    {
        if (DateTime.UtcNow - geoCheckedAt < TimeSpan.FromMilliseconds(40)) return;
        geoCheckedAt = DateTime.UtcNow;
        try
        {
            var info = new FileInfo(GeometryFile.DefaultPath);
            if (!info.Exists) { geo = null; geoStamp = DateTime.MinValue; return; }
            var stamp = info.LastWriteTimeUtc;
            if (stamp == geoStamp) return;
            var parsed = GeometryFile.Read(GeometryFile.DefaultPath);
            if (parsed == null) return;   // файл как раз пишется — прочитаем в следующий раз
            geo = parsed;
            geoStamp = stamp;
        }
        catch { /* прочитаем в следующий раз */ }
    }

    // Какой панели принадлежат измерения: по размеру окна XAML (он совпадает с размером окна панели).
    BarGeometry? Match(Native.RECT rc, bool primary)
    {
        if (geo == null || geo.Bars.Count == 0) return null;
        foreach (var b in geo.Bars)
            if (Math.Abs(b.RootW - rc.Width) <= 3 && Math.Abs(b.RootH - rc.Height) <= 3) return b;
        // размеры не сошлись (редкий масштаб экрана): единственная панель в измерениях — основная
        return primary && geo.Bars.Count == 1 ? geo.Bars[0] : null;
    }

    void Tick()
    {
        try
        {
            var s = settings;
            if (s.Shape == AppearanceSettings.ShapeNone) { HideAll(); return; }
            if (!engine.BarTransparent) { HideAll(); SetNote(engine.Ready ? "Панель ещё не стала прозрачной — плашки появятся, когда движок её подготовит." : ""); return; }
            RefreshGeometry();

            var fill = Palette.Resolve();
            var seen = new HashSet<IntPtr>();
            var wins = TaskbarWindows();
            var issues = new List<string>();
            for (var n = 0; n < wins.Count; n++)
            {
                var hwnd = wins[n];
                if (!Native.GetWindowRect(hwnd, out var rc)) continue;
                var scale = Native.Scale(hwnd);

                string? hide = null;
                if (!Native.IsWindowVisible(hwnd)) hide = "панель не видна";
                else if (!Native.IsTopmost(hwnd)) hide = "панель опущена под окно (полноэкранное приложение)";
                else if (Native.IsHiddenAtBottom(hwnd, rc, scale)) hide = "панель спрятана (автоскрытие)";
                else if (Native.FullscreenActive()) hide = "на экране полноэкранное приложение";
                var bar = hide == null ? Match(rc, n == 0) : null;
                if (hide == null && bar == null) hide = geo == null ? "нет данных от движка о положении значков" : "измерения не относятся к этой панели";

                if (hide != null)
                {
                    if (windows.TryGetValue(hwnd, out var hidden)) hidden.Hide();
                    notes[hwnd] = hide;
                    if (n == 0 && hide.StartsWith("нет данных") || hide.StartsWith("измерения")) issues.Add(hide);
                    seen.Add(hwnd);
                    continue;
                }

                // план плашек пересчитываем, только когда поменялись настройки, измерения или размер окна
                var key = $"{settingsVersion}|{geoStamp.Ticks}|{rc.Width}x{rc.Height}|{bar!.Scale100}";
                if (!planKeys.TryGetValue(hwnd, out var old) || old != key)
                {
                    plans[hwnd] = ShapeBuilder.Build(s, rc.Width, rc.Height, bar.Scale, bar);
                    planKeys[hwnd] = key;
                }
                if (!windows.TryGetValue(hwnd, out var ov)) windows[hwnd] = ov = new OverlayWindow(rc);
                var plan = plans[hwnd];
                ov.SetShapes(plan, fill.Color, fill.Alpha);
                ov.Place(hwnd, rc);
                notes[hwnd] = plan.Count == 0 ? "плашек нет: значки не найдены" : $"плашек: {plan.Count}";
                if (plan.Count == 0) issues.Add("значки панели не найдены");
                seen.Add(hwnd);
            }

            // панели, которых больше нет (перезапуск Проводника, отключён монитор), — убираем окна
            foreach (var h in windows.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                windows[h].Dispose(); windows.Remove(h); notes.Remove(h); plans.Remove(h); planKeys.Remove(h);
            }
            SetNote(issues.Count == 0 ? "" : string.Join("; ", issues.Distinct()));
        }
        catch (Exception ex) { SetNote("плашки: ошибка — " + ex.Message); }
    }

    void HideAll()
    {
        foreach (var w in windows.Values) w.Hide();
    }

    // Подробности для отчёта разработчику: где окна, что показано, что видит движок.
    public string DebugText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Плашки: " + (Note.Length == 0 ? "работают" : Note));
        foreach (var kv in windows)
        {
            Native.GetWindowRect(kv.Key, out var tb);
            Native.GetWindowRect(kv.Value.Hwnd, out var ov);
            sb.AppendLine($"  панель {kv.Key:X} ({tb.Left},{tb.Top},{tb.Right},{tb.Bottom}); окно плашек {(kv.Value.IsShown ? "показано" : "скрыто")} ({ov.Left},{ov.Top},{ov.Right},{ov.Bottom}); " +
                          $"сразу под панелью: {Native.GetWindow(kv.Key, Native.GW_HWNDNEXT) == kv.Value.Hwnd}; {(notes.TryGetValue(kv.Key, out var n) ? n : "")}");
            if (plans.TryGetValue(kv.Key, out var p))
                foreach (var r in p) sb.AppendLine($"    плашка x={r.X:F0} y={r.Y:F0} ш={r.W:F0} в={r.H:F0} скругление={r.R:F0}");
        }
        if (geo != null)
            foreach (var b in geo.Bars)
                sb.AppendLine($"  измерения: окно {b.RootW}x{b.RootH} масштаб {b.Scale100}%, значки {b.Apps?.L}..{b.Apps?.R}, виджеты {b.Widgets?.L}..{b.Widgets?.R}, трей {b.System?.L}..{b.System?.R}, отдельных значков {b.Icons.Count}");
        return sb.ToString();
    }

    public void Dispose()
    {
        timer.Stop();
        foreach (var w in windows.Values) w.Dispose();
        windows.Clear();
    }
}

// Цвет плашек: берётся у Color Plus (если он применён и в режиме «цвет»), иначе тёмный или светлый, как у панели Windows.
static class Palette
{
    public readonly record struct Fill(Color? Color, double Alpha);

    static DateTime themeAt = DateTime.MinValue;
    static bool light;

    static bool LightTheme()
    {
        if (DateTime.UtcNow - themeAt < TimeSpan.FromSeconds(5)) return light;
        themeAt = DateTime.UtcNow;
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            light = k?.GetValue("SystemUsesLightTheme") is int v && v != 0;
        }
        catch { light = false; }
        return light;
    }

    public static Color Parse(string hex)
    {
        var h = hex.TrimStart('#');
        var v = Convert.ToInt32(h, 16);
        return System.Windows.Media.Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public static Fill Resolve()
    {
        var link = ColorPlusLink.Current;
        if (link.Active)
        {
            if (link.Mode == "clear") return new Fill(null, 0);
            if (link.Mode is "solid" or "acrylic") return new Fill(Parse(link.Color), Math.Clamp(link.Opacity / 100.0, 0.05, 1));
        }
        return LightTheme() ? new Fill(Parse("#F3F3F3"), 0.88) : new Fill(Parse("#1F1F1F"), 0.88);
    }
}
