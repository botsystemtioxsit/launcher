using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BattleLauncher;

record Profile(string Id, string Name, string Role, string Status, string PlayerId);
record TokenInfo(string Label, long? ExpiresAt);

// Минимальный клиент Firebase Realtime Database поверх REST — без SDK и без Firebase Auth
// (проект на бесплатном плане Spark). База открыта на чтение и запись, поэтому всё прочитанное
// отсюда — подсказка для интерфейса, а не доказательство личности: настоящий замок — подтверждение
// входа человеком в Telegram.
static class Rtdb
{
    const string DbUrl = "https://zolotaya-kletka-default-rtdb.firebaseio.com";
    const string Gateway = "https://battle-role-gateway.tioxsit.workers.dev";

    public static string EmailToKey(string email) => email.Trim().ToLowerInvariant().Replace('.', ',');

    public static bool IsValidEmail(string email)
    {
        var s = email.Trim();
        var at = s.IndexOf('@');
        return s.Length <= 254 && at > 0 && s.IndexOf('.', at) > at + 1 && !s.Contains(' ') && s.LastIndexOf('.') < s.Length - 1;
    }

    // Значение узла как текст: строка или число (telegramId бывает и тем и другим); иначе null.
    static async Task<string?> GetScalar(string path, CancellationToken ct = default)
    {
        var text = await Net.GetText($"{DbUrl}/{path}.json", ct);
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.ValueKind switch
        {
            JsonValueKind.String => doc.RootElement.GetString(),
            JsonValueKind.Number => doc.RootElement.GetRawText(),
            _ => null,
        };
    }

    static void CheckId(string id)
    {
        if (!Net.SafeId.IsMatch(id)) throw new ArgumentException("Некорректный id");
    }

    // Поля читаем по одному: в users/<id> лежит аватарка в base64, целиком не нужна.
    public static async Task<Profile> GetProfile(string id, CancellationToken ct = default)
    {
        CheckId(id);
        var name = GetScalar($"users/{id}/firstName", ct);
        var role = GetScalar($"users/{id}/role", ct);
        var status = GetScalar($"users/{id}/status", ct);
        var pid = GetScalar($"users/{id}/playerId", ct);
        await Task.WhenAll(name, role, status, pid);
        static string Cut(string? s, int n) => (s ?? "").Length > n ? s![..n] : s ?? "";
        return new Profile(id, Cut(name.Result, 40), string.IsNullOrEmpty(role.Result) ? "user" : Cut(role.Result, 20),
            string.IsNullOrEmpty(status.Result) ? "active" : Cut(status.Result, 20), Cut(pid.Result, 24));
    }

    public static async Task<string?> FindAccountByEmail(string email, CancellationToken ct = default)
    {
        var id = await GetScalar($"emailLogins/{Uri.EscapeDataString(EmailToKey(email))}", ct);
        return id != null && Net.SafeId.IsMatch(id) ? id : null;
    }

    public static async Task<string> CreateLoginRequest(string targetId, CancellationToken ct = default)
    {
        CheckId(targetId);
        // Метка устройства попадает в сообщение бота — Worker принимает только буквы/цифры/пробел и . _ ( ) , / -
        var body = JsonSerializer.Serialize(new { device = "Лаунчер (Windows)", app = "launcher", requestedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), status = "pending" });
        using var res = await Net.Http.PostAsync($"{DbUrl}/users/{targetId}/loginRequests.json", new StringContent(body, Encoding.UTF8, "application/json"), ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var requestId = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() : null;
        if (requestId == null || !Net.SafeId.IsMatch(requestId)) throw new InvalidOperationException("База не вернула номер заявки");
        // Бот пишет владельцу в Telegram с кнопками «Разрешить/Отклонить». Не вышло — заявку
        // всё равно можно подтвердить в игре или в админ-панели.
        var notify = JsonSerializer.Serialize(new { telegramId = targetId, requestId });
        _ = Net.Http.PostAsync($"{Gateway}/login-notify", new StringContent(notify, Encoding.UTF8, "application/json")).ContinueWith(t => { _ = t.Exception; });
        return requestId;
    }

