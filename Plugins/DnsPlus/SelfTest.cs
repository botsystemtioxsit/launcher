using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace DnsPlusApp;

/// <summary>
/// Проверка на настоящей Windows (запуск: DnsPlus.exe --selftest=&lt;папка&gt; от администратора, например на машине GitHub).
/// Разбор ID, затем настоящее включение и выключение: настройки DNS до / во время / после сравниваются, особенно «автоматический DNS остался автоматическим».
/// Меняет DNS на этом компьютере на несколько секунд и всё возвращает; в конце ещё раз сбрасывает DNS в «автоматически» там, где так было.
/// </summary>
static class SelfTest
{
    const string Inspect = @"
$ErrorActionPreference = 'Continue'
$ips = @('45.90.28.0','45.90.30.0','2a07:a8c0::','2a07:a8c1::')
$rows = @()
foreach ($a in (Get-NetAdapter | Where-Object { $_.Status -eq 'Up' -and $_.HardwareInterface -eq $true })) {
  foreach ($fam in 'IPv4','IPv6') {
    $c = Get-DnsClientServerAddress -InterfaceIndex $a.ifIndex -AddressFamily $fam -ErrorAction SilentlyContinue
    $svc = if ($fam -eq 'IPv6') { 'Tcpip6' } else { 'Tcpip' }
    $ns = (Get-ItemProperty -Path ('HKLM:\SYSTEM\CurrentControlSet\Services\' + $svc + '\Parameters\Interfaces\' + $a.InterfaceGuid) -Name NameServer -ErrorAction SilentlyContinue).NameServer
    $rows += [pscustomobject]@{ i = $a.ifIndex; name = $a.Name; f = $fam; a = @($c.ServerAddresses); stat = (-not [string]::IsNullOrEmpty($ns)) }
  }
}
$doh = @(Get-DnsClientDohServerAddress -ErrorAction SilentlyContinue | Where-Object { $ips -contains $_.ServerAddress } | ForEach-Object {
  [pscustomobject]@{ ip = $_.ServerAddress; tpl = $_.DohTemplate; udp = [bool]$_.AllowFallbackToUdp; up = [bool]$_.AutoUpgrade } })
[pscustomobject]@{ rows = $rows; doh = $doh } | ConvertTo-Json -Depth 6 -Compress
";

    sealed record Row(int I, string Name, string F, string[] A, bool Stat);
    sealed record Snap(Row[] Rows, (string Ip, string Tpl, bool Udp, bool Up)[] Doh);

    static string Ps(string script)
    {
        var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)))
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit(120000);
        return o;
    }

    static Snap Take()
    {
        var txt = Ps(Inspect).Trim();
        using var d = JsonDocument.Parse(txt);
        static IEnumerable<JsonElement> Items(JsonElement e) => e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : e.ValueKind == JsonValueKind.Object ? new[] { e } : Array.Empty<JsonElement>();
        static string[] Strs(JsonElement e) => e.ValueKind == JsonValueKind.String ? new[] { e.GetString() ?? "" } : Items(e).Select(x => x.GetString() ?? "").ToArray();
        var rows = Items(d.RootElement.GetProperty("rows")).Select(r => new Row(
            r.GetProperty("i").GetInt32(), r.GetProperty("name").GetString() ?? "", r.GetProperty("f").GetString() ?? "",
            Strs(r.GetProperty("a")), r.GetProperty("stat").GetBoolean())).ToArray();
        var doh = Items(d.RootElement.GetProperty("doh")).Select(x => (x.GetProperty("ip").GetString() ?? "", x.GetProperty("tpl").GetString() ?? "", x.GetProperty("udp").GetBoolean(), x.GetProperty("up").GetBoolean())).ToArray();
        return new Snap(rows, doh);
    }

    public static async Task<int> Run(string dir)
    {
        Directory.CreateDirectory(dir);
        var log = new StringBuilder();
        int bad = 0;
        void Say(string s) => log.AppendLine(s);
        void Ok(string name, bool cond, string extra = "")
        {
            Say((cond ? "ok      " : "FAIL    ") + name + (extra.Length > 0 ? "   [" + extra + "]" : ""));
            if (!cond) bad++;
        }
        static string Show(IEnumerable<Row> rows) => string.Join("; ", rows.Select(r => $"{r.Name}/{r.F}: " + (r.A.Length == 0 ? "—" : string.Join(",", r.A)) + (r.Stat ? " (вручную)" : " (автоматически)")));

        Say($"DNS Plus {typeof(SelfTest).Assembly.GetName().Version} · Windows {Environment.OSVersion.Version} · 11 или новее: {Dns.Windows11Plus()}");

        // 1. разбор ID: нормальные записи и враждебные (ID попадает в команду администратора)
        Ok("ID: просто 6 знаков", Dns.ParseId("abc123") == "abc123");
        Ok("ID: из ссылки https://dns.nextdns.io/abc123", Dns.ParseId("https://dns.nextdns.io/abc123") == "abc123");
        Ok("ID: из адреса abc123.dns.nextdns.io", Dns.ParseId("abc123.dns.nextdns.io") == "abc123");
        Ok("ID: пробелы и заглавные", Dns.ParseId("  ABC123 ") == "abc123");
        Ok("ID: 5 знаков отклоняется", Dns.ParseId("abc12") == null);
        Ok("ID: 7 знаков отклоняется", Dns.ParseId("abc1234") == null);
        Ok("ID: кавычка и команда отклоняются", Dns.ParseId("abc123'; calc") == null && Dns.ParseId("a'b123") == null);
        Ok("ID: пусто отклоняется", Dns.ParseId("") == null && Dns.ParseId(null) == null);

        var adminOk = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        Say("администратор: " + adminOk);
        if (!Dns.Windows11Plus() || !adminOk) { Say("Дальше нужны Windows 11 и права администратора — пропускаю."); File.WriteAllText(Path.Combine(dir, "report.txt"), log.ToString()); return bad == 0 ? 0 : 1; }

        Snap before;
        try { before = Take(); } catch (Exception ex) { Ok("состояние до включения читается", false, ex.Message); File.WriteAllText(Path.Combine(dir, "report.txt"), log.ToString()); return 1; }
        Say("до включения: " + Show(before.Rows));
        Ok("есть хотя бы один активный сетевой адаптер", before.Rows.Length > 0);
        Ok("до включения наших записей DoH нет", before.Doh.Length == 0);
        Ok("до включения плагин считает себя выключенным", !Dns.IsOn());

        try
        {
            // 2. включение
            var (ok, msg) = await Dns.EnableAsync("abc123");
            Ok("включение прошло", ok, msg);
            var during = Take();
            Say("во время: " + Show(during.Rows));
            Ok("плагин считает себя включённым (файл состояния)", Dns.IsOn());
            Ok("записано 4 адреса DoH", during.Doh.Length == 4, string.Join(", ", during.Doh.Select(d => d.Ip)));
            Ok("у всех шаблон https://dns.nextdns.io/abc123", during.Doh.Length > 0 && during.Doh.All(d => d.Tpl == "https://dns.nextdns.io/abc123"), string.Join(", ", during.Doh.Select(d => d.Tpl)));
            Ok("откат на обычный UDP запрещён, автоповышение включено", during.Doh.Length > 0 && during.Doh.All(d => !d.Udp && d.Up));
            // у части «активных» адаптеров настроек DNS нет вовсе (виртуальные, без IP) — их плагин пропускает и не должен падать из-за них
            var withDns = before.Rows.Where(r => r.F == "IPv4" && r.A.Length > 0).ToArray();
            Ok("есть подключение с настроенным DNS (иначе проверять нечего)", withDns.Length > 0);
            foreach (var b in withDns)
            {
                var now = during.Rows.FirstOrDefault(r => r.I == b.I && r.F == "IPv4");
                Ok($"адаптер {b.Name}: DNS IPv4 = NextDNS", now != null && now.A.SequenceEqual(new[] { "45.90.28.0", "45.90.30.0" }), now == null ? "нет" : string.Join(",", now.A));
            }
            foreach (var b in before.Rows.Where(r => r.F == "IPv4" && r.A.Length == 0))
            {
                var now = during.Rows.FirstOrDefault(r => r.I == b.I && r.F == "IPv4");
                Ok($"адаптер {b.Name} без настроек DNS пропущен и не тронут", now != null && now.A.Length == 0);
            }

            // повторное включение с другим ID: прежние DNS не должны затереться «NextDNS-ом как прежним»
            var (ok2, msg2) = await Dns.EnableAsync("zzz999");
            Ok("повторное включение с другим ID прошло", ok2, msg2);
            var again = Take();
            Ok("шаблон сменился на новый ID", again.Doh.Length == 4 && again.Doh.All(d => d.Tpl == "https://dns.nextdns.io/zzz999"), string.Join(", ", again.Doh.Select(d => d.Tpl)));
        }
        finally
        {
            // 3. выключение
            var (offOk, offMsg) = await Dns.DisableAsync();
            Ok("выключение прошло", offOk, offMsg);
        }

        var after = Take();
        Say("после: " + Show(after.Rows));
        Ok("файл состояния убран", !Dns.IsOn());
        Ok("записи DoH убраны", after.Doh.Length == 0, string.Join(", ", after.Doh.Select(d => d.Ip)));
        foreach (var b in before.Rows)
        {
            var a = after.Rows.FirstOrDefault(r => r.I == b.I && r.F == b.F);
            Ok($"адаптер {b.Name}/{b.F}: способ задания DNS прежний ({(b.Stat ? "вручную" : "автоматически")})", a != null && a.Stat == b.Stat, a == null ? "нет" : (a.Stat ? "вручную" : "автоматически"));
            Ok($"адаптер {b.Name}/{b.F}: адреса прежние", a != null && a.A.OrderBy(x => x).SequenceEqual(b.A.OrderBy(x => x)), a == null ? "нет" : string.Join(",", a.A));
        }

        // 4. выключение, когда плагин ничего не включал (ничего не ломает)
        var (idleOk, idleMsg) = await Dns.DisableAsync();
        Ok("повторное выключение при выключенном ничего не ломает", idleOk, idleMsg);
        var last = Take();
        Ok("и ничего не меняет", last.Rows.Length == after.Rows.Length && last.Rows.All(r => after.Rows.Any(x => x.I == r.I && x.F == r.F && x.Stat == r.Stat && x.A.OrderBy(y => y).SequenceEqual(r.A.OrderBy(y => y)))));

        Say(bad == 0 ? "ВСЕ ПРОВЕРКИ ПРОШЛИ" : $"НЕ ПРОШЛО ПРОВЕРОК: {bad}");
        File.WriteAllText(Path.Combine(dir, "report.txt"), log.ToString());
        return bad == 0 ? 0 : 1;
    }
}
