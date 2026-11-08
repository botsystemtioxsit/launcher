using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GatewayPlusApp;

// «Мои серверы»: профиль — это один арендованный сервер. На нём могут стоять оба протокола (WireGuard и GSP): это два конфига одного сервера,
// а не два разных VPN. Сервер определяется адресом из Endpoint профилей, поэтому прежние профили сами собираются в серверы без переноса.
// Рядом хранятся подпись, SSH-порт и логин и — только если человек сам согласился — сохранённый доступ по SSH, чтобы не вводить пароль при каждом
// переключении протокола. Доступ шифруется DPAPI (читает только эта учётная запись Windows на этом компьютере), никуда не отправляется и забывается кнопкой.
sealed class ServerInfo
{
    public string Id { get; set; } = "";
    public string Host { get; set; } = "";      // адрес из Endpoint профилей — по нему профили сводятся в сервер
    public string SshHost { get; set; } = "";   // куда заходить по SSH, если это не тот же адрес (например имя вместо IP); пусто — тот же Host
    public int SshPort { get; set; } = 22;
    public string SshUser { get; set; } = "root";
    public string Name { get; set; } = "";      // подпись в списке; пусто — показываем адрес
    public bool HasAccess { get; set; }         // доступ сохранён (сам он лежит отдельно, зашифрованный)

    public string SshAddress => SshHost.Length > 0 ? SshHost : Host;
}

sealed class SavedAccess
{
    public string? Password { get; set; }
    public string? KeyFile { get; set; }        // путь к файлу ключа (сам файл остаётся там, где лежит)
    public string? KeyPass { get; set; }
}

sealed class ServerRow
{
    public ServerInfo Info = new();
    public string? Wg;                           // профиль WireGuard этого сервера (основной)
    public string? Gsp;                          // профиль GSP этого сервера (основной)
    public List<string> Extra = new();           // остальные профили на этом же сервере (второй WireGuard и т. п.)
    public IEnumerable<string> All => new[] { Wg, Gsp }.Where(x => x != null).Select(x => x!).Concat(Extra);
}

static class Servers
{
    static string FilePath => Path.Combine(Store.Dir, "servers.json");
    static string AccessDir => Path.Combine(Store.Dir, "servers");
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GhostShell.GatewayPlus.ssh.v1");
    static readonly Regex IdRe = new(@"^s-[0-9a-f]{6,16}$");

    sealed class Model { public List<ServerInfo> Servers { get; set; } = new(); }

    static Model Read()
    {
        try { return JsonSerializer.Deserialize<Model>(File.ReadAllText(FilePath)) ?? new Model(); } catch { return new Model(); }
    }

