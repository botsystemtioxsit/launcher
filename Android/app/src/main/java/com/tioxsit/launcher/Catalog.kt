package com.tioxsit.launcher

// Что умеет ставить лаунчер на Android. Только эти приложения и только из релизов этих репозиториев
// (тег android-latest, файл-APK).
data class AppDef(
    val id: String,
    val name: String,
    val desc: String,
    val repo: String,
    val pkg: String?,          // null — Android-версии пока нет
    val apk: String?,
    val roles: Set<String>?,   // null — любой аккаунт игры, но не сессия по токену
) {
    fun isAllowed(role: String) = if (roles == null) role != "token" else role in roles
}

object Catalog {
    const val OWNER = "botsystemtioxsit"
    const val LAUNCHER_REPO = "launcher"
    const val LAUNCHER_APK = "TIOXSITLauncher.apk"

    val apps = listOf(
        AppDef("troll-battle", "Тролль-Баттл", "Голосовые баттлы — та же игра, что в Telegram.",
            "index.html", "com.tioxsit.trollbattle.icontest", "troll-battle.apk", null),
        AppDef("battle-admin", "БАТТЛ Админ", "Панель администратора: игроки, заявки, журнал, бэкапы, ВидеоЧат.",
            "battle-admin-bot", "com.tioxsit.battleadmin.icontest", "battle-admin.apk", setOf("moderator", "admin", "superadmin", "token")),
        AppDef("videochat", "ВидеоЧат", "Случайные видеоразговоры. Аккаунт тот же, что в «Тролль-Баттле».",
            "videochat", null, null, null),
    )
}
