using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BattleLauncher;

namespace TioxsitTerminal;

// REST к Realtime Database и role-gateway Worker. База открыта на чтение и запись, поэтому всё, что читается
// отсюда, — подсказка для интерфейса; настоящий замок на банах — Worker (подпись доверенного устройства).
static class Db
{
    const string BaseUrl = "https://zolotaya-kletka-default-rtdb.firebaseio.com";
    public const string Gateway = "https://battle-role-gateway.tioxsit.workers.dev";

    public static string Q(string s) => Uri.EscapeDataString(s);

    // последние N записей узла по ключу (push-ключи идут по времени)
    public static string LastByKey(string node, int n) => $"{node}.json?orderBy={Q("\"$key\"")}&limitToLast={n}";

    public static string Equal(string node, string field, string value) =>
        $"{node}.json?orderBy={Q("\"" + field + "\"")}&equalTo={Q("\"" + value + "\"")}";

    public static async Task<JsonElement> Get(string path, CancellationToken ct = default)
    {
        var text = await Net.GetText($"{BaseUrl}/{path}", ct);
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    // сколько дочерних узлов (shallow: приходят только ключи, без данных)
    public static async Task<int> Count(string node)
    {
        var e = await Get($"{node}.json?shallow=true");
        return e.ValueKind == JsonValueKind.Object ? e.EnumerateObject().Count() : 0;
    }

    public static async Task Write(HttpMethod method, string path, object? body)
    {
        using var req = new HttpRequestMessage(method, $"{BaseUrl}/{path}.json");
        if (method != HttpMethod.Delete) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var res = await Net.Http.SendAsync(req);
        res.EnsureSuccessStatusCode();
    }

    public static Task Put(string path, object? value) => Write(HttpMethod.Put, path, value);
    public static Task Patch(string path, object value) => Write(HttpMethod.Patch, path, value);
    public static Task Delete(string path) => Write(HttpMethod.Delete, path, null);

    public static async Task<string> Push(string node, object value)
    {
        using var res = await Net.Http.PostAsync($"{BaseUrl}/{node}.json", new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json"));
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.S("name");
    }

    // Запись в «Журнал действий» — те же поля, что пишет веб-админка (logAction)
    public static Task Log(Account me, string type, string targetKey, string targetName, string targetPlayerId, string details) =>
        Push("logs", new
        {
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            actorPlayerId = me.Id,
            actorName = (me.Name.Length > 0 ? me.Name : "Админ") + " (Терминал)",
            actionType = type,
            targetKey,
            targetName,
            targetPlayerId,
            details,
        });

    // ---------- role-gateway ----------
    // Личность — аккаунт + deviceId этого компьютера: Worker сверяет sha256(deviceId) с подписанной записью
    // users/<id>/trustedDevices/<ключ>; сырой deviceId в базу не попадает.
    public static async Task SetStatus(Account me, string deviceId, string targetId, string newStatus, long? restrictedUntil)
    {
        var body = JsonSerializer.Serialize(new { telegramId = me.Id, deviceId, targetUserId = targetId, newStatus, restrictedUntil });
        using var res = await Net.Http.PostAsync($"{Gateway}/set-status", new StringContent(body, Encoding.UTF8, "application/json"));
        if (res.IsSuccessStatusCode) return;
        string msg = $"HTTP {(int)res.StatusCode}";
        try
        {
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var err = doc.RootElement.S("error");
            if (err.Length > 0) msg = err;
        }
        catch { /* ответ не JSON — оставляем код */ }
        throw new InvalidOperationException(msg);
    }

    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(20)).ToLowerInvariant();
}
