using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BattleLauncher;

class Account
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Role { get; set; } = "user";
    public string PlayerId { get; set; } = "";
    public long AddedAt { get; set; }

    [JsonIgnore] public string Initials => (Name.Length > 0 ? Name : PlayerId.Length > 0 ? PlayerId : "??").Trim().PadRight(2)[..2].Trim().ToUpperInvariant();
    [JsonIgnore] public string Display => Name.Length > 0 ? Name : "Аккаунт " + Id;
    [JsonIgnore] public string RoleName => Role switch { "moderator" => "Модератор", "admin" => "Админ", "superadmin" => "Суперадмин", "user" => "Игрок", _ => Role };
}

// Список аккаунтов и текущий. Ничего секретного: только id, имя и роль (всё это и так лежит в открытой базе).
class AccountStore
{
    const int Max = 10;
    static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BattleLauncher", "launcher.json");

    public List<Account> Accounts { get; set; } = new();
    public string? ActiveId { get; set; }

    [JsonIgnore] public Account? Active => Accounts.FirstOrDefault(a => a.Id == ActiveId);

    public static AccountStore Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<AccountStore>(File.ReadAllText(FilePath)) ?? new AccountStore();
            // файл могли править руками — выбрасываем всё, что не похоже на наш формат
            s.Accounts = s.Accounts.Where(a => Net.SafeId.IsMatch(a.Id)).Take(Max).ToList();
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

    public void Upsert(Profile p)
    {
        var prev = Accounts.FirstOrDefault(a => a.Id == p.Id);
        var acc = new Account { Id = p.Id, Name = p.Name, Role = p.Role, PlayerId = p.PlayerId, AddedAt = prev?.AddedAt ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
        Accounts = Accounts.Where(a => a.Id != p.Id).Prepend(acc).Take(Max).ToList();
        ActiveId = p.Id;
        Save();
    }

    public void Remove(string id)
    {
        Accounts = Accounts.Where(a => a.Id != id).ToList();
        if (ActiveId == id) ActiveId = null;
        Save();
    }
}
