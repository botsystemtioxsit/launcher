package com.tioxsit.aiplus

import android.app.AlertDialog
import android.content.Context
import android.graphics.Color
import android.net.Uri
import android.os.Bundle
import android.view.Gravity
import android.view.ViewGroup
import android.webkit.JavascriptInterface
import android.webkit.WebChromeClient
import android.webkit.WebResourceError
import android.webkit.WebResourceRequest
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.Button
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity

// AI Plus: окно чата с вашей нейросетью. Сам чат (персонажи, имя, характер, история) — страница, которую отдаёт ваш сервер внутри VPN
// (скрипт «Поставить нейросеть на сервер»). Приложение только открывает её и помнит адрес. Работает при включённом Gateway Plus.
class MainActivity : AppCompatActivity() {
    private lateinit var web: WebView
    private lateinit var errorBox: LinearLayout
    private lateinit var errorText: TextView
    private val prefs by lazy { getSharedPreferences("aiplus", Context.MODE_PRIVATE) }

    private fun serverUrl(): String = prefs.getString("url", DEFAULT_URL) ?: DEFAULT_URL

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val root = FrameLayout(this).apply { setBackgroundColor(Color.parseColor("#070314")) }
        web = WebView(this).apply {
            settings.javaScriptEnabled = true
            settings.domStorageEnabled = true            // чаты и персонажи хранятся в самой странице
            settings.cacheMode = WebSettings.LOAD_NO_CACHE
            settings.allowFileAccess = false
            settings.allowContentAccess = false
            setBackgroundColor(Color.parseColor("#070314"))
            addJavascriptInterface(Bridge(), "AiPlus")
            webChromeClient = WebChromeClient()
            webViewClient = object : WebViewClient() {
                override fun shouldOverrideUrlLoading(v: WebView, r: WebResourceRequest): Boolean = !sameHost(r.url)   // чужие ссылки в приложении не открываем
                override fun onReceivedError(v: WebView, r: WebResourceRequest, e: WebResourceError) {
                    if (r.isForMainFrame) showError("Нет связи с чатом (${e.description}).\nВключён ли Gateway Plus и работает ли сервер?")
                }
                override fun onPageFinished(v: WebView, url: String) { errorBox.visibility = android.view.View.GONE }
            }
        }
        root.addView(web, ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))

        errorText = TextView(this).apply { setTextColor(Color.parseColor("#EAF8FF")); textSize = 16f; gravity = Gravity.CENTER }
        errorBox = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER
            setPadding(48, 48, 48, 48)
            setBackgroundColor(Color.parseColor("#070314"))
            visibility = android.view.View.GONE
            addView(errorText)
            addView(Button(context).apply { text = "Повторить"; setOnClickListener { load() } })
            addView(Button(context).apply { text = "Адрес сервера…"; setOnClickListener { askUrl() } })
        }
        root.addView(errorBox, ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))
        setContentView(root)
        load()
    }

    private fun sameHost(u: Uri): Boolean = u.host == Uri.parse(serverUrl()).host

    private fun load() {
        errorBox.visibility = android.view.View.GONE
        web.loadUrl(serverUrl())
    }

    private fun showError(t: String) {
        errorText.text = t
        errorBox.visibility = android.view.View.VISIBLE
    }

    private fun askUrl() {
        val box = EditText(this).apply { setText(serverUrl()); setSingleLine(); setPadding(48, 32, 48, 32) }
        AlertDialog.Builder(this)
            .setTitle("Адрес чата на сервере")
            .setMessage("Адрес внутри VPN, например http://10.66.66.1:8080 (его печатает скрипт установки). Разрешены только адреса 10.66.66.1 и 10.78.0.1.")
            .setView(box)
            .setPositiveButton("Сохранить") { _, _ ->
                val v = box.text.toString().trim().trimEnd('/')
                val uri = Uri.parse(v)
                if (uri.scheme != "http" || uri.host.isNullOrEmpty()) {
                    showError("Нужен адрес вида http://10.66.66.1:8080")
                } else {
                    prefs.edit().putString("url", v).apply()
                    load()
                }
            }
            .setNegativeButton("Отмена", null)
            .show()
    }

    // Страница чата умеет попросить сменить адрес (кнопка внизу списка чатов, видна только в этом приложении)
    inner class Bridge {
        @JavascriptInterface fun changeServer() { runOnUiThread { askUrl() } }
    }

    @Deprecated("Deprecated in Java")
    override fun onBackPressed() {
        if (errorBox.visibility == android.view.View.GONE && web.canGoBack()) web.goBack() else super.onBackPressed()
    }

    companion object { const val DEFAULT_URL = "http://10.66.66.1:8080" }
}
