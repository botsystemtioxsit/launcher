package com.tioxsit.gatewayplus

// Профили (конфиги WireGuard) без Android-классов — их проверяют обычные тесты (ProfilesTest).
object Profiles {
    val NAME_RE = Regex("^[A-Za-z0-9_=+.\\-]{1,32}$")

    fun validName(s: String) = NAME_RE.matches(s)

    fun safeName(s: String): String {
        var t = s.trim().replace(Regex("[^A-Za-z0-9_=+.\\-]"), "_").trim('_')
        if (t.length > 32) t = t.substring(0, 32)
        return t.ifEmpty { "gateway" }
    }

    // Профиль GSP (наш протокол): первая строка «# GSP» или строка FEC =. На телефоне пока не включается.
    fun isGsp(conf: String): Boolean {
        val first = conf.replace("\r", "").split('\n').map { it.trim() }.firstOrNull { it.isNotEmpty() }
        if (first != null && first.equals("# GSP", ignoreCase = true)) return true
        return Regex("(?im)^\\s*FEC\\s*=").containsMatchIn(conf)
    }

    private fun lines(conf: String) = conf.replace("\r", "").split('\n').map { it.trim() }.filter { it.isNotEmpty() && !it.startsWith("#") }

    private fun has(lines: List<String>, key: String) =
        lines.any { it.startsWith(key, ignoreCase = true) && it.substring(key.length).trimStart().startsWith("=") }

    // Проверка до сохранения и перед подключением: текст проблемы или null. Команды PreUp/PostUp/… не принимаем вовсе.
    fun validate(conf: String): String? {
        if (conf.isBlank()) return "Конфиг пустой."
        if (conf.length > 20000) return "Конфиг слишком большой."
        if (isGsp(conf)) return "Это профиль протокола GSP: на телефоне он пока не включается. Возьмите профиль WireGuard того же сервера (на компьютере: «Телефон и ускорение сервера…» → QR)."
        val l = lines(conf)
        if (l.none { it.equals("[Interface]", ignoreCase = true) }) return "В конфиге нет раздела [Interface]."
        if (l.none { it.equals("[Peer]", ignoreCase = true) }) return "В конфиге нет раздела [Peer] (сервера)."
        if (!has(l, "PrivateKey")) return "В конфиге нет PrivateKey."
        if (!has(l, "PublicKey")) return "В конфиге нет PublicKey сервера."
        if (!has(l, "Endpoint")) return "В конфиге нет Endpoint (адрес сервера)."
        for (bad in listOf("PreUp", "PostUp", "PreDown", "PostDown"))
            if (has(l, bad)) return "В конфиге есть команда $bad: такие конфиги не принимаются."
        for (x in l) if (!(x.startsWith("[") || x.contains('='))) return "В конфиге есть непонятная строка: " + (if (x.length > 40) x.substring(0, 40) + "…" else x)
        return null
    }

    // Адрес сервера из Endpoint без порта (для сведения профилей в серверы и для показа).
    fun endpointHost(conf: String): String {
        for (raw in conf.replace("\r", "").split('\n')) {
            val l = raw.trim()
            if (!l.startsWith("Endpoint", ignoreCase = true)) continue
            val v = l.substring(l.indexOf('=') + 1).trim()
            val i = v.lastIndexOf(':')
            return if (i > 0) v.substring(0, i).trim('[', ']') else v
        }
        return ""
    }

    // Значение строки из раздела [Interface]; пусто, если строки нет.
    fun interfaceValue(conf: String, key: String): String {
        var inIface = false
        for (raw in conf.replace("\r", "").split('\n')) {
            val t = raw.trim()
            if (t.startsWith("[")) { inIface = t.equals("[Interface]", ignoreCase = true); continue }
            if (inIface && t.startsWith(key, ignoreCase = true) && t.substring(key.length).trimStart().startsWith("="))
                return t.substring(t.indexOf('=') + 1).trim()
        }
        return ""
    }

    // Меняет (или добавляет) строку в разделе [Interface]. Остальное остаётся как было.
    fun withInterfaceValue(conf: String, key: String, value: String): String {
        val ls = conf.replace("\r", "").split('\n').toMutableList()
        var inIface = false
        var last = -1
        var done = false
        for (i in ls.indices) {
            val t = ls[i].trim()
            if (t.startsWith("[")) { inIface = t.equals("[Interface]", ignoreCase = true); if (inIface) last = i; continue }
            if (!inIface) continue
            if (t.isNotEmpty()) last = i
            if (t.startsWith(key, ignoreCase = true) && t.substring(key.length).trimStart().startsWith("=")) { ls[i] = "$key = $value"; done = true }
        }
        if (!done) ls.add(last + 1, "$key = $value")
        return ls.joinToString("\n")
    }

    // Конфиг для туннеля: исключённые приложения (по именам пакетов) идут мимо VPN. Если список пуст — строка ExcludedApplications убирается.
    fun forTunnel(conf: String, excluded: List<String>): String {
        val withoutOld = conf.replace("\r", "").split('\n').filterNot { it.trim().startsWith("ExcludedApplications", ignoreCase = true) }.joinToString("\n")
        return if (excluded.isEmpty()) withoutOld else withInterfaceValue(withoutOld, "ExcludedApplications", excluded.joinToString(","))
    }

    // Профили → серверы: по адресу из Endpoint (без учёта регистра). Порядок серверов — по алфавиту, профилей внутри — по алфавиту.
    fun groupByServer(confs: Map<String, String>): List<Pair<String, List<String>>> {
        val by = linkedMapOf<String, MutableList<String>>()
        for ((name, conf) in confs.toSortedMap(String.CASE_INSENSITIVE_ORDER)) {
            val host = endpointHost(conf).ifEmpty { "?" }
            val key = by.keys.firstOrNull { it.equals(host, ignoreCase = true) } ?: host
            by.getOrPut(key) { mutableListOf() }.add(name)
        }
        return by.entries.sortedBy { it.key.lowercase() }.map { it.key to it.value.toList() }
    }
}
