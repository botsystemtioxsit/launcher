using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;

namespace GatewayPlusApp;

// Замеры для окна «Скорость и размер пакета»: какой размер пакета проходит по вашему подключению без дробления (MTU) и какова скорость сейчас.
static class Speed
{
    public sealed record MtuResult(bool Ok, int PathMtu, string Via, string Note);
    public sealed record SpeedResult(double DownMbps, double UpMbps, int PingMs, string Where);

    // true — прошёл без дробления; false — не прошёл (слишком большой или потерялся); PingException/тайм-аут считаем «не прошёл»
    static async Task<bool> PingDf(string host, int payload, CancellationToken ct)
    {
        using var ping = new Ping();
        var buf = new byte[payload];
        for (var attempt = 0; attempt < 2; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var r = await ping.SendPingAsync(host, 1500, buf, new PingOptions(64, true));
                if (r.Status == IPStatus.Success) return true;
                if (r.Status == IPStatus.PacketTooBig) return false;
            }
            catch (PingException) { }
            catch (InvalidOperationException) { }
        }
        return false;
    }

    // Ищет самый большой пакет, проходящий без дробления (бинарный поиск 1200…1472 байта данных; +28 байт заголовков = MTU пути).
    // allowFallback: если сервер не отвечает на пинг, пробуем 1.1.1.1 и 8.8.8.8 — но только когда шлюз выключен (иначе мерили бы сам туннель).
    public static async Task<MtuResult> ProbePathMtuAsync(string endpointHost, bool allowFallback, CancellationToken ct)
    {
        var hosts = new List<string>();
        if (!string.IsNullOrWhiteSpace(endpointHost)) hosts.Add(endpointHost);
        if (allowFallback) { hosts.Add("1.1.1.1"); hosts.Add("8.8.8.8"); }
        foreach (var host in hosts)
        {
            if (!await PingDf(host, 32, ct)) continue;   // хост вообще не отвечает на пинг — пробуем следующий
            var via = host == endpointHost ? "до вашего сервера" : "до " + host + " (сервер на пинг не отвечает)";
            if (await PingDf(host, 1472, ct)) return new(true, 1500, via, "");
            var lo = 1200;
            if (!await PingDf(host, lo, ct))
            {
                lo = 548;
                if (!await PingDf(host, lo, ct)) return new(false, 0, via, "Даже небольшие пакеты не проходят без дробления — сеть странная, подобрать размер нельзя.");
            }
            var hi = 1472;   // lo — проходит, hi — не проходит
            while (hi - lo > 1)
            {
                var mid = (lo + hi) / 2;
                if (await PingDf(host, mid, ct)) lo = mid; else hi = mid;
            }
            return new(true, lo + 28, via, "");
        }
        return new(false, 0, "", allowFallback
            ? "Ни сервер, ни 1.1.1.1 / 8.8.8.8 не отвечают на пинг — размер пакета подобрать нельзя."
            : "Сервер не отвечает на пинг. Отключите шлюз и повторите: тогда проверим по обычному интернету.");
    }

    // Размер пакета для WireGuard: путь минус его заголовки (60 байт при IPv4 и 80 при IPv6), не больше 1420 и не меньше 1280.
    public static int RecommendedWgMtu(int pathMtu, string endpointHost)
    {
        var v6 = IPAddress.TryParse(endpointHost, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
        return Math.Clamp(pathMtu - (v6 ? 80 : 60), 1280, 1420);
    }

    // Скорость «так, как идёт трафик сейчас» (через шлюз, если он включён). Сервер замеров — Cloudflare. Результат примерный.
    public static async Task<SpeedResult> MeasureAsync(Action<string> say, CancellationToken ct)
    {
        using var handler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(8) };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(40) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GatewayPlus");

        say("Проверяю связь…");
        var where = "";
        try
        {
            var trace = await http.GetStringAsync("https://speed.cloudflare.com/cdn-cgi/trace", ct);
            string F(string k) => Regex.Match(trace, "^" + k + "=(.*)$", RegexOptions.Multiline).Groups[1].Value.Trim();
            where = $"{F("ip")} · страна {F("loc")} · узел Cloudflare {F("colo")}";
        }
        catch { }

        var pings = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            ct.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            try { using var r = await http.GetAsync("https://speed.cloudflare.com/__down?bytes=0", ct); pings.Add((int)sw.ElapsedMilliseconds); }
            catch (HttpRequestException) { }
        }
        if (pings.Count == 0) throw new InvalidOperationException("нет связи с сервером замеров (speed.cloudflare.com)");

        say("Меряю скачивание (8 секунд)…");
        long down = 0;
        var dsw = Stopwatch.StartNew();
        using (var dl = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            dl.CancelAfter(TimeSpan.FromSeconds(8));
            await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
            {
                var buf = new byte[81920];
                try
                {
                    while (!dl.IsCancellationRequested)
                    {
                        using var resp = await http.GetAsync("https://speed.cloudflare.com/__down?bytes=25000000", HttpCompletionOption.ResponseHeadersRead, dl.Token);
                        await using var st = await resp.Content.ReadAsStreamAsync(dl.Token);
                        int n;
                        while ((n = await st.ReadAsync(buf, dl.Token)) > 0) Interlocked.Add(ref down, n);
                    }
                }
                catch (OperationCanceledException) { }
                catch (HttpRequestException) { }
            }));
        }
        ct.ThrowIfCancellationRequested();
        var downMbps = down * 8.0 / Math.Max(0.5, dsw.Elapsed.TotalSeconds) / 1e6;

        say("Меряю отдачу (6 секунд)…");
        long up = 0;
        var usw = Stopwatch.StartNew();
        using (var ul = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            ul.CancelAfter(TimeSpan.FromSeconds(6));
            var chunk = new byte[2_000_000];
            await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
            {
                try
                {
                    while (!ul.IsCancellationRequested)
                    {
                        using var resp = await http.PostAsync("https://speed.cloudflare.com/__up", new ByteArrayContent(chunk), ul.Token);
                        Interlocked.Add(ref up, chunk.Length);
                    }
                }
                catch (OperationCanceledException) { }
                catch (HttpRequestException) { }
            }));
        }
        ct.ThrowIfCancellationRequested();
        var upMbps = up * 8.0 / Math.Max(0.5, usw.Elapsed.TotalSeconds) / 1e6;
        return new SpeedResult(downMbps, upMbps, pings.Min(), where);
    }
}
