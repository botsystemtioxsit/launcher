using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GatewayPlusApp;

// Общая база проекта (Firebase Realtime Database, REST, без SDK и без Firebase Auth). Узлы Gateway Plus: gatewayAccounts/<id> (аккаунт),
// gatewayBlocks/<id> и gatewayBlockedDevices/<dev> (блокировки — их пишет только Worker, плагин читает по точному ключу). Правила базы — в репозитории index.html.
// С 2.1.0 это ОТДЕЛЬНАЯ база Firebase (проект vpn0dns) со строгими правилами: gateway-db/database.rules.json в этом репозитории.
static class Cloud
{
    public const string DefaultDbUrl = "https://vpn0dns-default-rtdb.europe-west1.firebasedatabase.app";
    public static string? TestUrl;   // только для самопроверки (подставная база на этом компьютере)
    static string Base => (TestUrl ?? DefaultDbUrl).TrimEnd('/');
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    static readonly Regex IdRe = new("^[0-9a-f]{24}$"), DevRe = new("^[0-9a-f]{32}$");

    public sealed record Access(bool Blocked, string Reason, bool Offline);
    public sealed class CloudException : Exception { public CloudException(string m) : base(m) { } }

    // Последний известный ответ — чтобы без связи заблокированный не стал «свободным», просто отключив интернет.
    sealed class Cache { public bool Blocked { get; set; } public string Reason { get; set; } = ""; public long At { get; set; } }
    static string CacheFile => Path.Combine(Store.Dir, "access.json");
    static Cache LoadCache() { try { return JsonSerializer.Deserialize<Cache>(File.ReadAllText(CacheFile)) ?? new Cache(); } catch { return new Cache(); } }
    static void SaveCache(bool blocked, string reason)
    {
        try { Directory.CreateDirectory(Store.Dir); File.WriteAllText(CacheFile, JsonSerializer.Serialize(new Cache { Blocked = blocked, Reason = reason, At = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() })); } catch { }
    }

    static async Task<JsonElement?> GetAsync(string path)
    {
        using var res = await Http.GetAsync($"{Base}/{path}.json");
        if (!res.IsSuccessStatusCode) throw new CloudException("база ответила HTTP " + (int)res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.ValueKind == JsonValueKind.Null ? null : doc.RootElement.Clone();
    }

    static async Task SendAsync(HttpMethod method, string path, object body)
    {
        using var req = new HttpRequestMessage(method, $"{Base}/{path}.json") { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        using var res = await Http.SendAsync(req);
        if (!res.IsSuccessStatusCode) throw new CloudException("база не приняла запись (HTTP " + (int)res.StatusCode + ")");
    }

    static readonly Dictionary<string, string> Now = new() { [".sv"] = "timestamp" };

    static string ReasonOf(JsonElement? el)
    {
        string r = "";
        if (el is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty("reason", out var v) && v.ValueKind == JsonValueKind.String) r = Account.CleanReason(v.GetString());
        return r.Length > 0 ? r : "причина не указана";
    }

    // Заблокированы ли аккаунт или этот компьютер. Нет связи → последний известный ответ (Offline = true).
    public static async Task<Access> CheckAccessAsync(string? id, string dev)
    {
        try
        {
            if (!DevRe.IsMatch(dev) || (id != null && !IdRe.IsMatch(id))) throw new CloudException("плохой номер");
            var a = id == null ? null : await GetAsync("gatewayBlocks/" + id);
            var d = await GetAsync("gatewayBlockedDevices/" + dev);
            var res = a != null ? new Access(true, ReasonOf(a), false)
                    : d != null ? new Access(true, ReasonOf(d) + " (заблокирован этот компьютер)", false)
                    : new Access(false, "", false);
            SaveCache(res.Blocked, res.Reason);
            return res;
        }
        catch
        {
            var c = LoadCache();
            return new Access(c.Blocked, c.Reason, true);
        }
    }

    public sealed record Remote(string Name);

    public static async Task<Remote?> GetAccountAsync(string id)
    {
        if (!IdRe.IsMatch(id)) throw new CloudException("плохой номер аккаунта");
        try
        {
            var el = await GetAsync("gatewayAccounts/" + id);
            if (el is not { ValueKind: JsonValueKind.Object } o) return null;
            return new Remote(o.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "");
        }
        catch (CloudException) { throw; }
        catch (Exception ex) { throw new CloudException("нет связи с базой: " + ex.Message); }
    }

    // Новый аккаунт. Дату создания ставит сервер (правила не дают её менять).
    public static async Task RegisterAsync(string id, string name, string dev, string ver, int servers)
    {
        if (!IdRe.IsMatch(id) || !DevRe.IsMatch(dev)) throw new CloudException("плохой номер");
        try
        {
            await SendAsync(HttpMethod.Put, "gatewayAccounts/" + id, new Dictionary<string, object>
            {
                ["name"] = name, ["createdAt"] = Now, ["lastSeen"] = Now, ["termsV"] = Terms.Version, ["ver"] = ver, ["dev"] = dev, ["servers"] = Math.Clamp(servers, 0, 50),
            });
        }
        catch (CloudException) { throw; }
        catch (Exception ex) { throw new CloudException("нет связи с базой: " + ex.Message); }
    }

    // «Пульс»: когда заходили, версия, сколько серверов, какие правила приняты, на каком компьютере.
    public static async Task HeartbeatAsync(string id, string dev, string ver, int servers, int termsV, string? name = null)
    {
        if (!IdRe.IsMatch(id) || !DevRe.IsMatch(dev)) return;
        try
        {
            await SendAsync(HttpMethod.Patch, "gatewayAccounts/" + id, new Dictionary<string, object>
            {
                ["lastSeen"] = Now, ["ver"] = ver, ["dev"] = dev, ["servers"] = Math.Clamp(servers, 0, 50), ["termsV"] = Math.Clamp(termsV, 0, 100),
            });
        }
        catch
        {
            // Аккаунт был заведён в прежней (общей) базе, а в новой записи ещё нет: правила не дают дописать поля в несуществующую
            // запись — заводим её заново под тем же номером и именем (ключ у человека прежний, ничего вводить не нужно).
            if (name != null && name.Length > 0)
            {
                try { if (await GetAsync("gatewayAccounts/" + id) == null) await RegisterAsync(id, name, dev, ver, servers); }
                catch { /* нет связи — в следующий раз */ }
            }
        }
    }
}
