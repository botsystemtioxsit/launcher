using System.IO;
using System.Net.NetworkInformation;
using System.Text;

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
}
