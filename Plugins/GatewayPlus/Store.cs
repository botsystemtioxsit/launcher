using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GatewayPlusApp;

// Профили шлюза (конфиги WireGuard) хранятся зашифрованными (DPAPI: расшифровать может только этот пользователь Windows на этом компьютере).
static class Store
{
    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BattleLauncher", "gateway-plus");
    static string ProfilesDir => Path.Combine(Dir, "profiles");
    static string SettingsFile => Path.Combine(Dir, "settings.json");
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GhostShell.GatewayPlus.v1");

    sealed class Settings { public string Active { get; set; } = ""; }

    // Имя туннеля: латиница, цифры, «_ - . = +», до 32 знаков (так требует WireGuard).
    public static bool ValidName(string s) => Regex.IsMatch(s ?? "", @"^[A-Za-z0-9_=+.\-]{1,32}$");

    public static string SafeName(string s)
    {
        var t = Regex.Replace((s ?? "").Trim(), @"[^A-Za-z0-9_=+.\-]", "_").Trim('_');
        if (t.Length > 32) t = t[..32];
        return t.Length == 0 ? "gateway" : t;
    }

    public static List<string> List()
    {
        try
        {
            if (!Directory.Exists(ProfilesDir)) return new();
            return Directory.GetFiles(ProfilesDir, "*.dat").Select(f => Path.GetFileNameWithoutExtension(f)).Where(ValidName).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch { return new(); }
    }

    public static void Save(string name, string conf)
    {
        if (!ValidName(name)) throw new ArgumentException("плохое имя");
        Directory.CreateDirectory(ProfilesDir);
        var enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(conf), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(Path.Combine(ProfilesDir, name + ".dat"), enc);
    }

    public static string? Load(string name)
    {
        try
        {
            if (!ValidName(name)) return null;
            var f = Path.Combine(ProfilesDir, name + ".dat");
            if (!File.Exists(f)) return null;
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(f), Entropy, DataProtectionScope.CurrentUser));
        }
        catch { return null; }
    }

    public static void Delete(string name)
    {
        if (!ValidName(name)) return;
        try { File.Delete(Path.Combine(ProfilesDir, name + ".dat")); } catch { }
    }

    public static string Active
    {
        get
        {
            try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsFile))?.Active ?? ""; } catch { return ""; }
        }
        set
        {
            try { Directory.CreateDirectory(Dir); File.WriteAllText(SettingsFile, JsonSerializer.Serialize(new Settings { Active = value })); } catch { }
        }
    }

    // Проверка конфига до сохранения и перед подключением. Возвращает текст проблемы или null.
    // Команды PreUp/PostUp/… не принимаем вовсе: WireGuard выполняет их от имени системы.
    public static string? Validate(string conf)
    {
        if (Gsp.IsGsp(conf)) return Gsp.Validate(conf);   // профиль GSP проверяется своими правилами
        if (string.IsNullOrWhiteSpace(conf)) return "Конфиг пустой.";
        if (conf.Length > 20000) return "Конфиг слишком большой.";
        var lines = conf.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
        if (!lines.Any(l => l.Equals("[Interface]", StringComparison.OrdinalIgnoreCase))) return "В конфиге нет раздела [Interface].";
        if (!lines.Any(l => l.Equals("[Peer]", StringComparison.OrdinalIgnoreCase))) return "В конфиге нет раздела [Peer] (сервера).";
        bool Has(string key) => lines.Any(l => l.StartsWith(key, StringComparison.OrdinalIgnoreCase) && l[key.Length..].TrimStart().StartsWith('='));
        if (!Has("PrivateKey")) return "В конфиге нет PrivateKey.";
        if (!Has("PublicKey")) return "В конфиге нет PublicKey сервера.";
        if (!Has("Endpoint")) return "В конфиге нет Endpoint (адрес сервера).";
        foreach (var bad in new[] { "PreUp", "PostUp", "PreDown", "PostDown" })
            if (Has(bad)) return $"В конфиге есть команда {bad}: такие конфиги не принимаются (она выполнялась бы с правами системы).";
        foreach (var l in lines)
            if (!(l.StartsWith('[') || l.Contains('='))) return "В конфиге есть непонятная строка: " + (l.Length > 40 ? l[..40] + "…" : l);
        return null;
    }

    // Значение строки из раздела [Interface] (например, MTU или Address); пусто, если строки нет.
    public static string InterfaceValue(string conf, string key)
    {
        var inIface = false;
        foreach (var raw in conf.Replace("\r", "").Split('\n'))
        {
            var t = raw.Trim();
            if (t.StartsWith('[')) { inIface = t.Equals("[Interface]", StringComparison.OrdinalIgnoreCase); continue; }
            if (inIface && t.StartsWith(key, StringComparison.OrdinalIgnoreCase) && t[key.Length..].TrimStart().StartsWith('='))
                return t[(t.IndexOf('=') + 1)..].Trim();
        }
        return "";
    }

    // Меняет (или добавляет) строку в разделе [Interface]. Остальное в конфиге остаётся как было.
    public static string WithInterfaceValue(string conf, string key, string value)
    {
        var lines = conf.Replace("\r", "").Split('\n').ToList();
        var inIface = false; var last = -1; var done = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith('[')) { inIface = t.Equals("[Interface]", StringComparison.OrdinalIgnoreCase); if (inIface) last = i; continue; }
            if (!inIface) continue;
            if (t.Length > 0) last = i;
            if (t.StartsWith(key, StringComparison.OrdinalIgnoreCase) && t[key.Length..].TrimStart().StartsWith('=')) { lines[i] = key + " = " + value; done = true; }
        }
        if (!done) lines.Insert(last + 1, key + " = " + value);
        return string.Join("\n", lines);
    }

    public static int MtuOf(string conf) => int.TryParse(InterfaceValue(conf, "MTU"), out var m) ? m : 0;

    // Дополняет Address адресом IPv6 внутри туннеля (после того как сервер включил IPv6): …/32 с номером N → <префикс>N/128.
    public static string WithIpv6(string conf, string prefix)
    {
        if (!Regex.IsMatch(prefix ?? "", @"^fd[0-9a-f:]{4,40}::$")) return conf;
        var addr = InterfaceValue(conf, "Address");
        if (addr.Length == 0 || addr.Contains(prefix, StringComparison.OrdinalIgnoreCase)) return conf;
        var m = Regex.Match(addr, @"(?:^|,)\s*\d{1,3}\.\d{1,3}\.\d{1,3}\.(\d{1,3})/32");
        if (!m.Success) return conf;
        return WithInterfaceValue(conf, "Address", addr + ", " + prefix + m.Groups[1].Value + "/128");
    }

    // Куда ведёт конфиг (для показа в окне): адрес сервера без порта.
    public static string EndpointOf(string conf)
    {
        foreach (var raw in conf.Replace("\r", "").Split('\n'))
        {
            var l = raw.Trim();
            if (!l.StartsWith("Endpoint", StringComparison.OrdinalIgnoreCase)) continue;
            var v = l[(l.IndexOf('=') + 1)..].Trim();
            var i = v.LastIndexOf(':');
            return i > 0 ? v[..i].Trim('[', ']') : v;
        }
        return "";
    }

    public static string ServerScript()
    {
        using var s = typeof(Store).Assembly.GetManifestResourceStream("server-setup.sh");
        if (s == null) return "";
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd().Replace("\r\n", "\n");
    }

    public static string GspServerScript()
    {
        using var s = typeof(Store).Assembly.GetManifestResourceStream("gsp-server-setup.sh");
        if (s == null) return "";
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd().Replace("\r\n", "\n");
    }

    public static string AddDeviceScript()
    {
        using var s = typeof(Store).Assembly.GetManifestResourceStream("add-device.sh");
        if (s == null) return "";
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd().Replace("\r\n", "\n");
    }

    public static string TuneScript()
    {
        using var s = typeof(Store).Assembly.GetManifestResourceStream("tune-server.sh");
        if (s == null) return "";
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd().Replace("\r\n", "\n");
    }
}
