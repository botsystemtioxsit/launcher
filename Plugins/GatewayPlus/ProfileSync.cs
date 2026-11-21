using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GatewayPlusApp;

// Серверы (профили WireGuard/GSP) следуют за аккаунтом: на другом компьютере и на телефоне после входа по тому же ключу они появляются сами.
// В профиле лежит закрытый ключ, поэтому в базу он уходит ТОЛЬКО зашифрованным на этом устройстве: AES-256-GCM, ключ шифрования — sha256 от ключа аккаунта
// (его в базе нет), имя профиля подписано в зашифрованное. База видит только шифротекст; удалить запись она не даёт — удаление записывается «могилой» {d:1}.
// Формат совпадает с телефоном (AndroidGateway ProfileSync.kt): запись gatewayProfiles/<номер аккаунта>/<имя> = {n: nonce, c: шифротекст+тег, at}.
static class ProfileSync
{
    static byte[] KeyOf(string raw) => SHA256.HashData(Encoding.UTF8.GetBytes("ghostshell.gateway.sync.v1:" + raw));

    public static (string N, string C) Encrypt(string raw, string name, string conf, byte[]? nonce = null)
    {
        var n = nonce ?? RandomNumberGenerator.GetBytes(12);
        var pt = Encoding.UTF8.GetBytes(conf);
        var ct = new byte[pt.Length];
        var tag = new byte[16];
        using var g = new AesGcm(KeyOf(raw), 16);
        g.Encrypt(n, pt, ct, tag, Encoding.UTF8.GetBytes(name));
        return (Convert.ToBase64String(n), Convert.ToBase64String(ct.Concat(tag).ToArray()));
    }

    public static string? Decrypt(string raw, string name, string n, string c)
    {
        try
        {
            var nb = Convert.FromBase64String(n);
            var cb = Convert.FromBase64String(c);
            if (nb.Length != 12 || cb.Length < 17) return null;
            var ct = cb[..^16];
            var tag = cb[^16..];
            var pt = new byte[ct.Length];
            using var g = new AesGcm(KeyOf(raw), 16);
            g.Decrypt(nb, ct, tag, pt, Encoding.UTF8.GetBytes(name));
            return Encoding.UTF8.GetString(pt);
        }
        catch { return null; }
    }

    // Профиль сохранён или удалён на этом компьютере — отправляем в аккаунт в фоне (ошибки молча: нет связи — отправим при следующей сверке).
    public static void Changed(string name, bool deleted)
    {
        var acc = Account.Load();
        if (acc == null || Store.DirOverride != null || Cloud.TestUrl != null) return;
        _ = Task.Run(async () => { try { await PutAsync(acc, name, deleted); } catch { } });
    }

    static async Task PutAsync(Account.Info acc, string name, bool deleted)
    {
        if (!Store.ValidName(name)) return;
        var at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (deleted) { await Cloud.PutProfileAsync(acc.Id, name, new { d = 1, at }); return; }
        var conf = Store.Load(name);
        if (conf == null) return;
        var (n, c) = Encrypt(acc.Key, name, conf);
        await Cloud.PutProfileAsync(acc.Id, name, new { n, c, at });
    }

    // Сверка: чего нет у нас — забираем из аккаунта; чего нет в аккаунте — отправляем. Локальные профили никогда не затираются и не удаляются.
    // Возвращает, сколько серверов подтянуто.
    public static async Task<int> SyncAsync()
    {
        var acc = Account.Load();
        if (acc == null || Cloud.TestUrl != null) return 0;
        var remote = await Cloud.GetProfilesAsync(acc.Id);
        var have = new HashSet<string>(Store.List(), StringComparer.Ordinal);
        var remoteNames = new HashSet<string>(StringComparer.Ordinal);
        int imported = 0;
        if (remote is { ValueKind: JsonValueKind.Object } o)
        {
            foreach (var p in o.EnumerateObject())
            {
                if (!Store.ValidName(p.Name)) continue;
                remoteNames.Add(p.Name);
                if (have.Contains(p.Name)) continue;
                var e = p.Value;
                if (e.ValueKind != JsonValueKind.Object || e.TryGetProperty("d", out _)) continue;   // «могила»: на другом устройстве удалили
                if (!e.TryGetProperty("n", out var nn) || !e.TryGetProperty("c", out var cc) || nn.ValueKind != JsonValueKind.String || cc.ValueKind != JsonValueKind.String) continue;
                var conf = Decrypt(acc.Key, p.Name, nn.GetString()!, cc.GetString()!);
                if (conf == null || Store.Validate(conf) != null) continue;
                Store.Save(p.Name, conf, sync: false);
                imported++;
            }
        }
        foreach (var name in have)
            if (!remoteNames.Contains(name)) await PutAsync(acc, name, false);
        return imported;
    }
}
