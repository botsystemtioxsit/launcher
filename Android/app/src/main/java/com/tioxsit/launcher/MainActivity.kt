package com.tioxsit.launcher

import android.app.Activity
import android.app.AlertDialog
import android.content.Intent
import android.net.Uri
import android.os.Handler
import android.os.Looper
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.os.Bundle
import android.text.InputType
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import android.widget.Toast

// Весь интерфейс лаунчера: вход (почта / токен / код устройства), аккаунты, список приложений.
// Данные из базы и GitHub выводятся только как текст.
class MainActivity : Activity() {
    companion object {
        const val BG = 0xFF06050D.toInt()
        const val PANEL = 0xFF0D0B1A.toInt()
        const val PANEL2 = 0xFF15112B.toInt()
        const val LINE = 0xFF2D2060.toInt()
        const val TEXT = 0xFFE6F4FF.toInt()
        const val MUTED = 0xFF8B86B8.toInt()
        const val ACCENT = 0xFF00F0FF.toInt()
        const val ON_ACCENT = 0xFF001418.toInt()
        const val RED = 0xFFFF3366.toInt()
        const val GREEN = 0xFF2CFF9A.toInt()
        const val REQUEST_TTL_MS = 5 * 60 * 1000L
    }

    private lateinit var store: Store
    @Volatile private var loginGen = 0
    @Volatile private var screenGen = 0
    private var mainShown = false

    private data class Row(val def: AppDef, val installed: String?, val latest: ApkRelease?, val error: String?, val missing: Boolean = false, var progress: Int? = null)
    private var rows: List<Row> = emptyList()
    private var launcherUpdate: ApkRelease? = null
    private var appsBox: LinearLayout? = null
    private var bannerBox: LinearLayout? = null
    private var tab = "apps"
    private var contentBox: LinearLayout? = null
    private var sessionNote: String? = null
    private val handler = Handler(Looper.getMainLooper())
    private val periodic = object : Runnable {
        override fun run() {
            // раз в 5 минут, пока лаунчер открыт: новые версии приложений и самого лаунчера
            if (mainShown && tab == "apps") loadApps(true)
            handler.postDelayed(this, 5 * 60 * 1000L)
        }
    }

