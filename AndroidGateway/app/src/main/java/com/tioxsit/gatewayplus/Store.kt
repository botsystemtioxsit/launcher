package com.tioxsit.gatewayplus

import android.content.Context
import android.provider.Settings
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import org.json.JSONObject
import java.io.File
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

// Всё, что лежит на телефоне: аккаунт и профили. Файлы зашифрованы ключом из Android Keystore — ключ не покидает телефон, поэтому скопированный файл бесполезен.
class Store(private val ctx: Context) {
    private val dir = File(ctx.filesDir, "gateway").apply { mkdirs() }
    private val profilesDir = File(dir, "profiles").apply { mkdirs() }
    private val prefs = ctx.getSharedPreferences("settings", Context.MODE_PRIVATE)

    // ---------- шифрование ----------
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

    private fun protect(plain: String): String {
        val c = Cipher.getInstance("AES/GCM/NoPadding")
        c.init(Cipher.ENCRYPT_MODE, key())
        val ct = c.doFinal(plain.toByteArray(Charsets.UTF_8))
        return Base64.encodeToString(c.iv + ct, Base64.NO_WRAP)
    }

    private fun unprotect(text: String?): String? {
        if (text.isNullOrEmpty()) return null
        return try {
            val raw = Base64.decode(text, Base64.NO_WRAP)
            val c = Cipher.getInstance("AES/GCM/NoPadding")
            c.init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, raw.copyOfRange(0, 12)))
            String(c.doFinal(raw, 12, raw.size - 12), Charsets.UTF_8)
        } catch (e: Exception) { null }
    }

    // ---------- аккаунт ----------
    data class Acct(val nick: String, val key: String, val termsV: Int) { val id: String get() = Account.idOf(key) }

    private val acctFile get() = File(dir, "account.dat")

    fun account(): Acct? {
        return try {
            val raw = unprotect(acctFile.takeIf { it.exists() }?.readText()) ?: return null
            val j = JSONObject(raw)
            Acct(j.getString("nick"), j.getString("key"), j.optInt("termsV", 0)).takeIf { Regex("^[A-Z2-7]{32}$").matches(it.key) }
        } catch (e: Exception) { null }
    }

    fun saveAccount(a: Acct) {
        acctFile.writeText(protect(JSONObject().put("nick", a.nick).put("key", a.key).put("termsV", a.termsV).toString()))
    }

    fun clearAccount() {
        acctFile.delete()
        ctx.getSharedPreferences("access", Context.MODE_PRIVATE).edit().clear().apply()
    }

    // Обезличенный номер телефона для блокировки (см. Account.deviceHash).
    fun deviceHash(): String = Account.deviceHash(Settings.Secure.getString(ctx.contentResolver, Settings.Secure.ANDROID_ID) ?: "")

    // ---------- профили ----------
    fun profileNames(): List<String> = (profilesDir.listFiles() ?: emptyArray()).map { it.name.removeSuffix(".dat") }.filter { Profiles.validName(it) }.sortedBy { it.lowercase() }

    fun loadProfile(name: String): String? {
        if (!Profiles.validName(name)) return null
        val f = File(profilesDir, "$name.dat")
        return unprotect(f.takeIf { it.exists() }?.readText())
    }

    fun saveProfile(name: String, conf: String) {
        require(Profiles.validName(name)) { "плохое имя" }
        File(profilesDir, "$name.dat").writeText(protect(conf))
    }

    fun deleteProfile(name: String) {
        if (Profiles.validName(name)) File(profilesDir, "$name.dat").delete()
        if (active == name) active = ""
    }

    fun allProfiles(): Map<String, String> = profileNames().mapNotNull { n -> loadProfile(n)?.let { n to it } }.toMap()

    // Свободное имя для нового профиля (name, name-2, name-3…).
    fun uniqueName(base: String): String {
        val have = profileNames().map { it.lowercase() }.toSet()
        val b = Profiles.safeName(base)
        if (b.lowercase() !in have) return b
        for (i in 2 until 100) { val n = Profiles.safeName("$b-$i"); if (n.lowercase() !in have) return n }
        return b
    }

    // ---------- настройки ----------
    var active: String
        get() = prefs.getString("active", "") ?: ""
        set(v) { prefs.edit().putString("active", v).apply() }

    // «WhatsApp мимо VPN»: по умолчанию включено (звонки через VPN у многих то слышны, то нет).
    var bypassWhatsApp: Boolean
        get() = prefs.getBoolean("bypassWhatsApp", true)
        set(v) { prefs.edit().putBoolean("bypassWhatsApp", v).apply() }

    companion object { private const val ALIAS = "gatewayplus_android_key" }
}
