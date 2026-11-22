package com.tioxsit.aiplus

import android.Manifest
import android.app.AlertDialog
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Color
import android.net.Uri
import android.os.Bundle
import android.speech.RecognitionListener
import android.speech.RecognizerIntent
import android.speech.SpeechRecognizer
import android.speech.tts.TextToSpeech
import android.speech.tts.UtteranceProgressListener
import org.json.JSONArray
import org.json.JSONObject
import java.util.Locale
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
    private var tts: TextToSpeech? = null
    private var ttsReady = false
    private var recognizer: SpeechRecognizer? = null
    private var pendingListenLang: String? = null
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

        // ---- озвучка (родной синтез речи телефона) ----
        @JavascriptInterface fun speak(text: String, rate: Float, pitch: Float, voice: String, id: Int) {
            runOnUiThread { speakNow(text, rate, pitch, voice, id) }
        }
        @JavascriptInterface fun stopSpeak() { runOnUiThread { tts?.stop() } }
        @JavascriptInterface fun voices(): String {
            val arr = JSONArray()
            try {
                tts?.voices?.filter { v -> val l = v.locale.language; l == "ru" || l == "en" || l == "de" }
                    ?.sortedBy { if (it.locale.language == "ru") 0 else 1 }
                    ?.forEach { arr.put(JSONObject().put("name", it.name).put("lang", it.locale.toLanguageTag())) }
            } catch (_: Exception) { }
            return arr.toString()
        }

        // ---- распознавание речи (родное, как голосовой ввод Google/Samsung) ----
        @JavascriptInterface fun canListen(): Boolean = SpeechRecognizer.isRecognitionAvailable(this@MainActivity)
        @JavascriptInterface fun listen(lang: String) { runOnUiThread { listenNow(lang) } }
        @JavascriptInterface fun stopListen() { runOnUiThread { try { recognizer?.cancel() } catch (_: Exception) { } } }
    }

    private fun js(type: String, payload: String) {
        runOnUiThread { web.evaluateJavascript("window.AiPlusEvt&&AiPlusEvt('$type',${JSONObject.quote(payload)})", null) }
    }

    private fun ensureTts() {
        if (tts != null) return
        tts = TextToSpeech(this) { st ->
            ttsReady = st == TextToSpeech.SUCCESS
            if (ttsReady) {
                val t = tts!!
                if (t.isLanguageAvailable(Locale("ru", "RU")) >= TextToSpeech.LANG_AVAILABLE) t.language = Locale("ru", "RU")
                t.setOnUtteranceProgressListener(object : UtteranceProgressListener() {
                    override fun onStart(utteranceId: String?) {}
                    override fun onDone(utteranceId: String?) { js("speakDone", utteranceId ?: "") }
                    @Deprecated("Deprecated in Java") override fun onError(utteranceId: String?) { js("speakDone", utteranceId ?: "") }
                })
            }
        }
    }

    private fun speakNow(text: String, rate: Float, pitch: Float, voice: String, id: Int) {
        ensureTts()
        val t = tts
        if (t == null || !ttsReady) {
            // движок ещё запускается: повторим через секунду, а если так и не готов — «договорили» сразу, чтобы разговор не завис
            web.postDelayed({ if (ttsReady) speakNow(text, rate, pitch, voice, id) else js("speakDone", id.toString()) }, 1200)
            return
        }
        try {
            t.setSpeechRate(rate.coerceIn(0.5f, 2.0f))
            t.setPitch(pitch.coerceIn(0.5f, 2.0f))
            if (voice.isNotEmpty()) t.voices?.firstOrNull { it.name == voice }?.let { t.voice = it }
            t.speak(text, TextToSpeech.QUEUE_ADD, null, id.toString())
        } catch (_: Exception) {
            js("speakDone", id.toString())
        }
    }

    private fun listenNow(lang: String) {
        if (checkSelfPermission(Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
            pendingListenLang = lang
            requestPermissions(arrayOf(Manifest.permission.RECORD_AUDIO), 77)
            return
        }
        if (!SpeechRecognizer.isRecognitionAvailable(this)) { js("error", "unsupported"); return }
        try {
            recognizer?.destroy()
            val r = SpeechRecognizer.createSpeechRecognizer(this)
            recognizer = r
            r.setRecognitionListener(object : RecognitionListener {
                override fun onReadyForSpeech(p: Bundle?) {}
                override fun onBeginningOfSpeech() {}
                override fun onRmsChanged(v: Float) {}
                override fun onBufferReceived(b: ByteArray?) {}
                override fun onEndOfSpeech() {}
                override fun onPartialResults(p: Bundle?) {}
                override fun onEvent(t: Int, p: Bundle?) {}
                override fun onResults(res: Bundle?) {
                    val best = res?.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION)?.firstOrNull().orEmpty()
                    js("result", best)
                }
                override fun onError(code: Int) {
                    js("error", when (code) {
                        SpeechRecognizer.ERROR_NO_MATCH, SpeechRecognizer.ERROR_SPEECH_TIMEOUT -> "no-speech"
                        SpeechRecognizer.ERROR_INSUFFICIENT_PERMISSIONS -> "not-allowed"
                        SpeechRecognizer.ERROR_CLIENT -> "aborted"
                        SpeechRecognizer.ERROR_NETWORK, SpeechRecognizer.ERROR_NETWORK_TIMEOUT -> "network"
                        else -> "error"
                    })
                }
            })
            r.startListening(Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH).apply {
                putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM)
                putExtra(RecognizerIntent.EXTRA_LANGUAGE, lang)
                putExtra(RecognizerIntent.EXTRA_MAX_RESULTS, 1)
                putExtra(RecognizerIntent.EXTRA_PARTIAL_RESULTS, false)
            })
        } catch (_: Exception) {
            js("error", "error")
        }
    }

    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<out String>, grantResults: IntArray) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode == 77) {
            val lang = pendingListenLang
            pendingListenLang = null
            if (grantResults.isNotEmpty() && grantResults[0] == PackageManager.PERMISSION_GRANTED && lang != null) listenNow(lang)
            else js("error", "not-allowed")
        }
    }

    override fun onDestroy() {
        try { recognizer?.destroy() } catch (_: Exception) { }
        try { tts?.stop(); tts?.shutdown() } catch (_: Exception) { }
        super.onDestroy()
    }

    @Deprecated("Deprecated in Java")
    override fun onBackPressed() {
        if (errorBox.visibility == android.view.View.GONE && web.canGoBack()) web.goBack() else super.onBackPressed()
    }

    companion object { const val DEFAULT_URL = "http://10.66.66.1:8080" }
}
