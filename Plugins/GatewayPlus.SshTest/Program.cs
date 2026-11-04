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

Say(bad == 0 ? "ВСЕ ПРОВЕРКИ ПРОШЛИ" : $"ПРОВАЛЕНО: {bad}");
File.WriteAllText(Path.Combine(outDir, "report.txt"), report.ToString());
return bad == 0 ? 0 : 1;
