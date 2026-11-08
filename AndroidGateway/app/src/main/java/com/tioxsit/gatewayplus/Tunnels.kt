package com.tioxsit.gatewayplus

import android.content.Context
import android.content.pm.PackageManager
import com.wireguard.android.backend.GoBackend
import com.wireguard.android.backend.Tunnel
import com.wireguard.config.Config
import java.io.BufferedReader
import java.io.StringReader

// Включение и выключение туннеля через библиотеку WireGuard (GoBackend: свой VpnService, wireguard-go внутри).
// Туннель на телефоне один — «gateway»; какой профиль в него загружен, решает Store.active.
object Tunnels {
    private const val NAME = "gateway"
    private val WHATSAPP = listOf("com.whatsapp", "com.whatsapp.w4b")

    @Volatile private var backend: GoBackend? = null
    private val tunnel = object : Tunnel {
        override fun getName(): String = NAME
        override fun onStateChange(newState: Tunnel.State) {}
    }

    private fun backend(ctx: Context): GoBackend =
        backend ?: synchronized(this) { backend ?: GoBackend(ctx.applicationContext).also { backend = it } }

    fun isUp(ctx: Context): Boolean = try { backend(ctx).runningTunnelNames.contains(NAME) } catch (e: Exception) { false }

    // Какие из приложений «мимо VPN» установлены (у неустановленного пакета VpnService.Builder падает).
    fun installedBypass(ctx: Context, wanted: Boolean): List<String> {
        if (!wanted) return emptyList()
        val pm = ctx.packageManager
        return WHATSAPP.filter { pkg -> try { pm.getPackageInfo(pkg, 0); true } catch (e: PackageManager.NameNotFoundException) { false } }
    }

    // Блокирующий вызов (до нескольких секунд) — вызывать не из главного потока. Нужно заранее получить согласие системы (VpnService.prepare).
    fun up(ctx: Context, conf: String, excluded: List<String>) {
        val text = Profiles.forTunnel(conf, excluded)
        val cfg = Config.parse(BufferedReader(StringReader(text)))
        backend(ctx).setState(tunnel, Tunnel.State.UP, cfg)
    }

    fun down(ctx: Context) {
        backend(ctx).setState(tunnel, Tunnel.State.DOWN, null)
    }
}
