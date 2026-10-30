using System.Globalization;
using System.IO;

namespace TaskbarPlusApp;

// Обрезка окна панели задач по островам. Окно панели одно на весь экран, и всё, что Windows или другие плагины кладут на окно целиком
// (акцент Windows, размытие Color Plus), видно и между островами — получается сплошная полоса. Здесь окно обрезается (SetWindowRgn) до прямоугольников,
// которые движок сам видит и рисует: острова, середина плавающей панели, значки без острова. Остальное прозрачно, видны обои.
// Безопасность: движок присылает признак safe — «каждый значок панели лежит внутри какого-то прямоугольника». Если это не так, не обрезаем вообще
// (иначе часы, трей или виджеты пропали бы, как в 1.4.2). Края выреза у Windows без сглаживания, поэтому вырез чуть больше островов.
sealed class TaskbarCrop : IDisposable
{
    static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BattleLauncher");

    sealed record RectSet(uint Pid, int PanelW, int PanelH, bool Safe, List<(double L, double T, double W, double H, double R)> Rects);

    readonly object gate = new();
    readonly HashSet<IntPtr> owned = new();
    readonly Dictionary<IntPtr, string> applied = new();
    CancellationTokenSource? cts;
    Task? loop;
    bool wanted;

    // on — обрезать, когда движок готов и у панели есть острова
    public void SetWanted(bool on)
    {
        lock (gate) wanted = on;
        if (on) Start(); else { Stop(); ClearAll(); }
    }

    void Start()
    {
        lock (gate)
        {
            if (cts != null) return;
            cts = new CancellationTokenSource();
            var t = cts.Token;
            loop = Task.Run(() => Loop(t));
        }
    }

    void Stop()
    {
        Task? l;
        lock (gate) { cts?.Cancel(); cts = null; l = loop; loop = null; }
        try { l?.Wait(1500); } catch { }
    }

    void Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { Tick(); } catch { }
            ct.WaitHandle.WaitOne(300);
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

    static List<RectSet> ReadSets()
    {
        var sets = new List<RectSet>();
        try
        {
            foreach (var f in Directory.GetFiles(DataDir, "taskbar-plus-engine-rects-*.txt"))
            {
                try
                {
                    uint pid = 0; int pw = 0, ph = 0; var safe = false; var rects = new List<(double, double, double, double, double)>();
                    using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var r = new StreamReader(fs);
                    string? line;
                    while ((line = r.ReadLine()) != null)
                    {
                        var i = line.IndexOf('=');
                        if (i <= 0) continue;
                        var k = line[..i]; var v = line[(i + 1)..];
                        if (k == "pid") uint.TryParse(v, out pid);
                        else if (k == "safe") safe = v == "1";
                        else if (k == "panel") { var p = v.Split(','); if (p.Length == 2) { int.TryParse(p[0], out pw); int.TryParse(p[1], out ph); } }
                        else if (k == "r")
                        {
                            var p = v.Split(',');
                            if (p.Length == 5 && p.All(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
                            {
                                var d = p.Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                                rects.Add((d[0], d[1], d[2], d[3], d[4]));
                            }
                        }
                    }
                    if (pid != 0 && pw > 0 && ph > 0) sets.Add(new RectSet(pid, pw, ph, safe, rects));
                }
                catch { }
            }
        }
        catch { }
        return sets;
    }

    void Tick()
    {
        var sets = ReadSets();
        foreach (var h in TaskbarWindows())
        {
            if (!Native.GetWindowRect(h, out var rc)) continue;
            Native.GetWindowThreadProcessId(h, out var pid);
            var scale = Native.Scale(h);
            // подходит набор прямоугольников того же Проводника и того же размера окна
            var set = sets.FirstOrDefault(s => s.Pid == pid && Math.Abs(s.PanelW * scale - rc.Width) <= 4 && Math.Abs(s.PanelH * scale - rc.Height) <= 4);
            var hidden = Native.IsHiddenAtBottom(h, rc, scale);
            if (set == null || !set.Safe || set.Rects.Count == 0 || hidden || !Native.IsAtBottom(h, rc))
            {
                if (owned.Contains(h)) Release(h);
                continue;
            }
            var grow = Math.Max(1, (int)Math.Round(2 * scale));
            var key = $"{rc.Width}x{rc.Height}|{scale:F2}|" + string.Join(";", set.Rects.Select(r => $"{r.L:F0},{r.T:F0},{r.W:F0},{r.H:F0},{r.R:F0}"));
            if (applied.TryGetValue(h, out var old) && old == key && Native.GetWindowRgnBox(h, out _) != 0) continue;

            IntPtr all = IntPtr.Zero;
            foreach (var r in set.Rects)
            {
                var x1 = (int)Math.Floor(r.L * scale) - grow; var y1 = (int)Math.Floor(r.T * scale) - grow;
                var x2 = (int)Math.Ceiling((r.L + r.W) * scale) + grow; var y2 = (int)Math.Ceiling((r.T + r.H) * scale) + grow;
                x1 = Math.Max(0, x1); y1 = Math.Max(0, y1); x2 = Math.Min(rc.Width, x2); y2 = Math.Min(rc.Height + 40, y2);
                if (x2 - x1 < 2 || y2 - y1 < 2) continue;
                var d = Math.Max(1, (int)Math.Round(2 * (r.R * scale + grow)));
                var piece = Native.CreateRoundRectRgn(x1, y1, x2 + 1, y2 + 1, d, d);
                if (all == IntPtr.Zero) { all = piece; continue; }
                Native.CombineRgn(all, all, piece, Native.RGN_OR);
                Native.DeleteObject(piece);
            }
            if (all == IntPtr.Zero) continue;
            if (Native.SetWindowRgn(h, all, true) == 0) Native.DeleteObject(all);   // после успеха регионом владеет Windows
            else { owned.Add(h); applied[h] = key; }
        }
    }

    void Release(IntPtr h)
    {
        try { Native.SetWindowRgn(h, IntPtr.Zero, true); } catch { }
        owned.Remove(h);
        applied.Remove(h);
    }

    void ClearAll()
    {
        foreach (var h in owned.ToList()) Release(h);
    }

    public void Dispose()
    {
        Stop();
        ClearAll();
    }
}
