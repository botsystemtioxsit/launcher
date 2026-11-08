package com.tioxsit.gatewayplus

import okhttp3.OkHttpClient
import okhttp3.Request
import org.json.JSONObject
import java.util.concurrent.TimeUnit

// «Каким вас видят сайты»: внешний адрес, страна, город (сервис ipwho.is).
object IpInfo {
    data class Info(val ok: Boolean, val ip: String = "", val country: String = "", val city: String = "", val error: String = "")

    private val http = OkHttpClient.Builder().connectTimeout(8, TimeUnit.SECONDS).readTimeout(8, TimeUnit.SECONDS).build()

    fun get(): Info = try {
        val req = Request.Builder().url("https://ipwho.is/").header("User-Agent", "GhostShell-GatewayPlus-Android").build()
        http.newCall(req).execute().use { res ->
            if (!res.isSuccessful) return Info(false, error = "сервис проверки ответил HTTP ${res.code}")
            val o = JSONObject(res.body?.string().orEmpty())
            if (!o.optBoolean("success", false)) Info(false, error = Account.cleanName(o.optString("message", "не удалось проверить")))
            else Info(true, Account.cleanName(o.optString("ip")), Account.cleanName(o.optString("country")), Account.cleanName(o.optString("city")))
        }
    } catch (e: Exception) {
        Info(false, error = "нет связи с сервисом проверки")
    }
}
