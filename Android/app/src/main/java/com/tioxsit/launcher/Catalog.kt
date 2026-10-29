package com.tioxsit.launcher

import org.json.JSONObject

// Что умеет ставить лаунчер на Android. Список живёт в catalog.json в репозитории launcher и читается при каждом
// открытии и обновлении списка: новое приложение или новая версия появляется без обновления самого лаунчера.
// Если у приложения в релизе ещё нет APK, кнопка остаётся закрытой и включается сама, когда файл появится.
// Встроенный список — запасной, на случай, если сети нет.
data class AppDef(
    val id: String,
    val name: String,
    val desc: String,
    val repo: String,          // репозиторий APK
    val pkg: String?,          // null — Android-версии пока нет в каталоге
    val apk: String?,
    val tag: String,           // тег релиза с APK
    val roles: Set<String>?,   // null — любой аккаунт игры, но не сессия по токену
    val site: String? = null,  // облегчённая версия — страница в браузере
) {
    fun isAllowed(role: String) = if (roles == null) role != "token" else role in roles
}

// Программы, которые лаунчер не ставит сам (Linux, расширения Chrome): только кнопка «Скачать» со ссылкой на релиз.
data class DownloadDef(val id: String, val name: String, val desc: String, val platform: String, val url: String, val os: Set<String>)

// Плагин (сейчас только тема оформления). Ссылка собирается из проверенных частей и ведёт в наш репозиторий.
data class PluginDef(val id: String, val name: String, val desc: String, val version: String, val url: String, val os: Set<String>)

object Catalog {
    const val OWNER = "botsystemtioxsit"
    const val LAUNCHER_REPO = "launcher"
    const val LAUNCHER_APK = "TIOXSITLauncher.apk"

    private val SAFE = Regex("^[A-Za-z0-9._-]{1,100}$")
    private var lastFetch = 0L

    @Volatile var apps: List<AppDef> = defaults()
        private set

    @Volatile var downloads: List<DownloadDef> = emptyList()
        private set

    @Volatile var plugins: List<PluginDef> = emptyList()
        private set

    private fun defaults() = listOf(
        AppDef("troll-battle", "Тролль-Баттл", "Голосовые баттлы — та же игра, что в Telegram.",
            "index.html", "com.tioxsit.trollbattle.icontest", "troll-battle.apk", "android-latest", null,
            site = "https://botsystemtioxsit.github.io/index.html/"),
        AppDef("battle-admin", "Ghost Shell Terminal", "Панель администратора: игроки, заявки, журнал, бэкапы, ВидеоЧат.",
            "battle-admin-bot", "com.tioxsit.battleadmin.icontest", "battle-admin.apk", "android-latest",
            setOf("moderator", "admin", "superadmin", "token"),
            site = "https://botsystemtioxsit.github.io/battle-admin-bot/public/admin.html"),
        AppDef("videochat", "ВидеоЧат", "Случайные видеоразговоры. Аккаунт тот же, что в «Тролль-Баттле».",
            "videochat", null, null, "android-latest", null),
    )

    fun refresh(force: Boolean = false) {
        if (!force && System.currentTimeMillis() - lastFetch < 5 * 60 * 1000L) return
        try {
            val text = Net.get("https://raw.githubusercontent.com/$OWNER/launcher/main/catalog.json")
            val parsed = parse(text)
            if (parsed.isNotEmpty()) apps = parsed
            downloads = parseDownloads(text)
            plugins = parsePlugins(text)
            lastFetch = System.currentTimeMillis()
        } catch (_: Exception) { /* нет сети или файл повреждён — остаётся прежний список */ }
    }

