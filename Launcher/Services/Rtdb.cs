using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace BattleLauncher;

record Profile(string Id, string Name, string Role, string Status, string PlayerId);

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
}
