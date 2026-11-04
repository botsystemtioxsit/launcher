using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DnsPlusApp;

/// <summary>
/// Включает встроенный в Windows 11 «DNS через HTTPS» (DoH) с адресом NextDNS и вашим ID.
/// Своего DNS-клиента нет: плагин только прописывает настройки Windows и умеет вернуть прежние.
/// Права администратора нужны только на время действия (один запрос Windows).
/// </summary>
static class Dns
{
    public static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BattleLauncher", "dns-plus");
    static string SettingsFile => Path.Combine(Dir, "settings.json");
    static string ResultFile => Path.Combine(Dir, "result.json");
    // Прежние DNS адаптеров лежат здесь, пока DNS Plus включён (помощник читает этот файл).
    public static string StateFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GhostShell", "DnsPlus", "state.json");

    // ID из NextDNS: 6 знаков (латиница в нижнем регистре и цифры). Всё остальное отклоняем:
    // ID попадает в команду, которую выполнит помощник с правами администратора.
    static readonly Regex IdRe = new("^[a-z0-9]{6}$", RegexOptions.Compiled);

    public static string? ParseId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim().ToLowerInvariant();
        // можно вставить и адрес вида https://dns.nextdns.io/abc123 или abc123.dns.nextdns.io
        var m = Regex.Match(s, @"nextdns\.io/([a-z0-9]{6})(?![a-z0-9])");
        if (m.Success) return m.Groups[1].Value;
        m = Regex.Match(s, @"^([a-z0-9]{6})\.dns\.nextdns\.io");
        if (m.Success) return m.Groups[1].Value;
        return IdRe.IsMatch(s) ? s : null;
    }

    public static string LoadId()
    {
        try
        {
            using var d = JsonDocument.Parse(File.ReadAllText(SettingsFile));
            return d.RootElement.TryGetProperty("id", out var v) ? ParseId(v.GetString()) ?? "" : "";
        }
        catch { return ""; }
    }

    public static void SaveId(string id)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(new { id }));
    }

    public static bool IsOn() { try { return File.Exists(StateFile); } catch { return false; } }

    public static bool Windows11Plus() => Environment.OSVersion.Version.Build >= 22000;

    const string Prelude = @"
$ErrorActionPreference = 'Stop'
$out = '__RESULT__'
function Done($ok, $msg) { (@{ ok = $ok; msg = $msg } | ConvertTo-Json -Compress) | Set-Content -Path $out -Encoding UTF8; exit 0 }
trap { Done $false ('Ошибка: ' + $_.Exception.Message) }
if (-not (Get-Command Add-DnsClientDohServerAddress -ErrorAction SilentlyContinue)) { Done $false 'Нужна Windows 11 (в Windows 10 нет встроенного DNS через HTTPS).' }
$state = '__STATE__'
$ips = @('45.90.28.0','45.90.30.0','2a07:a8c0::','2a07:a8c1::')
";

    const string EnableBody = @"
$id = '__ID__'
$ad = Get-NetAdapter | Where-Object { $_.Status -eq 'Up' -and $_.HardwareInterface -eq $true }
if (-not $ad) { Done $false 'Не найдено активное сетевое подключение.' }
if (-not (Test-Path $state)) {
  $saved = @()
  foreach ($a in $ad) {
    foreach ($fam in 'IPv4','IPv6') {
      $c = Get-DnsClientServerAddress -InterfaceIndex $a.ifIndex -AddressFamily $fam -ErrorAction SilentlyContinue
      $saved += @{ i = $a.ifIndex; f = $fam; a = @($c.ServerAddresses) }
    }
  }
  New-Item -ItemType Directory -Force -Path (Split-Path $state) | Out-Null
  ConvertTo-Json -InputObject $saved -Depth 4 | Set-Content -Path $state -Encoding UTF8
  icacls (Split-Path $state) /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)R' | Out-Null
}
foreach ($ip in $ips) {
  Remove-DnsClientDohServerAddress -ServerAddress $ip -ErrorAction SilentlyContinue
  Add-DnsClientDohServerAddress -ServerAddress $ip -DohTemplate ('https://dns.nextdns.io/' + $id) -AllowFallbackToUdp $false -AutoUpgrade $true
}
foreach ($a in $ad) {
  Set-DnsClientServerAddress -InterfaceIndex $a.ifIndex -ServerAddresses @('45.90.28.0','45.90.30.0')
  try { Set-DnsClientServerAddress -InterfaceIndex $a.ifIndex -ServerAddresses @('2a07:a8c0::','2a07:a8c1::') } catch { }
}
Clear-DnsClientCache
Done $true 'Готово'
";

    const string DisableBody = @"
