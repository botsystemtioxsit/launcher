package com.tioxsit.gatewayplus

import android.app.AlertDialog
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.net.VpnService
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.provider.Settings
import android.view.ViewGroup
import android.widget.CheckBox
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import androidx.activity.ComponentActivity
import androidx.activity.result.contract.ActivityResultContracts
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import kotlin.concurrent.thread

// Один экран: вход (ник и пароль) → главный (аккаунт, «как вас видят», мои серверы, включить/выключить, настройки).
class MainActivity : ComponentActivity() {
    private lateinit var ui: Ui
    private lateinit var store: Store
    private lateinit var page: LinearLayout
    private lateinit var scroll: ScrollView
    private val main = Handler(Looper.getMainLooper())

    private var acct: Store.Acct? = null
    private var access: Cloud.Access? = null
    private var ip: IpInfo.Info? = null
    private var connected = false
    private var busy = false
    private var status = ""
    private var statusColor = Pal.PINK
    private var lastAccessCheck = 0L

    private val vpnConsent = registerForActivityResult(ActivityResultContracts.StartActivityForResult()) { r ->
        if (r.resultCode == RESULT_OK) doConnect() else say("Без разрешения системы VPN не включить.")
    }
    private val qrScan = registerForActivityResult(ScanContract()) { r -> r.contents?.let { importConf(it) } }
    private val pickFile = registerForActivityResult(ActivityResultContracts.GetContent()) { uri -> if (uri != null) importFromUri(uri) }

