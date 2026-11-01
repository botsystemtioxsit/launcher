using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text.Json;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace WallpaperPlusApp;

// Данные компьютера для обоев «Терминал»: процессор и память, звук, что играет, курсор, журнал событий, погода.
// Страница получает только то, что перечислено в её манифесте ("Telemetry"), и только пока обои видны. Ничего не записывается на диск и не отправляется
// (кроме запроса погоды к Open-Meteo по названию города). Страница сама ничего не может запросить: данные только приходят к ней сообщениями.
sealed class TelemetryHub
{
    public static readonly TelemetryHub I = new();

    sealed class Sub
    {
        public CoreWebView2 Core = null!;
        public HashSet<string> Kinds = new();
        public Func<bool> Paused = () => false;
        public long JournalCursor;
    }
    sealed class JEntry { public long Id; public long T; public string Lv = "INFO", Src = "", Msg = ""; }

    readonly List<Sub> subs = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    readonly AudioTap audio = new();
    readonly MediaInfo media = new();
    readonly List<JEntry> journal = new();
    long journalId;
    WpSettings cfg = new();
    long tick;
    bool running;

    // погода
    object? weatherMsg;
    string weatherKey = "";
    bool weatherBusy;

    // процессор, сеть, ввод
    ulong lastIdle, lastKernel, lastUser;
    double cpu, ram;
    long lastNetBytesDown, lastNetBytesUp, lastNetTick;
    double netDown, netUp;
    int hotCpu;
    long cpuCooldown, ramCooldown;
    string lastFocus = "";
    Dictionary<uint, string> lastWindows = new();
    bool windowsPrimed;
    string lastTrack = "";
    int lastVolPct = -1; long volSince;
    int px = -1, py = -1; bool wasDown; long evUntil; string ev = "IDLE";

    TelemetryHub() { timer.Tick += (_, _) => { try { Tick(); } catch (Exception ex) { Paths.Log("данные терминала: " + ex.Message); } }; }

    // ---------- подписки ----------
    public void Attach(CoreWebView2 core, IEnumerable<string> kinds, Func<bool>? paused = null)
    {
        var set = new HashSet<string>(kinds.Where(k => k is "sys" or "audio" or "media" or "input" or "journal" or "weather"));
        if (set.Count == 0) return;
        Detach(core);
        subs.Add(new Sub { Core = core, Kinds = set, Paused = paused ?? (() => false), JournalCursor = Math.Max(0, journalId - 12) });
        if (!running) Start();
    }

    public void Detach(CoreWebView2? core)
    {
        if (core == null) return;
        subs.RemoveAll(s => ReferenceEquals(s.Core, core));
        if (subs.Count == 0 && running) Stop();
    }

    // Страница загрузилась: ей заново отдаются свежие события и погода (то, что отправилось до загрузки, она не видела)
    public void Resync(CoreWebView2 core)
    {
        var s = subs.FirstOrDefault(x => ReferenceEquals(x.Core, core));
        if (s == null) return;
        s.JournalCursor = Math.Max(0, journalId - 12);
        if (weatherMsg != null && s.Kinds.Contains("weather")) { try { core.PostWebMessageAsJson(Json("weather", weatherMsg)); } catch { } }
    }

    public void SetConfig(WpSettings s)
    {
        cfg = s.Clone();
        if (running) RefreshWeather(false);
    }

    void Start()
    {
        running = true;
        tick = 0;
        NetworkChange.NetworkAvailabilityChanged += OnNet;
        try { SystemEvents.SessionSwitch += OnSession; } catch { }
        AddJournal("INFO", "TERM", "terminal online");
        RefreshWeather(true);
        timer.Start();
    }

    void Stop()
    {
        running = false;
        timer.Stop();
        NetworkChange.NetworkAvailabilityChanged -= OnNet;
        try { SystemEvents.SessionSwitch -= OnSession; } catch { }
        audio.Dispose();
        windowsPrimed = false; lastWindows.Clear(); lastFocus = ""; lastTrack = ""; lastVolPct = -1;
    }

    bool Wants(string kind)
    {
        if (kind is "audio" or "media" && !cfg.TelAudio) return false;
        if (kind == "journal" && !cfg.TelJournal) return false;
        return subs.Any(s => s.Kinds.Contains(kind) && !s.Paused());
    }

