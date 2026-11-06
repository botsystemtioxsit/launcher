using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GatewayPlusApp;

// «Мимо шлюза»: сервисы, которым VPN вредит (звонки WhatsApp прерывались: голос идёт по UDP через серверы Meta, а UDP внутри UDP добавляет
// задержку и потери). Windows маршрутизирует по АДРЕСАМ, а не по программам, поэтому исключение сделано по адресам серверов сервиса: при
// подключении «всё в туннель» (AllowedIPs = 0.0.0.0/0) в конфиг, который уходит службе, подставляется «всё, кроме этих сетей» — ровно
// так считает «AllowedIPs calculator» для WireGuard. Сам сохранённый профиль не меняется. Работает одинаково для WireGuard и GSP.
// Честно: исключаются ВСЕ адреса сервиса (у WhatsApp это сети Meta: Facebook и Instagram тоже пойдут напрямую), а при включённом
// исключении WireGuard не может закрыть «прямой выход» (его защита включается только при чистом 0.0.0.0/0).
static class Split
{
    public sealed record Preset(string Id, string Title, string[] Cidrs);

    // Сети Meta (AS32934, RIPEstat на 2026-11-06, склеенные). Свежий список плагин подтягивает при подключении (RefreshIfStaleAsync), это — запасной.
    static readonly string[] MetaCidrs =
    {
        "31.13.24.0/21", "31.13.64.0/18", "45.64.40.0/22", "57.141.0.0/24", "57.141.2.0/23", "57.141.4.0/23", "57.141.6.0/24", "57.141.8.0/24",
        "57.141.10.0/24", "57.141.12.0/23", "57.141.14.0/24", "57.141.16.0/22", "57.141.20.0/24", "57.141.22.0/24", "57.141.24.0/24", "57.144.0.0/14",
        "66.220.144.0/20", "69.63.176.0/20", "69.171.224.0/19", "74.119.76.0/22", "102.132.96.0/20", "103.4.96.0/22", "129.134.0.0/17",
        "157.240.0.0/17", "157.240.192.0/18", "163.70.128.0/17", "163.77.132.0/23", "163.77.136.0/23", "163.77.160.0/20", "173.252.64.0/18",
        "179.60.192.0/22", "185.60.216.0/22", "185.89.216.0/22", "204.15.20.0/22", "2620:0:1c00::/40", "2a03:2880::/32",
    };

    public static readonly Preset[] Presets = { new("whatsapp", "WhatsApp", MetaCidrs) };
    public static string[] Default => new[] { "whatsapp" };

    static string CacheFile => Path.Combine(Store.Dir, "bypass-whatsapp.json");