    // Всё из файла проверяется: имена репозиториев/файлов/пакетов — только безопасные символы, ссылки — только https.
    fun parse(json: String): List<AppDef> {
        val out = ArrayList<AppDef>()
        val arr = JSONObject(json).optJSONArray("apps") ?: return out
        for (i in 0 until arr.length()) {
            try {
                val a = arr.getJSONObject(i)
                val id = a.getString("id")
                if (!Net.SAFE_ID.matches(id)) continue
                val lite = a.optJSONObject("lite")
                val android = a.optJSONObject("android")
                var roles: Set<String>? = null
                a.optJSONArray("roles")?.let { r -> roles = (0 until r.length()).map { r.getString(it) }.filter { SAFE.matches(it) }.toSet() }
                val site = lite?.optString("site", "")?.takeIf { it.startsWith("https://") }
                var repo = lite?.optString("repo", "") ?: ""
                var pkg: String? = null; var apk: String? = null; var tag = "android-latest"
                if (android != null) {
                    val r = android.optString("repo", ""); val p = android.optString("package", ""); val f = android.optString("asset", "")
                    val t = android.optString("tag", "android-latest")
                    if (SAFE.matches(r) && SAFE.matches(p) && SAFE.matches(f) && SAFE.matches(t)) { repo = r; pkg = p; apk = f; tag = t }
                }
                if (!SAFE.matches(repo)) continue
                out.add(AppDef(id, a.getString("name").take(60), a.optString("desc", "").take(200), repo, pkg, apk, tag, roles, site))
            } catch (_: Exception) { /* одну битую запись пропускаем */ }
        }
        return out
    }

    // Раздел "downloads" каталога: ссылка собирается только из проверенных частей и ведёт на релиз нашего же аккаунта.
    fun parseDownloads(json: String): List<DownloadDef> {
        val out = ArrayList<DownloadDef>()
        val arr = try { JSONObject(json).optJSONArray("downloads") } catch (_: Exception) { null } ?: return out
        for (i in 0 until arr.length()) {
            try {
                val d = arr.getJSONObject(i)
                val id = d.getString("id"); val repo = d.getString("repo"); val tag = d.getString("tag"); val asset = d.getString("asset")
                if (!Net.SAFE_ID.matches(id) || !SAFE.matches(repo) || !SAFE.matches(tag) || !SAFE.matches(asset)) continue
                val osArr = d.optJSONArray("os")
                val os = if (osArr == null) emptySet() else (0 until osArr.length()).map { osArr.getString(it) }.filter { SAFE.matches(it) }.toSet()
                out.add(DownloadDef(id, d.getString("name").take(60), d.optString("desc", "").take(240), d.optString("platform", "").take(40),
                    "https://github.com/$OWNER/$repo/releases/download/$tag/$asset", os))
            } catch (_: Exception) { /* одну битую запись пропускаем */ }
        }
        return out
    }

    private val PLUGIN_FILE = Regex("^plugins/[A-Za-z0-9._-]{1,80}\\.json$")

    // Раздел "plugins" каталога: файл должен лежать в plugins/ репозитория launcher, остальное — только безопасные символы.
    fun parsePlugins(json: String): List<PluginDef> {
        val out = ArrayList<PluginDef>()
        val arr = try { JSONObject(json).optJSONArray("plugins") } catch (_: Exception) { null } ?: return out
        for (i in 0 until arr.length()) {
            try {
                val d = arr.getJSONObject(i)
                val id = d.getString("id"); val file = d.getString("file")
                if (d.optString("kind") != "theme" || !Net.SAFE_ID.matches(id) || !PLUGIN_FILE.matches(file)) continue
                val version = d.optString("version", "1").takeIf { SAFE.matches(it) } ?: "1"
                val osArr = d.optJSONArray("os")
                val os = if (osArr == null) emptySet() else (0 until osArr.length()).map { osArr.getString(it) }.filter { SAFE.matches(it) }.toSet()
                out.add(PluginDef(id, d.getString("name").take(60), d.optString("desc", "").take(240), version, "https://raw.githubusercontent.com/$OWNER/$LAUNCHER_REPO/main/$file", os))
            } catch (_: Exception) { /* одну битую запись пропускаем */ }
        }
        return out
    }
}
