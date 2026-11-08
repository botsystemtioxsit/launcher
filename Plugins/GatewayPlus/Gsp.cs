using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GatewayPlusApp;

// Профили протокола GSP (Ghost Shell Protocol, Protocols/Gsp в этом репозитории): отличить от WireGuard, проверить, прочитать состояние.
// Файл без Windows-специфики — его собирает и проверка мастера на Linux (GatewayPlus.SshTest).
static class Gsp
{
    public static string ServiceName(string name) => "GhostShellGsp$" + name;

    // GSP-конфиг начинается с комментария «# GSP» (его пишет скрипт сервера) или содержит строку FEC (у WireGuard такой не бывает).
    public static bool IsGsp(string conf)
    {
        if (string.IsNullOrWhiteSpace(conf)) return false;
        foreach (var raw in conf.Replace("\r", "").Split('\n'))
        {
            var t = raw.Trim();
            if (t.Length == 0) continue;
            if (t.Equals("# GSP", StringComparison.OrdinalIgnoreCase)) return true;
            break;
        }
        return Regex.IsMatch(conf, @"(?im)^\s*FEC\s*=");
    }

    static readonly Regex KeyRe = new(@"^[A-Za-z0-9+/]{43}=$");
    static readonly Regex Cidr4 = new(@"^\d{1,3}(\.\d{1,3}){3}/\d{1,2}$");
    static readonly Regex Cidr6 = new(@"^[0-9a-fA-F:]{2,39}/\d{1,3}$");
    static readonly Regex Ep4 = new(@"^\d{1,3}(\.\d{1,3}){3}:\d{1,5}$");
    static readonly Regex FecRe = new(@"^(off|auto|\d{1,2}\+\d{1,2})$", RegexOptions.IgnoreCase);
    static readonly Regex KillRe = new(@"^(auto|on|off)$", RegexOptions.IgnoreCase);
    static readonly Regex NameRe = new(@"^[A-Za-z0-9 _\-.]{1,40}$");

    static readonly HashSet<string> IfaceKeys = new(StringComparer.OrdinalIgnoreCase) { "PrivateKey", "Address", "ListenPort", "MTU", "FEC", "Pad", "Name", "DNS", "KillSwitch" };
    static readonly HashSet<string> PeerKeys = new(StringComparer.OrdinalIgnoreCase) { "PublicKey", "Endpoint", "AllowedIPs", "PersistentKeepalive" };

    static bool Cidrs(string v) => v.Split(',').Select(x => x.Trim()).All(x => x.Length > 0 && (Cidr4.IsMatch(x) || Cidr6.IsMatch(x)) && int.TryParse(x[(x.IndexOf('/') + 1)..], out var b) && b <= (x.Contains(':') ? 128 : 32));

