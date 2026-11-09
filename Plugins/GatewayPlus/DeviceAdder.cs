using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Renci.SshNet;

namespace GatewayPlusApp;

// «Ещё одно устройство» (например, телефон): заходит на ваш сервер по SSH и добавляет на нём ОТДЕЛЬНЫЙ ключ (add-device.sh),
// поэтому компьютер и телефон работают одновременно. Старые профили не затрагиваются. Пароль нигде не сохраняется.
static class DeviceAdder
{
    static readonly Regex NameRe = new(@"^[A-Za-z0-9_-]{1,20}$");
    public static bool ValidName(string s) => NameRe.IsMatch(s ?? "");

    public static Task<ProvisionResult> RunAsync(SshTarget t, string deviceName, Func<HostKeyQuestion, bool> confirmKey, Action<string> log, CancellationToken ct = default)
        => Task.Run(() => Run(t, deviceName, confirmKey, log, ct, false), ct);

    // «Ускорить сервер»: проверяет и настраивает уже работающий сервер (tune-server.sh), конфиг не выдаёт.
    // extraEnv — только для проверки на тестовой машине (например GS_FORCE_IPV6=1); из окна не задаётся
    public static Task<ProvisionResult> TuneAsync(SshTarget t, Func<HostKeyQuestion, bool> confirmKey, Action<string> log, CancellationToken ct = default, string extraEnv = "")
        => Task.Run(() => Run(t, "phone", confirmKey, log, ct, true, extraEnv), ct);

    static readonly Regex EnvRe = new(@"^[A-Z][A-Z0-9_]{0,30}=[A-Za-z0-9_]{0,30}$");
    static readonly Regex Ipv6PrefixRe = new(@"^GS_IPV6_PREFIX=(fd[0-9a-f:]{4,40}::)\s*$", RegexOptions.Multiline);

    static ProvisionResult Run(SshTarget t, string deviceName, Func<HostKeyQuestion, bool> confirmKey, Action<string> log, CancellationToken ct, bool tune, string extraEnv = "")
    {
        var result = new ProvisionResult();
        var bad = Provisioner.ValidateTarget(t);
        if (bad != null) { result.Error = bad; return result; }
        if (!ValidName(deviceName)) { result.Error = "Название устройства: латиницей, цифры, «-» и «_», до 20 знаков (например phone)."; return result; }
        var st = new Provisioner.ConnectState();
        SshClient? ssh = null;
        string? scriptPath = null;
        try
        {
            ssh = Provisioner.BuildClient(t, confirmKey, log, st, "Отпечаток сервера знакомый.");
            var sshRef = ssh;
            using var cancelReg = ct.Register(() => { try { sshRef.Disconnect(); } catch { } });
            log($"Подключаюсь к {t.Host}:{t.Port} как {t.User}…");
            ct.ThrowIfCancellationRequested();
            ssh.Connect();
            t.LoggedIn();
            result.Fingerprint = st.Fingerprint;
            log("Вход выполнен.");

            string Exec(string cmd, int seconds = 60)
            {
                using var c = sshRef.CreateCommand(cmd);
                c.CommandTimeout = TimeSpan.FromSeconds(seconds);
                var o = c.Execute();
                return o + c.Error;
            }

            var pre = "";
            if (Exec("id -u").Trim() != "0")
            {
                if (!Exec("sudo -n true 2>/dev/null && echo SUDO_OK || echo SUDO_NO").Contains("SUDO_OK"))
                { result.Error = $"Вы вошли как «{t.User}», а не root, и sudo просит пароль. Войдите как root."; return result; }
                pre = "sudo -n ";
            }

            var script = tune ? Store.TuneScript() : Store.AddDeviceScript();
            if (script.Length < 100) { result.Error = "Внутренняя ошибка: скрипт не найден в плагине."; return result; }
            scriptPath = "/tmp/ghostshell-" + (tune ? "tune-" : "device-") + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant() + ".sh";
            var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(script));
            if (!Exec($"umask 077; printf '%s' '{b64}' | base64 -d > {scriptPath} && chmod 700 {scriptPath} && echo UPLOADED").Contains("UPLOADED"))
            { result.Error = "Не получилось передать скрипт на сервер."; scriptPath = null; return result; }
            log(tune ? "Проверяю и настраиваю сервер (до минуты)…" : "Добавляю устройство «" + deviceName + "» на сервер…");

            var full = new StringBuilder();
            bool inConfig = false;
            int exit;
            if (extraEnv.Length > 0 && !EnvRe.IsMatch(extraEnv)) { result.Error = "Внутренняя ошибка: недопустимая переменная окружения."; return result; }
            var envPart = extraEnv.Length > 0 ? extraEnv + " " : "";
            using (var cmd = sshRef.CreateCommand(tune ? $"{pre}env {envPart}bash {scriptPath} 2>&1" : $"{pre}env DEVNAME={deviceName} bash {scriptPath} 2>&1"))
            {
                cmd.CommandTimeout = TimeSpan.FromMinutes(3);
                var ar = cmd.BeginExecute();
                using var reader = new StreamReader(cmd.OutputStream, Encoding.UTF8);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    ct.ThrowIfCancellationRequested();
                    full.AppendLine(line);
                    // сам конфиг (в нём закрытый ключ) в журнал окна не выводим
                    if (line.Contains("НАЧАЛО КОНФИГА")) { inConfig = true; log("Получаю конфиг…"); continue; }
                    if (line.Contains("КОНЕЦ КОНФИГА")) { inConfig = false; continue; }
                    if (!inConfig && line.Trim().Length > 0) log(line.TrimEnd());
                }
                cmd.EndExecute(ar);
                exit = cmd.ExitStatus is int code ? code : -1;
            }
            if (exit == 2) { result.Error = "На этом сервере нет WireGuard, настроенного мастером Gateway Plus. Сначала создайте VPN кнопкой «Создать мой VPN на сервере…»."; return result; }
            if (exit != 0) { result.Error = $"Скрипт на сервере завершился с ошибкой (код {exit}). Последние строки журнала выше."; return result; }

            if (tune)
            {
                var m = Ipv6PrefixRe.Match(full.ToString().Replace("\r", ""));
                if (m.Success) result.Ipv6Prefix = m.Groups[1].Value;
                result.Ok = true; log("Готово."); return result;
            }
            var conf = Provisioner.ParseConfig(full.ToString());
            if (conf == null) { result.Error = "Скрипт отработал, но конфиг в его выводе не найден."; return result; }
            var badConf = Store.Validate(conf);
            if (badConf != null) { result.Error = "Сервер вернул странный конфиг: " + badConf; return result; }
            result.Conf = conf;
            result.Endpoint = Store.EndpointOf(conf);
            result.Ok = true;
            log("Готово: ключ для устройства создан.");
            return result;
        }
        catch (OperationCanceledException) { result.Error = "Отменено."; return result; }
        catch (Exception ex)
        {
            Provisioner.Fail(result, ex, st);
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