    private val version: String
        get() = try { packageManager.getPackageInfo(packageName, 0).versionName ?: "1.0.0" } catch (e: Exception) { "1.0.0" }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        ui = Ui(this)
        store = Store(this)
        scroll = ScrollView(this).apply { setBackgroundColor(Pal.BG); isFillViewport = true }
        page = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(ui.dp(16), ui.dp(18), ui.dp(16), ui.dp(24))
        }
        scroll.addView(page, ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))
        setContentView(scroll)
        acct = store.account()
        render()
    }

    override fun onResume() {
        super.onResume()
        acct = store.account()
        thread {
            val up = Tunnels.isUp(this)
            main.post { connected = up; render() }
            refreshAccess(force = false)
            refreshIp()
        }
    }

    // ---------- вспомогательное ----------
    private fun say(text: String, color: Int = Pal.PINK) { status = text; statusColor = color; main.post { render() } }

    private fun refreshAccess(force: Boolean) {
        val a = acct ?: return
        if (!force && System.currentTimeMillis() - lastAccessCheck < 10 * 60_000L) return
        lastAccessCheck = System.currentTimeMillis()
        val dev = store.deviceHash()
        val res = Cloud.checkAccess(this, a.id, dev)
        if (!res.blocked) Cloud.heartbeat(a.id, dev, version, store.profileNames().size.coerceAtMost(50), a.termsV)
        main.post {
            access = res
            // заблокирован, а VPN включён — отключаем (выключить можно всегда, включить нельзя)
            if (res.blocked && connected) { say("Доступ закрыт владельцем проекта — отключаю VPN."); disconnect() } else render()
        }
    }

    private fun refreshIp() {
        val info = IpInfo.get()
        main.post { ip = info; render() }
    }

    private fun blocked() = access?.blocked == true

    // ---------- сборка экрана ----------
    private fun render() {
        page.removeAllViews()
        page.addView(ui.title("GATEWAY PLUS"))
        page.addView(ui.note("Личный VPN через ваш собственный сервер: сайты видят адрес и страну сервера, а не ваши."))
        if (acct == null) loginScreen() else mainScreen()
        if (status.isNotEmpty()) page.addView(ui.text(status, statusColor, 13f).apply { setPadding(0, ui.dp(8), 0, 0) })
    }

    private lateinit var nickBox: EditText
    private lateinit var pwBox: EditText

    private fun loginScreen() {
        nickBox = ui.input("Ник (имя аккаунта)").apply { setText(lastNick) }
        pwBox = ui.input("Пароль", password = true)
        page.addView(ui.card(
            ui.heading("Вход"),
            ui.note("Придумайте ник и пароль — это и есть ваш аккаунт: без почты и без телефона. Ник и пароль нигде не хранятся: из них считается номер аккаунта, и тот же ник и пароль на другом телефоне откроют тот же аккаунт. Забыли пароль — просто создайте новый аккаунт: ваши серверы лежат на телефоне и на сервере, в аккаунте их нет."),
            nickBox, pwBox,
            ui.button(if (busy) "Подождите…" else "Войти или создать аккаунт", "primary") { if (!busy) login() },
            ui.note("Нужен для того, чтобы действовали правила проекта: только личное использование, без коммерции. Владелец проекта может закрыть доступ тем, кто их нарушает."),
        ))
    }

    private var lastNick = ""

    private fun mainScreen() {
        val a = acct!!
        // аккаунт
        val (st, col) = when {
            blocked() -> "Доступ закрыт${if (access?.offline == true) " (по последним сведениям; нет связи с базой)" else ""}." to Pal.RED
            a.termsV < Terms.VERSION -> "Правила обновились — примите их перед включением VPN." to Pal.PINK
            access == null -> "Проверяю…" to Pal.MUTED
            access!!.offline -> "Нет связи с базой — проверим позже. Правила приняты." to Pal.MUTED
            else -> "Доступ открыт · правила приняты (версия ${a.termsV})" to Pal.GREEN
        }
        page.addView(ui.card(
            ui.heading("Аккаунт"),
            ui.text("${a.nick}  ·  номер ${a.id.take(8)}…", bold = true),
            ui.text(st, col, 13f),
            ui.row(
                ui.button("Правила") { showTerms(readOnly = true) {} },
                ui.button("Выйти", "danger") { logout() },
            ),
        ))
        if (blocked()) page.addView(ui.card(
            ui.heading("Доступ закрыт"),
            ui.text("Владелец проекта закрыл доступ: ${access?.reason.orEmpty()}", Pal.RED),
            ui.note("Включить VPN сейчас нельзя. Отключить уже включённый можно всегда. Если это ошибка — напишите владельцу проекта."),
        ))

        // как видят
        val seenText = when {
            ip == null -> "Проверяю…"
            !ip!!.ok -> "Не удалось проверить: ${ip!!.error}"
            else -> (ip!!.country.ifEmpty { "?" }) + (if (ip!!.city.isNotEmpty()) ", " + ip!!.city else "")
        }
        page.addView(ui.card(
            ui.heading("Сейчас вас видят так"),
            ui.text(seenText, if (connected) Pal.GREEN else Pal.TEXT, 20f, bold = true),
            if (ip?.ok == true) ui.note("Внешний адрес: ${ip!!.ip}") else ui.gap(2),
            ui.text(if (connected) "VPN включён: сайты видят адрес сервера." else "VPN выключен: сайты видят ваш настоящий адрес.", if (connected) Pal.GREEN else Pal.PINK, 13f),
            ui.button("Проверить ещё раз") { ip = null; render(); thread { refreshIp() } },
        ))

        serversCard()
        optionsCard()
        footerCard()
    }

    private fun serversCard() {
        val all = store.allProfiles()
        val groups = Profiles.groupByServer(all)
        val views = mutableListOf<android.view.View>()
        views.add(ui.heading(if (groups.isEmpty()) "Мои серверы" else "Мои серверы: ${groups.size}"))
        if (groups.isEmpty()) {
            views.add(ui.note("Серверов пока нет. Самый простой путь: на компьютере в Gateway Plus нажмите «Телефон и ускорение сервера…» — там будет QR-код. Здесь нажмите «＋ Добавить профиль» и наведите камеру на QR. Можно и вставить конфиг WireGuard текстом или файлом."))
        } else {
            views.add(ui.note("Один сервер — одна аренда у хостера. Выберите профиль, потом нажмите «Включить». GSP (наш протокол) на телефоне пока не работает — используйте профиль WireGuard."))
            for ((host, names) in groups) {
                val btns = names.map { n ->
                    ui.button(n + if (n == store.active) "  ✓" else "", if (n == store.active) "primary" else "plain") {
                        if (connected) say("Сначала отключите VPN.") else { store.active = n; say("Выбран профиль «$n». Нажмите «Включить».", Pal.MUTED) }
                    }
                }
                views.add(ui.inner(names.contains(store.active),
                    ui.text(host, bold = true),
                    ui.row(*btns.toTypedArray()),
                    ui.button("Убрать сервер", "danger") { confirmRemove(host, names) },
                ))
            }
        }
        // включить / отключить
        if (groups.isNotEmpty() && store.active.isNotEmpty()) {
            views.add(ui.gap(6))
            if (!connected) {
                val b = ui.button(if (busy) "Подождите…" else "Включить", "primary") { if (!busy) connect() }
                b.isEnabled = !blocked() && !busy
                views.add(b)
            } else {
                views.add(ui.button(if (busy) "Подождите…" else "Отключить", "danger") { if (!busy) disconnect() })
            }
        }
        views.add(ui.button("＋ Добавить профиль") { addProfileMenu() })
        page.addView(ui.card(*views.toTypedArray()))
    }

    private fun optionsCard() {
        val wa = CheckBox(this).apply {
            text = "Не пускать WhatsApp через VPN (звонки идут напрямую — так они слышны лучше)"
            setTextColor(Pal.TEXT); isChecked = store.bypassWhatsApp
            setOnCheckedChangeListener { _, v ->
                store.bypassWhatsApp = v
                say(if (connected) "Изменение вступит в силу при следующем включении: отключите VPN и включите снова." else "Сохранено: вступит в силу при включении.", Pal.MUTED)
            }
        }
        page.addView(ui.card(
            ui.heading("Настройки"),
            wa,
            ui.note("WhatsApp увидит ваш настоящий адрес. Если WhatsApp не установлен, настройка ни на что не влияет."),
            ui.text("Защита от утечек", bold = true),
            ui.note("Чтобы без VPN интернета не было вообще: откройте настройки VPN Android → шестерёнка у Gateway Plus → включите «Постоянный VPN» и «Блокировать соединения без VPN». Тогда при обрыве VPN трафик не уйдёт наружу, а остановится."),
            ui.button("Открыть настройки VPN Android") {
                try { startActivity(Intent(Settings.ACTION_VPN_SETTINGS)) } catch (e: Exception) { say("Не удалось открыть настройки: ${e.message}") }
            },
        ))
    }

    private fun footerCard() {
        page.addView(ui.card(
            ui.row(
                ui.button("Сведения для поддержки") { copyDiagnostics() },
                ui.button("О программе") { aboutDialog() },
            ),
            ui.note("Версия $version. Для личного использования; продавать доступ к своему серверу нельзя (правила)."),
        ))
    }

    // ---------- вход и аккаунт ----------
    private fun login() {
        val nick = nickBox.text.toString().trim()
        val pw = pwBox.text.toString()
        lastNick = nick
        val err = Account.nickError(nick) ?: Account.passwordError(pw)
        if (err != null) { say(err); return }
        busy = true
        say("Считаю ключ… (до нескольких секунд)", Pal.MUTED)
        thread {
            val key = Account.deriveKey(nick, pw)
            val id = Account.idOf(key)
            val remote: Int? = try { Cloud.accountTermsVersion(id) } catch (e: Cloud.CloudException) {
                main.post { busy = false; say("Не вышло: ${e.message}. Проверьте интернет и повторите.") }
                return@thread
            }
            main.post {
                busy = false
                when {
                    remote == null -> askCreate(nick, key)
                    remote < Terms.VERSION -> showTerms(readOnly = false) { finishLogin(nick, key, remote, accept = true) }
                    else -> finishLogin(nick, key, remote, accept = false)
                }
            }
        }
    }

    private fun askCreate(nick: String, key: String) {
        AlertDialog.Builder(this)
            .setTitle("Создать новый аккаунт?")
            .setMessage("Аккаунта с ником «$nick» и таким паролем нет.\n\nЕсли вы просто ошиблись в пароле — вернитесь и введите его заново. Если пароль забыт — создайте новый аккаунт: ваши серверы хранятся на телефоне и на сервере и не пропадут.")
            .setPositiveButton("Создать") { _, _ -> showTerms(readOnly = false) { createAccount(nick, key) } }
            .setNegativeButton("Назад", null)
            .show()
    }

    private fun createAccount(nick: String, key: String) {
        busy = true
        say("Создаю аккаунт…", Pal.MUTED)
        thread {
            try {
                Cloud.register(Account.idOf(key), Account.cleanName(nick), store.deviceHash(), version, store.profileNames().size)
                main.post { busy = false; finishLogin(nick, key, Terms.VERSION, accept = false) }
            } catch (e: Cloud.CloudException) {
                main.post { busy = false; say("Не вышло: ${e.message}") }
            }
        }
    }

    private fun finishLogin(nick: String, key: String, termsV: Int, accept: Boolean) {
        val a = Store.Acct(Account.cleanName(nick), key, if (accept) Terms.VERSION else termsV)
        store.saveAccount(a)
        acct = a
        status = ""
        lastNick = ""
        render()
        thread {
            if (accept) Cloud.acceptTerms(a.id, store.deviceHash(), version, store.profileNames().size)
            refreshAccess(force = true)
            refreshIp()
        }
    }

    private fun logout() {
        AlertDialog.Builder(this)
            .setTitle("Выйти из аккаунта?")
            .setMessage("Ник и пароль нужны, чтобы войти снова — убедитесь, что помните их. Профили серверов останутся на телефоне. Без аккаунта VPN не включается (уже включённый можно отключить).")
            .setPositiveButton("Выйти") { _, _ -> store.clearAccount(); acct = null; access = null; status = ""; render() }
            .setNegativeButton("Остаться", null)
            .show()
    }

    private fun showTerms(readOnly: Boolean, onAccept: () -> Unit) {
        val tv = TextView(this).apply { text = Terms.TEXT; setTextColor(Pal.TEXT); setPadding(ui.dp(20), ui.dp(12), ui.dp(20), ui.dp(4)) }
        val sv = ScrollView(this).apply { addView(tv) }
        val b = AlertDialog.Builder(this).setTitle("Правила Gateway Plus").setView(sv)
        if (readOnly) b.setPositiveButton("Закрыть", null)
        else b.setPositiveButton("Принимаю") { _, _ -> onAccept() }.setNegativeButton("Не принимаю", null)
        b.show()
    }

    // ---------- профили ----------
    private fun addProfileMenu() {
        AlertDialog.Builder(this)
            .setTitle("Добавить профиль")
            .setItems(arrayOf("Сканировать QR-код с компьютера", "Вставить из буфера обмена", "Выбрать файл (.conf)")) { _, which ->
                when (which) {
                    0 -> qrScan.launch(ScanOptions().setDesiredBarcodeFormats(ScanOptions.QR_CODE).setPrompt("Наведите на QR-код из Gateway Plus («Для телефона»)").setBeepEnabled(false).setOrientationLocked(false))
                    1 -> {
                        val cm = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
                        val t = cm.primaryClip?.takeIf { it.itemCount > 0 }?.getItemAt(0)?.coerceToText(this)?.toString().orEmpty()
                        if (t.isBlank()) say("В буфере обмена пусто: сначала скопируйте конфиг WireGuard.") else importConf(t)
                    }
                    else -> pickFile.launch("*/*")
                }
            }
            .setNegativeButton("Отмена", null)
            .show()
    }

    private fun importFromUri(uri: Uri) {
        try {
            val text = contentResolver.openInputStream(uri)?.use { it.readBytes().copyOf(minOf(it.available().coerceAtLeast(0) + 1, 40000)).toString(Charsets.UTF_8) }
            // читаем файл целиком, но не больше 40 КБ: конфиг WireGuard — это пара десятков строк
            val full = contentResolver.openInputStream(uri)?.use { s -> s.readNBytes(40001).toString(Charsets.UTF_8) } ?: text.orEmpty()
            importConf(full)
        } catch (e: Exception) { say("Не удалось прочитать файл: ${e.message}") }
    }

    private fun importConf(textIn: String) {
        val text = textIn.trim().replace("\r", "") + "\n"
        val bad = Profiles.validate(text)
        if (bad != null) { say(bad); return }
        val host = Profiles.endpointHost(text)
        val box = ui.input("Название профиля (латиницей)").apply { setText(store.uniqueName(host.ifEmpty { "server" })) }
        AlertDialog.Builder(this)
            .setTitle("Сохранить профиль")
            .setMessage("Сервер: $host")
            .setView(LinearLayout(this).apply { setPadding(ui.dp(20), 0, ui.dp(20), 0); addView(box) })
            .setPositiveButton("Сохранить") { _, _ ->
                val name = Profiles.safeName(box.text.toString())
                if (store.profileNames().any { it.equals(name, ignoreCase = true) }) { say("Профиль с таким названием уже есть."); return@setPositiveButton }
                store.saveProfile(name, text)
                store.active = name
                say("Профиль «$name» сохранён. Нажмите «Включить».", Pal.GREEN)
                thread { acct?.let { Cloud.heartbeat(it.id, store.deviceHash(), version, store.profileNames().size, it.termsV) } }
            }
            .setNegativeButton("Отмена", null)
            .show()
    }

    private fun confirmRemove(host: String, names: List<String>) {
        if (connected) { say("Сначала отключите VPN."); return }
        AlertDialog.Builder(this)
            .setTitle("Убрать сервер?")
            .setMessage("Из приложения будут удалены профили сервера $host (${names.joinToString(", ")}). Сам сервер у хостера не меняется. Вернуть профили можно, только если у вас осталась копия конфигов или QR на компьютере.")
            .setPositiveButton("Убрать") { _, _ -> names.forEach { store.deleteProfile(it) }; say("Сервер убран из списка.", Pal.MUTED) }
            .setNegativeButton("Оставить", null)
            .show()
    }

    // ---------- VPN ----------
    private fun connect() {
        val a = acct ?: return
        if (a.termsV < Terms.VERSION) { showTerms(readOnly = false) { acceptNewTermsThenConnect(a) }; return }
        if (blocked()) { say("Доступ закрыт владельцем проекта."); return }
        val name = store.active
        val conf = name.takeIf { it.isNotEmpty() }?.let { store.loadProfile(it) }
        if (conf == null) { say("Сначала добавьте и выберите профиль."); return }
        val bad = Profiles.validate(conf)
        if (bad != null) { say(bad); return }
        val intent = VpnService.prepare(this)
        if (intent != null) vpnConsent.launch(intent) else doConnect()
    }

    private fun acceptNewTermsThenConnect(a: Store.Acct) {
        val updated = a.copy(termsV = Terms.VERSION)
        store.saveAccount(updated); acct = updated
        thread { Cloud.acceptTerms(updated.id, store.deviceHash(), version, store.profileNames().size) }
        connect()
    }

    private fun doConnect() {
        val a = acct ?: return
        val name = store.active
        val conf = store.loadProfile(name) ?: return
        busy = true
        say("Включаю…", Pal.MUTED)
        thread {
            // доступ проверяем непосредственно перед включением
            val res = Cloud.checkAccess(this, a.id, store.deviceHash())
            if (res.blocked) { main.post { busy = false; access = res; say("Доступ закрыт владельцем проекта: ${res.reason}") }; return@thread }
            try {
                Tunnels.up(this, conf, Tunnels.installedBypass(this, store.bypassWhatsApp))
                val bypass = Tunnels.installedBypass(this, store.bypassWhatsApp).isNotEmpty()
                main.post { busy = false; connected = true; access = res; say("VPN включён." + if (bypass) " WhatsApp идёт мимо VPN." else "", Pal.GREEN) }
                Thread.sleep(2000)
                refreshIp()
            } catch (e: Exception) {
                main.post { busy = false; say(explain(e)) }
            }
        }
    }

    private fun disconnect() {
        if (busy) return
        busy = true
        say("Отключаю…", Pal.MUTED)
        thread {
            try {
                Tunnels.down(this)
                main.post { busy = false; connected = false; say("VPN выключен.", Pal.MUTED) }
                Thread.sleep(1500)
                refreshIp()
            } catch (e: Exception) {
                main.post { busy = false; say("Не вышло: ${e.message}") }
            }
        }
    }

    private fun explain(e: Exception): String {
        val m = e.message ?: e.javaClass.simpleName
        return when {
            e.javaClass.simpleName == "BadConfigException" -> "Конфиг не принят: $m"
            m.contains("permission", ignoreCase = true) || m.contains("consent", ignoreCase = true) -> "Нет разрешения системы на VPN: нажмите «Включить» ещё раз и согласитесь."
            else -> "Не вышло включить: $m"
        }
    }

    // ---------- прочее ----------
    private fun copyDiagnostics() {
        val a = acct
        val active = store.active
        val conf = active.takeIf { it.isNotEmpty() }?.let { store.loadProfile(it) }
        val host = conf?.let { Profiles.endpointHost(it).replace(Regex("(\\d+\\.\\d+\\.\\d+)\\.\\d+$"), "$1.x") }.orEmpty()
        val text = buildString {
            appendLine("Gateway Plus (Android) $version · Android ${android.os.Build.VERSION.RELEASE} (API ${android.os.Build.VERSION.SDK_INT}) · ${android.os.Build.MANUFACTURER} ${android.os.Build.MODEL}")
            appendLine("Аккаунт: ${a?.let { "номер ${it.id.take(8)}…" } ?: "нет"}; доступ: ${if (blocked()) "закрыт" else "открыт"}; профилей: ${store.profileNames().size}; WhatsApp мимо VPN: ${if (store.bypassWhatsApp) "да" else "нет"}")
            appendLine("Профиль: ${active.ifEmpty { "нет" }}; сервер: ${host.ifEmpty { "—" }}; VPN сейчас: ${if (connected) "включён" else "выключен"}")
        }
        (getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager).setPrimaryClip(android.content.ClipData.newPlainText("Gateway Plus", text))
        say("Сведения скопированы (без ключей, паролей и полного адреса сервера).", Pal.MUTED)
    }

    private fun aboutDialog() {
        AlertDialog.Builder(this)
            .setTitle("О программе")
            .setMessage("Gateway Plus $version — бесплатный личный VPN на вашем сервере, часть проекта Ghost Shell. Создано нейросетью.\n\nТуннель — официальная библиотека WireGuard для Android (Apache-2.0, © WireGuard LLC); сканер QR — ZXing (Apache-2.0); сеть — OkHttp (Apache-2.0). «WireGuard» — зарегистрированная торговая марка Джейсона А. Доненфельда; приложение с WireGuard не связано.\n\nВаши серверы и ключи лежат только на этом телефоне в зашифрованном виде и никуда не отправляются.")
            .setPositiveButton("Закрыть", null)
            .show()
    }
}