if (Test-Path $state) {
  $saved = Get-Content -Raw $state | ConvertFrom-Json
  foreach ($s in @($saved)) {
    try {
      $addr = @($s.a | Where-Object { $_ })
      if ($addr.Count -eq 0) { Set-DnsClientServerAddress -InterfaceIndex $s.i -ResetServerAddresses }
      else { Set-DnsClientServerAddress -InterfaceIndex $s.i -ServerAddresses $addr }
    } catch { }
  }
}
# На случай, если файла не было: со всех активных адаптеров снимаем наши адреса
foreach ($a in (Get-NetAdapter | Where-Object { $_.Status -eq 'Up' })) {
  foreach ($fam in 'IPv4','IPv6') {
    $c = Get-DnsClientServerAddress -InterfaceIndex $a.ifIndex -AddressFamily $fam -ErrorAction SilentlyContinue
    if ($c -and (@($c.ServerAddresses | Where-Object { $ips -contains $_ }).Count -gt 0)) {
      try { Set-DnsClientServerAddress -InterfaceIndex $a.ifIndex -ResetServerAddresses } catch { }
    }
  }
}
foreach ($ip in $ips) { Remove-DnsClientDohServerAddress -ServerAddress $ip -ErrorAction SilentlyContinue }
Remove-Item -Force -ErrorAction SilentlyContinue $state
Clear-DnsClientCache
Done $true 'Готово'
";

    static string Esc(string s) => s.Replace("'", "''");

    static async Task<(bool ok, string msg)> RunElevated(string body)
    {
        Directory.CreateDirectory(Dir);
        try { File.Delete(ResultFile); } catch { }
        var script = (Prelude + body).Replace("__RESULT__", Esc(ResultFile)).Replace("__STATE__", Esc(StateFile));
        // -EncodedCommand: скрипт не лежит в файле, который можно подменить до запуска с правами администратора
        var enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var psi = new ProcessStartInfo("powershell.exe",
            "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " + enc)
        { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        Process? p;
        try { p = Process.Start(psi); }
        catch { return (false, "Windows не получила разрешение (окно «Разрешить?» закрыто или отклонено)."); }
        if (p == null) return (false, "Не удалось запустить помощника.");
        await Task.Run(() => p.WaitForExit(60000));
        try
        {
            using var d = JsonDocument.Parse(File.ReadAllText(ResultFile));
            return (d.RootElement.GetProperty("ok").GetBoolean(), d.RootElement.GetProperty("msg").GetString() ?? "");
        }
        catch { return (false, "Нет ответа от помощника администратора."); }
    }

    public static Task<(bool ok, string msg)> EnableAsync(string id)
    {
        var parsed = ParseId(id);
        if (parsed == null) return Task.FromResult((false, "ID должен состоять из 6 знаков (буквы и цифры), например abc123."));
        SaveId(parsed);
        return RunElevated(EnableBody.Replace("__ID__", parsed));
    }

    public static Task<(bool ok, string msg)> DisableAsync() => RunElevated(DisableBody);

    /// <summary>Спрашивает у test.nextdns.io, идёт ли трафик через NextDNS (показывает, что видит сам NextDNS).</summary>
    public static async Task<string> CheckAsync()
    {
        try
        {
            using var h = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            h.DefaultRequestHeaders.Add("User-Agent", "DnsPlus");
            h.DefaultRequestHeaders.Add("Accept", "application/json");
            var txt = await h.GetStringAsync("https://test.nextdns.io");
            using var d = JsonDocument.Parse(txt);
            var r = d.RootElement;
            string Get(string k) => r.TryGetProperty(k, out var v) ? v.ToString() : "";
            var status = Get("status");
            if (status == "ok")
                return $"Работает: трафик идёт через NextDNS (протокол {Get("protocol")}, ваш ID {Get("profile")}).";
            return "Трафик идёт мимо NextDNS (status: " + (status == "" ? "?" : status) + "). Нажмите «Включить».";
        }
        catch
        {
            return "Не удалось проверить (нет связи или test.nextdns.io недоступен).";
        }
    }
}
