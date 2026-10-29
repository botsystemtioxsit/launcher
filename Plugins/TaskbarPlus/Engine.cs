using System.Windows.Automation;

namespace TaskbarPlusApp;

// Движок внешнего вида: пока плагин запущен, раз в полсекунды следит за панелью задач и держит на ней форму
// (вырез окна панели — SetWindowRgn) и размытие/прозрачность. Всё это живёт только пока работает плагин:
// при выходе форма снимается, панель возвращается к обычному виду (если нет — «Перезапустить панель задач»).
sealed class TaskbarEngine : IDisposable
{
    readonly object gate = new();
    AppearanceSettings settings = new();
    CancellationTokenSource? cts;
    Task? loop;
    readonly Dictionary<IntPtr, string> applied = new();       // что уже стоит на окне: ключ формы
    readonly Dictionary<IntPtr, (DateTime At, List<(int L, int R)> Clusters)> islandCache = new();
    DateTime lastForced = DateTime.MinValue;
    bool accentOn;
    bool cacheStale;                                           // настройки поменялись — забыть, что уже применено
    string status = "";

    public string Status { get { lock (gate) return status; } }
    public AppearanceSettings Current { get { lock (gate) return settings.Clone(); } }
    public event Action? StatusChanged;

    void SetStatus(string s)
    {
        lock (gate) { if (status == s) return; status = s; }
        StatusChanged?.Invoke();
    }

    public void Update(AppearanceSettings s)
    {
        lock (gate) { settings = s.Clone(); cacheStale = true; }
        if (s.Active) Start();
        else { Stop(); ResetAll(); SetStatus(""); }
    }

    void Start()
    {
        lock (gate)
        {
            if (cts != null) return;
            cts = new CancellationTokenSource();
            var token = cts.Token;
            loop = Task.Run(() => Loop(token));
        }
    }

    void Stop()
    {
        Task? t;
        lock (gate) { cts?.Cancel(); cts = null; t = loop; loop = null; }
        try { t?.Wait(3000); } catch { }
    }

