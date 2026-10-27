package com.tioxsit.launcher

import java.io.File
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import java.security.MessageDigest

object Net {
    // Любой путь к базе, собранный из чужого текста, сначала проходит через это.
    val SAFE_ID = Regex("^[A-Za-z0-9_-]{1,64}$")

    private fun open(url: String, method: String, timeoutMs: Int = 30000): HttpURLConnection {
        val c = URL(url).openConnection() as HttpURLConnection
        c.requestMethod = method
        c.connectTimeout = timeoutMs
        c.readTimeout = timeoutMs
        c.setRequestProperty("User-Agent", "tioxsit-launcher-android/1")
        return c
    }

    fun get(url: String): String = send("GET", url, null)

    // Запрос с ETag: ответ 304 («не менялось») у GitHub не считается в лимит запросов (60 в час без токена).
    // Возвращает (код, тело, ETag); при 304 тело = null.
    fun getConditional(url: String, etag: String?): Triple<Int, String?, String?> {
        val c = open(url, "GET")
        try {
            c.setRequestProperty("Accept", "application/vnd.github+json")
            if (etag != null) c.setRequestProperty("If-None-Match", etag)
            val code = c.responseCode
            if (code == 304) return Triple(304, null, etag)
            if (code !in 200..299) throw IOException("HTTP $code")
            val body = c.inputStream.use { it.readBytes().toString(Charsets.UTF_8) }
            return Triple(code, body, c.getHeaderField("ETag"))
        } finally {
            c.disconnect()
        }
    }

    fun send(method: String, url: String, body: String?): String {
        val c = open(url, method)
        try {
            if (body != null) {
                c.doOutput = true
                c.setRequestProperty("Content-Type", "application/json")
                c.outputStream.use { it.write(body.toByteArray(Charsets.UTF_8)) }
            }
            val code = c.responseCode
            if (code !in 200..299) throw IOException("HTTP $code")
            return c.inputStream.use { it.readBytes().toString(Charsets.UTF_8) }
        } finally {
            c.disconnect()
        }
    }

    // Скачивает в файл и возвращает sha256 скачанного
    fun download(url: String, dest: File, onProgress: (Int) -> Unit): String {
        val c = open(url, "GET", 60000)
        try {
            val code = c.responseCode
            if (code !in 200..299) throw IOException("HTTP $code")
            val total = c.contentLengthLong
            val md = MessageDigest.getInstance("SHA-256")
            dest.parentFile?.mkdirs()
            c.inputStream.use { input ->
                dest.outputStream().use { out ->
                    val buf = ByteArray(65536)
                    var got = 0L
                    var last = -1
                    while (true) {
                        val n = input.read(buf)
                        if (n < 0) break
                        out.write(buf, 0, n)
                        md.update(buf, 0, n)
                        got += n
                        if (total > 0) {
                            val p = (got * 100 / total).toInt().coerceAtMost(99)
                            if (p != last) { last = p; onProgress(p) }
                        }
                    }
                }
            }
            return md.digest().joinToString("") { "%02x".format(it) }
        } finally {
            c.disconnect()
        }
    }

    // Скачивать разрешено только с github.com из релизов репозиториев проекта.
    fun isTrustedDownload(url: String, repo: String): Boolean = try {
        val u = URL(url)
        u.protocol == "https" && u.host == "github.com" && u.path.startsWith("/${Catalog.OWNER}/$repo/releases/")
    } catch (e: Exception) { false }

    // «1.2.10» > «1.2.9»
    fun compareVersions(a: String, b: String): Int {
        fun parse(s: String) = s.split('.').map { p -> p.takeWhile { it.isDigit() }.toIntOrNull() ?: 0 }
        val pa = parse(a); val pb = parse(b)
        for (i in 0 until maxOf(pa.size, pb.size)) {
            val d = (pa.getOrElse(i) { 0 }) - (pb.getOrElse(i) { 0 })
            if (d != 0) return if (d > 0) 1 else -1
        }
        return 0
    }
}
