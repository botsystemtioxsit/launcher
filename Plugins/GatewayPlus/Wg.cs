using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace GatewayPlusApp;

// Всё про WireGuard для Windows (официальная программа wireguard.com): где лежит, как запустить и остановить туннель, как поставить.
static class Wg
{
    public static string? ExePath()
    {
        var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WireGuard", "wireguard.exe");
        return File.Exists(p) ? p : null;
    }

    public static string? WgTool()
    {
        var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WireGuard", "wg.exe");
        return File.Exists(p) ? p : null;
    }

    public static string ServiceName(string tunnel) => "WireGuardTunnel$" + tunnel;

    public static (int Code, string Output) Run(string file, string args, int timeoutMs = 20000, string? stdin = null)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = stdin != null, StandardOutputEncoding = Encoding.UTF8 };
            using var p = Process.Start(psi)!;
            if (stdin != null) { p.StandardInput.Write(stdin); p.StandardInput.Close(); }
            var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return (-1, "не ответила за отведённое время"); }
            return (p.ExitCode, (o.Result + e.Result).Trim());
        }
        catch (Exception ex) { return (-2, ex.Message); }
    }

    // Работает ли туннель: служба WireGuardTunnel$<имя> в состоянии RUNNING.
    public static bool IsRunning(string tunnel)
    {
        if (!Store.ValidName(tunnel)) return false;
        var (_, o) = Run("sc.exe", "query \"" + ServiceName(tunnel) + "\"", 8000);
        return o.Contains("RUNNING", StringComparison.Ordinal);
    }

    public static bool ServiceExists(string tunnel)
    {
        var (code, _) = Run("sc.exe", "query \"" + ServiceName(tunnel) + "\"", 8000);
        return code == 0;
    }

    // Подпись программы-установщика: должна быть подписана WireGuard LLC и цепочка должна быть доверенной.
    public static bool SignedByWireGuard(string file, out string who)
    {
        who = "";
        try
        {
            var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(file));
            who = cert.GetNameInfo(X509NameType.SimpleName, false);
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            return chain.Build(cert) && who.Contains("WireGuard", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    // Скачать официальный установщик с wireguard.com, проверить подпись и запустить (Windows спросит разрешение администратора).
    public static async Task<string?> InstallAsync(Action<string> say)
    {
        try
        {
            var tmp = Path.Combine(Path.GetTempPath(), "wireguard-installer-" + Guid.NewGuid().ToString("N")[..8] + ".exe");
            say("Скачиваю установщик WireGuard с wireguard.com…");
            using (var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(90) })
            {
                var bytes = await http.GetByteArrayAsync("https://download.wireguard.com/windows-client/wireguard-installer.exe");
                await File.WriteAllBytesAsync(tmp, bytes);
            }
            if (!SignedByWireGuard(tmp, out var who)) { try { File.Delete(tmp); } catch { } return "Подпись установщика не подтвердилась (" + (who.Length > 0 ? who : "нет подписи") + ") — не запускаю."; }
            say("Подпись проверена: " + who + ". Сейчас Windows спросит разрешение — нажмите «Да».");
            var psi = new ProcessStartInfo(tmp) { UseShellExecute = true, Verb = "runas" };
            using var p = Process.Start(psi);
            if (p == null) return "Установщик не запустился.";
            await Task.Run(() => p.WaitForExit(300000));
            for (var i = 0; i < 30 && ExePath() == null; i++) await Task.Delay(1000);
            try { File.Delete(tmp); } catch { }
            return ExePath() != null ? null : "WireGuard не появился. Если установщик закрыли — нажмите кнопку ещё раз.";
        }
        catch (Exception ex) { return "Не получилось: " + ex.Message; }
    }
}