    void Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { Tick(); }
            catch (Exception ex) { SetStatus("Ошибка: " + ex.Message); }
            ct.WaitHandle.WaitOne(500);
        }
    }

    static List<IntPtr> TaskbarWindows()
    {
        var list = new List<IntPtr>();
        var main = Native.FindWindow("Shell_TrayWnd", null);
        if (main != IntPtr.Zero) list.Add(main);
        var h = IntPtr.Zero;
        while ((h = Native.FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero) list.Add(h);
        return list;
    }

    void Tick()
    {
        AppearanceSettings st;
        lock (gate)
        {
            st = settings;
            if (cacheStale) { applied.Clear(); islandCache.Clear(); cacheStale = false; }
        }
        var windows = TaskbarWindows();
        if (windows.Count == 0) { SetStatus("Панель задач не найдена (идёт перезапуск Проводника?)"); return; }

        var force = DateTime.UtcNow - lastForced > TimeSpan.FromSeconds(10); // раз в 10 с проверяем, что вырез не слетел
        if (force) lastForced = DateTime.UtcNow;
        var notes = new List<string>();

        foreach (var h in windows)
        {
            if (!Native.GetWindowRect(h, out var rc)) continue;
            if (st.Shape != AppearanceSettings.ShapeNone)
            {
                if (!Native.IsAtBottom(h, rc)) { notes.Add("форма работает только для панели внизу экрана"); if (applied.ContainsKey(h)) ClearRegion(h); }
                else ApplyShape(h, rc, st, force, notes);
            }
            else if (applied.ContainsKey(h)) ClearRegion(h);

            if (st.Backdrop != AppearanceSettings.BackdropNone)
            {
                var state = st.Backdrop switch { AppearanceSettings.BackdropClear => 2, AppearanceSettings.BackdropBlur => 3, _ => 4 };
                try { Native.SetAccent(h, state, st.TintAbgr()); accentOn = true; }
                catch (Exception ex) { notes.Add("размытие недоступно: " + ex.Message); }
            }
        }
        if (st.Backdrop == AppearanceSettings.BackdropNone && accentOn)
        {
            foreach (var h in windows) { try { Native.SetAccent(h, 0, 0); } catch { } }
            accentOn = false;
            Native.NotifySettingsChanged();
        }
        SetStatus(notes.Count == 0 ? "Применено" : "Применено, но: " + string.Join("; ", notes.Distinct()));
    }

    void ClearRegion(IntPtr h)
    {
        Native.SetWindowRgn(h, IntPtr.Zero, true);
        applied.Remove(h);
    }

    void ApplyShape(IntPtr h, Native.RECT rc, AppearanceSettings st, bool force, List<string> notes)
    {
        var scale = Native.Scale(h);
        List<(int L, int R)>? clusters = null;
        if (st.Shape == AppearanceSettings.ShapeIslands)
        {
            if (!islandCache.TryGetValue(h, out var c) || DateTime.UtcNow - c.At > TimeSpan.FromSeconds(1))
            {
                c = (DateTime.UtcNow, FindClusters(h, rc, scale, st.IslandMode == "each"));
                islandCache[h] = c;
            }
            clusters = c.Clusters;
            if (clusters.Count == 0) notes.Add("не нашёл значки панели — острова заменены плавающей панелью");
        }

        var key = $"{rc.Width}x{rc.Height}|{scale:F2}|{st.Shape}|{st.Radius}|{st.SideMargin}|{st.BottomGap}|{st.IslandStyle}|{st.IslandPadding}|{st.IslandMode}|{st.IslandGap}|" +
                  (clusters == null ? "" : string.Join(",", clusters.Select(c => c.L + "-" + c.R)));
        // уже стоит такая форма и Windows её не сбросила — ничего не делаем (иначе панель мерцала бы)
        if (!force && applied.TryGetValue(h, out var old) && old == key) return;
        if (applied.TryGetValue(h, out old) && old == key && Native.GetWindowRgnBox(h, out _) != 0) return;

        var rgn = BuildRegion(rc.Width, rc.Height, scale, st, clusters);
        if (rgn == IntPtr.Zero) return;
        // после успешного SetWindowRgn регионом владеет Windows; если не вышло — освобождаем сами
        if (Native.SetWindowRgn(h, rgn, true) == 0) Native.DeleteObject(rgn);
        else applied[h] = key;
    }

    // Форма панели в пикселях окна (отсчёт от его левого верхнего угла).
    static IntPtr BuildRegion(int w, int h, double scale, AppearanceSettings st, List<(int L, int R)>? clusters)
    {
        var r = (int)Math.Round(st.Radius * scale);
        var side = (int)Math.Round(st.SideMargin * scale);
        var gap = (int)Math.Round(st.BottomGap * scale);
        var pad = (int)Math.Round(st.IslandPadding * scale);
        var halfGap = (int)Math.Round(st.IslandGap * scale / 2.0);
        var each = st.IslandMode == "each";
        var inset = (int)Math.Round(3 * scale); // небольшой зазор сверху у плавающих форм
        IntPtr Round(int x1, int y1, int x2, int y2) => Native.CreateRoundRectRgn(x1, y1, x2 + 1, y2 + 1, Math.Max(1, 2 * r), Math.Max(1, 2 * r));

        switch (st.Shape)
        {
            case AppearanceSettings.ShapePill:
                return Round(side, inset, Math.Max(side + 1, w - side), Math.Max(inset + 1, h - gap));
            case AppearanceSettings.ShapeTab:
                // скруглён только верх: низ уходит за край окна и обрезается
                return Round(side, 0, Math.Max(side + 1, w - side), h + r);
            case AppearanceSettings.ShapeIslands:
                if (clusters == null || clusters.Count == 0) goto case AppearanceSettings.ShapePill;
                IntPtr all = IntPtr.Zero;
                foreach (var (l, rr) in clusters)
                {
                    // «каждый значок»: остров — сама кнопка, чуть сжатая, чтобы между соседними был зазор;
                    // «группа»: остров охватывает группу значков с полями
                    var x1 = each ? Math.Max(0, l + halfGap) : Math.Max(0, l - pad);
                    var x2 = each ? Math.Min(w, rr - halfGap) : Math.Min(w, rr + pad);
                    if (x2 - x1 < 4) continue;
                    var piece = st.IslandStyle == "tab" ? Round(x1, 0, x2, h + r) : Round(x1, inset, x2, Math.Max(inset + 1, h - gap));
                    if (all == IntPtr.Zero) { all = piece; continue; }
                    Native.CombineRgn(all, all, piece, Native.RGN_OR);
                    Native.DeleteObject(piece);
                }
                return all;
            default:
                return IntPtr.Zero;
        }
    }

    // Значки на панели (по одному или группами): через «Автоматизацию пользовательского интерфейса» Windows (UI Automation) берём
    // положение кнопок панели и склеиваем соседние в группы (Пуск+приложения, трей и т. д.). Если Windows
    // отдаёт другое устройство панели, групп не будет — плагин сообщит об этом, ничего не ломая.
    static List<(int L, int R)> FindClusters(IntPtr hwnd, Native.RECT rc, double scale, bool each)
    {
        var result = new List<(int L, int R)>();
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            var cond = new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.IsOffscreenProperty, false));
            var spans = new List<(int L, int R)>();
            foreach (AutomationElement el in root.FindAll(TreeScope.Descendants, cond))
            {
                System.Windows.Rect b;
                try { b = el.Current.BoundingRectangle; } catch { continue; }
                if (b.IsEmpty || b.Width < 4 || b.Height < 4) continue;
                if (b.Top < rc.Top - 4 || b.Bottom > rc.Bottom + 4 || b.Left < rc.Left - 4 || b.Right > rc.Right + 4) continue;
                spans.Add(((int)(b.Left - rc.Left), (int)(b.Right - rc.Left)));
            }
            spans.Sort((a, b) => a.L.CompareTo(b.L));
            var gap = (int)Math.Round(16 * scale);
            foreach (var s in spans)
            {
                if (result.Count > 0 && each)
                {
                    // одна и та же кнопка может прийти дважды (вложенные элементы) — такие повторы выбрасываем
                    var prev = result[^1];
                    if (s.L >= prev.L - 2 && s.R <= prev.R + 2) continue;
                    result.Add(s);
                }
                else if (result.Count > 0 && s.L - result[^1].R <= gap) result[^1] = (result[^1].L, Math.Max(result[^1].R, s.R));
                else result.Add(s);
            }
        }
        catch { /* Windows не отдала элементы панели — групп нет */ }
        return result;
    }

    // Снимает форму и размытие со всех окон панели.
    public void ResetAll()
    {
        try
        {
            foreach (var h in TaskbarWindows())
            {
                Native.SetWindowRgn(h, IntPtr.Zero, true);
                if (accentOn) { try { Native.SetAccent(h, 0, 0); } catch { } }
            }
            if (accentOn) Native.NotifySettingsChanged();
        }
        catch { /* при выходе ошибки не важны */ }
        accentOn = false;
        lock (gate) { applied.Clear(); islandCache.Clear(); }
    }

    public void Dispose()
    {
        Stop();
        ResetAll();
    }
}