    // ---------- журнал ----------
    void AddJournal(string lv, string src, string msg)
    {
        journal.Add(new JEntry { Id = ++journalId, T = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Lv = lv, Src = src, Msg = msg.Length > 60 ? msg[..60] : msg });
        if (journal.Count > 200) journal.RemoveRange(0, journal.Count - 200);
    }
    void OnNet(object? s, NetworkAvailabilityEventArgs e) => AddJournal(e.IsAvailable ? "INFO" : "ERROR", "NET", e.IsAvailable ? "online" : "offline");
    void OnSession(object? s, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock) AddJournal("INFO", "SESSION", "locked");
        else if (e.Reason == SessionSwitchReason.SessionUnlock) AddJournal("INFO", "SESSION", "unlocked");
    }

    // ---------- отправка ----------
    static string Json(string kind, object data) => JsonSerializer.Serialize(new { gs = 1, k = kind, d = data });

    void Send(string kind, object data)
    {
        if (!Wants(kind)) return;
        var json = Json(kind, data);
        foreach (var s in subs.ToArray())
        {
            if (!s.Kinds.Contains(kind) || s.Paused()) continue;
            try { s.Core.PostWebMessageAsJson(json); }
            catch { subs.Remove(s); }
        }
        if (subs.Count == 0 && running) Stop();
    }

    void SendJournal()
    {
        if (!cfg.TelJournal) return;
        foreach (var s in subs.ToArray())
        {
            if (!s.Kinds.Contains("journal") || s.Paused()) continue;
            var fresh = journal.Where(j => j.Id > s.JournalCursor).ToList();
            if (fresh.Count == 0) continue;
            s.JournalCursor = fresh[^1].Id;
            var items = fresh.Select(j => new { id = j.Id, t = j.T, lv = j.Lv, src = j.Src, msg = j.Msg }).ToList();
            try { s.Core.PostWebMessageAsJson(Json("journal", new { items })); }
            catch { subs.Remove(s); }
        }
        if (subs.Count == 0 && running) Stop();
    }

    // ---------- такт ----------
    void Tick()
    {
        if (subs.Count == 0) return;
        tick++;
        var now = Environment.TickCount64;
        if (Wants("audio")) Send("audio", audio.Snapshot());
        if (tick % 2 == 0 && Wants("input")) Send("input", ReadInput(now));
        if (tick % 25 == 0)
        {
            var needSys = Wants("sys") || Wants("journal");
            if (needSys) { var sys = ReadSys(now); Send("sys", sys); WatchLoad(now); }
            if (cfg.TelAudio && (Wants("media") || Wants("journal"))) { _ = media.PollAsync(); }
            if (Wants("media")) Send("media", media.Message());
            if (cfg.TelAudio) { WatchMedia(); WatchVolume(now); }
        }
        if (tick % 12 == 0 && cfg.TelJournal && Wants("journal")) WatchFocus();
        if (tick % 75 == 0 && cfg.TelJournal && Wants("journal")) WatchWindows();
        if (tick % 6 == 0) SendJournal();
        if (tick % 750 == 0 && weatherMsg != null) Send("weather", weatherMsg);   // каждые 30 с повторяем последнее значение
        if (tick % 15000 == 0) RefreshWeather(true);                             // раз в 10 минут обновляем
    }

    // ---------- погода ----------
    void RefreshWeather(bool force)
    {
        var key = cfg.WeatherName + "|" + cfg.WeatherLat + "|" + cfg.WeatherLon;
        if (weatherBusy || (!force && key == weatherKey)) return;
        weatherKey = key;
        if (cfg.WeatherName.Length == 0)
        {
            weatherMsg = new { ok = false, city = "", temp = 0.0, code = 0, prec = 0.0, wind = 0.0, sunrise = 0L, sunset = 0L, err = "укажите город в настройках" };
            Send("weather", weatherMsg);
            return;
        }
        weatherBusy = true;
        var lat = cfg.WeatherLat; var lon = cfg.WeatherLon; var name = cfg.WeatherName;
        _ = Task.Run(async () =>
        {
            var msg = await Weather.FetchAsync(lat, lon, name);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { weatherMsg = msg; weatherBusy = false; Send("weather", msg); });
        });
    }

    // ---------- процессор, память, сеть ----------
    static ulong U(FILETIME f) => ((ulong)(uint)f.dwHighDateTime << 32) | (uint)f.dwLowDateTime;

    object ReadSys(long now)
    {
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var i = U(idle); var k = U(kernel); var u = U(user);
            if (lastKernel != 0)
            {
                var dIdle = i - lastIdle; var dTotal = (k - lastKernel) + (u - lastUser);
                if (dTotal > 0) cpu = Math.Clamp((1.0 - (double)dIdle / dTotal) * 100, 0, 100);
            }
            lastIdle = i; lastKernel = k; lastUser = u;
        }
        var ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        double usedMb = 0, totalMb = 0;
        if (GlobalMemoryStatusEx(ref ms)) { ram = ms.dwMemoryLoad; totalMb = ms.ullTotalPhys / 1048576.0; usedMb = totalMb - ms.ullAvailPhys / 1048576.0; }
        long down = 0, up = 0;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var st = nic.GetIPv4Statistics(); down += st.BytesReceived; up += st.BytesSent;
            }
        }
        catch { }
        if (lastNetTick != 0 && now > lastNetTick)
        {
            var dt = (now - lastNetTick) / 1000.0;
            netDown = Math.Max(0, (down - lastNetBytesDown) / 1024.0 / dt); netUp = Math.Max(0, (up - lastNetBytesUp) / 1024.0 / dt);
        }
        lastNetBytesDown = down; lastNetBytesUp = up; lastNetTick = now;
        return new { cpu = Math.Round(cpu, 1), ram = Math.Round(ram, 1), ramUsedMB = Math.Round(usedMb), ramTotalMB = Math.Round(totalMb), netDown = Math.Round(netDown, 1), netUp = Math.Round(netUp, 1), uptime = Environment.TickCount64 / 1000 };
    }

    void WatchLoad(long now)
    {
        if (!cfg.TelJournal) return;
        hotCpu = cpu > 90 ? hotCpu + 1 : 0;
        if (hotCpu >= 3 && now > cpuCooldown) { AddJournal("WARN", "CPU", "load " + (int)cpu + "%"); cpuCooldown = now + 60000; }
        if (ram > 92 && now > ramCooldown) { AddJournal("WARN", "MEM", "usage " + (int)ram + "%"); ramCooldown = now + 120000; }
    }

    void WatchMedia()
    {
        var m = media.Last;
        var key = m.Has ? m.Artist + " — " + m.Title : "";
        if (key != lastTrack)
        {
            lastTrack = key;
            if (key.Length > 0 && m.Playing) AddJournal("INFO", "PLAY", key);
        }
    }

    void WatchVolume(long now)
    {
        if (!cfg.TelAudio || !Wants("audio")) return;
        // громкость читает AudioTap раз в 250 мс; здесь только фиксируем изменение (не чаще раза в 2 с)
        var v = (int)Math.Round(audio.Vol * 100);
        if (lastVolPct >= 0 && v != lastVolPct && now - volSince > 2000) { AddJournal("INFO", "VOL", v + "%"); volSince = now; }
        lastVolPct = v;
    }

    // ---------- курсор ----------
    object ReadInput(long now)
    {
        GetCursorPos(out var p);
        var mon = MonitorFromPoint(p, 2);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        int w = 0, h = 0, x = p.X, y = p.Y;
        if (mon != IntPtr.Zero && GetMonitorInfo(mon, ref mi)) { w = mi.rcMonitor.Right - mi.rcMonitor.Left; h = mi.rcMonitor.Bottom - mi.rcMonitor.Top; x = p.X - mi.rcMonitor.Left; y = p.Y - mi.rcMonitor.Top; }
        var down = (GetAsyncKeyState(1) & 0x8000) != 0 || (GetAsyncKeyState(2) & 0x8000) != 0 || (GetAsyncKeyState(4) & 0x8000) != 0;
        if (down && !wasDown) { ev = "DOWN"; evUntil = now + 450; }
        else if (!down && wasDown) { ev = "UP"; evUntil = now + 450; }
        else if ((p.X != px || p.Y != py) && px >= 0 && now > evUntil) { ev = "MOVE"; evUntil = now + 350; }
        if (now > evUntil) ev = "IDLE";
        wasDown = down; px = p.X; py = p.Y;
        return new { x, y, w, h, ev, btn = down ? "1" : "" };
    }

    // ---------- события: окна и программы ----------
    static readonly HashSet<string> IgnoreProcs = new(StringComparer.OrdinalIgnoreCase)
        { "ApplicationFrameHost", "TextInputHost", "SearchHost", "ShellExperienceHost", "StartMenuExperienceHost", "LockApp", "WallpaperPlus", "dwm", "RuntimeBroker" };

    static string? ProcName(uint pid)
    {
        try { return Process.GetProcessById((int)pid).ProcessName; } catch { return null; }
    }

    void WatchFocus()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;
        GetWindowThreadProcessId(hwnd, out var pid);
        var name = ProcName(pid);
        if (string.IsNullOrEmpty(name) || IgnoreProcs.Contains(name) || name == lastFocus) return;
        lastFocus = name;
        AddJournal("INFO", "FOCUS", name);
    }

    void WatchWindows()
    {
        var now = new Dictionary<uint, string>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || GetWindowTextLength(h) == 0) return true;
            if ((GetWindowLong(h, -20) & 0x80) != 0) return true;                 // служебные окна-палитры
            if (DwmGetWindowAttribute(h, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return true;   // «призраки» других рабочих столов
            GetWindowThreadProcessId(h, out var pid);
            if (pid == 0 || now.ContainsKey(pid)) return true;
            var name = ProcName(pid);
            if (!string.IsNullOrEmpty(name) && !IgnoreProcs.Contains(name)) now[pid] = name;
            return true;
        }, IntPtr.Zero);
        if (windowsPrimed)
        {
            var n = 0;
            foreach (var kv in now) if (!lastWindows.ContainsKey(kv.Key) && n++ < 4) AddJournal("INFO", "START", kv.Value);
            foreach (var kv in lastWindows) if (!now.ContainsKey(kv.Key) && !now.ContainsValue(kv.Value) && n++ < 8) AddJournal("INFO", "EXIT", kv.Value);
        }
        lastWindows = now; windowsPrimed = true;
    }

    // ---------- Windows ----------
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [StructLayout(LayoutKind.Sequential)] struct MEMORYSTATUSEX { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
}
