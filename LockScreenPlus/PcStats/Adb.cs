using System.Diagnostics;
using System.IO.Compression;
using System.Net.Sockets;

namespace PcStats;

/// <summary>
/// Запасная дорожка без USB-модема: «отладка по USB». Программа ищет официальный adb от Google
/// (в PATH, рядом, или скачивает platform-tools один раз с dl.google.com), пробрасывает порт телефона
/// на этот компьютер и шлёт данные по TCP.
/// </summary>
internal sealed class Adb
{
    private const int PhonePort = 47822;
    private const int LocalPort = 47822;
    private string? exe;
    private bool downloadTried;
    private TcpClient? tcp;
    private DateTime nextTry = DateTime.MinValue;
    public string Status { get; private set; } = "Жду телефон: подключите кабель и включите «USB-модем» или «Отладку по USB».";

    public bool TrySend(byte[] json)
    {
        try
        {
            if (tcp != null && tcp.Connected)
            {
                var st = tcp.GetStream();
                st.Write(json, 0, json.Length);
                st.WriteByte((byte)'\n');
                return true;
            }
        }
        catch
        {
            Drop();
        }
        if (DateTime.UtcNow < nextTry)
        {
            return false;
        }
        nextTry = DateTime.UtcNow.AddSeconds(3);
        if (!Connect())
        {
            return false;
        }
        return TrySend(json);
    }

    private void Drop()
    {
        try
        {
            tcp?.Close();
        }
        catch
        {
        }
        tcp = null;
    }

    private bool Connect()
    {
        exe ??= FindOrFetch();
        if (exe == null)
        {
            Status = "Не нашёл adb и не смог его скачать (нужен интернет на компьютере). Либо включите «USB-модем» на телефоне.";
            return false;
        }
        string devices = Run("devices");
        var lines = devices.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("List of") && !l.StartsWith("*")).ToList();
        if (lines.Count == 0)
        {
            Status = "Телефона по кабелю не видно. Включите на нём «Отладку по USB» и подтвердите запрос на экране, либо включите «USB-модем».";
            return false;
        }
        if (lines.Any(l => l.EndsWith("unauthorized", StringComparison.Ordinal)))
        {
            Status = "На телефоне появился запрос «Разрешить отладку по USB?» — нажмите «Разрешить».";
            return false;
        }
        if (!lines.Any(l => l.EndsWith("device", StringComparison.Ordinal)))
        {
            Status = "Телефон найден, но не готов: " + lines[0];
            return false;
        }
        Run($"forward tcp:{LocalPort} tcp:{PhonePort}");
        try
        {
            var c = new TcpClient();
            c.Connect("127.0.0.1", LocalPort);
            tcp = c;
            return true;
        }
        catch
        {
            Status = "Не удалось подключиться к приложению на телефоне. Откройте Lock Screen Plus и установите его обои (или нажмите «Режим часов → Показать сейчас»).";
            return false;
        }
    }

    private string Run(string args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe!, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi)!;
            string o = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(8000))
            {
                try { p.Kill(); } catch { }
            }
            return o;
        }
        catch
        {
            return "";
        }
    }

    private string? FindOrFetch()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LockScreenPcStats", "platform-tools");
        string local = Path.Combine(dir, "adb.exe");
        if (File.Exists(local))
        {
            return local;
        }
        string? onPath = Environment.GetEnvironmentVariable("PATH")?.Split(';')
            .Select(d => { try { return Path.Combine(d.Trim(), "adb.exe"); } catch { return ""; } })
            .FirstOrDefault(f => f.Length > 0 && File.Exists(f));
        if (onPath != null)
        {
            return onPath;
        }
        if (downloadTried)
        {
            return null;
        }
        downloadTried = true;
        try
        {
            Console.WriteLine();
            Console.WriteLine("Скачиваю официальный adb от Google (один раз, ~7 МБ)…");
            string root = Path.GetDirectoryName(dir)!;
            Directory.CreateDirectory(root);
            string zip = Path.Combine(root, "platform-tools.zip");
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) })
            using (var resp = http.GetAsync("https://dl.google.com/android/repository/platform-tools-latest-windows.zip").GetAwaiter().GetResult())
            {
                resp.EnsureSuccessStatusCode();
                using var fs = File.Create(zip);
                resp.Content.CopyToAsync(fs).GetAwaiter().GetResult();
            }
            ZipFile.ExtractToDirectory(zip, root, true);
            File.Delete(zip);
            return File.Exists(local) ? local : null;
        }
        catch
        {
            return null;
        }
    }
}
