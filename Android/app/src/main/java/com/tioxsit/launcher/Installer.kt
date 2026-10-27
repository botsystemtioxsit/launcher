package com.tioxsit.launcher

import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.provider.Settings
import androidx.core.content.FileProvider
import org.json.JSONObject
import java.io.File

data class ApkRelease(val version: String, val url: String, val sha256: String?, val size: Long)

object Installer {
    private val cache = HashMap<String, Pair<Long, ApkRelease>>()
    private const val CACHE_MS = 5 * 60 * 1000L // без токена у GitHub лимит 60 запросов в час

    @Volatile private var prefs: android.content.SharedPreferences? = null

    // Последняя удачно полученная версия хранится на телефоне: если GitHub не отвечает (нет связи или лимит
    // запросов — у мобильного интернета адрес общий, и 60 запросов в час кончаются быстро), показываем её,
    // а не ошибку и закрытые кнопки.
    fun init(ctx: Context) {
        if (prefs == null) prefs = ctx.applicationContext.getSharedPreferences("release-cache", Context.MODE_PRIVATE)
    }

    private fun saved(key: String): Pair<String?, ApkRelease?> {
        val s = prefs?.getString("rel:$key", null) ?: return null to null
        return try {
            val j = JSONObject(s)
            j.optString("etag").takeIf { it.isNotEmpty() } to ApkRelease(
                j.getString("version"), j.getString("url"), j.optString("sha256").takeIf { it.isNotEmpty() }, j.optLong("size", 0))
        } catch (e: Exception) { null to null }
    }

    private fun parse(body: String, repo: String, apk: String): ApkRelease {
        val json = JSONObject(body)
        val text = json.optString("body", "")
        val version = Regex("""[Вв]ерсия\s+(\d+(?:\.\d+)+)""").find(text)?.groupValues?.get(1)
            ?: throw IllegalStateException("В релизе нет версии")
        val assets = json.getJSONArray("assets")
        for (i in 0 until assets.length()) {
            val a = assets.getJSONObject(i)
            if (a.optString("name") != apk) continue
            val url = a.getString("browser_download_url")
            if (!Net.isTrustedDownload(url, repo)) throw IllegalStateException("Недоверенный адрес загрузки")
            val digest = a.optString("digest", "").removePrefix("sha256:").lowercase().takeIf { Regex("^[0-9a-f]{64}$").matches(it) }
            return ApkRelease(version, url, digest, a.optLong("size", 0))
        }
        throw IllegalStateException("В релизе нет файла $apk")
    }

    // Последняя сборка: релиз с фиксированным тегом android-latest, версия — в его описании («Версия 1.0.5, …»),
    // контрольная сумма — в поле digest у файла.
    fun latest(repo: String, apk: String, force: Boolean = false, tag: String = "android-latest"): ApkRelease {
        val key = "$repo/$tag/$apk"
        cache[key]?.let { if (!force && System.currentTimeMillis() - it.first < CACHE_MS) return it.second }
        val (oldEtag, old) = saved(key)
        try {
            val (code, body, etag) = Net.getConditional("https://api.github.com/repos/${Catalog.OWNER}/$repo/releases/tags/$tag",
                if (old != null) oldEtag else null)
            if (code == 304 && old != null) {
                cache[key] = System.currentTimeMillis() to old
                return old
            }
            val rel = parse(body ?: throw IllegalStateException("Пустой ответ"), repo, apk)
            cache[key] = System.currentTimeMillis() to rel
            prefs?.edit()?.putString("rel:$key", JSONObject().put("etag", etag ?: "").put("version", rel.version)
                .put("url", rel.url).put("sha256", rel.sha256 ?: "").put("size", rel.size).toString())?.apply()
            return rel
        } catch (e: IllegalStateException) {
            throw e
        } catch (e: Exception) {
            if ((e.message ?: "").startsWith("HTTP 404")) throw e
            if (old != null) return old // нет связи или лимит GitHub — последняя удачно полученная версия
            throw e
        }
    }

    fun installedVersion(ctx: Context, pkg: String): String? = try {
        ctx.packageManager.getPackageInfo(pkg, 0).versionName
    } catch (e: PackageManager.NameNotFoundException) { null }

    fun launch(ctx: Context, pkg: String): Boolean {
        val i = ctx.packageManager.getLaunchIntentForPackage(pkg) ?: return false
        i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        ctx.startActivity(i)
        return true
    }

    fun uninstall(ctx: Context, pkg: String) {
        ctx.startActivity(Intent(Intent.ACTION_DELETE, Uri.parse("package:$pkg")).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
    }

    fun canInstall(ctx: Context) = android.os.Build.VERSION.SDK_INT < 26 || ctx.packageManager.canRequestPackageInstalls()

    // Android не даёт ставить приложения без отдельного разрешения для самого лаунчера
    fun openInstallPermission(ctx: Context) {
        ctx.startActivity(Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:${ctx.packageName}")).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
    }

    // Скачивает файл, проверяет контрольную сумму и отдаёт системному установщику (подтверждает человек).
    fun downloadAndInstall(ctx: Context, rel: ApkRelease, onProgress: (Int) -> Unit) {
        val file = File(ctx.cacheDir, "apk/update.apk")
        file.parentFile?.listFiles()?.forEach { it.delete() }
        val sha = Net.download(rel.url, file, onProgress)
        if (rel.sha256 != null && sha != rel.sha256) {
            file.delete()
            throw java.io.IOException("Контрольная сумма не совпала — файл повреждён")
        }
        val uri = FileProvider.getUriForFile(ctx, ctx.packageName + ".files", file)
        val i = Intent(Intent.ACTION_VIEW).setDataAndType(uri, "application/vnd.android.package-archive")
            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK)
        ctx.startActivity(i)
    }
}