    // Сети сервиса: свежий кэш (не старше 30 дней), иначе вшитый список.
    public static string[] CidrsOf(string id)
    {
        var p = Presets.FirstOrDefault(x => x.Id == id);
        if (p == null) return Array.Empty<string>();
        if (id == "whatsapp")
        {
            try
            {
                if (File.Exists(CacheFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(CacheFile) < TimeSpan.FromDays(30))
                {
                    var cached = ParseRipe(File.ReadAllText(CacheFile));
                    if (cached != null) return cached;
                }
            }
            catch { }
        }
        return p.Cidrs;
    }

    // Раз в неделю спрашиваем актуальный список сетей Meta у RIPEstat (до подключения, пока трафик ещё идёт напрямую). Любая ошибка — молча остаётся прежний.
    public static async Task RefreshIfStaleAsync()
    {
        try
        {
            if (File.Exists(CacheFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(CacheFile) < TimeSpan.FromDays(7)) return;
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("GhostShell-GatewayPlus/" + Ui.Version);
            var json = await http.GetStringAsync("https://stat.ripe.net/data/announced-prefixes/data.json?resource=AS32934");
            var list = ParseRipe(json);
            if (list == null) return;
            Directory.CreateDirectory(Store.Dir);
            File.WriteAllText(CacheFile, JsonSerializer.Serialize(new { data = new { prefixes = list.Select(x => new { prefix = x }) } }));
        }
        catch { }
    }

    static readonly Regex CidrRe = new(@"^(\d{1,3}(\.\d{1,3}){3}|[0-9a-fA-F:]+)/\d{1,3}$", RegexOptions.Compiled);

    // Ответ RIPEstat → склеенный список сетей или null, если ответ не похож на правду (защита от подмены: ни «всё», ни огромных сетей, ни слишком мало).
    public static string[]? ParseRipe(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var arr = doc.RootElement.GetProperty("data").GetProperty("prefixes");
            var all = new List<string>();
            foreach (var e in arr.EnumerateArray())
            {
                var s = e.GetProperty("prefix").GetString() ?? "";
                if (!CidrRe.IsMatch(s) || !TryParse(s, out var r)) return null;
                var bits = int.Parse(s[(s.IndexOf('/') + 1)..]);
                if (r.V6 ? bits < 24 : bits < 12) return null;   // слишком широкая сеть — такого у Meta нет
                all.Add(s);
            }
            if (all.Count < 20 || all.Count > 3000) return null;
            var v4 = all.Where(x => !x.Contains(':')).ToList(); var v6 = all.Where(x => x.Contains(':')).ToList();
            var res = Merge(v4, false).Concat(Merge(v6, true)).ToArray();
            return res.Length >= 10 && res.Length <= 400 ? res : null;
        }
        catch { return null; }
    }

    // ---------- арифметика сетей (IPv4 и IPv6 одним кодом, через UInt128) ----------
    readonly record struct Rng(bool V6, UInt128 A, UInt128 B);

    static int BitsOf(bool v6) => v6 ? 128 : 32;
    static UInt128 MaxOf(bool v6) => v6 ? UInt128.MaxValue : 0xFFFFFFFFu;
    static UInt128 HostMask(bool v6, int n) { var bits = BitsOf(v6); return n >= bits ? 0 : (bits - n >= 128 ? UInt128.MaxValue : (((UInt128)1 << (bits - n)) - 1)); }

    static UInt128 FromBytes(byte[] b) { UInt128 v = 0; foreach (var x in b) v = (v << 8) | x; return v; }

    static bool TryParse(string cidr, out Rng r)
    {
        r = default;
        var i = cidr.IndexOf('/');
        if (i <= 0 || !int.TryParse(cidr[(i + 1)..], out var n) || !IPAddress.TryParse(cidr[..i], out var ip)) return false;
        var v6 = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
        if (!v6 && ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        if (n < 0 || n > BitsOf(v6)) return false;
        var host = HostMask(v6, n);
        var a = FromBytes(ip.GetAddressBytes()) & ~host;
        r = new Rng(v6, a, a | host);
        return true;
    }

    static string Format(bool v6, UInt128 a)
    {
        if (!v6) return $"{(uint)(a >> 24) & 255}.{(uint)(a >> 16) & 255}.{(uint)(a >> 8) & 255}.{(uint)a & 255}";
        var b = new byte[16];
        for (var i = 15; i >= 0; i--) { b[i] = (byte)(a & 0xFF); a >>= 8; }
        return new IPAddress(b).ToString();
    }

    static List<Rng> Union(IEnumerable<string> cidrs, bool v6)
    {
        var rs = new List<Rng>();
        foreach (var c in cidrs) if (TryParse(c, out var r) && r.V6 == v6) rs.Add(r);
        rs.Sort((x, y) => x.A.CompareTo(y.A));
        var res = new List<Rng>(); var max = MaxOf(v6);
        foreach (var r in rs)
        {
            if (res.Count > 0)
            {
                var last = res[^1];
                if (last.B == max || r.A <= last.B + 1) { res[^1] = new Rng(v6, last.A, r.B > last.B ? r.B : last.B); continue; }
            }
            res.Add(r);
        }
        return res;
    }

    // Диапазон [a, b] → минимальный набор сетей.
    static void RangeToCidrs(bool v6, UInt128 a, UInt128 b, List<string> output)
    {
        var bits = BitsOf(v6); var max = MaxOf(v6);
        while (a <= b)
        {
            if (a == 0 && b == max) { output.Add(Format(v6, 0) + "/0"); return; }
            var tz = a == 0 ? bits : (int)UInt128.TrailingZeroCount(a);
            var k = Math.Min(tz, (int)UInt128.Log2(b - a + 1));
            output.Add(Format(v6, a) + "/" + (bits - k));
            var step = (UInt128)1 << k;
            if (max - a < step) return;   // дошли до конца пространства адресов
            a += step;
        }
    }

    // Склеить сети (убрать вложенные и соседние).
    public static List<string> Merge(IEnumerable<string> cidrs, bool v6)
    {
        var res = new List<string>();
        foreach (var r in Union(cidrs, v6)) RangeToCidrs(v6, r.A, r.B, res);
        return res;
    }

    // «Всё адресное пространство, кроме этих сетей» (минимальный набор сетей).
    public static List<string> Complement(IEnumerable<string> exclude, bool v6)
    {
        var res = new List<string>(); var max = MaxOf(v6);
        UInt128 start = 0; var done = false;
        foreach (var r in Union(exclude, v6))
        {
            if (r.A > start) RangeToCidrs(v6, start, r.A - 1, res);
            if (r.B == max) { done = true; break; }
            start = r.B + 1;
        }
        if (!done) RangeToCidrs(v6, start, max, res);
        return res;
    }

    // Попадает ли адрес в одну из сетей (для проверок).
    public static bool Covers(IEnumerable<string> cidrs, string ipText)
    {
        if (!IPAddress.TryParse(ipText, out var ip)) return false;
        var v6 = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
        var a = FromBytes(ip.GetAddressBytes());
        foreach (var c in cidrs) if (TryParse(c, out var r) && r.V6 == v6 && a >= r.A && a <= r.B) return true;
        return false;
    }

    // Подставляет «всё, кроме выбранных сервисов и адреса самого сервера» вместо 0.0.0.0/0 (и ::/0) в AllowedIPs. Нечего менять — вернёт конфиг как есть.
    public static string Apply(string conf, IEnumerable<string> ids, IEnumerable<IPAddress> endpoint)
    {
        var ex4 = new List<string>(); var ex6 = new List<string>();
        foreach (var id in ids) foreach (var c in CidrsOf(id)) (c.Contains(':') ? ex6 : ex4).Add(c);
        if (ex4.Count == 0 && ex6.Count == 0) return conf;
        foreach (var ip0 in endpoint)
        {
            var ip = ip0.IsIPv4MappedToIPv6 ? ip0.MapToIPv4() : ip0;
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) ex4.Add(ip + "/32");
            else if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6) ex6.Add(ip + "/128");
        }
        var lines = conf.Replace("\r", "").Split('\n');
        var section = ""; var changed = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith('[')) { section = t.ToLowerInvariant(); continue; }
            if (section != "[peer]" || !t.StartsWith("AllowedIPs", StringComparison.OrdinalIgnoreCase)) continue;
            var eq = t.IndexOf('=');
            if (eq < 0) continue;
            var items = t[(eq + 1)..].Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            var has4 = items.Contains("0.0.0.0/0"); var has6 = items.Contains("::/0");
            if (!has4 && !has6) continue;
            var outItems = items.Where(x => x != "0.0.0.0/0" && x != "::/0").ToList();
            if (has4) outItems.AddRange(Complement(ex4, false));
            if (has6) outItems.AddRange(Complement(ex6, true));
            if (outItems.Count > 1500) return conf;   // странный результат — лучше без исключения, чем сломанный конфиг
            lines[i] = "AllowedIPs = " + string.Join(", ", outItems);
            changed = true;
        }
        return changed ? string.Join("\n", lines) : conf;
    }

    // Для телефона: приложение WireGuard для Android умеет исключать приложения по именам пакетов (строка ExcludedApplications в [Interface]).
    public static string ForPhone(string conf, IEnumerable<string> ids)
    {
        if (!ids.Contains("whatsapp")) return conf;
        return Store.WithInterfaceValue(conf, "ExcludedApplications", "com.whatsapp,com.whatsapp.w4b");
    }

    // Адреса сервера (литерал или имя) — до подключения, пока трафик идёт напрямую.
    public static async Task<IPAddress[]> ResolveAsync(string host)
    {
        if (IPAddress.TryParse(host, out var lit)) return new[] { lit };
        try { return await Dns.GetHostAddressesAsync(host); } catch { return Array.Empty<IPAddress>(); }
    }
}
