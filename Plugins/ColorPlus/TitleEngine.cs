using System.Diagnostics;

namespace ColorPlusApp;

// Красит заголовки и рамки обычных окон Windows 11 (DwmSetWindowAttribute: цвет заголовка, текста и рамки). Работает, пока Color Plus запущен в фоне —
// так красятся и новые окна. Не трогает: игры и окна, рисуемые видеокартой (GpuDetect), Steam, Wallpaper Engine, Lively, Rainmeter, OBS, окна без
// своей строки заголовка и собственное окно. Каждое тронутое окно помечается (SetProp): если плагин убит, при следующем запуске метки снимаются.
sealed class TitleEngine
{
    const string Prop = "GhostShell.ColorPlus.Title";
    static readonly bool Win11 = Environment.OSVersion.Version.Build >= 22000;
    static readonly HashSet<string> Hard = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam", "steamwebhelper", "wallpaper32", "wallpaper64", "webwallpaper32", "webwallpaper64", "ui32", "ui64", "lively", "livelywpf", "rainmeter", "obs64", "obs32", "obs", "colorplus",
    };

    readonly object gate = new();
    readonly Dictionary<IntPtr, string> done = new();
    readonly Dictionary<uint, (DateTime At, string Name)> names = new();
    readonly System.Threading.Timer timer;
    string color = "#FF2A9D";
    bool on;
    readonly uint selfPid = (uint)Environment.ProcessId;

    public TitleEngine() { timer = new System.Threading.Timer(_ => Tick(), null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite); }

    public bool Supported => Win11;
    public bool Running { get { lock (gate) return on; } }

    public void Set(string hex)
    {
        if (!Win11) return;
        lock (gate) { color = hex; on = true; }
        timer.Change(0, 600);
    }

    // Остановить и вернуть всем окнам цвета Windows
    public void Stop()
    {
        lock (gate) on = false;
        timer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        lock (done)
        {
            foreach (var h in done.Keys.ToList()) Reset(h);
            done.Clear();
        }
    }

    // Следы аварийно закрытого прошлого запуска: окна с нашей меткой возвращаются к цветам Windows
    public static void RecoverLeftovers()
    {
        if (!Win11) return;
        Native.EnumWindows((h, _) =>
        {
            try { if (Native.GetProp(h, Prop) != IntPtr.Zero && !Native.IsHungAppWindow(h)) Reset(h); } catch { }
            return true;
        }, IntPtr.Zero);
    }

    static void Reset(IntPtr h)
    {
        try
        {
            if (!Native.IsWindow(h)) return;
            Native.SetDwmColor(h, Native.DWMWA_CAPTION_COLOR, Native.ColorDefault);
            Native.SetDwmColor(h, Native.DWMWA_TEXT_COLOR, Native.ColorDefault);
            Native.SetDwmColor(h, Native.DWMWA_BORDER_COLOR, Native.ColorDefault);
            Native.RemoveProp(h, Prop);
        }
        catch { }
    }

    static uint ColorRef(string hex) { var (r, g, b) = Col.Parse(hex); return (uint)((b << 16) | (g << 8) | r); }

    string ProcName(uint pid)
    {
        if (names.TryGetValue(pid, out var c) && DateTime.UtcNow - c.At < TimeSpan.FromSeconds(30)) return c.Name;
        string n;
        try { using var p = Process.GetProcessById((int)pid); n = p.ProcessName; } catch { n = ""; }
        names[pid] = (DateTime.UtcNow, n);
        if (names.Count > 300) foreach (var k in names.Where(kv => DateTime.UtcNow - kv.Value.At > TimeSpan.FromMinutes(2)).Select(kv => kv.Key).ToList()) names.Remove(k);
        return n;
    }

    void Tick()
    {
        try
        {
            string hex;
            lock (gate) { if (!on) return; hex = color; }
            var seen = new HashSet<IntPtr>();
            lock (done)
            {
                Native.EnumWindows((h, _) => { try { Consider(h, hex, seen); } catch { } return true; }, IntPtr.Zero);
                // окна, которые закрылись или перестали подходить, забываем (подходящие уже перекрашены заново при смене цвета)
                foreach (var h in done.Keys.Where(k => !seen.Contains(k)).ToList()) { Reset(h); done.Remove(h); }
            }
        }
        catch { }
    }

    void Consider(IntPtr h, string hex, HashSet<IntPtr> seen)
    {
        if (!Native.IsWindowVisible(h)) return;
        if ((Native.GetLong(h, -16) & 0xC00000) != 0xC00000) return;                 // нет строки заголовка (WS_CAPTION)
        if ((Native.GetLong(h, -20) & 0x80) != 0) return;                            // окно-палитра (WS_EX_TOOLWINDOW)
        if (Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return;
        if (Native.IsHungAppWindow(h)) return;
        Native.GetWindowThreadProcessId(h, out var pid);
        if (pid == 0 || pid == selfPid) return;
        var proc = ProcName(pid);
        if (proc.Length == 0 || Hard.Contains(proc)) return;
        if (GpuDetect.Why(Native.ClassName(h), pid, proc) != null) return;          // игры и 3D — никогда
        seen.Add(h);
        if (done.TryGetValue(h, out var c) && c == hex) return;
        Native.SetDwmColor(h, Native.DWMWA_CAPTION_COLOR, ColorRef(hex));
        Native.SetDwmColor(h, Native.DWMWA_TEXT_COLOR, ColorRef(Col.Readable(hex)));
        Native.SetDwmColor(h, Native.DWMWA_BORDER_COLOR, ColorRef(hex));
        Native.SetProp(h, Prop, (IntPtr)1);
        done[h] = hex;
    }
}
