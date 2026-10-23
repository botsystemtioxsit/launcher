using System.Text.Json;
using BattleLauncher;

namespace TioxsitTerminal;

// Чтение данных админки из базы и разбор их в простые записи.
static class Data
{
    static readonly string[] PlayerFields = { "playerId", "firstName", "username", "role", "points", "starsBalance", "status", "restrictedUntil", "createdAt" };

    // Находит игрока по PLR-коду, @нику или числовому Telegram-id
    public static async Task<PlayerInfo?> FindPlayer(string query)
    {
        query = query.Trim();
        if (query.Length == 0) return null;
        string? uid = null;
        if (query.StartsWith("@"))
        {
            var key = query.TrimStart('@').Trim().ToLowerInvariant();
            if (!Net.SafeId.IsMatch(key)) return null;
            var e = await Db.Get($"usernames/{key}.json");
            uid = e.Sc();
        }
        else if (query.StartsWith("PLR-", StringComparison.OrdinalIgnoreCase))
        {
            var code = query.ToUpperInvariant();
            if (!Net.SafeId.IsMatch(code)) return null;
            var e = await Db.Get(Db.Equal("users", "playerId", code));
            if (e.ValueKind == JsonValueKind.Object) uid = e.EnumerateObject().Select(p => p.Name).FirstOrDefault();
        }
        else uid = query;
        if (string.IsNullOrEmpty(uid) || !Net.SafeId.IsMatch(uid)) return null;
        return await LoadPlayer(uid);
    }

    // Поля читаем по одному: в users/<id> лежит аватарка в base64, целиком не нужна
    public static async Task<PlayerInfo?> LoadPlayer(string uid)
    {
        var tasks = PlayerFields.Select(f => Db.Get($"users/{uid}/{f}.json")).ToArray();
        var stats = Db.Get($"users/{uid}/stats.json");
        await Task.WhenAll(tasks.Cast<Task>().Append(stats));
        var v = tasks.Select(t => t.Result).ToArray();
        if (v[0].Sc().Length == 0 && v[1].Sc().Length == 0) return null;
        var st = stats.Result;
        return new PlayerInfo(uid, v[0].Sc(), v[1].Sc(), v[2].Sc(), v[3].Sc().Length > 0 ? v[3].Sc() : "user", v[4].Lv(), v[5].Lv(),
            v[6].Sc().Length > 0 ? v[6].Sc() : "active", v[7].Lv(), v[8].Lv(),
            st.L("battles"), st.L("wins"), st.L("losses"), st.L("draws"), st.L("refereeBattles"), st.L("audienceLikes"));
    }

    public static async Task<List<LogEntry>> Logs(int n)
    {
        var root = await Db.Get(Db.LastByKey("logs", n));
        var list = new List<LogEntry>();
        if (root.ValueKind == JsonValueKind.Object)
            foreach (var p in root.EnumerateObject())
            {
                var e = p.Value;
                list.Add(new LogEntry(e.L("timestamp"), e.S("actionType"), e.S("actorName"), e.S("actorPlayerId"), e.S("targetName"), e.S("targetPlayerId"), e.S("details")));
            }
        return list.OrderByDescending(x => x.Time).ToList();
    }

    public static async Task<List<ReportEntry>> Reports(int n)
    {
        var root = await Db.Get(Db.LastByKey("reports", n));
        var list = new List<ReportEntry>();
        if (root.ValueKind == JsonValueKind.Object)
            foreach (var p in root.EnumerateObject())
            {
                var e = p.Value;
                list.Add(new ReportEntry(p.Name, e.L("createdAt"), e.S("reason"), e.S("reporterId"), e.S("targetId"), e.S("targetName"), e.S("battleCode"), e.S("status") == "closed"));
            }
        return list.OrderByDescending(x => x.Time).ToList();
    }

    public static async Task<List<ErrorEntry>> Errors(int n)
    {
        var root = await Db.Get(Db.LastByKey("errorReports", n));
        var list = new List<ErrorEntry>();
        if (root.ValueKind == JsonValueKind.Object)
            foreach (var p in root.EnumerateObject())
            {
                var e = p.Value;
                var who = e.S("firstName").Length > 0 ? $"{e.S("firstName")} ({e.S("playerId")})" : e.S("telegramId");
                list.Add(new ErrorEntry(p.Name, e.L("createdAt"), who.Length > 0 ? who : "неизвестно", e.S("log"), e.S("userAgent")));
            }
        return list.OrderByDescending(x => x.Time).ToList();
    }

