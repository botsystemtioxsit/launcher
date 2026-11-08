using System.Text;
using System.Text.Json;
using GatewayPlusApp;

// Проверка мастера против настоящего sshd. Запуск: dotnet run -- --host 127.0.0.1 --port 22 --user runner --key <файл> --fps "SHA256:…,SHA256:…" --out <папка>
string Arg(string name, string def = "") { var i = Array.IndexOf(args, "--" + name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : def; }
var host = Arg("host", "127.0.0.1"); var port = int.Parse(Arg("port", "22")); var user = Arg("user"); var key = Arg("key");
var fps = Arg("fps").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var outDir = Arg("out", "results"); Directory.CreateDirectory(outDir);
Provisioner.KnownHostsFile = Path.Combine(outDir, "known-hosts.json");
var report = new StringBuilder(); int bad = 0;
void Say(string s) { Console.WriteLine(s); report.AppendLine(s); }
void Check(string what, bool ok, string extra = "") { Say((ok ? "ok      " : "ПРОВАЛ  ") + what + (ok || extra.Length == 0 ? "" : "  → " + extra)); if (!ok) bad++; }
void ForgetHosts() { try { File.Delete(Provisioner.KnownHostsFile); } catch { } }

ProvisionResult Run(SshTarget t, Func<HostKeyQuestion, bool> ck, Func<string, bool> cr, List<string> log)
    => Provisioner.RunAsync(t, ck, cr, l => { lock (log) log.Add(l); }).GetAwaiter().GetResult();

Say($"Gateway Plus SSH-проверка · {host}:{port} · пользователь {user} · известные отпечатки: {string.Join(" ", fps)}");

// 1. недоступный порт
{
    var log = new List<string>();
    var r = Run(new SshTarget { Host = "127.0.0.1", Port = 1, User = user, Password = "x" }, _ => true, _ => true, log);
    Check("недоступный порт: понятная ошибка", !r.Ok && r.Error.Contains("связаться"), r.Error);
}

// 2. неверный пароль (и первое знакомство с сервером: отпечаток спрашивается, запоминается)
ForgetHosts();
HostKeyQuestion? firstQ = null; int asked = 0;
{
    var log = new List<string>();
    var r = Run(new SshTarget { Host = host, Port = port, User = user, Password = "definitely-wrong" }, q => { asked++; firstQ = q; return true; }, _ => true, log);
    Check("неверный пароль: понятная ошибка про вход", !r.Ok && r.Error.Contains("не принял вход"), r.Error);
    Check("первое подключение: отпечаток спросили один раз", asked == 1 && firstQ != null && !firstQ.Known && !firstQ.Changed);
    Check("отпечаток совпал с настоящим (ssh-keygen -lf)", firstQ != null && fps.Contains(firstQ.Fingerprint), firstQ?.Fingerprint ?? "—");
    Check("отпечаток запомнен в known-hosts", File.Exists(Provisioner.KnownHostsFile) && File.ReadAllText(Provisioner.KnownHostsFile).Contains("SHA256:"));
}

// 3. знакомый сервер — без вопроса
{
    int a2 = 0; var log = new List<string>();
    var r = Run(new SshTarget { Host = host, Port = port, User = user, Password = "definitely-wrong" }, _ => { a2++; return true; }, _ => true, log);
    Check("знакомый сервер: отпечаток больше не спрашивают", a2 == 0 && log.Any(l => l.Contains("знакомый")), string.Join(" | ", log));
}

// 4. отпечаток изменился — отдельное предупреждение
{
    var d = new Dictionary<string, string> { [host.ToLowerInvariant() + ":" + port] = "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" };
    File.WriteAllText(Provisioner.KnownHostsFile, JsonSerializer.Serialize(d));
    HostKeyQuestion? q = null; var log = new List<string>();
    var r = Run(new SshTarget { Host = host, Port = port, User = user, Password = "x" }, qq => { q = qq; return false; }, _ => true, log);
    Check("изменившийся отпечаток: вопрос с пометкой Changed, отказ останавливает подключение", q is { Changed: true, Known: true } && !r.Ok && r.Error.Contains("не подтвердили"), r.Error);
}

// 5. отказ подтвердить отпечаток на новом сервере
ForgetHosts();
{
    var log = new List<string>();
    var r = Run(new SshTarget { Host = host, Port = port, User = user, Password = "x" }, _ => false, _ => true, log);
    Check("отказ от отпечатка: подключение отменено, отпечаток не запомнен", !r.Ok && r.Error.Contains("не подтвердили") && !File.Exists(Provisioner.KnownHostsFile), r.Error);
}

// 6. настоящая установка по ключу (вход под обычным пользователем с sudo)
ForgetHosts();
string clientPriv = "";
{
    var log = new List<string>();
    int replaceAsked = 0;
    var r = Run(new SshTarget { Host = host, Port = port, User = user, KeyFile = key }, _ => true, _ => { replaceAsked++; return true; }, log);
    Say("--- журнал мастера ---"); foreach (var l in log) Say("  " + l); Say("--- конец журнала ---");
    Check("установка по ключу: успех", r.Ok, r.Error);
    if (r.Ok)
    {
        Check("конфиг проходит проверку плагина", Store.Validate(r.Conf) == null);
        Check("в конфиге есть адрес сервера (Endpoint)", r.Endpoint.Length > 0, r.Endpoint);
        Check("система определена", r.Os.StartsWith("ubuntu") || r.Os.StartsWith("debian"), r.Os);
        Check("вошли не под root: использован sudo", log.Any(l => l.Contains("sudo")));
        clientPriv = r.Conf.Replace("\r", "").Split('\n').First(l => l.TrimStart().StartsWith("PrivateKey")).Split('=', 2)[1].Trim();
        var all = string.Join("\n", log);
        Check("закрытый ключ клиента не попал в журнал мастера", clientPriv.Length > 30 && !all.Contains(clientPriv) && !all.Contains("PrivateKey"));
        File.WriteAllText(Path.Combine(outDir, "client.conf"), r.Conf);
        Check("мастер не спрашивал про перенастройку на чистом сервере", replaceAsked == 0);
    }
}

// 7. повторный запуск на уже настроенном сервере — спрашивает, отказ ничего не трогает
{
    var log = new List<string>(); int replaceAsked = 0;
    var r = Run(new SshTarget { Host = host, Port = port, User = user, KeyFile = key }, _ => true, m => { replaceAsked++; return false; }, log);
    Check("уже настроенный сервер: спросили про перенастройку, отказ — «Отменено»", replaceAsked == 1 && !r.Ok && r.Error.Contains("Отменено"), r.Error);
}

// 8. «ещё одно устройство» (телефон): отдельный ключ на том же сервере, первый конфиг остаётся рабочим
{
    var log = new List<string>();
    var t = new SshTarget { Host = host, Port = port, User = user, KeyFile = key };
    var r = DeviceAdder.RunAsync(t, "phone", _ => true, l => { lock (log) log.Add(l); }).GetAwaiter().GetResult();
    Say("--- журнал «ещё одно устройство» ---"); foreach (var l in log) Say("  " + l); Say("--- конец журнала ---");
    Check("второе устройство: успех", r.Ok, r.Error);
    if (r.Ok)
    {
        Check("конфиг второго устройства проходит проверку плагина", Store.Validate(r.Conf) == null);
        string Field(string conf, string name) => conf.Replace("\r", "").Split('\n').First(l => l.TrimStart().StartsWith(name)).Split('=', 2)[1].Trim();
        var phonePriv = Field(r.Conf, "PrivateKey");
        Check("у второго устройства СВОЙ закрытый ключ", phonePriv.Length > 30 && phonePriv != clientPriv);
        var first = File.Exists(Path.Combine(outDir, "client.conf")) ? File.ReadAllText(Path.Combine(outDir, "client.conf")) : "";
        if (first.Length > 0)
        {
            Check("у второго устройства СВОЙ адрес внутри VPN", Field(r.Conf, "Address") != Field(first, "Address"), Field(r.Conf, "Address") + " / " + Field(first, "Address"));
            Check("ключ сервера у обоих устройств один и тот же", Field(r.Conf, "PublicKey") == Field(first, "PublicKey"));
        }
        var all = string.Join("\n", log);
        Check("закрытый ключ второго устройства не попал в журнал", !all.Contains(phonePriv) && !all.Contains("PrivateKey"));
        File.WriteAllText(Path.Combine(outDir, "phone.conf"), r.Conf);
    }
    var bad2 = DeviceAdder.RunAsync(t, "bad name;rm -rf /", _ => true, _ => { }).GetAwaiter().GetResult();
    Check("название устройства с лишними знаками отклонено до подключения", !bad2.Ok && bad2.Error.Contains("Название устройства"), bad2.Error);
}

// 9. «ускорить сервер»: правило размера пакетов появилось в настройках сервера, повторный запуск ничего не дублирует
{
    var log = new List<string>();
    var t = new SshTarget { Host = host, Port = port, User = user, KeyFile = key };
    var r = DeviceAdder.TuneAsync(t, _ => true, l => { lock (log) log.Add(l); }).GetAwaiter().GetResult();
    Say("--- журнал «ускорить сервер» ---"); foreach (var l in log) Say("  " + l); Say("--- конец журнала ---");
    Check("ускорение сервера: успех", r.Ok, r.Error);
    Check("в журнале есть итог про скорость сервера", log.Any(l => l.Contains("Скорость самого сервера")));
    var r2 = DeviceAdder.TuneAsync(t, _ => true, _ => { }).GetAwaiter().GetResult();
    Check("повторный запуск: успех", r2.Ok, r2.Error);
}

// 10. быстрая пересылка UDP и IPv6 в туннеле: в журнале есть строки про оба; повторный запуск ничего не ломает
{
    var log = new List<string>();
    var t = new SshTarget { Host = host, Port = port, User = user, KeyFile = key };
    var r = DeviceAdder.TuneAsync(t, _ => true, l => { lock (log) log.Add(l); }).GetAwaiter().GetResult();
    Check("ускорение: в журнале есть строка про быструю пересылку UDP", log.Any(l => l.Contains("Быстрая пересылка UDP")), string.Join(" | ", log));
    Check("ускорение: в журнале есть строка про IPv6 в туннеле", log.Any(l => l.Contains("IPv6 в туннеле")), string.Join(" | ", log));
    Check("у сервера без своего IPv6 туннель остаётся как был (префикс не выдаётся)", r.Ok && (r.Ipv6Prefix.Length == 0 || log.Any(l => l.Contains("включён"))), r.Error);
    string Sh(string c) { var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("bash", new[] { "-c", c }) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!; var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd(); p.WaitForExit(30000); return o; }
    Say("--- wg0.conf после ускорения (PostUp/Address) ---"); Say(Sh("sudo grep -E '^(Address|PostUp|PostDown)' /etc/wireguard/wg0.conf | sed 's/PrivateKey.*//'"));
    Check("правило подгонки пакетов осталось в одном экземпляре (IPv4)", Sh("sudo grep -c '^PostUp = iptables .*TCPMSS' /etc/wireguard/wg0.conf").Trim() == "1");
}

// 11. принудительный IPv6 (на тестовой машине своего IPv6 нет): сервер либо включает его целиком, либо откатывает всё как было — VPN в любом случае жив
{
    var log = new List<string>();
    var t = new SshTarget { Host = host, Port = port, User = user, KeyFile = key };
    var r = DeviceAdder.TuneAsync(t, _ => true, l => { lock (log) log.Add(l); }, default, "GS_FORCE_IPV6=1").GetAwaiter().GetResult();
    Say("--- журнал «ускорить сервер» (IPv6 принудительно) ---"); foreach (var l in log) Say("  " + l); Say("--- конец журнала ---");
    string Sh2(string c) { var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("bash", new[] { "-c", c }) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!; var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd(); p.WaitForExit(30000); return o; }
    Check("принудительный IPv6: скрипт завершился успешно", r.Ok, r.Error);
    var up = Sh2("sudo wg show wg0 > /dev/null 2>&1 && echo UP || echo DOWN").Contains("UP");
    Check("после этого туннель на сервере работает (wg show wg0)", up);
    var v6 = r.Ipv6Prefix.Length > 0;
    Say("IPv6 включился: " + (v6 ? "да, префикс " + r.Ipv6Prefix : "нет (откат): " + string.Join(" | ", log.Where(l => l.Contains("IPv6")))));
    if (v6)
    {
        Check("адрес IPv6 стоит на wg0", Sh2("ip -6 addr show dev wg0").Contains("fd66:6761:7465::1"));
        Check("у всех устройств в wg0.conf появился IPv6-адрес", Sh2("sudo grep -c '^AllowedIPs = .*fd66:6761:7465::' /etc/wireguard/wg0.conf").Trim() == "2", Sh2("sudo grep '^AllowedIPs' /etc/wireguard/wg0.conf"));
        var log2 = new List<string>();
        var r2 = DeviceAdder.RunAsync(t, "tablet", _ => true, l => { lock (log2) log2.Add(l); }).GetAwaiter().GetResult();
        Check("новое устройство после включения IPv6 получает и IPv6-адрес", r2.Ok && r2.Conf.Contains("fd66:6761:7465::") && Store.Validate(r2.Conf) == null, r2.Error);
        var first = File.Exists(Path.Combine(outDir, "client.conf")) ? File.ReadAllText(Path.Combine(outDir, "client.conf")) : "";
        if (first.Length > 0)
        {
            var upd = Store.WithIpv6(first, r.Ipv6Prefix);
            Check("профиль компьютера дополняется IPv6-адресом, как у сервера", upd != first && Store.InterfaceValue(upd, "Address").Contains("fd66:6761:7465::2/128"), Store.InterfaceValue(upd, "Address"));
        }
        var r3 = DeviceAdder.TuneAsync(t, _ => true, _ => { }, default, "GS_FORCE_IPV6=1").GetAwaiter().GetResult();
        Check("повторное ускорение при включённом IPv6 ничего не ломает и не дублирует", r3.Ok && Sh2("sudo grep -c '^PostUp = ip6tables -A FORWARD' /etc/wireguard/wg0.conf").Trim() == "1");
    }
    else Check("если IPv6 не включился, это был аккуратный откат (в журнале сказано), а не поломка", log.Any(l => l.Contains("не получилось")) && up);
}

// 11. тот же мастер ставит наш протокол GSP на тот же сервер (WireGuard рядом остаётся рабочим)
var gspBin = Arg("gsp-bin");
if (gspBin.Length > 0)
{
    string Sh3(string cmd) { var psi = new System.Diagnostics.ProcessStartInfo("bash", "-c \"" + cmd.Replace("\"", "\\\"") + "\"") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false }; using var p = System.Diagnostics.Process.Start(psi)!; var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd(); p.WaitForExit(); return o; }
    var env = $"GSP_BIN={gspBin} GSP_PUBIP=127.0.0.1";
    // обновление там, где GSP ещё не поставлен: ничего не трогаем, ошибка понятная; для WireGuard обновление не бывает
    {
        var ln = new List<string>();
        var rn = Run(new SshTarget { Host = host, Port = port, User = user, KeyFile = key, Protocol = "gsp", Update = true, ScriptEnv = $"GSP_BIN={gspBin}" }, _ => true, _ => true, ln);
        Check("GSP: обновление на сервере без GSP — отказ с понятной причиной, служба не появилась", !rn.Ok && rn.Error.Contains("не настроен") && Sh3("test -f /etc/gsp/gsp0.conf && echo yes || echo no").Trim() == "no", rn.Error);
        Check("обновление для WireGuard отклоняется до подключения", Provisioner.ValidateTarget(new SshTarget { Host = host, Port = port, User = user, KeyFile = key, Protocol = "wg", Update = true }) is { } vm && vm.Contains("только для GSP"));
    }
    var log = new List<string>(); int replaceAsked = 0;
    var r = Run(new SshTarget { Host = host, Port = port, User = user, KeyFile = key, Protocol = "gsp", ScriptEnv = env }, _ => true, _ => { replaceAsked++; return true; }, log);
    Say("--- журнал мастера (GSP) ---"); foreach (var l in log) Say("  " + l); Say("--- конец журнала ---");
    Check("GSP: установка мастером: успех", r.Ok, r.Error);
    if (r.Ok)
    {
        Check("GSP: конфиг — профиль GSP и проходит проверку плагина", Gsp.IsGsp(r.Conf) && Store.Validate(r.Conf) == null && r.Protocol == "gsp");
        Check("GSP: профиль, выданный сервером, — версия протокола 2", Gsp.ProtoOf(r.Conf) == 2);
        Check("GSP: в конфиге адрес и порт сервера", r.Conf.Contains("Endpoint = 127.0.0.1:51821"), r.Endpoint);
        Check("GSP: мастер не спрашивал про перенастройку на чистом сервере", replaceAsked == 0);
        var priv = r.Conf.Replace("\r", "").Split('\n').First(l => l.TrimStart().StartsWith("PrivateKey")).Split('=', 2)[1].Trim();
        var all = string.Join("\n", log);
        Check("GSP: закрытый ключ клиента не попал в журнал мастера", priv.Length > 30 && !all.Contains(priv) && !all.Contains("PrivateKey"));
        File.WriteAllText(Path.Combine(outDir, "client-gsp.conf"), r.Conf);
        Check("GSP: служба gsp работает, порт 51821/udp слушается", Sh3("systemctl is-active gsp").Trim() == "active" && Sh3("sudo ss -ulnp | grep -c 51821").Trim() == "1");
        Check("GSP: WireGuard рядом остался рабочим", Sh3("sudo wg show wg0 | head -1").Contains("interface"), Sh3("sudo wg show wg0 | head -3"));
        Check("GSP: следов скрипта в /tmp не осталось", Sh3("ls /tmp/ghostshell-gateway-* 2>&1").Contains("No such file"));
    }
    // повторный запуск: мастер спрашивает про перенастройку, отказ ничего не трогает
    var log2 = new List<string>(); int asked2 = 0;
    var r2 = Run(new SshTarget { Host = host, Port = port, User = user, KeyFile = key, Protocol = "gsp", ScriptEnv = env }, _ => true, m => { asked2++; return false; }, log2);
    // обновление программы GSP на настроенном сервере: ключи и настройки те же, служба работает, конфиг не выдаётся
    {
        var before = Sh3("sudo sha256sum /etc/gsp/gsp0.conf").Split(' ')[0];
        var lu = new List<string>(); int askedU = 0;
        var ru = Run(new SshTarget { Host = host, Port = port, User = user, KeyFile = key, Protocol = "gsp", Update = true, ScriptEnv = $"GSP_BIN={gspBin}" }, _ => true, _ => { askedU++; return true; }, lu);
        Say("--- журнал обновления GSP ---"); foreach (var l in lu) Say("  " + l); Say("--- конец журнала ---");
        Check("GSP: обновление через мастер: успех, спрашивать про перенастройку не пришлось, конфиг не выдаётся", ru.Ok && askedU == 0 && ru.Conf.Length == 0, ru.Error);
        Check("GSP: после обновления ключи и настройки на сервере те же, служба работает", before.Length == 64 && Sh3("sudo sha256sum /etc/gsp/gsp0.conf").Split(' ')[0] == before && Sh3("systemctl is-active gsp").Trim() == "active");
        Check("GSP: после обновления следов скрипта в /tmp нет", Sh3("ls /tmp/ghostshell-gateway-* 2>&1").Contains("No such file"));
    }
    Check("GSP: уже настроенный сервер: спросили, отказ — «Отменено»", asked2 == 1 && !r2.Ok && r2.Error.Contains("Отменено") && Sh3("systemctl is-active gsp").Trim() == "active", r2.Error);
}