    // Проверка до сохранения и перед подключением (службу запускает администратор, поэтому принимаем только знакомые строки и значения).
    public static string? Validate(string conf)
    {
        if (string.IsNullOrWhiteSpace(conf)) return "Конфиг пустой.";
        if (conf.Length > 20000) return "Конфиг слишком большой.";
        string section = ""; int ifaces = 0, peers = 0;
        bool priv = false, pub = false, ep = false;
        foreach (var raw in conf.Replace("\r", "").Split('\n'))
        {
            var l = raw.Trim();
            if (l.Length == 0 || l.StartsWith('#')) continue;
            if (l.StartsWith('['))
            {
                if (l.Equals("[Interface]", StringComparison.OrdinalIgnoreCase)) { section = "i"; ifaces++; }
                else if (l.Equals("[Peer]", StringComparison.OrdinalIgnoreCase)) { section = "p"; peers++; }
                else return "Непонятный раздел в конфиге: " + (l.Length > 30 ? l[..30] + "…" : l);
                continue;
            }
            var eq = l.IndexOf('=');
            if (eq <= 0 || section.Length == 0) return "В конфиге есть непонятная строка: " + (l.Length > 40 ? l[..40] + "…" : l);
            var k = l[..eq].Trim(); var v = l[(eq + 1)..].Trim();
            if (section == "i")
            {
                if (!IfaceKeys.Contains(k)) return $"В конфиге GSP не бывает настройки «{k}» (команды вроде PostUp не принимаются вовсе).";
                switch (k.ToLowerInvariant())
                {
                    case "privatekey": if (!KeyRe.IsMatch(v)) return "PrivateKey: должен быть ключ из 44 знаков (base64)."; priv = true; break;
                    case "address": if (!Cidrs(v)) return "Address: ожидается адрес с маской, например 10.78.0.2/32."; break;
                    case "listenport": if (!int.TryParse(v, out var lp) || lp < 1 || lp > 65535) return "ListenPort: число от 1 до 65535."; break;
                    case "mtu": if (!int.TryParse(v, out var m) || m < 1200 || m > 1500) return "MTU: число от 1200 до 1500."; break;
                    case "fec": if (!FecRe.IsMatch(v)) return "FEC: off, auto или два числа через плюс (например 8+3)."; break;
                    case "pad": if (!int.TryParse(v, out var pd) || pd < 0 || pd > 255) return "Pad: число от 0 до 255."; break;
                    case "killswitch": if (!KillRe.IsMatch(v)) return "KillSwitch: auto, on или off."; break;
                    case "name": if (!NameRe.IsMatch(v)) return "Name: латиница, цифры, пробел, «_ - .», до 40 знаков."; break;
                    case "dns": if (!v.Split(',').All(x => IPAddress.TryParse(x.Trim(), out _))) return "DNS: список IP-адресов через запятую."; break;
                }
            }
            else
            {
                if (!PeerKeys.Contains(k)) return $"В разделе [Peer] конфига GSP не бывает настройки «{k}».";
                switch (k.ToLowerInvariant())
                {
                    case "publickey": if (!KeyRe.IsMatch(v)) return "PublicKey сервера: должен быть ключ из 44 знаков (base64)."; pub = true; break;
                    case "endpoint":
                        if (!Ep4.IsMatch(v) || !int.TryParse(v[(v.LastIndexOf(':') + 1)..], out var port) || port < 1 || port > 65535 || !IPAddress.TryParse(v[..v.LastIndexOf(':')], out _))
                            return "Endpoint: пока поддерживается только адрес IPv4 с портом, например 203.0.113.5:51821.";
                        ep = true; break;
                    case "allowedips": if (!Cidrs(v)) return "AllowedIPs: список сетей с маской, например 0.0.0.0/0."; break;
                    case "persistentkeepalive": if (!int.TryParse(v, out var ka) || ka < 0 || ka > 65535) return "PersistentKeepalive: число секунд."; break;
                }
            }
        }
        if (ifaces != 1) return "В конфиге должен быть ровно один раздел [Interface].";
        if (peers < 1) return "В конфиге нет раздела [Peer] (сервера).";
        if (!priv) return "В конфиге нет PrivateKey.";
        if (!pub) return "В конфиге нет PublicKey сервера.";
        if (!ep) return "В конфиге нет Endpoint (адрес сервера).";
        return null;
    }

    // Защита от утечек в профиле: "auto" (по умолчанию), "on" или "off".
    public static string KillSwitchOf(string conf)
    {
        var v = Store.InterfaceValue(conf, "KillSwitch").Trim().ToLowerInvariant();
        return v is "on" or "off" ? v : "auto";
    }

    public static string WithName(string conf, string adapterName) => Store.WithInterfaceValue(conf, "Name", adapterName);

    // Состояние, которое раз в секунду пишет служба (gsp … -status файл).
    public sealed class StatusInfo
    {
        public bool Connected { get; set; }
        public double LossPct { get; set; }
        public string Fec { get; set; } = "off";
        public long Recovered { get; set; }
        public long TxBytes { get; set; }
        public long RxBytes { get; set; }
        public int SessionAgeS { get; set; }
        public int LastRecvAgoS { get; set; }
        public bool KillSwitch { get; set; }     // защита от утечек (Windows) сейчас включена
        public string Error { get; set; } = "";
    }

    public static StatusInfo? ParseStatus(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var d = JsonDocument.Parse(json);
            var root = d.RootElement;
            var s = new StatusInfo();
            s.KillSwitch = root.TryGetProperty("killSwitch", out var ks) && ks.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String) { s.Error = e.GetString() ?? ""; return s; }
            if (!root.TryGetProperty("peers", out var peers) || peers.ValueKind != JsonValueKind.Array || peers.GetArrayLength() == 0) return s;
            var p = peers[0];
            long L(string n) => p.TryGetProperty(n, out var x) && x.ValueKind == JsonValueKind.Number ? x.GetInt64() : 0;
            s.Connected = p.TryGetProperty("connected", out var c) && c.ValueKind == JsonValueKind.True;
            s.LossPct = p.TryGetProperty("lossPct", out var lp) && lp.ValueKind == JsonValueKind.Number ? lp.GetDouble() : 0;
            s.Fec = p.TryGetProperty("fec", out var f) && f.ValueKind == JsonValueKind.String ? (f.GetString() ?? "off") : "off";
            s.Recovered = L("recovered"); s.TxBytes = L("txBytes"); s.RxBytes = L("rxBytes");
            s.SessionAgeS = (int)L("sessionAgeS"); s.LastRecvAgoS = (int)L("lastRecvAgoS");
            return s;
        }
        catch { return null; }
    }
}