    public static async Task<StatsData> Stats()
    {
        var users = Db.Count("users"); var online = Db.Count("presence"); var battles = Db.Count("battles");
        var matches = Db.Count("matches"); var clans = Db.Count("clans"); var errors = Db.Count("errorReports");
        var reports = Reports(100);
        var mods = Db.Get(Db.Equal("users", "role", "moderator"));
        var admins = Db.Get(Db.Equal("users", "role", "admin"));
        var supers = Db.Get(Db.Equal("users", "role", "superadmin"));
        await Task.WhenAll(users, online, battles, matches, clans, errors, reports, mods, admins, supers);
        static int N(JsonElement e) => e.ValueKind == JsonValueKind.Object ? e.EnumerateObject().Count() : 0;
        return new StatsData(users.Result, online.Result, battles.Result, matches.Result, clans.Result, reports.Result.Count(r => !r.Closed), errors.Result,
            N(mods.Result), N(admins.Result), N(supers.Result));
    }

    // Админы и Супердоступ (индекс users.role есть в правилах базы)
    public static async Task<List<StaffEntry>> Staff()
    {
        var root = await Db.Get($"users.json?orderBy={Db.Q("\"role\"")}&startAt={Db.Q("\"admin\"")}&endAt={Db.Q("\"superadmin\"")}");
        var list = new List<StaffEntry>();
        if (root.ValueKind == JsonValueKind.Object)
            foreach (var p in root.EnumerateObject())
            {
                var e = p.Value;
                var perms = e.P("permissions");
                var on = perms.ValueKind == JsonValueKind.Object
                    ? string.Join(", ", perms.EnumerateObject().Where(x => x.Value.ValueKind == JsonValueKind.True).Select(x => x.Name)) : "";
                list.Add(new StaffEntry(p.Name, e.S("playerId"), e.S("firstName"), e.S("role"), on));
            }
        return list.OrderBy(x => x.Role == "superadmin" ? 0 : 1).ThenBy(x => x.Name).ToList();
    }

    public static async Task<List<TokenEntry>> Tokens()
    {
        var root = await Db.Get("accessTokens.json");
        var list = new List<TokenEntry>();
        if (root.ValueKind == JsonValueKind.Object)
            foreach (var p in root.EnumerateObject())
            {
                var e = p.Value;
                var perms = e.P("permissions");
                var on = perms.ValueKind == JsonValueKind.Object
                    ? string.Join(", ", perms.EnumerateObject().Where(x => x.Value.ValueKind == JsonValueKind.True).Select(x => x.Name)) : "";
                list.Add(new TokenEntry(p.Name, e.S("label"), e.L("expiresAt"), e.L("createdAt"), on));
            }
        return list.OrderByDescending(x => x.CreatedAt).ToList();
    }

    // Заявки на вход в МОЙ аккаунт (их подтверждает владелец)
    public static async Task<List<LoginRequestEntry>> LoginRequests(string myId)
    {
        var root = await Db.Get($"users/{myId}/loginRequests.json");
        var list = new List<LoginRequestEntry>();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (root.ValueKind == JsonValueKind.Object)
            foreach (var p in root.EnumerateObject())
            {
                var e = p.Value;
                if (e.S("status") != "pending") continue;
                var at = e.L("requestedAt");
                if (Math.Abs(now - at) > 10 * 60 * 1000) continue; // как в админке: старше 10 минут не показываем
                list.Add(new LoginRequestEntry(p.Name, e.S("device"), e.S("app"), at));
            }
        return list.OrderByDescending(x => x.At).ToList();
    }

    public static async Task<(bool Maintenance, string MaintenanceText, bool Lockdown, string LockdownText)> Platform()
    {
        // config целиком не читаем (там секреты) — только известные флаги
        var a = Db.Get("config/maintenanceMode.json"); var b = Db.Get("config/maintenanceMessage.json");
        var c = Db.Get("config/emergencyLockdown.json"); var d = Db.Get("config/emergencyMessage.json");
        await Task.WhenAll(a, b, c, d);
        return (a.Result.ValueKind == JsonValueKind.True, b.Result.Sc(), c.Result.ValueKind == JsonValueKind.True, d.Result.Sc());
    }
}
