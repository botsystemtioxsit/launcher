using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BattleLauncher;

static class Secrets
{
    // Секреты (токен доступа, deviceId) лежат в файле только зашифрованными ключом этого пользователя
    // Windows (DPAPI): файл, скопированный на другой компьютер или к другому пользователю, бесполезен.
    public static string Protect(string plain) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser));

    public static string? Unprotect(string? protectedText)
    {
        if (string.IsNullOrEmpty(protectedText)) return null;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedText), null, DataProtectionScope.CurrentUser)); }
        catch { return null; }
    }
}

class Account
{
    public const string KindAccount = "account"; // обычный аккаунт игры (вход по почте или по коду)
    public const string KindToken = "token";     // сессия по токену доступа, без аккаунта

    public string Id { get; set; } = "";
    public string Kind { get; set; } = KindAccount;
    public string Name { get; set; } = "";
    public string Role { get; set; } = "user";
    public string PlayerId { get; set; } = "";
    public bool Paired { get; set; }               // подключён по коду: владелец может отозвать это устройство
    public string? TokenProtected { get; set; }    // только для Kind == token
    public long AddedAt { get; set; }

    [JsonIgnore] public bool IsToken => Kind == KindToken;
    [JsonIgnore] public string Initials => (Name.Length > 0 ? Name : PlayerId.Length > 0 ? PlayerId : "??").Trim().PadRight(2)[..2].Trim().ToUpperInvariant();
    [JsonIgnore] public string Display => Name.Length > 0 ? Name : IsToken ? "Токен доступа" : "Аккаунт " + Id;
    [JsonIgnore] public string RoleName => Role switch { "moderator" => "Модератор", "admin" => "Админ", "superadmin" => "Суперадмин", "user" => "Игрок", "token" => "Токен доступа", _ => Role };
}

// Список аккаунтов и текущий. Открытым текстом тут только то, что и так лежит в открытой базе
// (id, имя, роль); секреты — через Secrets.Protect.
class AccountStore
{
    const int Max = 10;
    static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BattleLauncher", "launcher.json");

    public List<Account> Accounts { get; set; } = new();
    public string? ActiveId { get; set; }
    public string? DeviceIdProtected { get; set; }

    [JsonIgnore] public Account? Active => Accounts.FirstOrDefault(a => a.Id == ActiveId);

    public static AccountStore Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<AccountStore>(File.ReadAllText(FilePath)) ?? new AccountStore();
            // файл могли править руками — выбрасываем всё, что не похоже на наш формат
            s.Accounts = s.Accounts.Where(a => Net.SafeId.IsMatch(a.Id) && (!a.IsToken || a.TokenProtected != null)).Take(Max).ToList();
            return s;
        }
        catch { return new AccountStore(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this));
        File.Move(tmp, FilePath, true); // запись целиком или никак
    }

    // deviceId — секрет этого компьютера (в базу уходит только его sha256, см. Rtdb.DeviceKeyOf)
    public string DeviceId()
    {
        var id = Secrets.Unprotect(DeviceIdProtected);
        if (id != null) return id;
        id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        DeviceIdProtected = Secrets.Protect(id);
        Save();
        return id;
    }

    public void Upsert(Profile p, bool? paired = null)
    {
        var prev = Accounts.FirstOrDefault(a => a.Id == p.Id);
        var acc = new Account
        {
            Id = p.Id, Name = p.Name, Role = p.Role, PlayerId = p.PlayerId,
            Paired = paired ?? prev?.Paired ?? false,
            AddedAt = prev?.AddedAt ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        Put(acc);
    }

    public void UpsertToken(string token, string label)
    {
        var id = TokenAccountId(token);
        var prev = Accounts.FirstOrDefault(a => a.Id == id);
        Put(new Account
        {
            Id = id, Kind = Account.KindToken, Name = label, Role = "token", TokenProtected = Secrets.Protect(token),
            AddedAt = prev?.AddedAt ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });
    }

    // Id сессии по токену — по хэшу, чтобы сам токен нигде, кроме зашифрованного поля, не светился
    public static string TokenAccountId(string token) =>
        "t_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)))[..16].ToLowerInvariant();

    void Put(Account acc)
    {
        Accounts = Accounts.Where(a => a.Id != acc.Id).Prepend(acc).Take(Max).ToList();
        ActiveId = acc.Id;
        Save();
    }

    public void Remove(string id)
    {
        Accounts = Accounts.Where(a => a.Id != id).ToList();
        if (ActiveId == id) ActiveId = null;
        Save();
    }
}
