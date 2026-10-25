package com.tioxsit.launcher

import org.json.JSONObject
import org.json.JSONTokener
import java.security.MessageDigest
import java.security.SecureRandom

data class Profile(val id: String, val name: String, val role: String, val status: String, val playerId: String)
data class TokenInfo(val label: String, val expiresAt: Long?)

// Минимальный клиент Firebase Realtime Database поверх REST — без SDK и без Firebase Auth
// (проект на бесплатном плане Spark). База открыта на чтение и запись, поэтому всё прочитанное
// отсюда — подсказка для интерфейса, а не доказательство личности: настоящий замок — подтверждение
// входа человеком в Telegram.
object Rtdb {
    private const val DB = "https://zolotaya-kletka-default-rtdb.firebaseio.com"
    private const val GATEWAY = "https://battle-role-gateway.tioxsit.workers.dev"

    fun emailToKey(email: String) = email.trim().lowercase().replace('.', ',')

    fun isValidEmail(email: String): Boolean {
        val s = email.trim()
        val at = s.indexOf('@')
        return s.length <= 254 && at > 0 && s.indexOf('.', at) > at + 1 && !s.contains(' ') && s.lastIndexOf('.') < s.length - 1
    }

    // Значение узла как текст: строка или число (telegramId бывает и тем и другим); иначе null.
    private fun scalar(path: String): String? {
        val v = JSONTokener(Net.get("$DB/$path.json")).nextValue()
        return when (v) {
            is String -> v
            is Number -> v.toString()
            else -> null
        }
    }

    private fun checkId(id: String) {
        if (!Net.SAFE_ID.matches(id)) throw IllegalArgumentException("Некорректный id")
    }

    // Поля читаем по одному: в users/<id> лежит аватарка в base64, целиком не нужна.
    fun getProfile(id: String): Profile {
        checkId(id)
        fun cut(s: String?, n: Int) = (s ?: "").take(n)
        val name = scalar("users/$id/firstName")
        val role = scalar("users/$id/role")
        val status = scalar("users/$id/status")
        val pid = scalar("users/$id/playerId")
        return Profile(id, cut(name, 40), if (role.isNullOrEmpty()) "user" else cut(role, 20),
            if (status.isNullOrEmpty()) "active" else cut(status, 20), cut(pid, 24))
    }

    fun findAccountByEmail(email: String): String? {
        val id = scalar("emailLogins/" + java.net.URLEncoder.encode(emailToKey(email), "UTF-8"))
        return if (id != null && Net.SAFE_ID.matches(id)) id else null
    }

    fun createLoginRequest(targetId: String): String {
        checkId(targetId)
        // Метка устройства попадает в сообщение бота — Worker принимает только буквы/цифры/пробел и . _ ( ) , / -
        val body = JSONObject().put("device", "Лаунчер (Android)").put("app", "launcher")
            .put("requestedAt", System.currentTimeMillis()).put("status", "pending").toString()
        val res = JSONObject(Net.send("POST", "$DB/users/$targetId/loginRequests.json", body))
        val requestId = res.optString("name", "")
        if (!Net.SAFE_ID.matches(requestId)) throw IllegalStateException("База не вернула номер заявки")
        // Бот пишет владельцу в Telegram с кнопками «Разрешить/Отклонить». Не вышло — заявку
        // всё равно можно подтвердить в игре или в админ-панели.
        try { Net.send("POST", "$GATEWAY/login-notify", JSONObject().put("telegramId", targetId).put("requestId", requestId).toString()) } catch (_: Exception) { }
        return requestId
    }

    fun loginRequestStatus(targetId: String, requestId: String): String? {
        checkId(targetId); checkId(requestId)
        return scalar("users/$targetId/loginRequests/$requestId/status")
    }

    fun removeLoginRequest(targetId: String, requestId: String) {
        try { checkId(targetId); checkId(requestId); Net.send("DELETE", "$DB/users/$targetId/loginRequests/$requestId.json", null) } catch (_: Exception) { }
    }

    // null — такого токена нет (отозван или не существует)
    fun getAccessToken(token: String): TokenInfo? {
        checkId(token)
        val v = JSONTokener(Net.get("$DB/accessTokens/$token.json")).nextValue()
        if (v !is JSONObject) return null
        val label = v.optString("label", "").take(40)
        val exp = if (v.has("expiresAt") && v.opt("expiresAt") is Number) v.getLong("expiresAt") else null
        return TokenInfo(label, exp)
    }

    // sha256(deviceId) — в базу уходит только он; сам deviceId живёт на этом телефоне
    fun deviceKeyOf(deviceId: String): String =
        MessageDigest.getInstance("SHA-256").digest(deviceId.toByteArray(Charsets.UTF_8)).joinToString("") { "%02x".format(it) }

    // без 0/O/1/I — на слух и на глаз легко перепутать при диктовке кода
    fun randomPairingCode(): String {
        val alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"
        val bytes = ByteArray(6).also { SecureRandom().nextBytes(it) }
        return bytes.map { alphabet[(it.toInt() and 0xff) % alphabet.length] }.joinToString("")
    }

    class CodeTaken : Exception()

    fun createPairing(code: String, deviceKey: String) {
        checkId(code)
        if (scalar("devicePairings/$code/status") != null) throw CodeTaken()
        val body = JSONObject().put("deviceKey", deviceKey).put("device", "Лаунчер / Android")
            .put("requestedAt", System.currentTimeMillis()).put("status", "pending").toString()
        Net.send("PUT", "$DB/devicePairings/$code.json", body)
    }

    // (status, telegramId): status null — запись пропала
    fun getPairing(code: String): Pair<String?, String?> {
        checkId(code)
        val status = scalar("devicePairings/$code/status")
        val tg = scalar("devicePairings/$code/telegramId")
        return status to (if (tg != null && Net.SAFE_ID.matches(tg)) tg else null)
    }

    fun removePairing(code: String) {
        try { checkId(code); Net.send("DELETE", "$DB/devicePairings/$code.json", null) } catch (_: Exception) { }
    }
}
