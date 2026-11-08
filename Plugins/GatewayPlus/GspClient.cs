using System.IO;
using System.Security.Cryptography;

namespace GatewayPlusApp;

// GSP на Windows: программа gsp.exe (+ wintun.dll — сетевая карта) вложена в сам плагин, службу ставит и снимает помощник с правами администратора.
static class GspClient
{
    public static string ProgDir => Path.Combine(Helper.WorkDir, "gsp");
    public static string ExePath => Path.Combine(ProgDir, "gsp.exe");
    public static string StatusDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GhostShell", "GatewayPlus-status");
    public static string StatusFile(string name) => Path.Combine(StatusDir, name + ".json");
    public static string ConfFile(string name) => Path.Combine(Helper.WorkDir, "gsp-" + name + ".conf");

    // есть ли в этой сборке плагина программа GSP (в обычной сборке из CI — да; при сборке на своём компьютере без Go — нет)
    public static bool Available
    {
        get
        {
            var names = typeof(GspClient).Assembly.GetManifestResourceNames();
            return names.Contains("gsp.exe") && names.Contains("wintun.dll");
        }
    }

    static byte[]? Res(string name)
    {
        using var s = typeof(GspClient).Assembly.GetManifestResourceStream(name);
        if (s == null) return null;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    // Кладёт gsp.exe и wintun.dll в папку (если там уже такие же — не трогает). Возвращает текст ошибки или null.
    public static string? ExtractTo(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            foreach (var n in new[] { "gsp.exe", "wintun.dll" })
            {
                var data = Res(n);
                if (data == null) return "В этой сборке плагина нет файла " + n + ".";
                var dst = Path.Combine(dir, n);
                if (File.Exists(dst) && new FileInfo(dst).Length == data.Length && SHA256.HashData(File.ReadAllBytes(dst)).AsSpan().SequenceEqual(SHA256.HashData(data))) continue;
                File.WriteAllBytes(dst, data);
            }
            return null;
        }
        catch (Exception ex) { return "Не удалось записать программу GSP: " + ex.Message; }
    }

    public static bool IsRunning(string name)
    {
        if (!Store.ValidName(name)) return false;
        var (_, o) = Wg.Run("sc.exe", "query \"" + Gsp.ServiceName(name) + "\"", 8000);
        return o.Contains("RUNNING", StringComparison.Ordinal);
    }

    public static bool ServiceExists(string name)
    {
        if (!Store.ValidName(name)) return false;
        var (code, _) = Wg.Run("sc.exe", "query \"" + Gsp.ServiceName(name) + "\"", 8000);
        return code == 0;
    }

    public static Gsp.StatusInfo? ReadStatus(string name)
    {
        try
        {
            if (!Store.ValidName(name)) return null;
            var f = StatusFile(name);
            if (!File.Exists(f)) return null;
            using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var r = new StreamReader(fs);
            return Gsp.ParseStatus(r.ReadToEnd());
        }
        catch { return null; }
    }

    // Подпись состояния для окна: «GSP · потери сети 3,2% · защита 8+3 · восстановлено 120 пакетов».
    public static string Describe(Gsp.StatusInfo? s)
    {
        if (s == null) return "GSP · состояние пока не получено";
        if (s.Error.Length > 0) return "GSP · ошибка: " + s.Error;
        if (!s.Connected) return "GSP · связь с сервером устанавливается…";
        var loss = s.LossPct < 0.05 ? "потерь нет" : "потери сети " + s.LossPct.ToString("0.#") + "%";
        var fec = s.Fec == "off" ? "защита от потерь выключена (включится сама, если понадобится)" : "защита от потерь " + s.Fec;
        var rec = s.Recovered > 0 ? " · восстановлено пакетов: " + s.Recovered : "";
        var ks = s.KillSwitch ? " · защита от утечек включена" : "";
        return "GSP · " + loss + " · " + fec + rec + ks;
    }
}
