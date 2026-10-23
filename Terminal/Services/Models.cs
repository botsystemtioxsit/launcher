using System.Text.Json;

namespace TioxsitTerminal;

// Короткие помощники для чтения JSON из базы: нет поля — пустая строка / 0, а не исключение.
static class J
{
    public static JsonElement P(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

    public static string Sc(this JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString() ?? "",
        JsonValueKind.Number => e.GetRawText(),
        JsonValueKind.True => "true",
        _ => "",
    };

    public static string S(this JsonElement e, string name) => e.P(name).Sc();

    public static long Lv(this JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Number) return 0;
        if (e.TryGetInt64(out var n)) return n;
        return e.TryGetDouble(out var d) ? (long)d : 0;
    }

    public static long L(this JsonElement e, string name) => e.P(name).Lv();
}

record PlayerInfo(string Id, string PlayerId, string Name, string Username, string Role, long Points, long Stars, string Status, long RestrictedUntil, long CreatedAt,
    long Battles, long Wins, long Losses, long Draws, long Referee, long Likes);
record LogEntry(long Time, string Type, string Actor, string ActorId, string Target, string TargetId, string Details);
record ReportEntry(string Id, long Time, string Reason, string ReporterId, string TargetId, string TargetName, string BattleCode, bool Closed);
record ErrorEntry(string Id, long Time, string Who, string Text, string Agent);
record StatsData(int Users, int Online, int Battles, int Matches, int Clans, int NewReports, int Errors, int Moderators, int Admins, int Superadmins);
record StaffEntry(string Id, string PlayerId, string Name, string Role, string Permissions);
record TokenEntry(string Token, string Label, long ExpiresAt, long CreatedAt, string Permissions);
record LoginRequestEntry(string Id, string Device, string App, long At);

static class Texts
{
    public static readonly Dictionary<string, string> ActionLabels = new()
    {
        ["report_closed"] = "Жалоба закрыта",
        ["stats_change"] = "Изменение статистики",
        ["ban_temp"] = "Временный бан",
        ["ban_forever"] = "Бан навсегда",
        ["unban"] = "Снятие бана",
        ["badge_given"] = "Выдан значок",
        ["badge_revoked"] = "Снят значок",
        ["badge_created"] = "Создан новый значок",
        ["event_published"] = "Опубликован ивент",
        ["event_removed"] = "Ивент снят с главного экрана",
        ["full_reset"] = "⚠️ Полный сброс перед бета-тестом",
        ["reset_secret_changed"] = "Пароль сброса задан/изменён",
        ["badge_mass_given"] = "Значок выдан всем игрокам",
        ["guests_deleted"] = "Удалены все гостевые аккаунты",
        ["clan_approved"] = "Клан одобрен",
        ["clan_rejected"] = "Клан отклонён",
        ["clan_chat_approved"] = "Ссылка на чат клана одобрена",
        ["clan_chat_rejected"] = "Ссылка на чат клана отклонена",
        ["report_closed"] = "Жалоба закрыта",
        ["role_changed"] = "Изменена роль",
        ["admin_permission_changed"] = "Изменено право администратора",
        ["maintenance_toggled"] = "Режим обслуживания переключён",
        ["maintenance_message_changed"] = "Сообщение режима обслуживания изменено",
        ["emergency_lockdown_toggled"] = "Аварийный режим переключён",
        ["emergency_message_changed"] = "Сообщение аварийного режима изменено",
        ["closed_beta_toggled"] = "Закрытый бета-тест переключён",
        ["beta_application_approved"] = "Заявка на бета-тест одобрена",
        ["beta_application_rejected"] = "Заявка на бета-тест отклонена",
        ["token_collision_detected"] = "Обнаружен дублирующийся токен устройства — аккаунты заблокированы",
        ["player_deleted"] = "Профиль игрока удалён",
        ["vc_ban"] = "ВидеоЧат: блокировка",
        ["vc_unban"] = "ВидеоЧат: блокировка снята",
        ["vc_report_closed"] = "ВидеоЧат: жалоба закрыта",
        ["vcg_action"] = "Комната ВидеоЧата: действие модерации",
        ["vc_comm_approved"] = "ВидеоЧат: чат подключён",
        ["vc_comm_rejected"] = "ВидеоЧат: заявка чата отклонена",
        ["vc_comm_toggled"] = "ВидеоЧат: комната включена/отключена",
        ["vc_comm_subadmin"] = "ВидеоЧат: назначен суб-админ комнаты",
        ["vc_comm_deleted"] = "ВидеоЧат: сообщество удалено"
    };

    public static string Action(string type) => ActionLabels.TryGetValue(type, out var s) ? s : type;

    public static string Reason(string r) => r switch
    {
        "discrimination" => "Дискриминация (национальная, расовая, религиозная)",
        "threats" => "Угрозы или травля",
        "sexual" => "Сексуальные домогательства",
        "spam" => "Спам или реклама",
        _ => "Другое",
    };

    public static string Role(string r) => r switch
    {
        "superadmin" => "Супердоступ",
        "admin" => "Админ",
        "moderator" => "Модератор (устар.)",
        "token" => "Токен доступа",
        _ => "Игрок",
    };

    public static string Status(string s) => s switch
    {
        "banned" => "Забанен",
        "restricted" => "Ограничен",
        _ => "Активен",
    };

    public static string Time(long ms) =>
        ms <= 0 ? "—" : DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("dd.MM.yyyy HH:mm");

    public static string Short(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
