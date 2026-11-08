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

            // 3-split. «Мимо шлюза»: арифметика сетей и подмена AllowedIPs
            SplitChecks(Check, log);

            // 3-servers. «Мои серверы»: профили собираются в серверы по адресу; сохранённый доступ шифруется и забывается
            ServersChecks(Check);

            // 3a. аккаунт: ключ, номер, шифрование
            var raw = Account.NewKey();
            Check("ключ: 32 знака A-Z2-7", Regex.IsMatch(raw, "^[A-Z2-7]{32}$") && Account.NewKey() != raw);
            var shown = Account.Format(raw);
            Check("ключ для показа: GW-XXXX-… (8 групп по 4)", Regex.IsMatch(shown, "^GW(-[A-Z2-7]{4}){8}$"));
            Check("ключ читается обратно (с тире, пробелами, строчными)", Account.Normalize(shown) == raw && Account.Normalize("  " + shown.ToLowerInvariant().Replace("-", " ") + " ") == raw);
            Check("мусор вместо ключа отклоняется", Account.Normalize("hello") == null && Account.Normalize(shown + "A") == null && Account.Normalize(null) == null && Account.Normalize(shown[..3] + "1" + shown[4..]) == null);   // «1» в base32 не бывает; подмена первого знака всегда меняет ключ (Replace("A") не менял его, если «A» в случайном ключе не было)
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

            // 3d. GSP: профили и (если программа вложена в сборку) настоящая служба против ответчика на этом же компьютере
            GspChecks(Check, log, dir);

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
                var conf = $"[Interface]\nPrivateKey = {priv}\nAddress = 10.99.0.2/32\n\n[Peer]\nPublicKey = {spub}\nEndpoint = 192.0.2.1:51820\nAllowedIPs = 10.99.0.0/24, {ManyRoutes}\nPersistentKeepalive = 25\n";
                Check("конфиг из ключей проходит проверку", Store.Validate(conf) == null);
                var tmp = Path.Combine(Path.GetTempPath(), "gp-selftest"); Directory.CreateDirectory(tmp);
                var file = Path.Combine(tmp, "citest.conf"); File.WriteAllText(file, conf);
                var r = Task.Run(() => Helper.RunAsync(new Job { Action = "connect", Name = "citest", Conf = file })).GetAwaiter().GetResult();
                log.AppendLine($"подключение: {(r.Ok ? "ok" : "ПРОВАЛ")} — {r.Message}");
                Check("служба WireGuardTunnel$citest работает", Wg.IsRunning("citest"));
                Check("сетевой адаптер citest появился", NetworkInterface.GetAllNetworkInterfaces().Any(n => n.Name.Equals("citest", StringComparison.OrdinalIgnoreCase)));
                // «Мимо шлюза» даёт службе сотни сетей вместо 0.0.0.0/0: WireGuard должен принять и проложить их все (проверка на безопасном блоке 198.18.0.0/15)
                var wgRoutes = WaitRoutes("citest", 340);
                Check($"WireGuard: {ManyCount} маршрутов из AllowedIPs проложены ({wgRoutes})", wgRoutes >= ManyCount);
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
    // «Мои серверы» (Servers.cs) на настоящем DPAPI. Данные кладутся во временную папку (Store.DirOverride), настоящие профили не трогаются.
    static void ServersChecks(Action<string, bool> Check)
    {
        var prev = Store.DirOverride;
        var tmp = Path.Combine(Path.GetTempPath(), "gp-servers-" + Guid.NewGuid().ToString("N")[..8]);
        Store.DirOverride = tmp;
        try
        {
            const string wg = "[Interface]\nPrivateKey = AAAA\nAddress = 10.0.0.2/32\n\n[Peer]\nPublicKey = BBBB\nEndpoint = {0}:51820\nAllowedIPs = 0.0.0.0/0\n";
            const string gsp = "# GSP\n[Interface]\nPrivateKey = AAAA\nAddress = 10.78.0.2/32\nFEC = auto\n\n[Peer]\nPublicKey = BBBB\nEndpoint = {0}:51821\nAllowedIPs = 0.0.0.0/0\n";
            const string h1 = "203.0.113.5", h2 = "198.51.100.7";
            Store.Save("de-wg", string.Format(wg, h1));
            Store.Save("de-gsp", string.Format(gsp, h1));
            Store.Save("de-wg2", string.Format(wg, h1));
            Store.Save("fi", string.Format(wg, h2));
            Store.Active = "de-gsp";
            var rows = Servers.List();
            Check("серверы: четыре профиля на двух адресах → два сервера", rows.Count == 2);
            var de = rows.First(r => r.Info.Host == h1);
            var fi = rows.First(r => r.Info.Host == h2);
            Check("серверы: у первого есть WireGuard и GSP, лишний профиль отдельно", de.Wg is "de-wg" or "de-wg2" && de.Gsp == "de-gsp" && de.Extra.Count == 1 && de.All.Count() == 3);
            Check("серверы: у второго только WireGuard", fi.Wg == "fi" && fi.Gsp == null && fi.Extra.Count == 0);
            Check("серверы: номера вида s-xxxxxxxx и servers.json записан", rows.All(r => Regex.IsMatch(r.Info.Id, "^s-[0-9a-f]{8}$")) && File.Exists(Path.Combine(tmp, "servers.json")));
            var again = Servers.List();
            Check("серверы: номера не меняются между вызовами", again.Select(r => r.Info.Id).OrderBy(x => x).SequenceEqual(rows.Select(r => r.Info.Id).OrderBy(x => x)));

            Check("доступ: по умолчанию не сохранён", !de.Info.HasAccess && Servers.LoadAccess(de.Info.Id) == null && Servers.Find(h1) is { HasAccess: false });
            Servers.SaveAccess(de.Info.Id, new SavedAccess { Password = "S3cret-пароль" });
            var file = Path.Combine(tmp, "servers", de.Info.Id + ".dat");
            var back = Servers.LoadAccess(de.Info.Id);
            Check("доступ: пароль шифруется (в файле его нет) и читается обратно", back?.Password == "S3cret-пароль" && File.Exists(file) && !File.ReadAllText(file, Encoding.Latin1).Contains("S3cret") && !File.ReadAllText(Path.Combine(tmp, "servers.json")).Contains("S3cret") && Servers.Find(h1) is { HasAccess: true });
            var tgt = Servers.Target(Servers.Find(h1)!, out var problem);
            Check("доступ: данные для входа собираются (адрес, порт, логин, пароль)", tgt is { Host: h1, Port: 22, User: "root", Password: "S3cret-пароль" } && problem == null);
            Check("доступ: в словах — без самого пароля", !Servers.SavedSentence(Servers.Find(h1)!, back!).Contains("S3cret") && Servers.SavedSentence(Servers.Find(h1)!, back!).Contains("root"));

            // запомнить другой адрес для входа по SSH и порт; подпись не затирается
            Servers.Rename(de.Info.Id, "  Герм\u202eания\n ");
            var rem = Servers.Remember(h1, "de.example.com", 2222, "admin", "другое");
            Check("сервер: подпись очищена от управляющих знаков и не затирается при повторной записи", Servers.Label(rem) == "Герм ания" && rem.Id == de.Info.Id);
            Check("сервер: адрес и порт для SSH запоминаются, находится и по ним", rem.SshHost == "de.example.com" && rem.SshPort == 2222 && rem.SshUser == "admin" && Servers.Find("de.example.com") is { } f && f.Id == de.Info.Id);
            var tgt2 = Servers.Target(Servers.Find("de.example.com")!, out _);
            Check("доступ: после смены адреса SSH данные для входа берут новый адрес и порт", tgt2 is { Host: "de.example.com", Port: 2222, User: "admin" });

            // файл ключа: если файла нет, понятная причина; если есть — путь и парольная фраза
            Servers.SaveAccess(de.Info.Id, new SavedAccess { KeyFile = Path.Combine(tmp, "нет-такого-ключа"), KeyPass = "фраза" });
            var tk = Servers.Target(Servers.Find(h1)!, out var why);
            Check("доступ: ключ-файл пропал → входа нет, причина названа", tk == null && why != null && why.Contains("Файл ключа не найден"));
            var keyFile = Path.Combine(tmp, "id_test"); File.WriteAllText(keyFile, "stub");
            Servers.SaveAccess(de.Info.Id, new SavedAccess { KeyFile = keyFile, KeyPass = "фраза" });
            Check("доступ: ключ-файл на месте → путь и фраза отдаются", Servers.Target(Servers.Find(h1)!, out _) is { } kt && kt.KeyFile == keyFile && kt.KeyPass == "фраза" && string.IsNullOrEmpty(kt.Password));

            Servers.ForgetAccess(de.Info.Id);
            Check("доступ: «забыть» удаляет файл и снимает отметку", !File.Exists(file) && Servers.Find(h1) is { HasAccess: false } && Servers.LoadAccess(de.Info.Id) == null);

            // сервер без профилей забывается вместе с доступом; удаление сервера убирает его профили
            Servers.SaveAccess(fi.Info.Id, new SavedAccess { Password = "x" });
            var fiFile = Path.Combine(tmp, "servers", fi.Info.Id + ".dat");
            Store.Delete("fi");
            var after = Servers.List();
            Check("серверы: без профилей сервер исчезает из списка вместе с сохранённым доступом", after.Count == 1 && !File.Exists(fiFile) && Servers.Find(h2) == null);
            var deNow = after.First();
            Servers.Remove(deNow);
            Check("серверы: «Убрать сервер» удаляет все его профили, выбранный профиль не остаётся пустым", Store.List().Count == 0 && Servers.List().Count == 0 && Store.Active.Length == 0);

            // профиль, у которого адрес не читается, в список попадает, но в файл не пишется
            File.WriteAllBytes(Path.Combine(tmp, "profiles", "broken.dat"), new byte[] { 1, 2, 3 });
            var br = Servers.List();
            Check("серверы: нечитаемый профиль виден в списке отдельно, без номера", br.Count == 1 && br[0].Info.Id.Length == 0 && br[0].Info.Name == "broken");
            Check("подпись по умолчанию: «germany-gsp» → «germany»", Servers.SuggestName("germany-gsp") == "germany" && Servers.SuggestName("de_wg") == "de" && Servers.SuggestName("nl") == "nl");
        }
        catch (Exception ex) { Check("серверы: исключение " + ex.GetType().Name + ": " + ex.Message, false); }
        finally
        {
            Store.DirOverride = prev;
            try { Directory.Delete(tmp, true); } catch { }
        }
    }

    // «Мимо шлюза» (Split.cs): сети считаются верно, подмена AllowedIPs не ломает конфиг, ответ сети проверяется.
    static void SplitChecks(Action<string, bool> Check, StringBuilder log)
    {
        // арифметика: «всё, кроме 10.0.0.0/8» — ровно 8 сетей, 10.x не покрыто, остальное покрыто
        var c10 = Split.Complement(new[] { "10.0.0.0/8" }, false);
        Check("«всё, кроме 10.0.0.0/8» = 8 сетей (" + c10.Count + ")", c10.Count == 8 && !Split.Covers(c10, "10.1.2.3") && Split.Covers(c10, "9.255.255.255") && Split.Covers(c10, "11.0.0.0") && Split.Covers(c10, "0.0.0.0") && Split.Covers(c10, "255.255.255.255"));
        var c0 = Split.Complement(Array.Empty<string>(), false);
        Check("без исключений остаётся 0.0.0.0/0", c0.Count == 1 && c0[0] == "0.0.0.0/0");
        var cAll = Split.Complement(new[] { "0.0.0.0/0" }, false);
        Check("исключили всё — пусто", cAll.Count == 0);
        var edge = Split.Complement(new[] { "0.0.0.0/32", "255.255.255.255/32" }, false);
        Check("края адресного пространства (0.0.0.0 и 255.255.255.255) исключаются точно", !Split.Covers(edge, "0.0.0.0") && !Split.Covers(edge, "255.255.255.255") && Split.Covers(edge, "0.0.0.1") && Split.Covers(edge, "255.255.255.254"));
        var m = Split.Merge(new[] { "10.0.0.0/25", "10.0.0.128/25", "10.0.1.0/24", "10.0.0.0/30" }, false);
        Check("соседние и вложенные сети склеиваются (10.0.0.0/23)", m.Count == 1 && m[0] == "10.0.0.0/23");
        var c6 = Split.Complement(new[] { "2a03:2880::/32" }, true);
        Check("IPv6: «всё, кроме 2a03:2880::/32»", c6.Count > 0 && !Split.Covers(c6, "2a03:2880:f101::1") && Split.Covers(c6, "2001:4860:4860::8888") && Split.Covers(c6, "2a03:2881::1") && Split.Covers(c6, "::1"));

        // подмена в конфиге
        const string conf = "[Interface]\nPrivateKey = AAAA\nAddress = 10.0.0.2/32\n\n[Peer]\nPublicKey = BBBB\nEndpoint = 192.0.2.1:51820\nAllowedIPs = 0.0.0.0/0\n";
        var ep = new[] { System.Net.IPAddress.Parse("192.0.2.1") };
        var applied = Split.Apply(conf, new[] { "whatsapp" }, ep);
        var allowed = Store.InterfaceValueOfPeer(applied, "AllowedIPs").Split(',').Select(x => x.Trim()).ToList();
        Check("подмена: 0.0.0.0/0 исчез, сетей " + allowed.Count, !allowed.Contains("0.0.0.0/0") && allowed.Count > 100 && allowed.Count < 800);
        Check("подмена: WhatsApp (157.240.1.1, 31.13.65.1) и адрес сервера (192.0.2.1) вне туннеля", !Split.Covers(allowed, "157.240.1.1") && !Split.Covers(allowed, "31.13.65.1") && !Split.Covers(allowed, "192.0.2.1"));
        Check("подмена: обычные адреса (8.8.8.8, 1.1.1.1, 192.168.1.5, 10.0.0.1) в туннеле", new[] { "8.8.8.8", "1.1.1.1", "192.168.1.5", "10.0.0.1", "93.184.216.34" }.All(a => Split.Covers(allowed, a)));
        Check("подмена: WireGuard-конфиг после неё проходит проверку", Store.Validate(applied) == null && applied.Length < 20000);
        var gspConf = "# GSP\n[Interface]\nPrivateKey = " + new string('A', 43) + "=\nAddress = 10.78.0.2/32\nFEC = auto\n\n[Peer]\nPublicKey = " + new string('B', 43) + "=\nEndpoint = 192.0.2.1:51821\nAllowedIPs = 0.0.0.0/0\n";
        var gspApplied = Split.Apply(gspConf, new[] { "whatsapp" }, ep);
        Check("подмена: GSP-конфиг после неё проходит проверку", gspApplied != gspConf && Gsp.Validate(gspApplied) == null && Store.Validate(gspApplied) == null);
        Check("без выбранных сервисов конфиг не меняется", Split.Apply(conf, Array.Empty<string>(), ep) == conf);
        var partial = conf.Replace("0.0.0.0/0", "10.0.0.0/24");
        Check("если в туннель идёт не «всё» — конфиг не меняется", Split.Apply(partial, new[] { "whatsapp" }, ep) == partial);
        var v6conf = conf.Replace("AllowedIPs = 0.0.0.0/0", "AllowedIPs = 0.0.0.0/0, ::/0");
        var v6 = Store.InterfaceValueOfPeer(Split.Apply(v6conf, new[] { "whatsapp" }, ep), "AllowedIPs").Split(',').Select(x => x.Trim()).ToList();
        Check("0.0.0.0/0 и ::/0 заменяются оба (Meta по IPv6 — вне туннеля)", !v6.Contains("::/0") && !v6.Contains("0.0.0.0/0") && !Split.Covers(v6, "2a03:2880:f101::1") && Split.Covers(v6, "2001:4860:4860::8888"));
        Check("телефон: ExcludedApplications добавляется (com.whatsapp)", Split.ForPhone(conf, new[] { "whatsapp" }).Contains("ExcludedApplications = com.whatsapp,com.whatsapp.w4b") && Split.ForPhone(conf, Array.Empty<string>()) == conf);

        // ответ RIPEstat: настоящий — принимается, подделка — нет
        string Ripe(IEnumerable<string> ps) => "{\"data\":{\"prefixes\":[" + string.Join(",", ps.Select(x => "{\"prefix\":\"" + x + "\"}")) + "]}}";
        var many = Enumerable.Range(0, 40).Select(i => $"{40 + i}.{i}.0.0/16").Concat(new[] { "2a03:2880::/32" });   // не соседние сети: после склейки их остаётся достаточно
        Check("ответ сети: нормальный список принимается", Split.ParseRipe(Ripe(many)) != null);
        Check("ответ сети: «0.0.0.0/0» отклоняется (иначе шлюз перестал бы работать)", Split.ParseRipe(Ripe(many.Concat(new[] { "0.0.0.0/0" }))) == null);
        Check("ответ сети: слишком широкая сеть (/8) отклоняется", Split.ParseRipe(Ripe(many.Concat(new[] { "57.0.0.0/8" }))) == null);
        Check("ответ сети: слишком короткий список и мусор отклоняются", Split.ParseRipe(Ripe(new[] { "157.240.0.0/16" })) == null && Split.ParseRipe("{ не json") == null && Split.ParseRipe(Ripe(many.Concat(new[] { "evil" }))) == null);
        var meta = Split.CidrsOf("whatsapp");
        Check("вшитый список Meta: " + meta.Length + " сетей, в нём 157.240.0.0/17 и 2a03:2880::/32", meta.Length >= 30 && meta.Contains("157.240.0.0/17") && meta.Contains("2a03:2880::/32"));
    }

    static void GspChecks(Action<string, bool> Check, StringBuilder log, string dir)
    {
        var k = new string('A', 43) + "=";
        var good = $"# GSP\n[Interface]\nPrivateKey = {k}\nAddress = 10.78.0.2/32\nMTU = 1380\nDNS = 1.1.1.1, 9.9.9.9\nFEC = auto\n\n[Peer]\nPublicKey = {k}\nEndpoint = 203.0.113.5:51821\nAllowedIPs = 0.0.0.0/0\nPersistentKeepalive = 25\n";
        var wg = $"[Interface]\nPrivateKey = {k}\nAddress = 10.0.0.2/32\n\n[Peer]\nPublicKey = {k}\nEndpoint = 192.0.2.1:51820\nAllowedIPs = 0.0.0.0/0\n";
        Check("GSP: конфиг с «# GSP» узнаётся, WireGuard — нет", Gsp.IsGsp(good) && !Gsp.IsGsp(wg) && Gsp.IsGsp(wg.Replace("[Interface]\n", "[Interface]\nFEC = auto\n")));
        Check("GSP: нормальный конфиг принимается (и через Store.Validate)", Gsp.Validate(good) == null && Store.Validate(good) == null);
        Check("GSP: PostUp и неизвестные строки отклоняются", Gsp.Validate(good.Replace("FEC = auto", "PostUp = calc")) != null && Gsp.Validate(good.Replace("FEC = auto", "Table = off")) != null && Gsp.Validate(good + "что-то\n") != null);
        Check("GSP: плохие значения отклоняются (IPv6-адрес сервера, MTU, FEC, DNS, ключ)", Gsp.Validate(good.Replace("203.0.113.5:51821", "[2001:db8::1]:51821")) != null && Gsp.Validate(good.Replace("MTU = 1380", "MTU = 100")) != null && Gsp.Validate(good.Replace("FEC = auto", "FEC = fast")) != null && Gsp.Validate(good.Replace("1.1.1.1", "не-адрес")) != null && Gsp.Validate(good.Replace(k, "abc")) != null);
        Check("GSP: нужны Interface, Peer, оба ключа и Endpoint", Gsp.Validate("# GSP\n[Interface]\nPrivateKey = " + k + "\n") != null && Gsp.Validate(good.Replace("Endpoint = 203.0.113.5:51821\n", "")) != null);
        var named = Gsp.WithName(good, "GhostShell-x");
        Check("GSP: имя сетевой карты вставляется в [Interface] один раз", Store.InterfaceValue(named, "Name") == "GhostShell-x" && Gsp.WithName(named, "Other") is string n2 && Regex.Matches(n2, "(?m)^Name").Count == 1 && Gsp.Validate(named) == null);
        var okJson = "{\"version\":\"0.2.0\",\"port\":1,\"peers\":[{\"key\":\"k\",\"connected\":true,\"sessionAgeS\":5,\"lastRecvAgoS\":0,\"txBytes\":10,\"rxBytes\":20,\"recovered\":7,\"lossPct\":3.25,\"fec\":\"8+3\"}]}";
        var st = Gsp.ParseStatus(okJson);
        Check("GSP: состояние читается (потери, защита, восстановлено)", st is { Connected: true, Recovered: 7, Fec: "8+3" } && Math.Abs(st.LossPct - 3.25) < 0.001);
        Check("GSP: подпись состояния понятна", GspClient.Describe(st).Contains("потери сети 3,3%") || GspClient.Describe(st).Contains("потери сети 3.3%") || GspClient.Describe(st).Contains("потери сети 3,2%") || GspClient.Describe(st).Contains("потери сети 3.2%"));
        Check("GSP: ошибка службы и мусор в состоянии не ломают чтение", Gsp.ParseStatus("{\"error\":\"нет Peer\"}") is { Error: "нет Peer" } && Gsp.ParseStatus("не json") == null && Gsp.ParseStatus(null) == null && GspClient.Describe(null).Length > 5);
        var gscript = Store.GspServerScript();
        Check("GSP: скрипт сервера вшит (" + gscript.Length + " знаков)", gscript.StartsWith("#!/usr/bin/env bash") && gscript.Contains("gsp-linux-") && gscript.Contains("sha256sum") && !gscript.Contains('\r'));
        File.WriteAllText(Path.Combine(dir, "gsp-server-setup.sh"), gscript, new UTF8Encoding(false));
        Check("GSP: мастер не принимает чужой протокол и странные переменные", Provisioner.ValidateTarget(new SshTarget { Host = "1.2.3.4", Password = "x", Protocol = "ovpn" }) != null && Provisioner.ValidateTarget(new SshTarget { Host = "1.2.3.4", Password = "x", Protocol = "gsp", ScriptEnv = "A=b; rm -rf /" }) != null && Provisioner.ValidateTarget(new SshTarget { Host = "1.2.3.4", Password = "x", Protocol = "gsp", ScriptEnv = "GSP_BIN=/tmp/gsp GSP_PUBIP=10.0.0.1" }) == null);

        log.AppendLine("GSP вложен в сборку: " + GspClient.Available);
        if (!GspClient.Available) return;
        var prog = Path.Combine(Path.GetTempPath(), "gp-gsp-selftest");
        try
        {
            try { Directory.Delete(prog, true); } catch { }
            Check("GSP: программа и wintun.dll извлекаются", GspClient.ExtractTo(prog) == null && File.Exists(Path.Combine(prog, "gsp.exe")) && File.Exists(Path.Combine(prog, "wintun.dll")));
            var exe = Path.Combine(prog, "gsp.exe");
            var (cv, ver) = Wg.Run(exe, "version", 10000);
            Check("GSP: программа запускается (" + ver + ")", cv == 0 && ver.StartsWith("gsp"));
            var (c1, sk) = Wg.Run(exe, "genkey", 10000); var (c2, sp) = Wg.Run(exe, "pubkey", 10000, sk);
            var (c3, ck) = Wg.Run(exe, "genkey", 10000); var (c4, cp) = Wg.Run(exe, "pubkey", 10000, ck);
            Check("GSP: ключи создаются (genkey/pubkey)", c1 == 0 && c2 == 0 && c3 == 0 && c4 == 0 && sk.Length == 44 && sp.Length == 44 && ck.Length == 44 && cp.Length == 44);
            if (!Helper.IsAdmin()) { log.AppendLine("GSP: запущено не от администратора — проверка службы пропущена."); return; }

            // сервер — ответчик на ping (на этом же компьютере), клиент — настоящая служба Windows
            var psi = new System.Diagnostics.ProcessStartInfo(exe, $"echo-server -key {sk} -peer {cp} -allowed 10.77.0.2/32 -port 7110") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            using var echo = System.Diagnostics.Process.Start(psi)!;
            try
            {
                var first = echo.StandardOutput.ReadLine() ?? "";
                Check("GSP: ответчик запущен (" + first + ")", first.StartsWith("echo-server port=7110"));
                var conf = $"# GSP\n[Interface]\nPrivateKey = {ck}\nAddress = 10.77.0.2/32\nMTU = 1380\nFEC = auto\n\n[Peer]\nPublicKey = {sp}\nEndpoint = 127.0.0.1:7110\nAllowedIPs = 10.77.0.1/32, {ManyRoutes}\nPersistentKeepalive = 5\n";
                Check("GSP: настоящий конфиг проходит проверку", Store.Validate(conf) == null);
                var tmp = Path.Combine(Path.GetTempPath(), "gp-selftest-gsp"); Directory.CreateDirectory(tmp);
                var file = Path.Combine(tmp, "citest-gsp.conf"); File.WriteAllText(file, conf);
                var r = Task.Run(() => Helper.RunAsync(new Job { Action = "gsp-connect", Name = "citest-gsp", Conf = file })).GetAwaiter().GetResult();
                log.AppendLine($"GSP подключение: {(r.Ok ? "ok" : "ПРОВАЛ")} — {r.Message}");
                Check("GSP: служба работает", r.Ok && GspClient.IsRunning("citest-gsp"));
                Check("GSP: сетевая карта шлюза появилась", NetworkInterface.GetAllNetworkInterfaces().Any(n => n.Name.Equals("GhostShell-citest-gsp", StringComparison.OrdinalIgnoreCase)));
                var gspRoutes = WaitRoutes("GhostShell-citest-gsp", 340);
                Check($"GSP: {ManyCount} маршрутов из AllowedIPs проложены ({gspRoutes})", gspRoutes >= ManyCount);
                Check("GSP: конфиг и программа лежат в ProgramData", File.Exists(GspClient.ConfFile("citest-gsp")) && File.Exists(GspClient.ExePath));
                var acl = Wg.Run("icacls.exe", "\"" + Helper.WorkDir + "\"", 10000).Output;
                Check("GSP: права на папку: обычных пользователей нет (в ней закрытый ключ и программа службы)", !acl.Contains("BUILTIN\\Users", StringComparison.OrdinalIgnoreCase) && !acl.Contains("Everyone", StringComparison.OrdinalIgnoreCase));
                var ok = false;
                using (var ping = new Ping())
                    for (var i = 0; i < 8 && !ok; i++) { try { ok = ping.Send("10.77.0.1", 2000)?.Status == IPStatus.Success; } catch { } if (!ok) Thread.Sleep(500); }
                Check("GSP: ping 10.77.0.1 идёт через туннель", ok);
                Thread.Sleep(2500);
                var s = GspClient.ReadStatus("citest-gsp");
                log.AppendLine("GSP состояние: " + GspClient.Describe(s));
                Check("GSP: окно плагина читает состояние службы (соединено)", s is { Connected: true });
                var d = Task.Run(() => Helper.RunAsync(new Job { Action = "gsp-disconnect", Name = "citest-gsp" })).GetAwaiter().GetResult();
                log.AppendLine($"GSP отключение: {(d.Ok ? "ok" : "ПРОВАЛ")} — {d.Message}");
                Check("GSP: служба снята, карта и конфиг убраны", d.Ok && !GspClient.ServiceExists("citest-gsp") && !File.Exists(GspClient.ConfFile("citest-gsp")) && !NetworkInterface.GetAllNetworkInterfaces().Any(n => n.Name.Equals("GhostShell-citest-gsp", StringComparison.OrdinalIgnoreCase)));
                // «Вернуть как было» убирает и забытые службы GSP
                var r2 = Task.Run(() => Helper.RunAsync(new Job { Action = "gsp-connect", Name = "citest-gsp2", Conf = file })).GetAwaiter().GetResult();
                var cl = Task.Run(() => Helper.RunAsync(new Job { Action = "cleanup" })).GetAwaiter().GetResult();
                log.AppendLine($"GSP уборка: {cl.Message}");
                Check("GSP: «Вернуть как было» снимает забытую службу и убирает папку", r2.Ok && !GspClient.ServiceExists("citest-gsp2") && !Directory.Exists(GspClient.ProgDir));
                try { Directory.Delete(tmp, true); } catch { }
            }
            finally { try { if (!echo.HasExited) echo.Kill(true); } catch { } }
        }
        catch (Exception ex) { log.AppendLine("GSP: ОШИБКА проверки: " + ex); Check("GSP: проверка не упала с исключением", false); }
        finally { try { Directory.Delete(prog, true); } catch { } }
    }

    // Безопасный блок для проверки «сотни маршрутов»: 198.18.0.0/15 (для тестов сетей, в интернете не используется) — 340 сетей /24.
    const int ManyCount = 340;
    static string ManyRoutes => string.Join(", ", Enumerable.Range(0, ManyCount).Select(i => $"198.{18 + i / 256}.{i % 256}.0/24"));

    // Сколько маршрутов 198.18/15 лежит на сетевой карте (ждём, пока служба проложит их все).
    static int WaitRoutes(string alias, int want)
    {
        var best = -1;
        for (var i = 0; i < 40; i++)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe", $"-NoProfile -Command \"(Get-NetRoute -InterfaceAlias '{alias}' -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object {{ $_.DestinationPrefix -like '198.1[89].*' }} | Measure-Object).Count\"") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                using var p = System.Diagnostics.Process.Start(psi)!;
                var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(30000);
                if (int.TryParse(o.Trim(), out var n)) best = Math.Max(best, n);
            }
            catch { }
            if (best >= want) break;
            Thread.Sleep(1000);
        }
        return best;
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
