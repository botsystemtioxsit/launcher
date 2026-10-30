using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TaskbarPlusApp;

// Новый движок для Windows 11: маленькая DLL (папка Engine, C++) подключается к панели задач внутри Проводника
// через публичный интерфейс XAML Diagnostics и меняет настоящие элементы панели — отсюда гладкие края, прозрачность, размытие.
// Здесь, в плагине, — только «хозяин»: достаёт DLL, подключает её к Проводнику, пишет ей настройки и читает её статус.
// Если Проводник падает сразу после подключения (два раза подряд), движок сам отключается, остаётся обычный способ.
sealed class XamlEngine : IDisposable
{
    const string ResourceName = "GhostShellTap.dll";
    const string EngineVersion = "5";   // должна совпадать с GS_ENGINE_VERSION в Engine/common.h

    static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BattleLauncher");
    static readonly string CfgPath = Path.Combine(DataDir, "taskbar-plus-engine.cfg");
    static readonly string StatusPath = Path.Combine(DataDir, "taskbar-plus-engine-status.txt");
    static readonly string LogPath = Path.Combine(DataDir, "taskbar-plus-engine.log");
    static readonly string TreePath = Path.Combine(DataDir, "taskbar-plus-xaml-tree.txt");
    static readonly string HealthPath = Path.Combine(DataDir, "taskbar-plus-engine-health.json");
    static readonly string IslandsPath = Path.Combine(DataDir, "taskbar-plus-engine-islands.txt");
    static readonly string RulesPath = Path.Combine(DataDir, "taskbar-plus-engine-rules.txt");
    const string RulesUrl = "https://raw.githubusercontent.com/botsystemtioxsit/launcher/main/Plugins/TaskbarPlus/engine-rules.txt";
    const string ReportUrl = "https://zolotaya-kletka-default-rtdb.firebaseio.com/pluginReports/taskbar-plus/";
    static readonly string EngineDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GhostShell", "TaskbarPlus", "engine");

    class Health { public int Crashes { get; set; } public bool Blocked { get; set; } }

    readonly object gate = new();
    AppearanceSettings settings = new();
    CancellationTokenSource? cts;
    Task? loop;
    int dumpCounter = Environment.TickCount & 0xFFFFFF;
    string status = "";
    bool ready;
    Health health = LoadHealth();

    IntPtr hookModule, hook;
    uint lastPid;
    uint injectedPid;
    long injectAt;
    int attempts;
    long connectedSince;
    long expectedRestartUntil;
    bool crashCounted;
    bool autoReported;

    string[] rules = LoadCachedRules();

    public event Action? Changed;

    // ---------- что умеет эта машина ----------
    public static bool OsSupported => Environment.OSVersion.Version.Build >= 22000 && RuntimeInformation.OSArchitecture == Architecture.X64 && Environment.Is64BitProcess;
    public static bool DllIncluded => Assembly.GetExecutingAssembly().GetManifestResourceInfo(ResourceName) != null;
    public static string? UnavailableReason =>
        !OsSupported ? "Новый движок работает только в Windows 11 (64-бит). Здесь действует обычный способ."
        : !DllIncluded ? "В этой сборке плагина нет файла движка. Действует обычный способ."
        : null;

    public bool Ready { get { lock (gate) return ready; } }
    public bool NeedsRestart { get { lock (gate) return needsRestart; } }
    bool needsRestart;
    public string Status { get { lock (gate) return status; } }
    public bool Blocked { get { lock (gate) return health.Blocked; } }

    void SetState(string text, bool isReady)
    {
        bool fire;
        lock (gate) { fire = status != text || ready != isReady; status = text; ready = isReady; }
        if (fire) Changed?.Invoke();
    }

    static Health LoadHealth()
    {
        try { return JsonSerializer.Deserialize<Health>(File.ReadAllText(HealthPath)) ?? new Health(); } catch { return new Health(); }
    }

