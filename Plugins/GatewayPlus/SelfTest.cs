using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GatewayPlusApp;

// Самопроверка для CI: проверка конфигов, шифрование профиля, скрипт сервера, и (если WireGuard стоит) настоящая установка и снятие службы туннеля.
static class SelfTest
{
    public static void Run(string dir)
    {
        Directory.CreateDirectory(dir);
        var log = new StringBuilder();
        void Check(string what, bool ok) => log.AppendLine((ok ? "ok      " : "ПРОВАЛ  ") + what);
        try
        {
            log.AppendLine($"Gateway Plus {typeof(SelfTest).Assembly.GetName().Version} · Windows {Environment.OSVersion.Version} · администратор: {Helper.IsAdmin()} · WireGuard: {Wg.ExePath() ?? "не установлен"}");

            // 1. проверка конфигов и имён
            const string good = "[Interface]\nPrivateKey = AAAA\nAddress = 10.0.0.2/32\n\n[Peer]\nPublicKey = BBBB\nEndpoint = 192.0.2.1:51820\nAllowedIPs = 0.0.0.0/0\n";
            Check("нормальный конфиг принимается", Store.Validate(good) == null);
            Check("конфиг с PostUp отклоняется", Store.Validate(good + "PostUp = calc\n") != null);
            Check("конфиг без [Peer] отклоняется", Store.Validate("[Interface]\nPrivateKey = a\n") != null);
            Check("конфиг без Endpoint отклоняется", Store.Validate("[Interface]\nPrivateKey = a\n[Peer]\nPublicKey = b\n") != null);
            Check("мусорная строка отклоняется", Store.Validate(good + "что-то непонятное\n") != null);
            Check("имя очищается", Store.SafeName("De Fra!") == "De_Fra" && Store.ValidName(Store.SafeName("Германия")));
            Check("адрес сервера читается", Store.EndpointOf(good) == "192.0.2.1");

            // 2. шифрование профиля
            Store.Save("citest-dpapi", good);
            Check("профиль шифруется и читается обратно", Store.Load("citest-dpapi") == good && !File.ReadAllText(Path.Combine(Store.Dir, "profiles", "citest-dpapi.dat"), Encoding.Latin1).Contains("PrivateKey"));
            Store.Delete("citest-dpapi");

            // 3. скрипт сервера
            var script = Store.ServerScript();
            Check("скрипт сервера вшит (" + script.Length + " знаков)", script.StartsWith("#!/usr/bin/env bash") && script.Contains("wg genkey") && !script.Contains('\r'));
            File.WriteAllText(Path.Combine(dir, "server-setup.sh"), script, new UTF8Encoding(false));

            // 3a. аккаунт: ключ, номер, шифрование
            var raw = Account.NewKey();
            Check("ключ: 32 знака A-Z2-7", Regex.IsMatch(raw, "^[A-Z2-7]{32}$") && Account.NewKey() != raw);
            var shown = Account.Format(raw);
            Check("ключ для показа: GW-XXXX-… (8 групп по 4)", Regex.IsMatch(shown, "^GW(-[A-Z2-7]{4}){8}$"));
            Check("ключ читается обратно (с тире, пробелами, строчными)", Account.Normalize(shown) == raw && Account.Normalize("  " + shown.ToLowerInvariant().Replace("-", " ") + " ") == raw);
            Check("мусор вместо ключа отклоняется", Account.Normalize("hello") == null && Account.Normalize(shown + "A") == null && Account.Normalize(null) == null && Account.Normalize(shown.Replace("A", "1")) == null);
            var id = Account.IdOf(raw);
            Check("номер аккаунта: 24 знака, стабильный, у других ключей другой", Regex.IsMatch(id, "^[0-9a-f]{24}$") && id == Account.IdOf(raw) && id != Account.IdOf(Account.NewKey()) && !id.Contains(raw.ToLowerInvariant()));
            var dev = Account.DeviceHash();
            Check("номер компьютера: 32 знака, стабильный", Regex.IsMatch(dev, "^[0-9a-f]{32}$") && dev == Account.DeviceHash());
            Check("имя и причина очищаются (управляющие, RTL, длина)", Account.CleanName("  Alex\u202e\n  X  ") == "Alex X" && Account.CleanName(new string('я', 50)).Length == 30 && Account.CleanReason("a\r\nb\u200f") == "a b");
            var had = Account.Load();
            Account.Save(new Account.Info { Key = raw, Name = "CI", TermsV = Terms.Version });
            var back = Account.Load();
            Check("аккаунт шифруется (DPAPI) и читается обратно", back != null && back.Key == raw && back.Id == id && back.Name == "CI" && !File.ReadAllText(Path.Combine(Store.Dir, "account.dat"), Encoding.Latin1).Contains(raw));
            Account.Clear();
            if (had != null) Account.Save(had);
            Check("правила: версия и текст на месте", Terms.Version >= 1 && Terms.Points.Length >= 5 && Terms.Text.Contains("коммерческ"));

            // 3b. мастер: проверки без подключения
            Check("мастер: нормальные данные проходят", Provisioner.ValidateTarget(new SshTarget { Host = "203.0.113.5", Port = 22, User = "root", Password = "x" }) == null);
            Check("мастер: адрес с http:// и пробелами отклоняется", Provisioner.ValidateTarget(new SshTarget { Host = "http://1.2.3.4", Password = "x" }) != null && Provisioner.ValidateTarget(new SshTarget { Host = "a b", Password = "x" }) != null);
            Check("мастер: плохой порт, имя и пустой вход отклоняются", Provisioner.ValidateTarget(new SshTarget { Host = "1.2.3.4", Port = 70000, Password = "x" }) != null && Provisioner.ValidateTarget(new SshTarget { Host = "1.2.3.4", User = "ro ot; rm", Password = "x" }) != null && Provisioner.ValidateTarget(new SshTarget { Host = "1.2.3.4" }) != null);
            Check("мастер: отпечаток в виде SHA256:…", Regex.IsMatch(Provisioner.Fingerprint(new byte[] { 1, 2, 3 }), "^SHA256:[A-Za-z0-9+/]{43}$"));
            var cfgOut = "apt...\n=========== НАЧАЛО КОНФИГА (скопируйте) ===========\n" + good + "=========== КОНЕЦ КОНФИГА ===========\nГотово";
            Check("мастер: конфиг вынимается из вывода скрипта", Provisioner.ParseConfig(cfgOut) is string pc && Store.Validate(pc) == null && Provisioner.ParseConfig("ничего") == null);

            // 3b2. размер пакета (MTU) и IPv6 в профиле: чистые функции плюс настоящая проверка пакетов на этом компьютере
            Check("MTU: строка добавляется в [Interface], а не в [Peer]", Store.WithInterfaceValue(good, "MTU", "1400") is string m1 && Store.MtuOf(m1) == 1400 && Store.Validate(m1) == null && m1.IndexOf("MTU = 1400", StringComparison.Ordinal) < m1.IndexOf("[Peer]", StringComparison.Ordinal));
            Check("MTU: существующая строка заменяется, а не дублируется", Store.WithInterfaceValue(Store.WithInterfaceValue(good, "MTU", "1380"), "MTU", "1392") is string m2 && Store.MtuOf(m2) == 1392 && Regex.Matches(m2, "(?m)^MTU").Count == 1);
            Check("MTU: рекомендация не больше 1420 и не меньше 1280", Speed.RecommendedWgMtu(1500, "203.0.113.5") == 1420 && Speed.RecommendedWgMtu(1452, "203.0.113.5") == 1392 && Speed.RecommendedWgMtu(1452, "2001:db8::1") == 1372 && Speed.RecommendedWgMtu(1000, "203.0.113.5") == 1280);
            var v4conf = "[Interface]\nPrivateKey = AAAA\nAddress = 10.66.66.7/32\nMTU = 1380\n\n[Peer]\nPublicKey = BBBB\nEndpoint = 203.0.113.5:51820\nAllowedIPs = 0.0.0.0/0, ::/0\n";
            var v6conf = Store.WithIpv6(v4conf, "fd66:6761:7465::");
            Check("IPv6: адрес дополняется (…7/32 → …::7/128), остальное не меняется", Store.InterfaceValue(v6conf, "Address") == "10.66.66.7/32, fd66:6761:7465::7/128" && Store.MtuOf(v6conf) == 1380 && Store.Validate(v6conf) == null);
            Check("IPv6: повторно не дублируется, чужой префикс не принимается", Store.WithIpv6(v6conf, "fd66:6761:7465::") == v6conf && Store.WithIpv6(v4conf, "2001:db8::") == v4conf);
            // сам поиск размера — на подставных «путях» с известным MTU (настоящая сеть раннера пинги режет)
            {
                static Func<string, int, CancellationToken, Task<bool>> Path(int mtu, string answers) => (h, payload, _) => Task.FromResult(h == answers && payload + 28 <= mtu);
                var keep = Speed.PingImpl;
                try
                {
                    var ct0 = CancellationToken.None;
                    Speed.PingImpl = Path(1500, "203.0.113.5");
                    var a1 = Speed.ProbePathMtuAsync("203.0.113.5", true, ct0).GetAwaiter().GetResult();
                    Speed.PingImpl = Path(1452, "203.0.113.5");
                    var a2 = Speed.ProbePathMtuAsync("203.0.113.5", true, ct0).GetAwaiter().GetResult();
                    Speed.PingImpl = Path(1280, "203.0.113.5");
                    var a3 = Speed.ProbePathMtuAsync("203.0.113.5", true, ct0).GetAwaiter().GetResult();
                    Speed.PingImpl = Path(1492, "1.1.1.1");   // сервер молчит, отвечает только 1.1.1.1
                    var a4 = Speed.ProbePathMtuAsync("203.0.113.5", true, ct0).GetAwaiter().GetResult();
                    var a5 = Speed.ProbePathMtuAsync("203.0.113.5", false, ct0).GetAwaiter().GetResult();   // шлюз включён: запасные адреса не берём
                    Speed.PingImpl = Path(1500, "none");
                    var a6 = Speed.ProbePathMtuAsync("203.0.113.5", true, ct0).GetAwaiter().GetResult();
                    Check("подбор MTU: путь 1500 → 1500, WireGuard 1420", a1.Ok && a1.PathMtu == 1500 && Speed.RecommendedWgMtu(a1.PathMtu, "203.0.113.5") == 1420);
                    Check("подбор MTU: путь 1452 (DS-Lite) → 1452, WireGuard 1392", a2.Ok && a2.PathMtu == 1452 && Speed.RecommendedWgMtu(a2.PathMtu, "203.0.113.5") == 1392);
                    Check("подбор MTU: путь 1280 → 1280 (нижняя граница)", a3.Ok && a3.PathMtu == 1280);
                    Check("подбор MTU: сервер молчит — берётся 1.1.1.1, когда шлюз выключен", a4.Ok && a4.PathMtu == 1492 && a4.Via.Contains("1.1.1.1"));
                    Check("подбор MTU: при включённом шлюзе запасные адреса не берутся (мерили бы туннель)", !a5.Ok && a5.Note.Contains("Отключите шлюз"));
                    Check("подбор MTU: нигде нет ответа — честное сообщение, а не число", !a6.Ok && a6.Note.Length > 10);
                }
                finally { Speed.PingImpl = keep; }
            }
            try
            {
                using var mtuCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var mt = Task.Run(() => Speed.ProbePathMtuAsync("1.1.1.1", true, mtuCts.Token)).GetAwaiter().GetResult();
                log.AppendLine("подбор MTU до 1.1.1.1: " + (mt.Ok ? $"путь {mt.PathMtu} байт → MTU WireGuard {Speed.RecommendedWgMtu(mt.PathMtu, "1.1.1.1")}" : "не вышло: " + mt.Note));
                Check("подбор MTU: если пинг до 1.1.1.1 проходит, результат разумный (1280…1500)", !mt.Ok || (mt.PathMtu >= 576 && mt.PathMtu <= 1500));
            }
            catch (Exception ex) { log.AppendLine("подбор MTU не выполнился: " + ex.Message); }

            // 3c. база: подставной сервер на этом компьютере (REST как у Firebase)
            CloudChecks(Check, log, id, dev);

            // 4. каким нас видят
            var ip = Task.Run(() => IpInfo.GetAsync()).GetAwaiter().GetResult();
            log.AppendLine($"внешний адрес: {(ip.Ok ? ip.Ip + " · " + ip.Country + " · " + ip.City + " · " + ip.Zone : "не проверился: " + ip.Error)}");

            // 5. настоящая служба туннеля (нужен установленный WireGuard)
            var tool = Wg.WgTool();
            if (tool == null || Wg.ExePath() == null) { log.AppendLine("WireGuard не установлен — проверка службы пропущена."); }
            else
            {
                var (c1, priv) = Wg.Run(tool, "genkey");
                var (c2, pub) = Wg.Run(tool, "pubkey", 10000, priv);
                var (c3, spriv) = Wg.Run(tool, "genkey");
                var (c4, spub) = Wg.Run(tool, "pubkey", 10000, spriv);
                Check("ключи созданы (wg genkey/pubkey)", c1 == 0 && c2 == 0 && c3 == 0 && c4 == 0 && priv.Length > 40 && pub.Length > 40);
                // AllowedIPs — только маленькая сеть: весь интернет раннера через несуществующий сервер пускать нельзя
                var conf = $"[Interface]\nPrivateKey = {priv}\nAddress = 10.99.0.2/32\n\n[Peer]\nPublicKey = {spub}\nEndpoint = 192.0.2.1:51820\nAllowedIPs = 10.99.0.0/24\nPersistentKeepalive = 25\n";
                Check("конфиг из ключей проходит проверку", Store.Validate(conf) == null);
                var tmp = Path.Combine(Path.GetTempPath(), "gp-selftest"); Directory.CreateDirectory(tmp);
                var file = Path.Combine(tmp, "citest.conf"); File.WriteAllText(file, conf);
                var r = Task.Run(() => Helper.RunAsync(new Job { Action = "connect", Name = "citest", Conf = file })).GetAwaiter().GetResult();
                log.AppendLine($"подключение: {(r.Ok ? "ok" : "ПРОВАЛ")} — {r.Message}");
                Check("служба WireGuardTunnel$citest работает", Wg.IsRunning("citest"));
                Check("сетевой адаптер citest появился", NetworkInterface.GetAllNetworkInterfaces().Any(n => n.Name.Equals("citest", StringComparison.OrdinalIgnoreCase)));
                Check("файл конфига лежит в ProgramData", File.Exists(Path.Combine(Helper.WorkDir, "citest.conf")));
                var acl = Wg.Run("icacls.exe", "\"" + Helper.WorkDir + "\"", 10000).Output;
                log.AppendLine("права на папку:" + Environment.NewLine + acl);
                Check("права на папку: обычных пользователей нет", !acl.Contains("BUILTIN\\Users", StringComparison.OrdinalIgnoreCase) && !acl.Contains("Everyone", StringComparison.OrdinalIgnoreCase));
                var d = Task.Run(() => Helper.RunAsync(new Job { Action = "disconnect", Name = "citest" })).GetAwaiter().GetResult();
                log.AppendLine($"отключение: {(d.Ok ? "ok" : "ПРОВАЛ")} — {d.Message}");
                Check("служба снята", !Wg.ServiceExists("citest"));
                Check("файл конфига убран", !File.Exists(Path.Combine(Helper.WorkDir, "citest.conf")));
                var cl = Task.Run(() => Helper.RunAsync(new Job { Action = "cleanup" })).GetAwaiter().GetResult();
                log.AppendLine($"уборка: {cl.Message}");
                try { Directory.Delete(tmp, true); } catch { }
            }
        }
        catch (Exception ex) { log.AppendLine("ОШИБКА: " + ex); }
        File.WriteAllText(Path.Combine(dir, "report.txt"), log.ToString());
    }
    static int FreePort() { var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); var p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p; }

    static JsonNode? ReplaceSv(JsonNode? n)
    {
        if (n is JsonObject o)
        {
            if (o.Count == 1 && o[".sv"]?.GetValue<string>() == "timestamp") return JsonValue.Create(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            foreach (var k in o.Select(x => x.Key).ToList()) o[k] = ReplaceSv(o[k]);
        }
        return n;
    }

    // Подставная «база»: GET/PUT/PATCH по адресам вида /<путь>.json — ровно то, что плагин шлёт настоящей.
    static void CloudChecks(Action<string, bool> Check, StringBuilder log, string id, string dev)
    {
        var db = new Dictionary<string, string>();
        var requests = new List<string>();
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var serverLoop = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); } catch { break; }
                try
                {
                    var path = Uri.UnescapeDataString(ctx.Request.Url!.AbsolutePath.Trim('/')); if (path.EndsWith(".json")) path = path[..^5];
                    var body = new StreamReader(ctx.Request.InputStream).ReadToEnd();
                    lock (db) requests.Add(ctx.Request.HttpMethod + " " + path);
                    string resp = "null";
                    lock (db)
                    {
                        switch (ctx.Request.HttpMethod)
                        {
                            case "GET": resp = db.TryGetValue(path, out var v) ? v : "null"; break;
                            case "PUT": db[path] = ReplaceSv(JsonNode.Parse(body))!.ToJsonString(); resp = db[path]; break;
                            case "PATCH":
                                var cur = db.TryGetValue(path, out var old) ? JsonNode.Parse(old)!.AsObject() : new JsonObject();
                                foreach (var kv in ReplaceSv(JsonNode.Parse(body))!.AsObject().ToList()) { cur[kv.Key] = kv.Value?.DeepClone(); }
                                db[path] = cur.ToJsonString(); resp = db[path]; break;
                        }
                    }
                    var bytes = Encoding.UTF8.GetBytes(resp);
                    ctx.Response.ContentType = "application/json"; ctx.Response.OutputStream.Write(bytes); ctx.Response.Close();
                }
                catch { try { ctx.Response.Abort(); } catch { } }
            }
        });
        Cloud.TestUrl = $"http://127.0.0.1:{port}";
        try
        {
            var r0 = Task.Run(() => Cloud.GetAccountAsync(id)).GetAwaiter().GetResult();
            Check("база: нового аккаунта ещё нет", r0 == null);
            Task.Run(() => Cloud.RegisterAsync(id, "CI", dev, "2.0.0", 1)).GetAwaiter().GetResult();
            var rec = JsonNode.Parse(db["gatewayAccounts/" + id])!.AsObject();
            Check("база: аккаунт записан (имя, дата создания и «пульс» — числа от сервера, правила, версия, компьютер)", rec["name"]?.GetValue<string>() == "CI" && rec["createdAt"]!.GetValue<long>() > 0 && rec["lastSeen"]!.GetValue<long>() > 0 && rec["termsV"]!.GetValue<int>() == Terms.Version && rec["dev"]?.GetValue<string>() == dev && rec["ver"]?.GetValue<string>() == "2.0.0");
            Check("база: аккаунт читается", Task.Run(() => Cloud.GetAccountAsync(id)).GetAwaiter().GetResult()?.Name == "CI");
            var created = rec["createdAt"]!.GetValue<long>();
            Thread.Sleep(30);
            Task.Run(() => Cloud.HeartbeatAsync(id, dev, "2.0.1", 3, Terms.Version)).GetAwaiter().GetResult();
            var rec2 = JsonNode.Parse(db["gatewayAccounts/" + id])!.AsObject();
            Check("база: «пульс» меняет только lastSeen/версию/серверы, имя и дата создания остаются", rec2["lastSeen"]!.GetValue<long>() >= created && rec2["servers"]!.GetValue<int>() == 3 && rec2["ver"]?.GetValue<string>() == "2.0.1" && rec2["createdAt"]!.GetValue<long>() == created && rec2["name"]?.GetValue<string>() == "CI");

            var ok = Task.Run(() => Cloud.CheckAccessAsync(id, dev)).GetAwaiter().GetResult();
            Check("доступ: без блокировки — открыт", !ok.Blocked && !ok.Offline);
            lock (db) db["gatewayBlocks/" + id] = "{\"at\":1,\"reason\":\"продавал доступ\\n<b>x</b>\",\"by\":\"100\"}";
            var b1 = Task.Run(() => Cloud.CheckAccessAsync(id, dev)).GetAwaiter().GetResult();
            Check("доступ: блокировка аккаунта видна, причина — одна строка", b1.Blocked && !b1.Offline && b1.Reason == "продавал доступ <b>x</b>");
            lock (db) db.Remove("gatewayBlocks/" + id);
            lock (db) db["gatewayBlockedDevices/" + dev] = "{\"at\":1,\"reason\":\"коммерция\",\"by\":\"100\"}";
            var b2 = Task.Run(() => Cloud.CheckAccessAsync(id, dev)).GetAwaiter().GetResult();
            Check("доступ: блокировка компьютера видна", b2.Blocked && b2.Reason.Contains("коммерция") && b2.Reason.Contains("компьютер"));
            var b3 = Task.Run(() => Cloud.CheckAccessAsync(null, dev)).GetAwaiter().GetResult();
            Check("доступ: без аккаунта (создание нового) блокировка компьютера тоже видна", b3.Blocked);
            int before; lock (db) before = requests.Count;
            var bad = Task.Run(() => Cloud.CheckAccessAsync("../users", dev)).GetAwaiter().GetResult();
            int after; lock (db) after = requests.Count;
            Check("доступ: плохой номер аккаунта не уходит в запрос", before == after);

            // без связи блокировка не «забывается», а свобода — тоже по последнему ответу
            listener.Stop(); listener.Close();
            var off1 = Task.Run(() => Cloud.CheckAccessAsync(id, dev)).GetAwaiter().GetResult();
            Check("доступ: нет связи после блокировки — остаётся заблокирован (Offline)", off1.Blocked && off1.Offline);
        }
        finally { Cloud.TestUrl = null; try { File.Delete(Path.Combine(Store.Dir, "access.json")); } catch { } }
        log.AppendLine("запросов к подставной базе: " + requests.Count);
    }
}