    public static async Task<string?> GetLoginRequestStatus(string targetId, string requestId, CancellationToken ct = default)
    {
        CheckId(targetId); CheckId(requestId);
        return await GetScalar($"users/{targetId}/loginRequests/{requestId}/status", ct);
    }

    public static async Task RemoveLoginRequest(string targetId, string requestId)
    {
        try
        {
            CheckId(targetId); CheckId(requestId);
            using var res = await Net.Http.DeleteAsync($"{DbUrl}/users/{targetId}/loginRequests/{requestId}.json");
        }
        catch { /* заявка и так сама протухнет */ }
    }

    // ---------- токен доступа (accessTokens/<token>) ----------
    // null — такого токена нет (отозван или не существует). Токен читается всеми, как и вся база,
    // поэтому «вошёл по токену» — это удобство, а не защита: настоящий контроль — у Супердоступа,
    // который может отозвать токен.
    public static async Task<TokenInfo?> GetAccessToken(string token, CancellationToken ct = default)
    {
        CheckId(token);
        var text = await Net.GetText($"{DbUrl}/accessTokens/{token}.json", ct);
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        var label = root.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() ?? "" : "";
        long? exp = root.TryGetProperty("expiresAt", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt64() : null;
        return new TokenInfo(label.Length > 40 ? label[..40] : label, exp);
    }

    // ---------- подключение устройства по коду (devicePairings/<код>) ----------
    // sha256(deviceId) — в базу уходит только он; сам deviceId живёт на этом компьютере
    public static string DeviceKeyOf(string deviceId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(deviceId))).ToLowerInvariant();

    // без 0/O/1/I — на слух и на глаз легко перепутать при диктовке кода
    public static string RandomPairingCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = RandomNumberGenerator.GetBytes(6);
        return new string(bytes.Select(b => alphabet[b % alphabet.Length]).ToArray());
    }

    public static async Task CreatePairing(string code, string deviceKey, CancellationToken ct = default)
    {
        CheckId(code);
        if ((await GetScalar($"devicePairings/{code}/status", ct)) != null) throw new InvalidOperationException("Код занят, попробуй ещё раз");
        var body = JsonSerializer.Serialize(new { deviceKey, device = "Лаунчер / Windows", requestedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), status = "pending" });
        using var res = await Net.Http.PutAsync($"{DbUrl}/devicePairings/{code}.json", new StringContent(body, Encoding.UTF8, "application/json"), ct);
        res.EnsureSuccessStatusCode();
    }

    // (status, telegramId): status null — запись пропала
    public static async Task<(string? Status, string? TelegramId)> GetPairing(string code, CancellationToken ct = default)
    {
        CheckId(code);
        var status = GetScalar($"devicePairings/{code}/status", ct);
        var tg = GetScalar($"devicePairings/{code}/telegramId", ct);
        await Task.WhenAll(status, tg);
        return (status.Result, tg.Result != null && Net.SafeId.IsMatch(tg.Result) ? tg.Result : null);
    }

    public static async Task RemovePairing(string code)
    {
        try
        {
            CheckId(code);
            using var res = await Net.Http.DeleteAsync($"{DbUrl}/devicePairings/{code}.json");
        }
        catch { /* запись и так сама протухнет */ }
    }

    // Запись о доверенном устройстве живёт, пока владелец не нажал «Отозвать» в панели.
    // true/false — есть/нет; сетевые ошибки пробрасываются (не путать «нет связи» с «отозвано»).
    public static async Task<bool> IsDeviceTrusted(string userId, string deviceKey, CancellationToken ct = default)
    {
        CheckId(userId);
        if (!System.Text.RegularExpressions.Regex.IsMatch(deviceKey, "^[0-9a-f]{64}$")) throw new ArgumentException("Некорректный ключ устройства");
        return await GetScalar($"users/{userId}/trustedDevices/{deviceKey}/sig", ct) != null;
    }
}