    void SaveHealth()
    {
        try { Directory.CreateDirectory(DataDir); File.WriteAllText(HealthPath, JsonSerializer.Serialize(health)); } catch { }
    }

    // Пользователь сам снимает блокировку («Попробовать снова»).
    public void ClearBlock()
    {
        lock (gate) { health = new Health(); }
        SaveHealth();
    }

    // Плагин сам перезапускает Проводник — это не падение, считать его не надо.
    public void NoteExplorerRestart() => Interlocked.Exchange(ref expectedRestartUntil, Environment.TickCount64 + 40_000);

    // ---------- управление ----------
    public void Update(AppearanceSettings s)
    {
        lock (gate) settings = s.Clone();
        var want = s.UseXaml && UnavailableReason == null;
        if (want && !Blocked)
        {
            WriteCfg(true);
            Start();
        }
        else
        {
            var wasRunning = loop != null;
            Stop();
            if (wasRunning || File.Exists(CfgPath)) WriteCfg(false);
            SetState(s.UseXaml ? (UnavailableReason ?? BlockedText) : "", false);
        }
    }

    const string BlockedText = "Новый движок отключён: Проводник два раза подряд закрывался сразу после подключения. Действует обычный способ. Нажмите «Попробовать снова» или сохраните отчёт и отправьте его.";

    void Start()
    {
        lock (gate)
        {
            if (cts != null) return;
            cts = new CancellationTokenSource();
            var token = cts.Token;
            loop = Task.Run(() => Loop(token));
        }
        SetState("Подключаюсь к панели задач…", false);
        _ = RefreshRulesAsync();
    }

    void Stop()
    {
        Task? t;
        lock (gate) { cts?.Cancel(); cts = null; t = loop; loop = null; }
        try { t?.Wait(1000); } catch { }
        Unhook();
    }