    private fun dp(v: Int) = (v * resources.displayMetrics.density + 0.5f).toInt()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        store = Store(this)
        render()
        // роль могла смениться, аккаунт — заблокироваться, токен — отозваться, устройство — быть отозвано
        if (store.active != null) {
            Thread {
                val note = refreshActiveProfile()
                runOnUiThread { if (note != null) sessionNote = note; render() }
            }.start()
        }
        handler.postDelayed(periodic, 5 * 60 * 1000L)
        // уведомления о новых версиях: разрешение (Android 13+) и фоновая проверка раз в ~30 минут
        if (android.os.Build.VERSION.SDK_INT >= 33 && checkSelfPermission("android.permission.POST_NOTIFICATIONS") != android.content.pm.PackageManager.PERMISSION_GRANTED)
            requestPermissions(arrayOf("android.permission.POST_NOTIFICATIONS"), 1)
        try { UpdateJob.schedule(this) } catch (_: Exception) { }
    }

    override fun onDestroy() {
        handler.removeCallbacks(periodic)
        super.onDestroy()
    }

    // Перечитывает текущую сессию из базы. «Нет связи» — не повод разлогинивать: остаются сохранённые данные.
    // Возвращает причину, если из аккаунта пришлось выйти.
    private fun refreshActiveProfile(): String? {
        val acc = store.active ?: return null
        try {
            if (acc.isToken) {
                val token = Secrets.unprotect(acc.tokenProtected)
                val info = if (token == null) null else Rtdb.getAccessToken(token)
                if (token == null || info == null || (info.expiresAt != null && info.expiresAt <= System.currentTimeMillis())) {
                    store.remove(acc.id)
                    return "Токен доступа отозван или истёк."
                }
                store.upsertToken(token, info.label)
                return null
            }
            val p = Rtdb.getProfile(acc.id)
            if (p.status == "banned") { store.remove(acc.id); return "Аккаунт заблокирован." }
            if (acc.paired && !Rtdb.isDeviceTrusted(acc.id, Rtdb.deviceKeyOf(store.deviceId()))) {
                store.remove(acc.id)
                return "Владелец отозвал доступ этому устройству — войди заново."
            }
            store.upsert(p)
        } catch (_: Exception) { /* нет сети — остаются сохранённые данные */ }
        return null
    }

    private fun requireAllowed(def: AppDef): Boolean {
        val me = store.active
        if (me == null) { render(); toast("Аккаунт больше недоступен — войди заново."); return false }
        if (!def.isAllowed(me.role)) { toast("Для этого приложения нет доступа у твоего аккаунта."); loadApps(false); return false }
        return true
    }

    override fun onResume() {
        super.onResume()
        // вернулись из установщика или из настроек — обновим, что стоит
        if (mainShown && rows.isNotEmpty()) loadApps(false)
    }

    // ---------- мелкие помощники интерфейса ----------
    private fun tv(text: String, size: Float = 14f, color: Int = TEXT, bold: Boolean = false): TextView = TextView(this).apply {
        this.text = text
        textSize = size
        setTextColor(color)
        if (bold) setTypeface(typeface, Typeface.BOLD)
    }

    private fun shape(fill: Int, radiusDp: Int = 12, stroke: Int? = null) = GradientDrawable().apply {
        setColor(fill)
        cornerRadius = dp(radiusDp).toFloat()
        if (stroke != null) setStroke(dp(1), stroke)
    }

    private fun button(text: String, primary: Boolean = false, danger: Boolean = false, onClick: () -> Unit): Button = Button(this).apply {
        this.text = text
        isAllCaps = false
        textSize = 14f
        setTextColor(if (primary) ON_ACCENT else if (danger) RED else TEXT)
        background = shape(if (primary) ACCENT else PANEL2, 10, if (primary) null else LINE)
        setPadding(dp(16), dp(10), dp(16), dp(10))
        stateListAnimator = null
        setOnClickListener { onClick() }
    }

    private fun field(hint: String, password: Boolean = false): EditText = EditText(this).apply {
        this.hint = hint
        setHintTextColor(MUTED)
        setTextColor(TEXT)
        textSize = 15f
        setSingleLine()
        inputType = if (password) InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD
        else InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS
        background = shape(PANEL2, 10, LINE)
        setPadding(dp(14), dp(12), dp(14), dp(12))
    }

    private fun card(): LinearLayout = LinearLayout(this).apply {
        orientation = LinearLayout.VERTICAL
        background = shape(PANEL, 14, LINE)
        setPadding(dp(16), dp(14), dp(16), dp(14))
    }

    private fun lp(top: Int = 0, width: Int = ViewGroup.LayoutParams.MATCH_PARENT) =
        LinearLayout.LayoutParams(width, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(top) }

    private fun screen(build: (LinearLayout) -> Unit): ScrollView {
        val col = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(18), dp(24), dp(18), dp(32))
        }
        build(col)
        val sv = ScrollView(this).apply {
            setBackgroundColor(BG)
            isFillViewport = true
            addView(col)
        }
        setContentView(sv)
        return sv
    }

    private fun toast(text: String) = runOnUiThread { Toast.makeText(this, text, Toast.LENGTH_LONG).show() }

    private fun ownVersion(): String = try { packageManager.getPackageInfo(packageName, 0).versionName ?: "?" } catch (e: Exception) { "?" }

    private fun render() {
        screenGen++
        loginGen++
        if (store.active == null) showLogin() else showMain()
    }

    // ---------- вход ----------
    private fun showLogin() {
        mainShown = false
        var sv: ScrollView? = null
        val ticker = Handler(Looper.getMainLooper())
        sv = screen { col ->
            col.addView(tv("Ghost Shell Launcher", 26f, TEXT, true))
            col.addView(tv("создано нейросетью", 12f, 0xFFFF2A9D.toInt()), lp(4))
            col.addView(tv("Войди в свой аккаунт — тот же, что в игре и админ-панели.", 14f, MUTED), lp(4))

            // Что сейчас происходит — сразу под заголовком, чтобы было видно: запрос отправлен, идёт ожидание
            val statusCard = card().apply { background = shape(PANEL2, 14, ACCENT); visibility = View.GONE }
            val spinner = android.widget.ProgressBar(this)
            val status = tv("", 14f, ACCENT, true)
            val countdown = tv("", 12f, MUTED)
            val code = tv("", 36f, ACCENT, true).apply { gravity = Gravity.CENTER; visibility = View.GONE }
            val cancel = button("Отменить") { loginGen++; render() }
            val head = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL }
            head.addView(spinner, LinearLayout.LayoutParams(dp(28), dp(28)))
            head.addView(status, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply { leftMargin = dp(12) })
            statusCard.addView(head)
            statusCard.addView(code, lp(10))
            statusCard.addView(countdown, lp(8))
            statusCard.addView(cancel, lp(10))
            col.addView(statusCard, lp(16))
            val error = tv("", 13f, RED).apply { visibility = View.GONE }
            sessionNote?.let { error.text = it; error.visibility = View.VISIBLE; sessionNote = null }
            col.addView(error, lp(12))

            val buttons = ArrayList<View>()
            var deadline = 0L
            val tick = object : Runnable {
                override fun run() {
                    if (statusCard.visibility != View.VISIBLE || deadline == 0L) return
                    val left = maxOf(0L, deadline - System.currentTimeMillis()) / 1000
                    countdown.text = "Ждём подтверждения · осталось ${left / 60}:${"%02d".format(left % 60)}"
                    ticker.postDelayed(this, 1000)
                }
            }
            fun setBusy(on: Boolean) {
                buttons.forEach { it.isEnabled = !on; it.alpha = if (on) 0.45f else 1f }
                statusCard.visibility = if (on) View.VISIBLE else View.GONE
                if (!on) { deadline = 0L; code.visibility = View.GONE; ticker.removeCallbacksAndMessages(null) }
            }
            fun info(t: String) = runOnUiThread {
                error.visibility = View.GONE; status.text = t
                deadline = System.currentTimeMillis() + REQUEST_TTL_MS
                ticker.removeCallbacksAndMessages(null); ticker.post(tick)
                sv?.smoothScrollTo(0, 0)
            }
            fun fail(t: String) = runOnUiThread { setBusy(false); error.text = t; error.visibility = View.VISIBLE; sv?.smoothScrollTo(0, 0) }
            fun done() = runOnUiThread { render() }
            fun begin(): Int {
                loginGen++
                error.visibility = View.GONE
                status.text = "Отправляю запрос…"; countdown.text = ""
                setBusy(true)
                sv?.smoothScrollTo(0, 0)
                return loginGen
            }

            // 1. заявка по почте
            val email = field("твой email")
            col.addView(tv("Вход по почте", 16f, TEXT, true), lp(22))
            col.addView(tv("Заявку подтверждаешь сам в Telegram, без пароля и без письма.", 13f, MUTED), lp(2))
            col.addView(email, lp(8))
            col.addView(button("Запросить вход", primary = true) {
                val addr = email.text.toString().trim()
                if (!Rtdb.isValidEmail(addr)) { fail("Впиши email целиком."); return@button }
                val gen = begin()
                Thread { emailLogin(gen, addr, ::info, ::fail, ::done) }.start()
            }.also { buttons.add(it) }, lp(8))
            buttons.add(email)

            // 2. токен доступа
            val token = field("токен доступа", password = true)
            col.addView(tv("Токен доступа", 16f, TEXT, true), lp(22))
            col.addView(tv("Если Супердоступ выдал тебе токен — он входит сразу, без аккаунта. Видна только админ-панель.", 13f, MUTED), lp(2))
            col.addView(token, lp(8))
            col.addView(button("Войти по токену") {
                val t = token.text.toString().trim()
                if (!Net.SAFE_ID.matches(t)) { fail("Впиши токен целиком."); return@button }
                val gen = begin()
                Thread { tokenLogin(gen, t, ::fail, ::done) }.start()
            }.also { buttons.add(it) }, lp(8))
            buttons.add(token)

            // 3. код устройства
            col.addView(tv("Подключить по коду", 16f, TEXT, true), lp(22))
            col.addView(tv("Телефон показывает код, ты отправляешь его боту TerminalBot в Telegram — и вход происходит сам.", 13f, MUTED), lp(2))
            col.addView(button("Показать код") {
                val gen = begin()
                Thread { pairLogin(gen, ::info, ::fail, ::done) { c -> runOnUiThread { code.text = c; code.visibility = if (c.isEmpty()) View.GONE else View.VISIBLE } } }.start()
            }.also { buttons.add(it) }, lp(8))

            // сохранённые аккаунты
            if (store.accounts.isNotEmpty()) {
                col.addView(tv("Сохранённые аккаунты", 16f, TEXT, true), lp(28))
                store.accounts.forEach { a ->
                    col.addView(button("${a.display} · ${a.roleName}") { store.setActive(a.id); render() }, lp(8))
                }
            }
            col.addView(tv("Версия ${ownVersion()}", 11f, MUTED), lp(24))
        }
    }

    private fun emailLogin(gen: Int, email: String, info: (String) -> Unit, fail: (String) -> Unit, done: () -> Unit) {
        var targetId: String? = null
        var requestId: String? = null
        try {
            val tid = Rtdb.findAccountByEmail(email)
            if (tid == null) { fail("Эта почта не привязана ни к одному аккаунту. Привяжи её в игре: Настройки → Аккаунт."); return }
            targetId = tid
            val rid = Rtdb.createLoginRequest(tid)
            requestId = rid
            info("⏳ Заявка отправлена. Открой в Telegram бота TerminalBot и нажми «Разрешить» в его сообщении (можно и в игре: Сообщество → Запросы, или в админ-панели: Аккаунт). Ждём 5 минут.")
            val deadline = System.currentTimeMillis() + REQUEST_TTL_MS
            while (System.currentTimeMillis() < deadline) {
                Thread.sleep(2000)
                if (gen != loginGen) return
                val status = try { Rtdb.loginRequestStatus(tid, rid) } catch (e: java.io.IOException) { null }
                if (status == "approved") {
                    Rtdb.removeLoginRequest(tid, rid); requestId = null
                    val p = Rtdb.getProfile(tid)
                    if (p.status == "banned") { fail("Аккаунт заблокирован."); return }
                    store.upsert(p, paired = false)
                    done(); return
                }
                if (status == "denied") { Rtdb.removeLoginRequest(tid, rid); requestId = null; fail("Вход отклонён на другом устройстве."); return }
            }
            fail("Время ожидания истекло, попробуй ещё раз.")
        } catch (e: Exception) {
            if (gen == loginGen) fail("Не удалось отправить заявку: " + (e.message ?: e.javaClass.simpleName))
        } finally {
            // заявка, которую никто не подтвердил, не должна висеть в базе
            if (targetId != null && requestId != null) Rtdb.removeLoginRequest(targetId, requestId)
        }
    }

    private fun tokenLogin(gen: Int, token: String, fail: (String) -> Unit, done: () -> Unit) {
        try {
            val info = Rtdb.getAccessToken(token)
            if (gen != loginGen) return
            if (info == null) { fail("Токен не найден или уже отозван Супердоступом."); return }
            if (info.expiresAt != null && info.expiresAt <= System.currentTimeMillis()) { fail("Срок действия токена истёк — попроси у Супердоступа новый."); return }
            store.upsertToken(token, info.label)
            done()
        } catch (e: Exception) {
            if (gen == loginGen) fail("Не удалось проверить токен: " + (e.message ?: e.javaClass.simpleName))
        }
    }

    private fun pairLogin(gen: Int, info: (String) -> Unit, fail: (String) -> Unit, done: () -> Unit, showCode: (String) -> Unit) {
        var code: String? = null
        try {
            val deviceKey = Rtdb.deviceKeyOf(store.deviceId())
            var attempt = 0
            while (true) {
                code = Rtdb.randomPairingCode()
                try { Rtdb.createPairing(code, deviceKey); break }
                catch (e: Rtdb.CodeTaken) { code = null; if (++attempt > 3) throw e }
            }
            showCode(code!!)
            info("Открой в Telegram бота TerminalBot и отправь ему этот код сообщением — вход произойдёт сам. Код живёт 5 минут.")
            val deadline = System.currentTimeMillis() + REQUEST_TTL_MS
            while (System.currentTimeMillis() < deadline) {
                Thread.sleep(2000)
                if (gen != loginGen) return
                val r = try { Rtdb.getPairing(code!!) } catch (e: java.io.IOException) { continue }
                if (r.first == "approved" && r.second != null) {
                    Rtdb.removePairing(code!!); code = null
                    val p = Rtdb.getProfile(r.second!!)
                    if (p.status == "banned") { fail("Аккаунт заблокирован."); return }
                    store.upsert(p, paired = true)
                    done(); return
                }
                if (r.first == "denied") { Rtdb.removePairing(code!!); code = null; fail("Отклонено."); return }
                if (r.first == null) { code = null; fail("Код пропал — попробуй ещё раз."); return }
            }
            fail("Время ожидания истекло, попробуй ещё раз.")
        } catch (e: Exception) {
            if (gen == loginGen) fail("Не удалось создать код: " + (e.message ?: e.javaClass.simpleName))
        } finally {
            if (code != null) Rtdb.removePairing(code)
        }
    }

    // ---------- главный экран ----------
    private fun showMain() {
        val me = store.active ?: return
        mainShown = true
        val content = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(18), dp(22), dp(18), dp(24))
        }
        contentBox = content
        val scroll = ScrollView(this).apply { addView(content) }

        // нижнее меню: Приложения / Аккаунт
        val nav = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            setBackgroundColor(PANEL)
            setPadding(dp(8), dp(6), dp(8), dp(6))
        }
        fun navBtn(id: String, label: String): TextView = TextView(this).apply {
            text = label
            gravity = Gravity.CENTER
            textSize = 14f
            setPadding(0, dp(12), 0, dp(12))
            setTextColor(if (tab == id) ACCENT else MUTED)
            if (tab == id) setTypeface(typeface, Typeface.BOLD)
            setOnClickListener { if (tab != id) { tab = id; showMain() } }
        }
        nav.addView(navBtn("apps", "Приложения"), LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
        nav.addView(navBtn("account", "Аккаунт"), LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))

        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(BG)
            addView(scroll, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f))
            addView(View(this@MainActivity).apply { setBackgroundColor(LINE) }, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(1)))
            addView(nav)
        }
        setContentView(root)

        if (tab == "account") {
            appsBox = null; bannerBox = null
            showAccountTab(content, me)
        } else {
            showAppsTab(content, me)
            drawApps()
            loadApps(false)
        }
    }

    private fun avatarView(a: Account, sizeDp: Int): TextView = TextView(this).apply {
        text = a.display.trim().take(2).uppercase()
        setTextColor(ON_ACCENT); setTypeface(typeface, Typeface.BOLD); gravity = Gravity.CENTER
        background = shape(ACCENT, sizeDp / 2)
        layoutParams = LinearLayout.LayoutParams(dp(sizeDp), dp(sizeDp))
    }

    private fun showAppsTab(col: LinearLayout, me: Account) {
        val banner = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        bannerBox = banner
        col.addView(banner)
        col.addView(tv("Приложения", 22f, TEXT, true), lp(if (launcherUpdate != null) 14 else 0))
        col.addView(tv("Ты вошёл как ${me.display} · ${me.roleName}", 13f, MUTED), lp(4))
        col.addView(tv("Android-версия (APK) ставится из GitHub и обновляется отсюда. Облегчённая версия открывается в браузере. Если версии ещё нет, кнопка закрыта и включится сама.", 13f, MUTED), lp(2))
        val list = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        appsBox = list
        col.addView(list, lp(10))
        col.addView(button("Проверить обновления") { loadApps(true) }, lp(14))
        col.addView(tv("Ghost Shell Launcher · версия ${ownVersion()}", 11f, MUTED), lp(20))
    }

    private fun showAccountTab(col: LinearLayout, me: Account) {
        col.addView(tv("Аккаунт", 22f, TEXT, true))
        col.addView(tv("Можно держать несколько аккаунтов и переключаться между ними.", 13f, MUTED), lp(4))

        val meCard = card()
        val head = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL }
        head.addView(avatarView(me, 52))
        val who = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; setPadding(dp(14), 0, 0, 0) }
        who.addView(tv(me.display, 16f, TEXT, true))
        who.addView(tv(me.roleName + if (me.playerId.isNotEmpty()) " · " + me.playerId else "", 13f, MUTED))
        head.addView(who)
        meCard.addView(head)
        col.addView(meCard, lp(14))

        col.addView(tv("Все аккаунты", 14f, MUTED, true), lp(22))
        store.accounts.forEach { a ->
            val row = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
                background = shape(PANEL2, 10, if (a.id == store.activeId) ACCENT else LINE)
                setPadding(dp(12), dp(10), dp(8), dp(10))
                setOnClickListener {
                    if (a.id != store.activeId) {
                        store.setActive(a.id)
                        Thread {
                            val note = refreshActiveProfile()
                            runOnUiThread { if (note != null) sessionNote = note; render() }
                        }.start()
                    }
                }
            }
            row.addView(avatarView(a, 34))
            val meta = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; setPadding(dp(12), 0, 0, 0) }
            meta.addView(tv(a.display, 14f, TEXT, true))
            meta.addView(tv(a.roleName + if (a.playerId.isNotEmpty()) " · " + a.playerId else "", 12f, MUTED))
            row.addView(meta, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
            if (a.id != store.activeId) {
                row.addView(tv("×", 22f, MUTED).apply {
                    setPadding(dp(14), 0, dp(10), 0)
                    setOnClickListener { store.remove(a.id); showMain() }
                })
            }
            col.addView(row, lp(8))
        }

        val actions = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        actions.addView(button("+ Добавить аккаунт") { store.signOut(); render() }, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
        actions.addView(button("Выйти", danger = true) { store.active?.let { store.remove(it.id) }; render() },
            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply { leftMargin = dp(8) })
        col.addView(actions, lp(14))
        col.addView(tv("Ghost Shell Launcher · версия ${ownVersion()}", 11f, MUTED), lp(24))
    }

    private fun loadApps(force: Boolean) {
        val me = store.active ?: return
        val gen = screenGen
        Thread {
            // список приложений и их версий читается из catalog.json — без обновления лаунчера
            Catalog.refresh(force)
            val list = Catalog.apps.filter { it.isAllowed(me.role) }.map { def ->
                val installed = def.pkg?.let { Installer.installedVersion(this, it) }
                var latest: ApkRelease? = null
                var err: String? = null
                var missing = false
                if (def.pkg != null && def.apk != null) {
                    try { latest = Installer.latest(def.repo, def.apk, force, def.tag) }
                    catch (e: Exception) {
                        // нет релиза или файла — версия ещё не вышла (кнопка включится сама); иначе — нет связи
                        if (e is IllegalStateException || (e.message ?: "").startsWith("HTTP 404")) missing = true
                        else err = "Не удалось узнать последнюю версию (нет связи с GitHub?)"
                    }
                }
                Row(def, installed, latest, err, missing)
            }
            var upd: ApkRelease? = null
            try {
                val l = Installer.latest(Catalog.LAUNCHER_REPO, Catalog.LAUNCHER_APK, force)
                if (Net.compareVersions(l.version, ownVersion()) > 0) upd = l
            } catch (_: Exception) { }
            runOnUiThread {
                if (gen != screenGen) return@runOnUiThread
                rows = list
                launcherUpdate = upd
                drawApps()
            }
        }.start()
    }

    private fun drawApps() {
        val box = appsBox ?: return
        box.removeAllViews()
        val banner = bannerBox
        banner?.removeAllViews()
        launcherUpdate?.let { u ->
            val c = card().apply { background = shape(PANEL2, 14, ACCENT) }
            c.addView(tv("Вышла новая версия лаунчера: ${u.version}", 15f, ACCENT, true))
            c.addView(button("Обновить лаунчер", primary = true) { installRelease(u, null, "") }, lp(10))
            banner?.addView(c)
        }
        if (rows.isEmpty()) {
            box.addView(tv("Загрузка…", 14f, MUTED))
            return
        }
        rows.forEach { box.addView(appCard(it), lp(10)) }
    }

    private fun appCard(r: Row): View {
        val c = card()
        c.addView(tv(r.def.name, 17f, TEXT, true))
        c.addView(tv(r.def.desc, 13f, MUTED), lp(3))
        val pkg = r.def.pkg
        fun liteButton() {
            val url = r.def.site
            if (url != null) c.addView(button("Открыть облегчённую версию") {
                try { startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url))) } catch (_: Exception) { toast("Не удалось открыть браузер") }
            }, lp(8))
            else c.addView(button("🔒 Облегчённая версия") { }.apply { isEnabled = false; alpha = 0.45f }, lp(8))
        }
        if (pkg == null || r.missing) {
            c.addView(tv("Android-версии (APK) пока нет — кнопка включится сама, когда она выйдет. Обновлять лаунчер для этого не нужно.", 13f, MUTED), lp(10))
            c.addView(button("🔒 Установить Android-версию (APK)") { }.apply { isEnabled = false; alpha = 0.45f }, lp(10))
            liteButton()
            return c
        }
        val installed = r.installed
        val latest = r.latest
        val stateText = when {
            r.error != null -> r.error
            installed == null && latest != null -> "Не установлено · последняя версия ${latest.version}"
            installed != null && latest != null && Net.compareVersions(latest.version, installed) > 0 -> "Установлено $installed · доступно ${latest.version}"
            installed != null -> "Установлено $installed · последняя версия"
            else -> ""
        }
        c.addView(tv(stateText ?: "", 13f, if (r.error != null) RED else if (installed != null && latest != null && Net.compareVersions(latest.version, installed) > 0) ACCENT else GREEN), lp(8))
        if (r.progress != null) {
            c.addView(tv("Скачиваю… ${r.progress}%", 13f, ACCENT), lp(8))
            return c
        }
        val row = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        fun add(b: Button) = row.addView(b, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply { rightMargin = dp(6) })
        if (installed == null) {
            if (latest != null) add(button("Установить (APK)", primary = true) { installRelease(latest, r, "Установка начата") })
        } else {
            if (latest != null && Net.compareVersions(latest.version, installed) > 0) add(button("Обновить", primary = true) { installRelease(latest, r, "Обновление начато") })
            add(button("Открыть", primary = latest == null || Net.compareVersions(latest.version, installed) <= 0) {
                if (!requireAllowed(r.def)) return@button
                if (!Installer.launch(this, pkg)) { toast("Приложение не установлено"); loadApps(true) }
            })
            add(button("Удалить", danger = true) {
                AlertDialog.Builder(this).setMessage("Удалить «${r.def.name}» с этого телефона?")
                    .setPositiveButton("Удалить") { _, _ -> Installer.uninstall(this, pkg) }
                    .setNegativeButton("Отмена", null).show()
            })
        }
        c.addView(row, lp(10))
        liteButton()
        return c
    }

    private fun installRelease(rel: ApkRelease, row: Row?, okText: String = "") {
        if (row != null && !requireAllowed(row.def)) return
        if (!Installer.canInstall(this)) {
            toast("Разреши лаунчеру устанавливать приложения и нажми «Установить» ещё раз.")
            Installer.openInstallPermission(this)
            return
        }
        Thread {
            try {
                Installer.downloadAndInstall(this, rel) { p ->
                    runOnUiThread { row?.progress = p; if (row != null) drawApps() }
                }
                if (okText.isNotEmpty() && row != null) toast("$okText: ${row.def.name}. Подтверди установку в окне Android.")
                runOnUiThread { row?.progress = null; drawApps() }
            } catch (e: Exception) {
                toast("Не получилось: " + (e.message ?: e.javaClass.simpleName))
                runOnUiThread { row?.progress = null; drawApps() }
            }
        }.start()
    }
}
