using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace GatewayPlusApp;

// Мастер «Создать свой VPN»: заходит на ваш сервер (VPS) по SSH, ставит там WireGuard скриптом server-setup.sh и забирает готовый конфиг для этого компьютера.
// Без окон и без Windows-специфики (проверяется и на Linux в CI против настоящего sshd). Пароль живёт только в памяти на время подключения и нигде не сохраняется.
sealed class SshTarget
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string User { get; set; } = "root";
    public string? Password { get; set; }
    public string? KeyFile { get; set; }
    public string? KeyPass { get; set; }
}

sealed class HostKeyQuestion
{
    public string Host { get; init; } = "";
    public int Port { get; init; }
    public string Algorithm { get; init; } = "";
    public string Fingerprint { get; init; } = "";   // SHA256:… — как печатает ssh-keygen -lf
    public bool Known { get; init; }                 // этот сервер уже был, отпечаток запомнен
    public bool Changed { get; init; }               // …и сейчас он ДРУГОЙ
}

sealed class ProvisionResult
{
    public bool Ok { get; set; }
    public string Error { get; set; } = "";
    public string Conf { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string Os { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string Ipv6Prefix { get; set; } = "";   // «Проверить и ускорить сервер»: сервер включил IPv6 в туннеле — профиль на компьютере нужно дополнить
}

static class Provisioner
{
    public static string KnownHostsFile { get; set; } = Path.Combine(Store.Dir, "known-hosts.json");
    const string BeginMark = "НАЧАЛО КОНФИГА", EndMark = "КОНЕЦ КОНФИГА";

    static readonly Regex HostRe = new(@"^[A-Za-z0-9.\-:]{1,253}$");
    static readonly Regex UserRe = new(@"^[a-z_][a-z0-9_.\-]{0,31}$", RegexOptions.IgnoreCase);

    // Что введено верно ли (до любого подключения). null — всё в порядке.
    public static string? ValidateTarget(SshTarget t)
    {
        if (!HostRe.IsMatch(t.Host ?? "")) return "Адрес сервера: введите IP-адрес (например 203.0.113.5) или имя сервера, без пробелов и «http://».";
        if (t.Port < 1 || t.Port > 65535) return "Порт должен быть числом от 1 до 65535 (обычно 22).";
        if (!UserRe.IsMatch(t.User ?? "")) return "Имя пользователя: латиницей, например root.";
        if (string.IsNullOrEmpty(t.Password) && string.IsNullOrEmpty(t.KeyFile)) return "Введите пароль от сервера или выберите файл ключа SSH.";
        if (!string.IsNullOrEmpty(t.KeyFile) && !File.Exists(t.KeyFile)) return "Файл ключа не найден: " + t.KeyFile;
        return null;
    }

    public static string Fingerprint(byte[] hostKey) => "SHA256:" + Convert.ToBase64String(SHA256.HashData(hostKey)).TrimEnd('=');

    // Конфиг клиента из вывода скрипта: всё между строками «НАЧАЛО КОНФИГА» и «КОНЕЦ КОНФИГА».
    public static string? ParseConfig(string output)
    {
        var lines = output.Replace("\r", "").Split('\n');
        int a = Array.FindIndex(lines, l => l.Contains(BeginMark)), b = Array.FindIndex(lines, l => l.Contains(EndMark));
        if (a < 0 || b <= a) return null;
        var conf = string.Join("\n", lines.Skip(a + 1).Take(b - a - 1)).Trim();
        return conf.Length == 0 ? null : conf + "\n";
    }

    internal static Dictionary<string, string> LoadKnown()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(KnownHostsFile)) ?? new(); } catch { return new(); }
    }

    internal static void RememberHost(string key, string fp)
    {
        try
        {
            var d = LoadKnown(); d[key] = fp;
            Directory.CreateDirectory(Path.GetDirectoryName(KnownHostsFile)!);
            File.WriteAllText(KnownHostsFile, JsonSerializer.Serialize(d));
        }
        catch { }
    }

    public static string Explain(Exception ex) => ex switch
    {
        SshAuthenticationException => "Сервер не принял вход: неверный пароль, логин или ключ. Проверьте данные из письма хостера (логин обычно root).",
        System.Net.Sockets.SocketException => "Не удалось связаться с сервером: проверьте адрес и порт, и что сервер включён.",
        SshOperationTimeoutException or TimeoutException => "Сервер не ответил вовремя. Проверьте адрес и порт, и что сервер включён.",
        InvalidOperationException or IOException when ex.Message.Contains("passphrase", StringComparison.OrdinalIgnoreCase) => "Не подошла парольная фраза ключа SSH.",
        SshException when ex.Message.Contains("passphrase", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("decrypt", StringComparison.OrdinalIgnoreCase) => "Не подошла парольная фраза ключа SSH.",
        SshConnectionException => "Соединение с сервером оборвалось: " + ex.Message,
        _ => ex.Message,
    };

    public static Task<ProvisionResult> RunAsync(SshTarget t, Func<HostKeyQuestion, bool> confirmKey, Func<string, bool> confirmReplace, Action<string> log, CancellationToken ct = default)
        => Task.Run(() => Run(t, confirmKey, confirmReplace, log, ct), ct);

    static ProvisionResult Run(SshTarget t, Func<HostKeyQuestion, bool> confirmKey, Func<string, bool> confirmReplace, Action<string> log, CancellationToken ct)
    {
        var result = new ProvisionResult();
        var bad = ValidateTarget(t);
        if (bad != null) { result.Error = bad; return result; }
        bool refused = false;
        SshClient? ssh = null;
        string? scriptPath = null;
        string pre = "";
        try
        {
            var methods = new List<AuthenticationMethod>();
            if (!string.IsNullOrEmpty(t.KeyFile))
            {
                var pk = string.IsNullOrEmpty(t.KeyPass) ? new PrivateKeyFile(t.KeyFile) : new PrivateKeyFile(t.KeyFile, t.KeyPass);
                methods.Add(new PrivateKeyAuthenticationMethod(t.User, pk));
            }
            if (!string.IsNullOrEmpty(t.Password))
            {
                var pwd = t.Password!;
                methods.Add(new PasswordAuthenticationMethod(t.User, pwd));
                var kbd = new KeyboardInteractiveAuthenticationMethod(t.User);
                kbd.AuthenticationPrompt += (_, e) => { foreach (var p in e.Prompts) p.Response = pwd; };
                methods.Add(kbd);
            }
            var info = new ConnectionInfo(t.Host, t.Port, t.User, methods.ToArray()) { Timeout = TimeSpan.FromSeconds(20) };
            ssh = new SshClient(info) { KeepAliveInterval = TimeSpan.FromSeconds(15) };
            var sshRef = ssh;
            using var cancelReg = ct.Register(() => { try { sshRef.Disconnect(); } catch { } });
            ssh.HostKeyReceived += (_, e) =>
            {
                var fp = Fingerprint(e.HostKey);
                result.Fingerprint = fp;
                var key = t.Host.ToLowerInvariant() + ":" + t.Port;
                var known = LoadKnown();
                known.TryGetValue(key, out var old);
                if (old == fp) { log("Отпечаток сервера знакомый — этот сервер уже был подтверждён раньше."); e.CanTrust = true; return; }
                var q = new HostKeyQuestion { Host = t.Host, Port = t.Port, Algorithm = e.HostKeyName, Fingerprint = fp, Known = old != null, Changed = old != null };
                e.CanTrust = confirmKey(q);
                if (e.CanTrust) RememberHost(key, fp); else refused = true;
            };
            log($"Подключаюсь к {t.Host}:{t.Port} как {t.User}…");
            ct.ThrowIfCancellationRequested();
            ssh.Connect();
            log("Вход выполнен.");

            string Exec(string cmd, int seconds = 60)
            {
                using var c = sshRef.CreateCommand(cmd);
                c.CommandTimeout = TimeSpan.FromSeconds(seconds);
                var o = c.Execute();
                return o + c.Error;
            }

            // права: root или sudo без пароля
            if (Exec("id -u").Trim() != "0")
            {
                if (!Exec("sudo -n true 2>/dev/null && echo SUDO_OK || echo SUDO_NO").Contains("SUDO_OK"))
                { result.Error = $"Вы вошли как «{t.User}», а не root, и sudo просит пароль. Войдите как root (так делает большинство хостеров) или настройте sudo без пароля."; return result; }
                pre = "sudo -n ";
                log("Вход не под root — буду использовать sudo.");
            }

            // система
            var os = Exec(". /etc/os-release 2>/dev/null; echo \"${ID:-?} ${VERSION_ID:-?}\"; command -v apt-get >/dev/null 2>&1 && echo APT_OK || echo APT_NO").Replace("\r", "").Split('\n');
            result.Os = os.Length > 0 ? os[0].Trim() : "?";
            if (!os.Any(l => l.Trim() == "APT_OK")) { result.Error = $"На сервере не Ubuntu/Debian (система: {result.Os}). Мастер умеет только Ubuntu 22.04/24.04 и Debian 12 — пересоздайте сервер с одной из них."; return result; }
            var supported = result.Os is "ubuntu 22.04" or "ubuntu 24.04" or "debian 12";
            log("Система: " + result.Os + (supported ? "." : " — скрипт рассчитан на Ubuntu 22.04/24.04 и Debian 12, но попробую."));

            // уже настроенный WireGuard: перенастройка делает новые ключи, прежние конфиги перестанут работать
            if (Exec(pre + "test -f /etc/wireguard/wg0.conf && echo EXISTS || echo FRESH").Contains("EXISTS"))
            {
                if (!confirmReplace("На этом сервере WireGuard уже настроен. Если продолжить, будут созданы новые ключи, а прежние конфиги (на этом и других устройствах) перестанут работать. Продолжить?"))
                { result.Error = "Отменено: на сервере уже есть WireGuard, я его не трогал."; return result; }
            }

            // скрипт передаётся одной командой (base64), без SFTP: так работает у любого хостера
            var script = Store.ServerScript();
            if (script.Length < 100) { result.Error = "Внутренняя ошибка: скрипт сервера не найден в плагине."; return result; }
            scriptPath = "/tmp/ghostshell-gateway-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant() + ".sh";
            var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(script));
            if (!Exec($"umask 077; printf '%s' '{b64}' | base64 -d > {scriptPath} && chmod 700 {scriptPath} && echo UPLOADED").Contains("UPLOADED"))
            { result.Error = "Не получилось передать скрипт на сервер."; scriptPath = null; return result; }
            log("Ставлю WireGuard на сервер. Это занимает 1–3 минуты…");

            var full = new StringBuilder();
            bool inConfig = false;
            using (var cmd = sshRef.CreateCommand($"{pre}bash {scriptPath} 2>&1"))
            {
                cmd.CommandTimeout = TimeSpan.FromMinutes(10);
                var ar = cmd.BeginExecute();
                using var reader = new StreamReader(cmd.OutputStream, Encoding.UTF8);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    ct.ThrowIfCancellationRequested();
                    full.AppendLine(line);
                    // сам конфиг (в нём закрытый ключ) в журнал окна не выводим
                    if (line.Contains(BeginMark)) { inConfig = true; log("Получаю конфиг…"); continue; }
                    if (line.Contains(EndMark)) { inConfig = false; continue; }
                    if (!inConfig && line.Trim().Length > 0) log(line.TrimEnd());
                }
                cmd.EndExecute(ar);
                int exit = cmd.ExitStatus is int code ? code : -1;
                if (exit != 0) { result.Error = $"Скрипт на сервере завершился с ошибкой (код {exit}). Последние строки журнала выше."; return result; }
            }

            var conf = ParseConfig(full.ToString());
            if (conf == null) { result.Error = "Скрипт отработал, но конфиг в его выводе не найден."; return result; }
            var badConf = Store.Validate(conf);
            if (badConf != null) { result.Error = "Сервер вернул странный конфиг: " + badConf; return result; }
            result.Conf = conf;
            result.Endpoint = Store.EndpointOf(conf);
            result.Ok = true;
            log("Готово: сервер настроен, конфиг получен.");
            return result;
        }
        catch (OperationCanceledException) { result.Error = "Отменено."; return result; }
        catch (Exception ex)
        {
            result.Error = refused ? "Подключение отменено: вы не подтвердили отпечаток сервера." : Explain(ex);
            return result;
        }
        finally
        {
            try
            {
                if (ssh is { IsConnected: true })
                {
                    if (scriptPath != null) { try { using var c = ssh.CreateCommand("rm -f " + scriptPath); c.Execute(); } catch { } }
                    ssh.Disconnect();
                }
            }
            catch { }
            ssh?.Dispose();
        }
    }
}