    void Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { Tick(); }
            catch (Exception ex) { SetState("Движок: ошибка — " + ex.Message, false); }
            ct.WaitHandle.WaitOne(1000);
        }
    }

    // ---------- один шаг наблюдения ----------
    void Tick()
    {
        var hwnd = Native.FindWindow("Shell_TrayWnd", null);
        if (hwnd == IntPtr.Zero) { SetState("Панель задач не найдена (Проводник перезапускается?)", false); return; }
        GetWindowThreadProcessId(hwnd, out var pid);

        if (lastPid != 0 && pid != lastPid) OnExplorerChanged(pid);
        lastPid = pid;
        if (Blocked) { SetState(BlockedText, false); return; }

        if (ColorPlusLink.Refresh()) WriteCfg(true);   // Color Plus поменял фон панели

        var now = Environment.TickCount64;
        var st = ReadStatus();
        if (st != null && st.GetValueOrDefault("pid") == pid.ToString())
        {
            var state = st.GetValueOrDefault("state") ?? "";
            long.TryParse(st.GetValueOrDefault("tick"), out var tick);
            var fresh = now - tick < 12_000;
            int.TryParse(st.GetValueOrDefault("bgfill"), out var bg);
            int.TryParse(st.GetValueOrDefault("apps"), out var apps);
            int.TryParse(st.GetValueOrDefault("nodes"), out var nodes);
            // Старая версия движка остаётся в Проводнике до его перезапуска; новая рядом с ней не запускается.
            if (state == "duplicate" || (state == "ready" && st.GetValueOrDefault("engine") != EngineVersion))
            {
                lock (gate) needsRestart = true;
                SetState("Обновился движок. Чтобы подключить новую версию, нужно перезапустить панель задач (кнопка внизу окна).", false);
                return;
            }
            lock (gate) needsRestart = false;
            if (state == "ready" && fresh)
            {
                if (connectedSince == 0) connectedSince = now;
                Unhook();
                if (bg > 0)
                {
                    SetState($"Новый движок работает. Элементов панели: {nodes}, значков: {apps}.", true);
                    if (now - connectedSince > 90_000 && health.Crashes != 0) { health.Crashes = 0; SaveHealth(); }
                    // Один раз за запуск, когда панель уже обжилась, отчёт уходит разработчику сам (владелец не нажимает кнопки ради отчётов).
                    if (!autoReported && now - connectedSince > 45_000)
                    {
                        bool send;
                        lock (gate) send = settings.AutoReport;
                        autoReported = true;
                        if (send) _ = UploadReportAsync();
                    }
                }
                else if (now - connectedSince > 10_000)
                {
                    int.TryParse(st.GetValueOrDefault("system"), out var sysCount);
                    int.TryParse(st.GetValueOrDefault("widgets"), out var wid);
                    int.TryParse(st.GetValueOrDefault("sessions"), out var sessions);
                    SetState($"Движок подключился (окон XAML: {sessions}, элементов {nodes}, значков {apps}, системная область {sysCount}, виджеты {wid}), но фон панели не нашёл. " +
                             "Панель остаётся как в Windows; отчёт уйдёт разработчику сам.", false);
                }
                else
                    SetState("Подключился, ищу элементы панели…", false);
                return;
            }
            if (state == "error")
            {
                SetState("Движок: " + (st.GetValueOrDefault("error") ?? "ошибка") + ". Действует обычный способ.", false);
                if (attempts >= 3) return;
            }
            else if (state == "connecting" && fresh && now - injectAt < 60_000)
            {
                SetState("Подключаюсь к панели задач…", false);
                return;
            }
        }

        // движка в этом Проводнике нет (или он не отвечает) — подключаем
        if (injectedPid == pid && attempts >= 3 && now - injectAt < 120_000) return;
        if (injectedPid != pid) attempts = 0;
        Inject(hwnd, pid);
    }

    void OnExplorerChanged(uint newPid)
    {
        var now = Environment.TickCount64;
        var expected = now < Interlocked.Read(ref expectedRestartUntil);
        var cameSoonAfterInject = injectedPid == lastPid && injectAt != 0 && now - injectAt < 90_000;
        if (!expected && cameSoonAfterInject && !crashCounted)
        {
            health.Crashes++;
            if (health.Crashes >= 2) health.Blocked = true;
            SaveHealth();
        }
        crashCounted = false;
        connectedSince = 0;
        attempts = 0;
        Unhook();
        SetState("Проводник перезапустился, подключаюсь заново…", false);
    }

    // ---------- подключение DLL к Проводнику ----------
    void Inject(IntPtr hwnd, uint pid)
    {
        var dll = ExtractDll();
        if (dll == null) { SetState(UnavailableReason ?? "Не удалось достать файл движка", false); return; }
        WriteCfg(true); // к моменту подключения настройки уже лежат на месте
        var tid = GetWindowThreadProcessId(hwnd, out _);
        if (hookModule == IntPtr.Zero) hookModule = LoadLibraryW(dll);
        if (hookModule == IntPtr.Zero) { SetState("Не удалось загрузить файл движка (ошибка " + Marshal.GetLastWin32Error() + ")", false); return; }
        var proc = GetProcAddress(hookModule, "GhostShellHookProc");
        if (proc == IntPtr.Zero) { SetState("В файле движка нет нужной точки входа", false); return; }
        Unhook();
        hook = SetWindowsHookExW(4 /* WH_CALLWNDPROC */, proc, hookModule, tid);
        if (hook == IntPtr.Zero) { SetState("Не удалось подключиться к панели задач (ошибка " + Marshal.GetLastWin32Error() + ")", false); return; }
        SendMessageTimeoutW(hwnd, 0 /* WM_NULL: любое сообщение окну панели запускает хук */, IntPtr.Zero, IntPtr.Zero, 2 /* SMTO_ABORTIFHUNG */, 2000, out _);
        injectedPid = pid;
        injectAt = Environment.TickCount64;
        attempts++;
        SetState("Подключаюсь к панели задач…", false);
    }

    void Unhook()
    {
        if (hook != IntPtr.Zero) { try { UnhookWindowsHookEx(hook); } catch { } hook = IntPtr.Zero; }
    }

    // DLL вложена в EXE плагина; достаём её в папку с именем по содержимому — после обновления плагина появится новый файл,
    // а старый, пока он нужен Проводнику, никто не трогает.
    static string? ExtractDll()
    {
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (s == null) return null;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            var bytes = ms.ToArray();
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).Substring(0, 12).ToLowerInvariant();
            Directory.CreateDirectory(EngineDir);
            var path = Path.Combine(EngineDir, $"GhostShellTap-{hash}.dll");
            if (!File.Exists(path))
            {
                var tmp = path + ".tmp";
                File.WriteAllBytes(tmp, bytes);
                File.Move(tmp, path, true);
                foreach (var old in Directory.GetFiles(EngineDir, "GhostShellTap-*.dll"))
                    if (!string.Equals(old, path, StringComparison.OrdinalIgnoreCase)) { try { File.Delete(old); } catch { /* ещё нужен Проводнику */ } }
            }
            return path;
        }
        catch { return null; }
    }

    // ---------- правила: какой элемент панели к какой группе относится ----------
    // Встроенные правила лежат в самом движке. Файл engine-rules.txt в репозитории позволяет подправить названия элементов
    // для другой сборки Windows без нового выпуска плагина: плагин скачивает его при запуске и кладёт в настройки движка.
    static string[] ParseRules(string text) => text.Split('\n').Select(l => l.Trim())
        .Where(l => l.Length > 0 && !l.StartsWith('#') && l.Split('|').Length >= 3 && l.Split('|')[0] is "bgfill" or "bgstroke" or "app" or "widgets" or "system" or "pill").Take(100).ToArray();

    static string[] LoadCachedRules()
    {
        try { return ParseRules(File.ReadAllText(RulesPath)); } catch { return Array.Empty<string>(); }
    }

    async Task RefreshRulesAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            var text = await http.GetStringAsync(RulesUrl + "?_=" + DateTime.UtcNow.Ticks);
            var parsed = ParseRules(text);
            if (parsed.Length == 0 && text.Trim().Length > 0 && !text.TrimStart().StartsWith('#')) return; // не похоже на файл правил
            bool changed;
            lock (gate) { changed = !rules.SequenceEqual(parsed); rules = parsed; }
            try { Directory.CreateDirectory(DataDir); File.WriteAllText(RulesPath, text); } catch { }
            if (changed && loop != null) WriteCfg(true);
        }
        catch { /* нет сети — остаются прежние правила */ }
    }

    // Список островов, который движок записывает после каждого пересчёта: системная область, виджеты и значки по именам.
    public record IslandInfo(string Kind, string Key, string Name);

    public static List<IslandInfo> ReadIslands()
    {
        var list = new List<IslandInfo>();
        try
        {
            foreach (var line in SafeRead(IslandsPath).Split('\n'))
            {
                var f = line.Trim().Split('|');
                if (f.Length < 3) continue;
                if (f[0] == "widgets") list.Add(new IslandInfo("widgets", "widgets", ""));
                else if (f[0] == "system") list.Add(new IslandInfo("system", "system", ""));
                else if (f[0] == "app" && f[1].StartsWith("app.")) list.Add(new IslandInfo("app", f[1], FromHex(f[2])));
            }
        }
        catch { }
        return list;
    }

    static string FromHex(string hex)
    {
        try
        {
            if (hex.Length == 0 || hex.Length % 2 != 0 || hex.Length > 400) return "";
            return Encoding.UTF8.GetString(Convert.FromHexString(hex));
        }
        catch { return ""; }
    }

    // ---------- файлы обмена с DLL ----------
    static Dictionary<string, string>? ReadStatus()
    {
        try
        {
            var d = new Dictionary<string, string>();
            using var fs = new FileStream(StatusPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var r = new StreamReader(fs, Encoding.UTF8);
            string? line;
            while ((line = r.ReadLine()) != null)
            {
                var i = line.IndexOf('=');
                if (i > 0) d[line[..i]] = line[(i + 1)..];
            }
            return d;
        }
        catch { return null; }
    }

    static int ZoneNum(string z) => z switch { AppearanceSettings.ZoneLeft => 1, AppearanceSettings.ZoneCenter => 2, AppearanceSettings.ZoneRight => 3, _ => 0 };

    static int Mode(string bg) => bg switch { AppearanceSettings.BgSolid => 1, AppearanceSettings.BgClear => 2, AppearanceSettings.BgAcrylic => 3, _ => 0 };

    void WriteCfg(bool enabled)
    {
        AppearanceSettings s;
        lock (gate) s = settings;
        var sb = new StringBuilder();
        void Put(string k, object v) => sb.Append(k).Append('=').Append(v).Append('\n');
        Put("enabled", enabled ? 1 : 0);
        // фон панели задаёт плагин Color Plus (см. ColorPlusLink); без него — родной фон Windows
        var bg = ColorPlusLink.Current;
        Put("bgMode", bg.Active ? Mode(bg.Mode) : 0);
        Put("color", bg.Color.TrimStart('#'));
        Put("opacity", bg.Opacity);
        Put("shape", s.Shape switch { AppearanceSettings.ShapePill => 1, AppearanceSettings.ShapeTab => 2, AppearanceSettings.ShapeIslands => 3, _ => 0 });
        Put("radius", s.Radius);
        Put("side", s.SideMargin);
        Put("gap", s.BottomGap);
        Put("hideStroke", bg.Active && bg.HideStroke ? 1 : 0);
        Put("keepEdges", s.KeepEdges ? 1 : 0);
        Put("unifySystem", 1);
        Put("islandMode", s.IslandMode == "group" ? 1 : 0);
        Put("islandStyle", s.IslandStyle == "tab" ? 1 : 0);
        Put("islandPad", s.IslandPadding);
        Put("islandGap", s.IslandGap);
        Put("dump", dumpCounter);
        Put("zone.widgets", ZoneNum(s.ZoneWidgets));
        Put("zone.system", ZoneNum(s.ZoneSystem));
        Put("zone.apps", ZoneNum(s.ZoneApps));
        Put("order", string.Join(",", s.LayoutOrder));
        Put("edgeMargin", s.EdgeMargin);
        foreach (var kv in s.Islands)
        {
            var st = kv.Value;
            // радиус;цвет;прозрачность;режим;скрыт — пустое поле значит «как у всех»
            var f = new[]
            {
                st.Radius >= 0 ? st.Radius.ToString() : "",
                st.Color != null ? st.Color.TrimStart('#') : "",
                st.Opacity >= 0 ? st.Opacity.ToString() : "",
                st.Mode == null ? "" : Mode(st.Mode).ToString(),
                st.Hidden ? "1" : "0",
            };
            Put("sty." + kv.Key, string.Join(";", f));
        }
        string[] rs;
        lock (gate) rs = rules;
        for (var i = 0; i < rs.Length; i++) Put("rule." + i, rs[i]);
        try
        {
            Directory.CreateDirectory(DataDir);
            var tmp = CfgPath + ".tmp";
            File.WriteAllText(tmp, sb.ToString());
            File.Move(tmp, CfgPath, true);
        }
        catch { /* движок продолжит со старыми настройками */ }
    }

    // ---------- отчёт для разбора ----------
    // Просит движок записать дерево элементов панели и собирает всё в один текст.
    async Task<string> BuildReportAsync()
    {
        dumpCounter++;
        AppearanceSettings s;
        lock (gate) s = settings;
        if (Ready || loop != null) WriteCfg(s.UseXaml);
        await Task.Delay(1500); // движок читает файл настроек примерно раз в 0,3 с

        var sb = new StringBuilder();
        sb.AppendLine("Taskbar Plus — отчёт");
        sb.AppendLine("Время: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("Плагин: " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?"));
        sb.AppendLine("Windows: " + Environment.OSVersion.Version + " " + RuntimeInformation.OSArchitecture);
        sb.AppendLine("Состояние: " + Status);
        sb.AppendLine();
        sb.AppendLine("--- настройки ---");
        try { sb.AppendLine(JsonSerializer.Serialize(s)); } catch { }
        sb.AppendLine();
        sb.AppendLine("--- статус движка ---");
        sb.AppendLine(SafeRead(StatusPath));
        sb.AppendLine("--- здоровье ---");
        sb.AppendLine(SafeRead(HealthPath));
        sb.AppendLine("--- журнал движка (последние строки) ---");
        var logLines = SafeRead(LogPath).Split('\n');
        sb.AppendLine(string.Join("\n", logLines.Skip(Math.Max(0, logLines.Length - 80))));
        sb.AppendLine("--- дерево элементов панели ---");
        sb.AppendLine(SafeRead(TreePath));
        return sb.ToString();
    }

    public async Task<string> SaveReportAsync()
    {
        var text = await BuildReportAsync();
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var path = Path.Combine(desktop, "TaskbarPlus-report.txt");
        File.WriteAllText(path, text, new UTF8Encoding(true));
        return path;
    }

    // Отправка отчёта разработчику: в общую базу проекта (узел pluginReports), откуда его забирает служебный процесс GitHub.
    // В отчёте нет ничего личного: версии, настройки плагина, статус движка и названия элементов панели (имя пользователя вычищается).
    // Возвращает null, если отправилось, иначе — текст ошибки.
    public async Task<string?> UploadReportAsync()
    {
        try
        {
            var text = Sanitize(await BuildReportAsync());
            if (text.Length > 180_000) text = text[..180_000] + "\n…(обрезано)";
            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.MachineName + "|" + Environment.UserName)))[..8].ToLowerInvariant();
            var body = JsonSerializer.Serialize(new
            {
                at = DateTime.UtcNow.ToString("o"),
                plugin = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?",
                windows = Environment.OSVersion.Version.ToString(),
                report = text,
            });
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            using var resp = await http.PutAsync(ReportUrl + id + ".json", new StringContent(body, Encoding.UTF8, "application/json"));
            return resp.IsSuccessStatusCode ? null : "сервер ответил " + (int)resp.StatusCode;
        }
        catch (Exception ex) { return ex.Message; }
    }

    static string Sanitize(string text)
    {
        foreach (var (what, with) in new[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%"),
            (Environment.UserName, "user"),
            (Environment.MachineName, "pc"),
        })
            if (what.Length >= 3) text = text.Replace(what, with, StringComparison.OrdinalIgnoreCase);
        return text;
    }

    static string SafeRead(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var r = new StreamReader(fs, Encoding.UTF8);
            return r.ReadToEnd();
        }
        catch { return "(нет файла)"; }
    }

    // Снимает всё с панели: движок возвращает исходный вид элементов и ждёт, пока это случится.
    public void Dispose()
    {
        try
        {
            bool had = loop != null || Ready;
            Stop();
            if (had || File.Exists(CfgPath))
            {
                WriteCfg(false);
                for (var i = 0; i < 12; i++)
                {
                    Thread.Sleep(150);
                    var st = ReadStatus();
                    if (st == null || st.GetValueOrDefault("applied") == "0") break;
                }
            }
        }
        catch { }
    }

    // ---------- вызовы Windows ----------
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryW(string path);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookExW(int id, IntPtr proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SendMessageTimeoutW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
}
