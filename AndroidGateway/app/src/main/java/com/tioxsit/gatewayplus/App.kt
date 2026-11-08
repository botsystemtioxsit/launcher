package com.tioxsit.gatewayplus

import android.app.Application
import com.wireguard.android.backend.GoBackend
import kotlin.concurrent.thread

class App : Application() {
    override fun onCreate() {
        super.onCreate()
        // «Постоянный VPN» из настроек Android: система сама запускает службу при включении телефона или после обрыва — поднимаем последний профиль.
        // Работает, только если человек вошёл в аккаунт, профиль выбран и аккаунт не заблокирован.
        GoBackend.setAlwaysOnCallback {
            thread {
                try {
                    val store = Store(this)
                    val acct = store.account() ?: return@thread
                    val conf = store.active.takeIf { it.isNotEmpty() }?.let { store.loadProfile(it) } ?: return@thread
                    if (Profiles.validate(conf) != null) return@thread
                    val access = Cloud.checkAccess(this, acct.id, store.deviceHash())
                    if (access.blocked) return@thread
                    Tunnels.up(this, conf, Tunnels.installedBypass(this, store.bypassWhatsApp))
                } catch (_: Exception) { }
            }
        }
    }
}
