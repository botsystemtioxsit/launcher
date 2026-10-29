package com.tioxsit.launcher

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject

// Плагины лаунчера. Сейчас один вид — темы оформления: файл только с цветами, никакого кода. Список плагинов
// живёт в catalog.json (раздел "plugins"), сами файлы — в папке plugins/ репозитория launcher. Установленное
// хранится в настройках приложения.
data class InstalledPlugin(val id: String, val name: String, val version: String, val colors: Map<String, String>)

object Plugins {
    private val HEX = Regex("^#[0-9A-Fa-f]{6}$")
    val KEYS = listOf("bg", "panel", "panel2", "line", "text", "muted", "accent", "onAccent", "accent2", "red", "green")

    fun validColors(colors: Map<String, String>?): Boolean = colors != null && KEYS.all { k -> colors[k]?.let { HEX.matches(it) } == true }

    // Разбор файла темы. null — файл не тот: чужой вид плагина, нет цвета или цвет не вида #RRGGBB.
    fun parseTheme(json: String): Map<String, String>? = try {
        val o = JSONObject(json)
        if (o.optString("kind") != "theme") null
        else {
            val cols = o.getJSONObject("colors")
            val map = KEYS.associateWith { cols.getString(it) }
            if (validColors(map)) map else null
        }
    } catch (_: Exception) { null }

    private fun prefs(ctx: Context) = ctx.getSharedPreferences("plugins", Context.MODE_PRIVATE)

    fun installed(ctx: Context): List<InstalledPlugin> = try {
        val arr = JSONArray(prefs(ctx).getString("installed", "[]"))
        (0 until arr.length()).mapNotNull { i ->
            val o = arr.getJSONObject(i)
            val cols = o.getJSONObject("colors")
            val map = KEYS.associateWith { cols.optString(it) }
            val id = o.getString("id")
            // файл настроек трогать некому, но проверяем так же, как при установке
            if (Net.SAFE_ID.matches(id) && validColors(map)) InstalledPlugin(id, o.optString("name"), o.optString("version"), map) else null
        }
    } catch (_: Exception) { emptyList() }

    fun active(ctx: Context): String? = prefs(ctx).getString("active", null)?.takeIf { id -> installed(ctx).any { it.id == id } }

    fun save(ctx: Context, list: List<InstalledPlugin>, active: String?) {
        val arr = JSONArray()
        list.forEach { p ->
            arr.put(JSONObject().put("id", p.id).put("name", p.name).put("version", p.version).put("colors", JSONObject(p.colors)))
        }
        val e = prefs(ctx).edit().putString("installed", arr.toString())
        if (active == null) e.remove("active") else e.putString("active", active)
        e.apply()
    }

    // Скачивает и проверяет плагин (вызывать не из главного потока).
    fun download(def: PluginDef): InstalledPlugin {
        val colors = parseTheme(Net.get(def.url)) ?: throw IllegalStateException("Файл плагина повреждён или не подходит")
        return InstalledPlugin(def.id, def.name, def.version, colors)
    }

    fun applyActive(ctx: Context) {
        val id = active(ctx)
        MainActivity.applyColors(installed(ctx).firstOrNull { it.id == id }?.colors)
    }
}
