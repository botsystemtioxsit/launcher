using System.Collections.Concurrent;
using System.Diagnostics;

namespace ColorPlusApp;

// Применяет эффекты к окнам всех программ и к панели задач, пока плагин запущен. Всё, что поменяли, запоминается и
// возвращается: при выключении эффекта, при выходе из плагина и когда окно перестаёт подходить под правила.
//
// Что умеет Windows делать с чужими окнами «снаружи» (честно):
//  • «только фон» — цветовой ключ слоёного окна: пиксели цвета фона становятся прозрачными, текст, картинки и заголовок остаются (BackgroundProbe;
//    не работает у окон, которые рисуются видеокартой: Chrome, Edge, Electron, магазинные приложения);
//  • прозрачность всего окна (WS_EX_LAYERED + SetLayeredWindowAttributes) — работает почти везде, видно рабочий стол сквозь окно;
//  • размытие, акрил, «слюда» за окном (DWM) — видны там, где окно само прозрачно или у рамки и заголовка; у окон, которые
//    закрашивают всё содержимое сплошным цветом, виден только заголовок (кнопка «растянуть эффект» помогает не всем программам);
//  • цвет и тёмный режим заголовка (Windows 11).
sealed class WindowEffects : IDisposable
{
    static readonly HashSet<string> HardClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland",
        "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "Shell_InputSwitchTopLevelWindow", "ForegroundStaging",
        "MultitaskingViewFrame", "Windows.UI.Input.InputSite.WindowClass", "TaskListThumbnailWnd", "DV2ControlHost", "SysShadow",
        "NativeHWNDHost", "Shell_LightDismissOverlay", "ApplicationManager_DesktopShellWindow", "EdgeUiInputTopWndClass",
        "Windows.UI.Composition.DesktopWindowContentBridge", "TaskManagerWindow",
    };

    static readonly HashSet<string> HardProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "dwm", "csrss", "winlogon", "logonui", "searchhost", "startmenuexperiencehost", "shellexperiencehost", "textinputhost",
        "lockapp", "widgets", "sihost", "fontdrvhost", "taskmgr",
        // программы, которые сами управляют своими окнами или рисуют обои: их не трогаем никогда (даже по правилу программы)
        "steam", "steamwebhelper", "steamservice", "wallpaper32", "wallpaper64", "webwallpaper32", "webwallpaper64", "applicationwallpaper32",
        "applicationwallpaper64", "ui32", "ui64", "launcher", "wallpaperengine", "lively", "rainmeter", "obs64", "obs32",
    };

    // Метка на окне, которое слоим мы: переживает падение плагина, по ней при следующем запуске окно возвращается как было.
    const string OurProp = "GhostShell.ColorPlus.Layered";

    class WinState
    {
        public bool OpacityChecked, HadLayered, LayeredByUs;
        public int Opacity = 100;
        public bool GlassActive;              // под окном лежит наше стекло
        public bool Keyed;                    // сейчас на окне цветовой ключ
        public uint KeyColor;
        public DateTime KeyAt = DateTime.MinValue;
        public string Outcome = "";           // background | alpha | none — что получилось на самом деле
        public string? NoKeyWhy;              // почему «только фон» у этого окна невозможен
        public string Backdrop = ColorSettings.BdNone;
        public bool Extended, AccentSet, DarkSet, CaptionSet;
        public int OrigDark = -1;
        public string Sig = "";
        public DateTime LastAssert = DateTime.MinValue;
    }

    sealed record Eff(string Mode, int GlassAlpha, string GlassTint, int GlassBlur, bool GlassStrip, int Opacity, string Backdrop, bool Extend, bool Dark, string? Caption, string? CaptionText, string Tint, int TintOpacity);

    readonly object gate = new();
    readonly Dictionary<IntPtr, WinState> states = new();
    readonly HashSet<IntPtr> taskbarTouched = new();
    readonly ConcurrentDictionary<uint, (string Name, DateTime At)> names = new();
    readonly uint selfPid = (uint)Environment.ProcessId;
    ColorSettings settings = new();
    CancellationTokenSource? cts;
    Task? loop;
    string[] running = Array.Empty<string>();
    Dictionary<string, string> unsupported = new();
    string status = "";
    bool taskbarWasOn;
    bool recovered;

    static readonly bool Win11 = Environment.OSVersion.Version.Build >= 22000;
    static readonly bool BackdropApi = Environment.OSVersion.Version.Build >= 22621;

    // Стекло под окнами (создаётся в Controller; окна стекла живут в потоке интерфейса)
    public GlassManager? Glass { get; set; }

    public event Action? Changed;
    public string Status { get { lock (gate) return status; } }
    // имена программ с окнами, которые движок видел в последний раз — для списка «Программы»
    public string[] RunningApps { get { lock (gate) return running; } }
    // программы, у которых «только фон» невозможен, и почему — для пометки в списке «Программы»
    public Dictionary<string, string> UnsupportedApps { get { lock (gate) return new Dictionary<string, string>(unsupported, StringComparer.OrdinalIgnoreCase); } }

    void SetStatus(string s)
    {
        lock (gate) { if (status == s) return; status = s; }
        Changed?.Invoke();
    }

    public void Update(ColorSettings s)
    {
        lock (gate) settings = s.Clone();
        if (!recovered) { recovered = true; try { RecoverLeftovers(); } catch { } }
        if (s.WindowsOn || s.TaskbarOn) Start();
        else { Stop(); RestoreEverything(); SetStatus(""); }
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

    // ---------- один шаг ----------
    void Tick()
    {
        ColorSettings s;
        lock (gate) s = settings;

        var seen = new HashSet<IntPtr>();
        var apps = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var applied = 0;
        var unsup = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Native.EnumWindows((h, _) =>
        {
            try
            {
                if (!Eligible(h, out var cls, out var proc)) return true;
                apps.Add(proc);
                if (!s.WindowsOn) return true;
                // окна без своей строки заголовка (Steam, Discord, Wallpaper Engine, программы на Qt/CEF) общими настройками не трогаем — только по правилу программы
                var hasCaption = (Native.GetLong(h, Native.GWL_STYLE) & Native.WS_CAPTION) == Native.WS_CAPTION;
                if (!hasCaption && !s.Apps.ContainsKey(proc)) return true;
                var eff = EffectiveFor(s, proc);
                if (eff == null) return true;
                seen.Add(h);
                if (!states.TryGetValue(h, out var st)) states[h] = st = new WinState();
                var sig = eff.ToString();
                if (sig != st.Sig || (DateTime.UtcNow - st.LastAssert).TotalSeconds > 6)
                {
                    Apply(h, st, eff, cls);
                    st.Sig = sig;
                    st.LastAssert = DateTime.UtcNow;
                }
                if (st.NoKeyWhy != null) unsup[proc] = st.NoKeyWhy;
                applied++;
            }
            catch { /* окно закрылось или нам не дали его менять — идём дальше */ }
            return true;
        }, IntPtr.Zero);

        // окна, которые больше не подходят (закрылись, исключены, эффект выключен) — вернуть как было
        foreach (var h in states.Keys.ToList())
        {
            if (seen.Contains(h) && Native.IsWindow(h)) continue;
            if (Native.IsWindow(h)) { try { Restore(h, states[h]); } catch { } }
            states.Remove(h);
        }
        lock (gate) { running = apps.ToArray(); unsupported = new Dictionary<string, string>(unsup); }
        Glass?.SetTargets(states.Where(kv => kv.Value.GlassActive && seen.Contains(kv.Key)).Select(kv => kv.Key).ToList(), new GlassConfig(s.GlassTint, s.GlassBlur, s.GlassKeepStrip));

        ApplyTaskbar(s);

        string text;
        if (!s.WindowsOn) text = s.TaskbarOn ? "Работает для панели задач" : "";
        else
        {
            var keyed = states.Values.Count(x => x.Outcome == "background");
            var whole = states.Values.Count(x => x.Outcome == "alpha");
            var glassed = states.Values.Count(x => x.Outcome == "glass");
            text = $"Окон под эффектами: {applied}";
            if (s.WindowMode == ColorSettings.WmBackground || keyed > 0) text += $" · только фон: {keyed}";
            if (whole > 0) text += $" · целиком: {whole}";
            if (glassed > 0) text += $" · стекло: {glassed}";
            if (unsup.Count > 0) text += " · у этих фон прозрачным не сделать (рисуются видеокартой): " + string.Join(", ", unsup.Keys);
        }
        SetStatus(text);
    }

    string ProcessName(uint pid)
    {
        if (names.TryGetValue(pid, out var c) && DateTime.UtcNow - c.At < TimeSpan.FromSeconds(30)) return c.Name;
        var name = "";
        try { using var p = Process.GetProcessById((int)pid); name = p.ProcessName.ToLowerInvariant(); } catch { }
        names[pid] = (name, DateTime.UtcNow);
        return name;
    }

    bool Eligible(IntPtr h, out string cls, out string proc)
    {
        cls = ""; proc = "";
        if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return false;
        if (Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero) return false;
        var ex = Native.GetLong(h, Native.GWL_EXSTYLE);
        if ((ex & (Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE)) != 0) return false;
        if (!Native.GetWindowRect(h, out var r) || r.Width < 120 || r.Height < 60) return false;
        if (Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        cls = Native.ClassName(h);
        if (HardClasses.Contains(cls)) return false;
        Native.GetWindowThreadProcessId(h, out var pid);
        if (pid == selfPid) return false;
        proc = ProcessName(pid);
        if (proc.Length == 0 || HardProcesses.Contains(proc)) return false;
        var style = Native.GetLong(h, Native.GWL_STYLE);
        if ((style & Native.WS_CAPTION) != Native.WS_CAPTION && Native.CoversMonitor(h, r)) return false; // полноэкранная игра или видео
        return true;
    }

    static Eff? EffectiveFor(ColorSettings s, string proc)
    {
        s.Apps.TryGetValue(proc, out var rule);
        if (rule?.Mode == "skip") return null;
        var custom = rule?.Mode == "custom";
        var mode = custom && rule!.WinMode != null ? rule.WinMode : s.WindowMode;   // «только фон» и «стекло» возможны лишь по правилу программы
        return new Eff(
            mode, s.GlassAlpha, s.GlassTint, s.GlassBlur, s.GlassKeepStrip,
            mode == ColorSettings.WmGlass ? s.GlassAlpha : custom ? rule!.Opacity : s.WindowOpacity,
            mode == ColorSettings.WmGlass ? ColorSettings.BdNone : custom && rule!.Backdrop != null ? rule.Backdrop : s.Backdrop,
            s.ExtendFrame, s.DarkTitle, s.TitleColor, s.TitleTextColor, s.Tint, s.TintOpacity);
    }

    // ---------- применить к окну ----------
    void Apply(IntPtr h, WinState st, Eff eff, string cls)
    {
        // прозрачность: только если окно само не слоёное (иначе сломаем его собственную прозрачность)
        if (!st.OpacityChecked)
        {
            st.HadLayered = (Native.GetLong(h, Native.GWL_EXSTYLE) & Native.WS_EX_LAYERED) != 0;
            st.OpacityChecked = true;
        }
        var canLayer = !st.HadLayered && cls != "ApplicationFrameWindow"; // окна магазинных приложений слоёность не любят
        var wantKey = eff.Mode == ColorSettings.WmBackground;
        var glass = eff.Mode == ColorSettings.WmGlass;
        var wantAlpha = eff.Mode == ColorSettings.WmAlpha || glass;
        var outcome = "none";
        if (wantKey)
        {
            var why = canLayer ? BackgroundProbe.WhyUnsupported(h, cls) : "окно само управляет своей прозрачностью";
            if (why == null)
            {
                // цвет фона: находим при первом применении и обновляем раз в 12 с (сменилась тема, открыта другая страница)
                if ((!st.Keyed || (DateTime.UtcNow - st.KeyAt).TotalSeconds > 12) && !Native.IsHungAppWindow(h))
                {
                    if (BackgroundProbe.TrySample(h, out var color, out var share) && share >= BackgroundProbe.MinShare) { st.KeyColor = color; st.Keyed = true; }
                    else if (!st.Keyed) why = "у окна нет одного явного цвета фона (картинка, градиент)";
                    st.KeyAt = DateTime.UtcNow;
                }
            }
            st.NoKeyWhy = why;
            if (why != null)
            {
                wantKey = false;
            }
        }
        var alphaPercent = wantAlpha ? eff.Opacity : 100;
        if ((wantKey || (wantAlpha && alphaPercent < 100)) && canLayer)
        {
            if (!st.LayeredByUs) { Native.SetProp(h, OurProp, (IntPtr)1); Native.SetLong(h, Native.GWL_EXSTYLE, Native.GetLong(h, Native.GWL_EXSTYLE) | Native.WS_EX_LAYERED); st.LayeredByUs = true; }
            var flags = (wantKey ? Native.LWA_COLORKEY : 0u) | (wantAlpha ? Native.LWA_ALPHA : 0u);
            Native.SetLayeredWindowAttributes(h, wantKey ? st.KeyColor : 0, (byte)Math.Round(alphaPercent * 255 / 100.0), flags);
            st.Opacity = alphaPercent;
            if (wantKey) outcome = "background";
            else if (wantAlpha) outcome = glass ? "glass" : "alpha";
        }
        else if (st.LayeredByUs) RemoveLayer(h, st);
        st.GlassActive = glass && outcome == "glass";
        if (!wantKey) st.Keyed = false;
        if (eff.Mode != ColorSettings.WmBackground) st.NoKeyWhy = null;
        st.Outcome = outcome;

        ApplyBackdrop(h, st, eff);

        if (Win11)
        {
            if (eff.Dark)
            {
                if (st.OrigDark < 0) st.OrigDark = Native.DwmGetWindowAttribute(h, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, out var d, sizeof(int)) == 0 ? d : 0;
                Native.SetDwm(h, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, 1);
                st.DarkSet = true;
            }
            else if (st.DarkSet) { Native.SetDwm(h, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, Math.Max(0, st.OrigDark)); st.DarkSet = false; }

            if (eff.Caption != null)
            {
                Native.SetDwmColor(h, Native.DWMWA_CAPTION_COLOR, Native.ColorRef(eff.Caption));
                Native.SetDwmColor(h, Native.DWMWA_BORDER_COLOR, Native.ColorRef(eff.Caption));
                if (eff.CaptionText != null) Native.SetDwmColor(h, Native.DWMWA_TEXT_COLOR, Native.ColorRef(eff.CaptionText));
                st.CaptionSet = true;
            }
            else if (st.CaptionSet) ResetCaption(h, st);
        }
    }

    void ApplyBackdrop(IntPtr h, WinState st, Eff eff)
    {
        var bd = eff.Backdrop;
        // системные подложки (Windows 11 22H2+): слюда, акрил, вкладки
        if (BackdropApi && bd is ColorSettings.BdMica or ColorSettings.BdTabbed or ColorSettings.BdAcrylic)
        {
            if (st.AccentSet) { Native.SetAccent(h, Native.AccentDisabled); st.AccentSet = false; }
            Native.SetDwm(h, Native.DWMWA_SYSTEMBACKDROP_TYPE, bd == ColorSettings.BdMica ? 2 : bd == ColorSettings.BdAcrylic ? 3 : 4);
        }
        else if (bd is ColorSettings.BdBlur or ColorSettings.BdAcrylic)
        {
            // размытие и акрил «через подложку окна» — для Windows 10 и ранних сборок Windows 11
            if (BackdropApi) Native.SetDwm(h, Native.DWMWA_SYSTEMBACKDROP_TYPE, 1);
            Native.SetAccent(h, bd == ColorSettings.BdBlur && eff.TintOpacity == 0 ? Native.AccentBlurBehind : Native.AccentAcrylic, Native.Abgr(eff.Tint, eff.TintOpacity));
            st.AccentSet = true;
        }
        else if (st.Backdrop != ColorSettings.BdNone)
        {
            if (st.AccentSet) { Native.SetAccent(h, Native.AccentDisabled); st.AccentSet = false; }
            if (BackdropApi) Native.SetDwm(h, Native.DWMWA_SYSTEMBACKDROP_TYPE, 0);
        }

        var want = bd != ColorSettings.BdNone && eff.Extend;
        if (want && !st.Extended) { Native.Margins(h, -1); st.Extended = true; }
        else if (!want && st.Extended) { Native.Margins(h, 0); st.Extended = false; }
        st.Backdrop = bd;
    }

    static void ResetCaption(IntPtr h, WinState st)
    {
        Native.SetDwmColor(h, Native.DWMWA_CAPTION_COLOR, Native.ColorDefault);
        Native.SetDwmColor(h, Native.DWMWA_BORDER_COLOR, Native.ColorDefault);
        Native.SetDwmColor(h, Native.DWMWA_TEXT_COLOR, Native.ColorDefault);
        st.CaptionSet = false;
    }

    static void RemoveLayer(IntPtr h, WinState st)
    {
        Native.SetLong(h, Native.GWL_EXSTYLE, Native.GetLong(h, Native.GWL_EXSTYLE) & ~Native.WS_EX_LAYERED);
        Native.RemoveProp(h, OurProp);
        Native.RedrawWindow(h, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0400 /* INVALIDATE | ERASE | ALLCHILDREN | FRAME */);
        st.LayeredByUs = false;
        st.Opacity = 100;
    }

    // Вернуть окну всё как было.
    static void Restore(IntPtr h, WinState st)
    {
        if (st.LayeredByUs) RemoveLayer(h, st);
        if (st.AccentSet) { Native.SetAccent(h, Native.AccentDisabled); st.AccentSet = false; }
        if (st.Backdrop != ColorSettings.BdNone && BackdropApi) Native.SetDwm(h, Native.DWMWA_SYSTEMBACKDROP_TYPE, 0);
        if (st.Extended) { Native.Margins(h, 0); st.Extended = false; }
        if (Win11)
        {
            if (st.DarkSet) { Native.SetDwm(h, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, Math.Max(0, st.OrigDark)); st.DarkSet = false; }
            if (st.CaptionSet) ResetCaption(h, st);
        }
        st.Backdrop = ColorSettings.BdNone;
    }

    // ---------- панель задач ----------
    // Размытие или цвет «за» панелью. В Windows 11 панель рисует свой фон сама (XAML): чтобы размытие было видно,
    // фон должен стать прозрачным — это делает движок Taskbar Plus (см. TaskbarLink).
    void ApplyTaskbar(ColorSettings s)
    {
        var on = s.TaskbarOn && s.TaskbarMode != ColorSettings.TbDefault;
        if (on)
        {
            var windows = TaskbarWindows();
            foreach (var h in windows)
            {
                var ok = s.TaskbarMode switch
                {
                    ColorSettings.TbBlur => Native.SetAccent(h, Native.AccentAcrylic, Native.Abgr(s.TaskbarColor, s.TaskbarOpacity)),
                    ColorSettings.TbSolid => Native.SetAccent(h, Native.AccentGradient, Native.Abgr(s.TaskbarColor, Math.Max(1, s.TaskbarOpacity))),
                    _ => Native.SetAccent(h, Native.AccentTransparentGradient, 0),
                };
                if (ok) taskbarTouched.Add(h);
            }
            taskbarWasOn = true;
        }
        else if (taskbarWasOn)
        {
            ResetTaskbar();
            taskbarWasOn = false;
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

    void ResetTaskbar()
    {
        foreach (var h in taskbarTouched.ToList()) { try { if (Native.IsWindow(h)) Native.SetAccent(h, Native.AccentDisabled); } catch { } }
        taskbarTouched.Clear();
        // Проводник заново выставит собственный вид панели
        Task.Run(() => { try { Native.SendMessageTimeout((IntPtr)0xFFFF, 0x1A, UIntPtr.Zero, "ImmersiveColorSet", 2, 1500, out _); } catch { } });
    }

    // Если плагин когда-то закрылся аварийно (например, убит в диспетчере задач), окна остались слоёными по нашей метке — возвращаем их.
    static void RecoverLeftovers()
    {
        Native.EnumWindows((h, _) =>
        {
            try
            {
                if (Native.GetProp(h, OurProp) == IntPtr.Zero) return true;
                if (Native.IsHungAppWindow(h)) return true;
                Native.SetLong(h, Native.GWL_EXSTYLE, Native.GetLong(h, Native.GWL_EXSTYLE) & ~Native.WS_EX_LAYERED);
                Native.RemoveProp(h, OurProp);
                Native.RedrawWindow(h, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0400);
            }
            catch { }
            return true;
        }, IntPtr.Zero);
    }

    // Вернуть всё, что трогали: окна и панель.
    void RestoreEverything()
    {
        RecoverLeftovers();
        Glass?.SetTargets(Array.Empty<IntPtr>(), new GlassConfig("#101018", 45, true));
        foreach (var kv in states.ToList())
        {
            if (Native.IsWindow(kv.Key)) { try { Restore(kv.Key, kv.Value); } catch { } }
        }
        states.Clear();
        if (taskbarWasOn || taskbarTouched.Count > 0) { ResetTaskbar(); taskbarWasOn = false; }
    }

    public void Dispose()
    {
        Stop();
        try { RestoreEverything(); } catch { }
    }
}