    static void Write(Model m)
    {
        try
        {
            Directory.CreateDirectory(Store.Dir);
            var json = JsonSerializer.Serialize(m);
            if (File.Exists(FilePath) && File.ReadAllText(FilePath) == json) return;
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, FilePath, true);
        }
        catch { }
    }

    static bool Same(string a, string b) => a.Length > 0 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public static string Label(ServerInfo s) => s.Name.Length > 0 ? s.Name : (s.Host.Length > 0 ? s.Host : "сервер");

    // Подпись по умолчанию из названия профиля: «germany-gsp» → «germany»
    public static string SuggestName(string profile)
    {
        var n = Regex.Replace(profile ?? "", @"[-_.](gsp|wg)$", "", RegexOptions.IgnoreCase).Trim();
        return n.Length > 0 ? n : (profile ?? "");
    }

    // Собирает профили в серверы по адресу, приводит servers.json в порядок (новые серверы добавляет, серверы без профилей убирает вместе с доступом).
    public static List<ServerRow> List()
    {
        var model = Read();
        var groups = new List<(string Host, List<(string Name, bool Gsp)> Profiles)>();
        foreach (var p in Store.List())
        {
            var c = Store.Load(p);
            var host = c == null ? "" : Store.EndpointOf(c);
            var isGsp = c != null && Gsp.IsGsp(c);
            var g = host.Length == 0 ? default : groups.FirstOrDefault(x => Same(x.Host, host));
            if (g.Profiles == null) { g = (host, new List<(string, bool)>()); groups.Add(g); }
            g.Profiles.Add((p, isGsp));
        }

        var active = Store.Active;
        var rows = new List<ServerRow>();
        var keep = new List<ServerInfo>();
        foreach (var (host, profiles) in groups)
        {
            var info = host.Length > 0 ? model.Servers.FirstOrDefault(s => Same(s.Host, host)) : null;
            if (info == null) info = new ServerInfo { Id = host.Length > 0 ? NewId() : "", Host = host, Name = host.Length == 0 ? profiles[0].Name : "" };
            if (host.Length > 0) keep.Add(info);   // профиль, у которого адрес не прочитался, в список показывается, но в файл не записывается
            var row = new ServerRow { Info = info };
            string? Pick(bool gsp)
            {
                var list = profiles.Where(x => x.Gsp == gsp).Select(x => x.Name).ToList();
                if (list.Count == 0) return null;
                var a = list.FirstOrDefault(x => x.Equals(active, StringComparison.Ordinal));
                return a ?? list[0];
            }
            row.Wg = Pick(false); row.Gsp = Pick(true);
            row.Extra = profiles.Select(x => x.Name).Where(n => n != row.Wg && n != row.Gsp).ToList();
            rows.Add(row);
        }

        // серверы, у которых не осталось профилей, забываем вместе с сохранённым доступом
        foreach (var gone in model.Servers.Where(s => !keep.Any(k => k.Id == s.Id)))
            DeleteAccessFile(gone.Id);
        if (model.Servers.Count != keep.Count || keep.Any(k => !model.Servers.Any(s => s.Id == k.Id)))
        {
            model.Servers = keep;
            Write(model);
        }
        return rows.OrderBy(r => Label(r.Info), StringComparer.OrdinalIgnoreCase).ToList();
    }

    static string NewId() => "s-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();

    // Сервер по адресу (из профиля или тот, по которому заходим по SSH); null, если такого нет.
    public static ServerInfo? Find(string host)
    {
        host = (host ?? "").Trim();
        if (host.Length == 0) return null;
        return Read().Servers.FirstOrDefault(s => Same(s.Host, host) || Same(s.SshHost, host));
    }

    // Запоминает сервер (после успешной установки или проверки). Подпись ставится, только если у сервера её ещё нет.
    public static ServerInfo Remember(string host, string sshHost, int sshPort, string sshUser, string suggestedName)
    {
        var m = Read();
        var s = m.Servers.FirstOrDefault(x => Same(x.Host, host));
        if (s == null) { s = new ServerInfo { Id = NewId(), Host = host }; m.Servers.Add(s); }
        s.SshHost = Same(sshHost, host) ? "" : sshHost;
        s.SshPort = sshPort is >= 1 and <= 65535 ? sshPort : 22;
        s.SshUser = string.IsNullOrWhiteSpace(sshUser) ? "root" : sshUser;
        if (s.Name.Length == 0 && !string.IsNullOrWhiteSpace(suggestedName)) s.Name = CleanName(suggestedName);
        Write(m);
        return s;
    }

    // Подпись сервера: одна строка, без управляющих знаков и «перевёрнутых» символов, до 40 знаков.
    public static string CleanName(string? s)
    {
        var t = Regex.Replace(s ?? "", @"[\p{C}‎‏‪-‮⁦-⁩]", " ");
        t = Regex.Replace(t, @"\s+", " ").Trim();
        return t.Length > 40 ? t[..40].Trim() : t;
    }

    public static void Rename(string id, string name)
    {
        var m = Read();
        var s = m.Servers.FirstOrDefault(x => x.Id == id);
        if (s == null) return;
        s.Name = CleanName(name);
        Write(m);
    }

    // Убирает сервер из списка: удаляет его профили и сохранённый доступ. Сам сервер у хостера не меняется.
    public static void Remove(ServerRow row)
    {
        foreach (var p in row.All) Store.Delete(p);
        var m = Read();
        m.Servers.RemoveAll(x => x.Id == row.Info.Id);
        Write(m);
        DeleteAccessFile(row.Info.Id);
        if (!Store.List().Contains(Store.Active, StringComparer.Ordinal)) Store.Active = Store.List().FirstOrDefault() ?? "";
    }

    // ---------- сохранённый доступ ----------
    static string AccessPath(string id) => Path.Combine(AccessDir, id + ".dat");

    static void DeleteAccessFile(string id) { if (IdRe.IsMatch(id)) { try { File.Delete(AccessPath(id)); } catch { } } }

    public static SavedAccess? LoadAccess(string id)
    {
        try
        {
            if (!IdRe.IsMatch(id)) return null;
            var f = AccessPath(id);
            if (!File.Exists(f)) return null;
            var json = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(f), Entropy, DataProtectionScope.CurrentUser));
            var a = JsonSerializer.Deserialize<SavedAccess>(json);
            return a != null && (!string.IsNullOrEmpty(a.Password) || !string.IsNullOrEmpty(a.KeyFile)) ? a : null;
        }
        catch { return null; }
    }

    public static void SaveAccess(string id, SavedAccess a)
    {
        if (!IdRe.IsMatch(id)) throw new ArgumentException("плохой номер сервера");
        Directory.CreateDirectory(AccessDir);
        var enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(a)), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(AccessPath(id), enc);
        SetHasAccess(id, true);
    }

    public static void ForgetAccess(string id)
    {
        DeleteAccessFile(id);
        SetHasAccess(id, false);
    }

    static void SetHasAccess(string id, bool has)
    {
        var m = Read();
        var s = m.Servers.FirstOrDefault(x => x.Id == id);
        if (s == null || s.HasAccess == has) return;
        s.HasAccess = has;
        Write(m);
    }

    // Что сохранено, словами (без самих секретов).
    public static string SavedSentence(ServerInfo s, SavedAccess a) =>
        string.IsNullOrEmpty(a.KeyFile)
            ? $"Доступ к серверу сохранён: пользователь {s.SshUser}, пароль. Вводить его заново не нужно."
            : $"Доступ к серверу сохранён: пользователь {s.SshUser}, файл ключа {a.KeyFile}. Вводить ничего заново не нужно.";

    // Данные для входа по SSH из сохранённого доступа. null и текст проблемы, если войти по ним нельзя.
    public static SshTarget? Target(ServerInfo s, out string? problem)
    {
        problem = null;
        var a = LoadAccess(s.Id);
        if (a == null) { problem = "Сохранённый доступ не прочитался — введите его заново."; return null; }
        var t = new SshTarget { Host = s.SshAddress, Port = s.SshPort, User = s.SshUser };
        if (!string.IsNullOrEmpty(a.KeyFile))
        {
            if (!File.Exists(a.KeyFile)) { problem = "Файл ключа не найден: " + a.KeyFile + ". Введите доступ заново."; return null; }
            t.KeyFile = a.KeyFile; t.KeyPass = a.KeyPass;
        }
        if (!string.IsNullOrEmpty(a.Password)) t.Password = a.Password;
        return t;
    }
}
