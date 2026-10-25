package com.tioxsit.launcher

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Build

// Фоновая проверка новых версий: вышла новая версия установленного приложения, появилась Android-версия,
// которой раньше не было, или вышел новый лаунчер. Работает и тогда, когда лаунчер закрыт (JobScheduler раз в ~30 минут).
object Updates {
    private const val CHANNEL = "updates"

    fun check(ctx: Context): List<Pair<String, String>> {
        val found = ArrayList<Pair<String, String>>()
        Catalog.refresh()
        val store = Store(ctx)
        val me = store.active ?: return found
        val prefs = ctx.getSharedPreferences("updates", Context.MODE_PRIVATE)
        for (def in Catalog.apps) {
            val pkg = def.pkg ?: continue
            val apk = def.apk ?: continue
            if (!def.isAllowed(me.role)) continue
            val installed = Installer.installedVersion(ctx, pkg)
            var latest: ApkRelease? = null
            try { latest = Installer.latest(def.repo, apk, false, def.tag) } catch (_: Exception) { }
            val stateKey = "state_${def.id}"
            val before = prefs.getString(stateKey, null)
            if (latest != null) {
                if (installed != null && Net.compareVersions(latest.version, installed) > 0)
                    found.add("${def.id}:${latest.version}" to "Вышла новая версия «${def.name}» ${latest.version}")
                else if (installed == null && before == "missing")
                    found.add("new:${def.id}" to "Появилась Android-версия «${def.name}» ${latest.version}")
                prefs.edit().putString(stateKey, "ok").apply()
            } else if (before != "ok") {
                prefs.edit().putString(stateKey, "missing").apply()
            }
        }
        try {
            val own = ctx.packageManager.getPackageInfo(ctx.packageName, 0).versionName ?: "0"
            val l = Installer.latest(Catalog.LAUNCHER_REPO, Catalog.LAUNCHER_APK, false)
            if (Net.compareVersions(l.version, own) > 0) found.add("launcher:${l.version}" to "Вышел новый лаунчер ${l.version}")
        } catch (_: Exception) { }
        return found
    }

    // Сообщает только о том, о чём ещё не сообщали
    fun notifyNew(ctx: Context, found: List<Pair<String, String>>) {
        if (found.isEmpty()) return
        val prefs = ctx.getSharedPreferences("updates", Context.MODE_PRIVATE)
        val seen = prefs.getStringSet("notified", emptySet())!!.toMutableSet()
        val fresh = found.filter { seen.add(it.first) }
        if (fresh.isEmpty()) return
        prefs.edit().putStringSet("notified", seen).apply()
        val nm = ctx.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        if (Build.VERSION.SDK_INT >= 26) nm.createNotificationChannel(NotificationChannel(CHANNEL, "Новые версии", NotificationManager.IMPORTANCE_DEFAULT))
        val open = PendingIntent.getActivity(ctx, 0, Intent(ctx, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        @Suppress("DEPRECATION")
        val b = if (Build.VERSION.SDK_INT >= 26) Notification.Builder(ctx, CHANNEL) else Notification.Builder(ctx)
        val text = fresh.joinToString("\n") { it.second }
        b.setSmallIcon(R.drawable.ic_stat_update).setContentTitle("TIOXSIT Launcher").setContentText(fresh.first().second)
            .setStyle(Notification.BigTextStyle().bigText(text)).setContentIntent(open).setAutoCancel(true)
        nm.notify(1, b.build())
    }
}
