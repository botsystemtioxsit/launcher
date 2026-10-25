package com.tioxsit.launcher

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import org.json.JSONArray
import org.json.JSONObject
import java.security.KeyStore
import java.security.MessageDigest
import java.security.SecureRandom
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

// Секреты (токен доступа, deviceId) лежат в файле только зашифрованными ключом из Android Keystore:
// ключ не покидает телефон, поэтому скопированный файл бесполезен.
object Secrets {
    private const val ALIAS = "tioxsit_launcher_key"

    private fun key(): SecretKey {
        val ks = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (ks.getKey(ALIAS, null) as? SecretKey)?.let { return it }
        val gen = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
        gen.init(
            KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .build()
        )
        return gen.generateKey()
    }

    fun protect(plain: String): String {
        val c = Cipher.getInstance("AES/GCM/NoPadding")
        c.init(Cipher.ENCRYPT_MODE, key())
        val ct = c.doFinal(plain.toByteArray(Charsets.UTF_8))
        return Base64.encodeToString(c.iv + ct, Base64.NO_WRAP)
    }

    fun unprotect(text: String?): String? {
        if (text.isNullOrEmpty()) return null
        return try {
            val raw = Base64.decode(text, Base64.NO_WRAP)
            val c = Cipher.getInstance("AES/GCM/NoPadding")
            c.init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, raw.copyOfRange(0, 12)))
            String(c.doFinal(raw, 12, raw.size - 12), Charsets.UTF_8)
        } catch (e: Exception) { null }
    }
}

data class Account(
    val id: String,
    val kind: String,            // "account" | "token"
    val name: String,
    val role: String,
    val playerId: String,
    val paired: Boolean,
    val tokenProtected: String?,
) {
    val isToken get() = kind == "token"
    val display get() = if (name.isNotEmpty()) name else if (isToken) "Токен доступа" else "Аккаунт $id"
    val roleName get() = when (role) {
        "moderator" -> "Модератор"; "admin" -> "Админ"; "superadmin" -> "Суперадмин"
        "user" -> "Игрок"; "token" -> "Токен доступа"; else -> role
    }
}

// Список аккаунтов и текущий. Открытым текстом тут только то, что и так лежит в открытой базе
// (id, имя, роль); секреты — через Secrets.protect.
class Store(ctx: Context) {
    private val prefs = ctx.getSharedPreferences("launcher", Context.MODE_PRIVATE)
    var accounts: List<Account> = load()
        private set
    var activeId: String? = prefs.getString("active", null)
        private set

    val active: Account? get() = accounts.firstOrNull { it.id == activeId }

    private fun load(): List<Account> = try {
        val arr = JSONArray(prefs.getString("accounts", "[]"))
        (0 until arr.length()).map { arr.getJSONObject(it) }.map {
            Account(it.getString("id"), it.optString("kind", "account"), it.optString("name"), it.optString("role", "user"),
                it.optString("playerId"), it.optBoolean("paired"), it.optString("token").ifEmpty { null })
        }.filter { Net.SAFE_ID.matches(it.id) }.take(10)
    } catch (e: Exception) { emptyList() }

    private fun save() {
        val arr = JSONArray()
        accounts.forEach {
            arr.put(JSONObject().put("id", it.id).put("kind", it.kind).put("name", it.name).put("role", it.role)
                .put("playerId", it.playerId).put("paired", it.paired).put("token", it.tokenProtected ?: ""))
        }
        prefs.edit().putString("accounts", arr.toString()).putString("active", activeId).apply()
    }

    // deviceId — секрет этого телефона (в базу уходит только его sha256, см. Rtdb.deviceKeyOf)
    fun deviceId(): String {
        Secrets.unprotect(prefs.getString("device", null))?.let { return it }
        val id = ByteArray(16).also { SecureRandom().nextBytes(it) }.joinToString("") { "%02x".format(it) }
        prefs.edit().putString("device", Secrets.protect(id)).apply()
        return id
    }

    private fun put(acc: Account) {
        accounts = (listOf(acc) + accounts.filter { it.id != acc.id }).take(10)
        activeId = acc.id
        save()
    }

    fun upsert(p: Profile, paired: Boolean) =
        put(Account(p.id, "account", p.name, p.role, p.playerId, paired, null))

    fun upsertToken(token: String, label: String) {
        val id = "t_" + MessageDigest.getInstance("SHA-256").digest(token.toByteArray()).joinToString("") { "%02x".format(it) }.take(16)
        put(Account(id, "token", label, "token", "", false, Secrets.protect(token)))
    }

    fun setActive(id: String) { if (accounts.any { it.id == id }) { activeId = id; save() } }

    fun signOut() { activeId = null; save() }

    fun remove(id: String) {
        accounts = accounts.filter { it.id != id }
        if (activeId == id) activeId = null
        save()
    }
}
