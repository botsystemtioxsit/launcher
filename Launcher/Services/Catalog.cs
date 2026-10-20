namespace BattleLauncher;

record AppDef(string Id, string Name, string Desc, string Repo, string ProductName, string[]? Roles)
{
    public bool IsAllowed(string role) => Roles == null || Roles.Contains(role);
}

// Что умеет ставить лаунчер. Только эти приложения и только из релизов этих репозиториев.
static class Catalog
{
    public const string Owner = "botsystemtioxsit";

    public static readonly AppDef[] Apps =
    {
        new("troll-battle", "Тролль-Баттл", "Голосовые баттлы в отдельном окне — та же игра, что в Telegram.",
            "index.html", "Тролль-Баттл", null),
        new("battle-admin", "БАТТЛ Админ", "Панель администратора: игроки, заявки, журнал, бэкапы.",
            "battle-admin-bot", "БАТТЛ Админ", new[] { "moderator", "admin", "superadmin" }),
    };

    public static AppDef? ById(string id) => Apps.FirstOrDefault(a => a.Id == id);
}
