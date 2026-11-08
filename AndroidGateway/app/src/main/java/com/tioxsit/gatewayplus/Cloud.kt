package com.tioxsit.gatewayplus

import android.content.Context
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject
import java.io.IOException
import java.util.concurrent.TimeUnit

// Общая база проекта (Firebase Realtime Database, REST, без SDK и без Firebase Auth) — та же отдельная база vpn0dns, что у плагина для компьютера.
// Узлы: gatewayAccounts/<номер> (аккаунт), gatewayBlocks/<номер> и gatewayBlockedDevices/<телефон> (блокировки — пишет только Worker, приложение читает по точному ключу).
// Правила базы — gateway-db/database.rules.json в этом репозитории.
object Cloud {
    const val DB_URL = "https://vpn0dns-default-rtdb.europe-west1.firebasedatabase.app"
    private val ID_RE = Regex("^[0-9a-f]{24}$")
    private val DEV_RE = Regex("^[0-9a-f]{32}$")
    private val JSON_TYPE = "application/json".toMediaType()
    private val http = OkHttpClient.Builder().connectTimeout(10, TimeUnit.SECONDS).readTimeout(10, TimeUnit.SECONDS).writeTimeout(10, TimeUnit.SECONDS).build()
    private val NOW = JSONObject().put(".sv", "timestamp")

    class CloudException(m: String) : Exception(m)

    // Результат проверки блокировки. offline = true: связи нет, показан последний известный ответ.
    data class Access(val blocked: Boolean, val reason: String, val offline: Boolean)

    private fun get(path: String): JSONObject? {
        val req = Request.Builder().url("$DB_URL/$path.json").get().build()
        http.newCall(req).execute().use { res ->
            if (!res.isSuccessful) throw CloudException("база ответила HTTP ${res.code}")
            val text = res.body?.string()?.trim().orEmpty()
            if (text.isEmpty() || text == "null") return null
            return if (text.startsWith("{")) JSONObject(text) else JSONObject().put("value", text)
        }
    }

    private fun send(method: String, path: String, body: JSONObject) {
        val req = Request.Builder().url("$DB_URL/$path.json").method(method, body.toString().toRequestBody(JSON_TYPE)).build()
        http.newCall(req).execute().use { res ->
            if (!res.isSuccessful) throw CloudException("база не приняла запись (HTTP ${res.code})")
        }
    }

    private fun reasonOf(o: JSONObject?): String {
        val r = Account.cleanReason(o?.optString("reason", ""))
        return r.ifEmpty { "причина не указана" }
    }

    // Последний известный ответ — чтобы без связи заблокированный не стал «свободным», просто отключив интернет.
    private fun prefs(ctx: Context) = ctx.getSharedPreferences("access", Context.MODE_PRIVATE)

    fun checkAccess(ctx: Context, id: String?, dev: String): Access {
        return try {
            if (!DEV_RE.matches(dev) || (id != null && !ID_RE.matches(id))) throw CloudException("плохой номер")
            val a = if (id == null) null else get("gatewayBlocks/$id")
            val d = get("gatewayBlockedDevices/$dev")
            val res = when {
                a != null -> Access(true, reasonOf(a), false)
                d != null -> Access(true, reasonOf(d) + " (заблокирован этот телефон)", false)
                else -> Access(false, "", false)
            }
            prefs(ctx).edit().putBoolean("blocked", res.blocked).putString("reason", res.reason).apply()
            res
        } catch (e: Exception) {
            val p = prefs(ctx)
            Access(p.getBoolean("blocked", false), p.getString("reason", "") ?: "", true)
        }
    }

    // Есть ли аккаунт с таким номером. Возвращает принятую версию правил (>= 0) или null, если аккаунта нет. Бросает CloudException без связи.
    fun accountTermsVersion(id: String): Int? {
        if (!ID_RE.matches(id)) throw CloudException("плохой номер аккаунта")
        try {
            val o = get("gatewayAccounts/$id") ?: return null
            return o.optInt("termsV", 0)
        } catch (e: CloudException) {
            throw e
        } catch (e: IOException) {
            throw CloudException("нет связи с базой")
        }
    }

    // Новый аккаунт. Дату создания ставит сервер (правила не дают её менять).
    fun register(id: String, name: String, dev: String, ver: String, servers: Int, termsV: Int = Terms.VERSION) {
        if (!ID_RE.matches(id) || !DEV_RE.matches(dev)) throw CloudException("плохой номер")
        try {
            send("PUT", "gatewayAccounts/$id", JSONObject()
                .put("name", name).put("createdAt", NOW).put("lastSeen", NOW).put("termsV", termsV.coerceIn(0, 100))
                .put("ver", "android-$ver").put("dev", dev).put("servers", servers.coerceIn(0, 50)))
        } catch (e: CloudException) {
            throw e
        } catch (e: IOException) {
            throw CloudException("нет связи с базой")
        }
    }

    // «Пульс»: когда заходили, версия, сколько серверов, какие правила приняты, на каком телефоне. Ошибки молча пропускаются.
    // Аккаунты, не заходившие полгода, база удаляет сама (задача «Cleanup Gateway accounts»); правила не дают дописать поля
    // в несуществующую запись, поэтому если записи нет — заводим её заново под тем же номером и именем (ключ у человека прежний).
    fun heartbeat(id: String, dev: String, ver: String, servers: Int, termsV: Int, name: String? = null) {
        if (!ID_RE.matches(id) || !DEV_RE.matches(dev)) return
        try {
            send("PATCH", "gatewayAccounts/$id", JSONObject()
                .put("lastSeen", NOW).put("ver", "android-$ver").put("dev", dev).put("servers", servers.coerceIn(0, 50)).put("termsV", termsV.coerceIn(0, 100)))
        } catch (_: Exception) {
            if (name.isNullOrEmpty()) return
            try { if (get("gatewayAccounts/$id") == null) register(id, name, dev, ver, servers, termsV) } catch (_: Exception) { /* нет связи — в следующий раз */ }
        }
    }

    // Принятые правила: пишем в аккаунт новую версию (если у аккаунта их не было или правила обновились).
    fun acceptTerms(id: String, dev: String, ver: String, servers: Int, name: String? = null) = heartbeat(id, dev, ver, servers, Terms.VERSION, name)
}
