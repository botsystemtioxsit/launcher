using System.Diagnostics;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace PcStats;

/// <summary>
/// FPS игры: события показа кадра (Present) от DXGI в журнале событий Windows (ETW) — так делают PresentMon и подобные программы.
/// Нужны права администратора. Работает с играми на DirectX 9/10/11/12; Vulkan и OpenGL-окна иногда не видны.
/// Считает по каждой программе отдельно и показывает самую «кадровую».
/// </summary>
internal sealed class FpsMeter : IDisposable
{
    private static readonly Guid Dxgi = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private readonly object gate = new();
    private readonly Dictionary<int, List<double>> frames = new();
    private TraceEventSession? session;
    private Thread? thread;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    public string Status { get; private set; } = "";

    public bool Start()
    {
        try
        {
            session = new TraceEventSession("LockScreenPcStatsFps") { StopOnDispose = true };
            session.EnableProvider(Dxgi, TraceEventLevel.Informational, ulong.MaxValue);
            thread = new Thread(() =>
            {
                try
                {
                    session.Source.Dynamic.All += OnEvent;
                    session.Source.Process();
                }
                catch
                {
                }
            }) { IsBackground = true, Name = "fps-etw" };
            thread.Start();
            return true;
        }
        catch (Exception e)
        {
            Status = "FPS недоступен: " + e.Message;
            return false;
        }
    }

    private void OnEvent(TraceEvent e)
    {
        if (e.ProviderGuid != Dxgi || (int)e.ID != 42) // 42 = Present::Start
        {
            return;
        }
        double now = clock.Elapsed.TotalMilliseconds;
        lock (gate)
        {
            if (!frames.TryGetValue(e.ProcessID, out var list))
            {
                frames[e.ProcessID] = list = new List<double>(512);
            }
            list.Add(now);
            if (list.Count > 4000)
            {
                list.RemoveRange(0, 2000);
            }
        }
    }

    /// <summary>Возвращает (fps, 1% low, время кадра мс, имя программы) или null, если игры не видно.</summary>
    public (double Fps, double Low, double Ft, string Game)? Snapshot()
    {
        double now = clock.Elapsed.TotalMilliseconds;
        int bestPid = -1;
        int bestCount = 0;
        List<double>? bestTimes = null;
        lock (gate)
        {
            var dead = new List<int>();
            foreach (var kv in frames)
            {
                kv.Value.RemoveAll(t => t < now - 5000);
                if (kv.Value.Count == 0)
                {
                    dead.Add(kv.Key);
                    continue;
                }
                int c = kv.Value.Count(t => t >= now - 1000);
                if (c > bestCount)
                {
                    bestCount = c;
                    bestPid = kv.Key;
                    bestTimes = new List<double>(kv.Value);
                }
            }
            foreach (int k in dead)
            {
                frames.Remove(k);
            }
        }
        if (bestPid < 0 || bestTimes == null || bestCount < 15)
        {
            return null;
        }
        string name;
        try
        {
            name = Process.GetProcessById(bestPid).ProcessName;
        }
        catch
        {
            return null;
        }
        string low = name.ToLowerInvariant();
        if (low is "dwm" or "explorer" or "lockscreenpcstats" or "searchhost" or "shellexperiencehost" or "startmenuexperiencehost")
        {
            return null;
        }
        var deltas = new List<double>();
        for (int i = 1; i < bestTimes.Count; i++)
        {
            double d = bestTimes[i] - bestTimes[i - 1];
            if (d > 0 && d < 1000)
            {
                deltas.Add(d);
            }
        }
        if (deltas.Count < 10)
        {
            return null;
        }
        var last = bestTimes.Where(t => t >= now - 1000).ToList();
        double fps = last.Count; // кадров за последнюю секунду
        deltas.Sort();
        double p99 = deltas[Math.Min(deltas.Count - 1, (int)Math.Floor(deltas.Count * 0.99))];
        double avg = deltas.TakeLast(Math.Max(5, last.Count)).Average();
        return (fps, 1000.0 / p99, avg, name + ".exe");
    }

    public void Dispose()
    {
        try
        {
            session?.Dispose();
        }
        catch
        {
        }
    }
}
