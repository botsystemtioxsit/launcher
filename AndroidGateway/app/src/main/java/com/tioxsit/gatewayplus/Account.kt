package com.tioxsit.gatewayplus

import java.security.MessageDigest
import java.text.Normalizer
import java.util.Locale
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

// Аккаунт Gateway Plus на телефоне: ник и пароль, без почты и без сервера, который хранил бы пароль.
// Из ника и пароля считается ключ (PBKDF2-HMAC-SHA256, как у ключа GW-… на компьютере), а номер аккаунта в общей базе — первые 24 знака sha256 от ключа.
// Поэтому: тот же ник и пароль на другом телефоне — тот же аккаунт; пароль и ключ в базу не попадают; забыли пароль — заведите новый аккаунт
// (серверы человека лежат у него самого и в аккаунте не хранятся, терять нечего).
// Файл без Android-классов — его проверяют обычные тесты на компьютере (AccountTest) по контрольному примеру из Python.
object Account {
    const val ITERATIONS = 300_000
    private const val ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"

    fun normalizeNick(s: String?): String = Normalizer.normalize(s ?: "", Normalizer.Form.NFKC).trim().lowercase(Locale.ROOT)

    // Ник: 3–30 знаков, одна строка без управляющих и «перевёрнутых» символов.
    fun nickError(nick: String): String? {
        val n = nick.trim()
        if (n.length < 3) return "Ник: хотя бы 3 знака."
        if (n.length > 30) return "Ник: не больше 30 знаков."
        if (n != cleanName(n)) return "Ник: без переносов строк и служебных символов."
        return null
    }

    fun passwordError(pw: String): String? {
        if (pw.length < 6) return "Пароль: хотя бы 6 знаков."
        if (pw.length > 128) return "Пароль: не больше 128 знаков."
        return null
    }

    // PBKDF2-HMAC-SHA256 своими руками (на Android до 8.0 нет готового SecretKeyFactory для SHA-256); результат проверен против Python (AccountTest).
    fun pbkdf2(password: ByteArray, salt: ByteArray, iterations: Int, outLen: Int): ByteArray {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(if (password.isEmpty()) ByteArray(1) else password, "HmacSHA256"))
        val out = ByteArray(outLen)
        var block = 1
        var pos = 0
        while (pos < outLen) {
            mac.update(salt)
            mac.update(byteArrayOf((block ushr 24).toByte(), (block ushr 16).toByte(), (block ushr 8).toByte(), block.toByte()))
            var u = mac.doFinal()
            val t = u.copyOf()
            for (i in 1 until iterations) {
                u = mac.doFinal(u)
                for (j in t.indices) t[j] = (t[j].toInt() xor u[j].toInt()).toByte()
            }
            val n = minOf(t.size, outLen - pos)
            System.arraycopy(t, 0, out, pos, n)
            pos += n
            block++
        }
        return out
    }

    // 20 байт → 32 знака A-Z2-7 (тот же вид, что у ключа на компьютере).
    fun base32(bytes: ByteArray): String {
        val sb = StringBuilder()
        var bits = 0
        var v = 0
        for (b in bytes) {
            v = (v shl 8) or (b.toInt() and 0xff)
            bits += 8
            while (bits >= 5) {
                sb.append(ALPHABET[(v shr (bits - 5)) and 31])
                bits -= 5
            }
            v = v and ((1 shl bits) - 1)
        }
        return sb.toString()
    }

    fun deriveKey(nick: String, password: String): String {
        val salt = ("ghostshell.gateway.acct.v1:" + normalizeNick(nick)).toByteArray(Charsets.UTF_8)
        return base32(pbkdf2(password.toByteArray(Charsets.UTF_8), salt, ITERATIONS, 20))
    }

    private fun hex(b: ByteArray, n: Int) = b.take(n).joinToString("") { "%02x".format(it) }

    // Номер аккаунта в базе: 24 знака 0-9a-f (как IdOf на компьютере).
    fun idOf(key: String): String = hex(MessageDigest.getInstance("SHA-256").digest(("ghostshell.gateway.v1:$key").toByteArray(Charsets.UTF_8)), 12)

    // Обезличенный номер телефона (32 знака 0-9a-f): из ANDROID_ID с солью — по нему не узнать сам ANDROID_ID.
    fun deviceHash(androidId: String): String =
        hex(MessageDigest.getInstance("SHA-256").digest(("ghostshell.gateway.dev.v1:" + androidId.ifEmpty { "no-android-id" }).toByteArray(Charsets.UTF_8)), 16)

    private val CTRL = Regex("[\\p{C}‎‏‪-‮⁦-⁩]")
    private val SPACES = Regex("\\s+")

    // Имя: одна строка без управляющих и «перевёрнутых» знаков, до 30 знаков.
    fun cleanName(s: String?): String {
        val t = SPACES.replace(CTRL.replace(s ?: "", " "), " ").trim()
        return if (t.length > 30) t.substring(0, 30).trim() else t
    }

    // Причина блокировки из базы: одна строка, до 200 знаков.
    fun cleanReason(s: String?): String {
        val t = SPACES.replace(CTRL.replace(s ?: "", " "), " ").trim()
        return if (t.length > 200) t.substring(0, 200) else t
    }
}
