package com.tioxsit.gatewayplus

import java.security.MessageDigest
import java.security.SecureRandom
import java.util.Base64
import javax.crypto.Cipher
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

// Серверы (профили) следуют за аккаунтом: на другом телефоне и на компьютере после входа по тому же ключу они появляются сами.
// В профиле лежит закрытый ключ, поэтому в базу он уходит ТОЛЬКО зашифрованным на устройстве: AES-256-GCM, ключ шифрования — sha256 от ключа аккаунта
// (его в базе нет), имя профиля подписано в зашифрованное. Формат совпадает с плагином на компьютере (Plugins/GatewayPlus/ProfileSync.cs):
// gatewayProfiles/<номер аккаунта>/<имя> = {n: nonce (base64), c: шифротекст+тег (base64), at}; удаление — «могила» {d:1, at} (правила базы не дают стирать).
// Файл без Android-классов: его проверяют обычные тесты (ProfileSyncTest).
object ProfileSync {
    private fun keyOf(raw: String): ByteArray = MessageDigest.getInstance("SHA-256").digest("ghostshell.gateway.sync.v1:$raw".toByteArray(Charsets.UTF_8))

    fun encrypt(raw: String, name: String, conf: String, nonce: ByteArray = ByteArray(12).also { SecureRandom().nextBytes(it) }): Pair<String, String> {
        val c = Cipher.getInstance("AES/GCM/NoPadding")
        c.init(Cipher.ENCRYPT_MODE, SecretKeySpec(keyOf(raw), "AES"), GCMParameterSpec(128, nonce))
        c.updateAAD(name.toByteArray(Charsets.UTF_8))
        val ct = c.doFinal(conf.toByteArray(Charsets.UTF_8))
        val b64 = Base64.getEncoder()
        return b64.encodeToString(nonce) to b64.encodeToString(ct)
    }

    fun decrypt(raw: String, name: String, n: String, c: String): String? = try {
        val d = Base64.getDecoder()
        val nonce = d.decode(n)
        val ct = d.decode(c)
        if (nonce.size != 12 || ct.size < 17) null else {
            val cipher = Cipher.getInstance("AES/GCM/NoPadding")
            cipher.init(Cipher.DECRYPT_MODE, SecretKeySpec(keyOf(raw), "AES"), GCMParameterSpec(128, nonce))
            cipher.updateAAD(name.toByteArray(Charsets.UTF_8))
            String(cipher.doFinal(ct), Charsets.UTF_8)
        }
    } catch (e: Exception) {
        null
    }
}