// «Проверить доступ» (окно «Сервер»): только заходит и смотрит систему, ничего не меняет и не оставляет на сервере
{
    ForgetHosts();
    var log = new List<string>();
    var r = Provisioner.TestAsync(new SshTarget { Host = host, Port = port, User = user, KeyFile = key }, _ => true, l => { lock (log) log.Add(l); }).GetAwaiter().GetResult();
    Check("проверка доступа по ключу: успех, система и отпечаток определены", r.Ok && r.Os.Length > 3 && r.Fingerprint.StartsWith("SHA256:") && !r.AuthFailed, r.Error + " " + r.Os);
    var w = Provisioner.TestAsync(new SshTarget { Host = host, Port = port, User = user, Password = "definitely-wrong" }, _ => true, _ => { }).GetAwaiter().GetResult();
    Check("проверка доступа: неверный пароль → AuthFailed (окно предложит ввести заново)", !w.Ok && w.AuthFailed && w.Error.Contains("не принял вход"), w.Error);
    var nc = Provisioner.TestAsync(new SshTarget { Host = "127.0.0.1", Port = 1, User = user, Password = "x" }, _ => true, _ => { }).GetAwaiter().GetResult();
    Check("проверка доступа: недоступный порт — не «неверный пароль»", !nc.Ok && !nc.AuthFailed && nc.Error.Contains("связаться"), nc.Error);
    ForgetHosts();
    var rf = Provisioner.TestAsync(new SshTarget { Host = host, Port = port, User = user, KeyFile = key }, _ => false, _ => { }).GetAwaiter().GetResult();
    Check("проверка доступа: отказ от отпечатка — подключение отменено, не «неверный пароль»", !rf.Ok && !rf.AuthFailed && rf.Error.Contains("не подтвердили"), rf.Error);
    var badT = Provisioner.TestAsync(new SshTarget { Host = "bad host", Port = port, User = user, KeyFile = key }, _ => true, _ => { }).GetAwaiter().GetResult();
    Check("проверка доступа: неверный адрес отклоняется до подключения", !badT.Ok && badT.Error.Contains("Адрес сервера"), badT.Error);
}

Say(bad == 0 ? "ВСЕ ПРОВЕРКИ ПРОШЛИ" : $"ПРОВАЛЕНО: {bad}");
File.WriteAllText(Path.Combine(outDir, "report.txt"), report.ToString());
return bad == 0 ? 0 : 1;
