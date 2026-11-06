using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

// Языки интерфейса (русский / English / Deutsch) для лаунчера и всех плагинов Ghost Shell. Один файл на все приложения.
//
// Устройство (как «ретрофит» в игре): русские строки остаются в коде и разметке как есть, а перевод — это словарь «русская строка → English → Deutsch»
// (файлы .loc, вшиты в exe). Пока выбран русский, модуль ничего не делает. Для English/Deutsch раз в ~0,4 с (и при загрузке каждого элемента) он обходит
// открытые окна и заменяет текст там, где нашёл строку в словаре: надписи, кнопки, подсказки, заголовки окон. Строки с подстановками (например,
// «Профиль «{}» сохранён.») записаны в словаре шаблоном с {} и сопоставляются по шаблону; в переводе подставляются как {0}, {1}…
// То, что пишет сам код после загрузки (строка состояния и т. п.), тоже ловится при очередном обходе. Исходный русский текст запоминается, поэтому
// язык переключается туда и обратно на лету.
//
// Язык один на все программы: файл %APPDATA%\BattleLauncher\language.txt ("ru" / "en" / "de"); его пишет лаунчер, плагины подхватывают на лету.
// Нет файла — по языку Windows (русский/украинский/белорусский/казахский → русский, немецкий → Deutsch, остальные → English).
// Служебные ключи запуска: --lang=ru|en|de (на один запуск, файл не трогает).
public static class Loc
{
    public static string Lang { get; private set; } = "ru";
    public static event Action? Changed;

    sealed class Pat { public Regex Rx = null!; public string En = "", De = ""; public int Holes; }
    sealed class St { public string? Src; public string? Applied; }

    static readonly Dictionary<string, (string en, string de)> exact = new(StringComparer.Ordinal);
    static readonly List<Pat> patterns = new();
    static readonly List<(string ru, string en, string de)> prefixes = new(), suffixes = new();   // обрывки «Не получилось: » + текст и «текст» + « — запущен снова»
    static readonly Dictionary<string, string> cacheEn = new(), cacheDe = new();   // уже вычисленные переводы (в том числе «нет перевода» = сама строка)
    static readonly HashSet<string> missing = new(StringComparer.Ordinal);
    static readonly ConditionalWeakTable<object, Dictionary<string, St>> states = new();
    static readonly Regex Cyr = new("[А-Яа-яЁё]", RegexOptions.Compiled);
    static bool loaded, started;
    static DispatcherTimer? timer;
    static DateTime fileStamp = DateTime.MinValue;
    static bool fixedByArg;

    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BattleLauncher", "language.txt");
    public static readonly string[] Languages = { "ru", "en", "de" };
    public static string NameOf(string lang) => lang switch { "en" => "English", "de" => "Deutsch", _ => "Русский" };

    // ---------- запуск ----------
    public static void Start(string[]? args = null)
    {
        if (started) return;
        started = true;
        LoadDictionaries();
        var arg = args?.FirstOrDefault(a => a.StartsWith("--lang=", StringComparison.Ordinal));
        if (arg != null && IsLang(arg[7..].Trim())) { Lang = arg[7..].Trim(); fixedByArg = true; }
        else { Lang = ReadFile() ?? Detect(); fileStamp = StampOf(); }
        // перевод каждого элемента сразу при загрузке, чтобы не мелькал русский
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) =>
        {
            if (Lang != "ru" && s is DependencyObject d) { try { FixOne(d); } catch { } }
        }));
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) => Tick();
        timer.Start();
        Sweep();
    }

    static bool IsLang(string s) => s == "ru" || s == "en" || s == "de";

    static string Detect()
    {
        try
        {
            var l = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            if (l is "ru" or "uk" or "be" or "kk") return "ru";
            if (l == "de") return "de";
        }
        catch { }
        return "en";
    }

    static string? ReadFile()
    {
        try { var t = File.ReadAllText(FilePath).Trim().ToLowerInvariant(); return IsLang(t) ? t : null; } catch { return null; }
    }
    static DateTime StampOf() { try { return File.GetLastWriteTimeUtc(FilePath); } catch { return DateTime.MinValue; } }

    // Выбрать язык (пишет общий файл — остальные программы подхватят сами).
    public static void Set(string lang)
    {
        if (!IsLang(lang)) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!); File.WriteAllText(FilePath, lang); fileStamp = StampOf(); } catch { }
        Apply(lang);
    }

    static void Apply(string lang)
    {
        if (lang == Lang) return;
        Lang = lang;
        Sweep();
        Changed?.Invoke();
    }

    static void Tick()
    {
        if (!fixedByArg)
        {
            var st = StampOf();
            if (st != fileStamp) { fileStamp = st; var l = ReadFile(); if (l != null && l != Lang) { Apply(l); return; } }
        }
        Sweep();
    }

    // ---------- словарь ----------
    static void LoadDictionaries()
    {
        if (loaded) return;
        loaded = true;
        try
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            foreach (var res in asm.GetManifestResourceNames().Where(n => n.StartsWith("loc/", StringComparison.Ordinal) && n.EndsWith(".loc", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal))
            {
                using var s = asm.GetManifestResourceStream(res);
                if (s == null) continue;
                using var r = new StreamReader(s, Encoding.UTF8);
                Parse(r.ReadToEnd());
            }
        }
        catch { }
    }

    // Формат: одна строка — одна запись «русский ⇒ English ⇒ Deutsch»; перевод строки — \n, обратная косая — \\; # — комментарий.
    // В русском ключе {} — место подстановки; в переводе подстановки — {0}, {1}…
    public static void Parse(string text)
    {
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            if (raw.Length == 0 || raw[0] == '#') continue;
            var parts = raw.Split(" ⇒ ");
            if (parts.Length < 3) continue;
            var ru = Unesc(parts[0]); var en = Unesc(parts[1]); var de = Unesc(parts[2]);
            if (ru.Length == 0) continue;
            if (en.Length == 0) en = ru;
            if (de.Length == 0) de = en;
            if (ru.Contains("{}"))
            {
                var pieces = ru.Split("{}");
                var sb = new StringBuilder("^");
                for (var i = 0; i < pieces.Length; i++) { sb.Append(Regex.Escape(pieces[i])); if (i < pieces.Length - 1) sb.Append("(.*?)"); }
                sb.Append('$');
                patterns.Add(new Pat { Rx = new Regex(sb.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant), En = en, De = de, Holes = pieces.Length - 1 });
            }
            else
            {
                exact[ru] = (en, de);
                // Обрывки, к которым код приписывает динамическую часть: «Не получилось: » + ошибка, ошибка + « (расширение)».
                if (ru.Length >= 4)
                {
                    if (ru.TrimEnd().Length != ru.Length) prefixes.Add((ru, en, de));
                    else if (ru.EndsWith(':')) prefixes.Add((ru + " ", en + " ", de + " "));
                    if (ru.TrimStart().Length != ru.Length || ".,;)—–·".IndexOf(ru[0]) >= 0) suffixes.Add((ru, en, de));
                }
            }
        }
        prefixes.Sort((a, b) => b.ru.Length.CompareTo(a.ru.Length));
        suffixes.Sort((a, b) => b.ru.Length.CompareTo(a.ru.Length));
        // шаблоны с большим числом постоянных знаков — раньше (они точнее)
        patterns.Sort((a, b) => b.Rx.ToString().Length.CompareTo(a.Rx.ToString().Length));
    }

    static string Unesc(string s)
    {
        if (s.IndexOf('\\') < 0) return s;
        var sb = new StringBuilder();
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length) { var d = s[i + 1]; if (d == 'n') { sb.Append('\n'); i++; continue; } if (d == '\\') { sb.Append('\\'); i++; continue; } if (d == 't') { sb.Append('\t'); i++; continue; } }
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    public static int Count => exact.Count + patterns.Count;

    // ---------- перевод строки ----------
    // Для кода: Loc.T("русский текст") → текст на выбранном языке (если перевода нет — как есть).
    public static string T(string ru)
    {
        if (Lang == "ru" || string.IsNullOrEmpty(ru) || !Cyr.IsMatch(ru)) return ru;
        var cache = Lang == "en" ? cacheEn : cacheDe;
        lock (cache)
        {
            if (cache.TryGetValue(ru, out var hit)) return hit;
            var t = Translate(ru);
            if (cache.Count > 4000) cache.Clear();
            cache[ru] = t;
            return t;
        }
    }

    static string Pick((string en, string de) e) => Lang == "en" ? e.en : e.de;

    static string Translate(string s)
    {
        if (exact.TryGetValue(s, out var e)) return Pick(e);
        // пробелы по краям и перевод строки в конце сохраняем
        var core = s.Trim();
        if (core.Length != s.Length && core.Length > 0)
        {
            var lead = s[..(s.Length - s.TrimStart().Length)];
            var trail = s[s.TrimEnd().Length..];
            var inner = Translate(core);
            return inner == core ? s : lead + inner + trail;
        }
        var p = ByPattern(s);
        if (p != null) return p;
        foreach (var pf in prefixes)
            if (s.Length > pf.ru.Length && s.StartsWith(pf.ru, StringComparison.Ordinal))
                return (Lang == "en" ? pf.en : pf.de) + T2(s[pf.ru.Length..]);
        foreach (var sf in suffixes)
            if (s.Length > sf.ru.Length && s.EndsWith(sf.ru, StringComparison.Ordinal))
                return T2(s[..^sf.ru.Length]) + (Lang == "en" ? sf.en : sf.de);
        // значок-украшение в начале («▲ Есть обновление», «⟳ Обновить»): переводим остаток, значок оставляем
        if (s.Length > 2 && s[1] == ' ' && "▲▼▶⟳☰✓✗●○←→＋+".IndexOf(s[0]) >= 0)
        {
            var rest = s[2..]; var rr = Translate(rest);
            if (rr != rest) return s[0] + " " + rr;
        }
        // многострочный текст — построчно
        if (s.Contains('\n'))
        {
            var lines = s.Split('\n'); var any = false;
            for (var i = 0; i < lines.Length; i++)
            {
                var l = lines[i];
                if (!Cyr.IsMatch(l)) continue;
                var r = Translate(l.TrimEnd('\r'));
                if (r != l.TrimEnd('\r')) { lines[i] = r; any = true; }
            }
            if (any) return string.Join("\n", lines);
        }
        // «Заголовок: значение» и «текст…» — по частям
        var idx = s.IndexOf(": ", StringComparison.Ordinal);
        if (idx > 0 && idx < s.Length - 2 && Cyr.IsMatch(s))
        {
            var head = s[..idx]; var tail = s[(idx + 2)..];
            if (exact.TryGetValue(head, out var he)) { var tt = Translate(tail); if (tt != tail || !Cyr.IsMatch(tail)) return Pick(he) + ": " + tt; }
        }
        if (s.EndsWith("…") && s.Length > 1 && exact.TryGetValue(s[..^1], out var de2)) return Pick(de2) + "…";
        // ЗАГЛАВНЫЕ заголовки (код делает ToUpper от обычной строки): ищем обычную форму
        if (s.Length > 2 && s == s.ToUpperInvariant() && s != s.ToLowerInvariant())
        {
            var norm = char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();
            if (exact.TryGetValue(norm, out var ue)) return Pick(ue).ToUpperInvariant();
        }
        // список через запятую: переводим каждую часть отдельно
        if (s.Contains(", "))
        {
            var parts = s.Split(", ");
            var anyChanged = false;
            for (var i = 0; i < parts.Length; i++)
            {
                if (!Cyr.IsMatch(parts[i])) continue;
                var r = Translate(parts[i]);
                if (r != parts[i]) { parts[i] = r; anyChanged = true; }
            }
            if (anyChanged) return string.Join(", ", parts);
        }
        if (Cyr.IsMatch(s)) lock (missing) { if (missing.Count < 600) missing.Add(s); }
        return s;
    }

    static string? ByPattern(string s)
    {
        foreach (var p in patterns)
        {
            var m = p.Rx.Match(s);
            if (!m.Success) continue;
            var tpl = Lang == "en" ? p.En : p.De;
            var sb = new StringBuilder();
            for (var i = 0; i < tpl.Length; i++)
            {
                if (tpl[i] == '{' && i + 1 < tpl.Length && char.IsDigit(tpl[i + 1]))
                {
                    var j = i + 1; var num = 0;
                    while (j < tpl.Length && char.IsDigit(tpl[j])) { num = num * 10 + (tpl[j] - '0'); j++; }
                    if (j < tpl.Length && tpl[j] == '}' && num < p.Holes) { sb.Append(Lang == "ru" ? m.Groups[num + 1].Value : T2(m.Groups[num + 1].Value)); i = j; continue; }
                }
                sb.Append(tpl[i]);
            }
            return sb.ToString();
        }
        return null;
    }

    // подставляемую часть тоже переводим (там могут быть слова из словаря: «Подключено», название режима и т. п.)
    static string T2(string part) => Cyr.IsMatch(part) ? Translate(part) : part;

    // Что осталось без перевода (для проверки покрытия и для отчётов).
    public static string[] Missing() { lock (missing) return missing.ToArray(); }

    // ---------- обход окон ----------
    static void Sweep()
    {
        var app = Application.Current;
        if (app == null) return;
        if (Lang == "ru" && !anyTranslated) return;
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        try
        {
            foreach (Window w in app.Windows)
            {
                Walk(w, seen);
            }
        }
        catch { }
        if (Lang == "ru") anyTranslated = false;
    }

    static bool anyTranslated;

    // Для служебных снимков (окно не показывается): перевести дерево немедленно.
    public static void ApplyTree(DependencyObject root)
    {
        if (!started) return;
        try { Walk(root, new HashSet<object>(ReferenceEqualityComparer.Instance)); } catch (Exception ex) { audit.Add("ОШИБКА перевода: " + ex.GetType().Name + " " + ex.Message); }
        if (Lang != "ru") foreach (var s in Remaining(root)) audit.Add(root.GetType().Name + " | " + s);
    }

    static readonly SortedSet<string> audit = new(StringComparer.Ordinal);

    // Служебные снимки: записать в папку список русских надписей, которые остались без перевода (для проверки покрытия словаря).
    public static void WriteAudit(string dir)
    {
        if (Lang == "ru") return;
        try
        {
            Directory.CreateDirectory(dir);
            var exe = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "app");
            File.WriteAllLines(Path.Combine(dir, $"loc-missing-{exe}-{Lang}.txt"), audit.Concat(new[] { "--- строки, которые просили перевести, но их нет в словаре ---" }).Concat(Missing().OrderBy(x => x, StringComparer.Ordinal)));
        }
        catch { }
    }

    // Для служебных снимков: список русских надписей, оставшихся в дереве (после перевода).
    public static List<string> Remaining(DependencyObject root)
    {
        var found = new List<string>();
        void Check(string? s, string where) { if (!string.IsNullOrEmpty(s) && Cyr.IsMatch(s)) found.Add(where + ": " + s.Replace("\n", "\\n")); }
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Rec(DependencyObject d)
        {
            if (!seen.Add(d)) return;
            switch (d)
            {
                case Window w: Check(w.Title, "Title"); break;
                case Run r: Check(r.Text, "Text"); break;
                case TextBlock tb: foreach (var il in tb.Inlines) if (il is Run ir) Check(ir.Text, "Text"); break;
                case HeaderedContentControl hc when hc.Header is string hs: Check(hs, "Header"); break;
            }
            if (d is ContentControl cc && cc.Content is string cs) Check(cs, "Content");
            if (d is FrameworkElement fe && fe.ToolTip is string ts) Check(ts, "ToolTip");
            if (d is ItemsControl ic) foreach (var it in ic.Items) { if (it is string si) Check(si, "Item"); else if (it is DependencyObject di) Rec(di); }
            if (d is Visual or System.Windows.Media.Media3D.Visual3D) { var n = VisualTreeHelper.GetChildrenCount(d); for (var i = 0; i < n; i++) Rec(VisualTreeHelper.GetChild(d, i)); }
            foreach (var ch in LogicalTreeHelper.GetChildren(d)) if (ch is DependencyObject cd) Rec(cd);
        }
        Rec(root);
        return found;
    }

    // Перевести сам элемент (без потомков).
    static void FixOne(DependencyObject d)
    {
        switch (d)
        {
            case Window w: Fix(w, "title", w.Title, v => w.Title = v); break;
            case Run r: Fix(r, "text", r.Text, v => r.Text = v); break;
            case TextBlock tb: FixInlines(tb.Inlines); break;   // обращение к Inlines превращает простой Text в Run — для перевода это то, что нужно
        }
        if (d is ContentControl cc && cc.Content is string cs) Fix(cc, "content", cs, v => cc.Content = v);
        if (d is HeaderedContentControl hc && hc.Header is string hs) Fix(hc, "header", hs, v => hc.Header = v);
        if (d is FrameworkElement fe)
        {
            if (fe.ToolTip is string ts) Fix(fe, "tip", ts, v => fe.ToolTip = v);
            else if (fe.ToolTip is ToolTip tt && tt.Content is string tc) Fix(tt, "content", tc, v => tt.Content = v);
        }
    }

    static void FixInlines(InlineCollection col)
    {
        // сначала копия списка: смена Run.Text меняет сам TextBlock, а менять коллекцию во время обхода нельзя
        foreach (var il in new List<Inline>(col))
        {
            if (il is Run r) Fix(r, "text", r.Text, v => r.Text = v);
            else if (il is Span sp) FixInlines(sp.Inlines);
        }
    }

    static void Walk(DependencyObject d, HashSet<object> seen)
    {
        if (!seen.Add(d)) return;
        FixOne(d);
        if (d is ItemsControl ic && ic is not TreeView)
        {
            var items = new List<object>();
            foreach (var it in ic.Items) items.Add(it);
            foreach (var it in items) if (it is DependencyObject di) Walk(di, seen);
        }
        if (d is Visual or System.Windows.Media.Media3D.Visual3D)
        {
            var n = VisualTreeHelper.GetChildrenCount(d);
            for (var i = 0; i < n; i++) Walk(VisualTreeHelper.GetChild(d, i), seen);
        }
        var kids = new List<DependencyObject>();   // дети — копией: перевод Run меняет TextBlock, а он перечисляет их же
        foreach (var ch in LogicalTreeHelper.GetChildren(d)) if (ch is DependencyObject cd) kids.Add(cd);
        foreach (var cd in kids) Walk(cd, seen);
    }

    static void Fix(object owner, string slot, string current, Action<string> set)
    {
        Dictionary<string, St>? d;
        if (!states.TryGetValue(owner, out d))
        {
            if (Lang == "ru" || !Cyr.IsMatch(current)) return;   // русский язык и нет что менять — ничего не создаём
            d = new Dictionary<string, St>(); states.Add(owner, d);
        }
        if (!d.TryGetValue(slot, out var st))
        {
            if (Lang == "ru" || !Cyr.IsMatch(current)) return;
            st = new St(); d[slot] = st;
        }
        // Если текст сейчас тот, что поставили мы, — исходник тот же; иначе код записал новый текст, он и есть исходник.
        var src = st.Applied != null && st.Src != null && current == st.Applied ? st.Src : current;
        var want = Lang == "ru" ? src : T(src);
        if (want != current) set(want);
        st.Src = src; st.Applied = want;
        if (Lang != "ru" && want != src) anyTranslated = true;
        else if (Lang == "ru" && want == src && src != current) anyTranslated = true;
    }

    // ---------- окна сообщений и диалоги ----------
    public static MessageBoxResult Box(string text) => MessageBox.Show(T(text));
    public static MessageBoxResult Box(string text, string caption) => MessageBox.Show(T(text), T(caption));
    public static MessageBoxResult Box(string text, string caption, MessageBoxButton b) => MessageBox.Show(T(text), T(caption), b);
    public static MessageBoxResult Box(string text, string caption, MessageBoxButton b, MessageBoxImage i) => MessageBox.Show(T(text), T(caption), b, i);
    public static MessageBoxResult Box(Window owner, string text) => MessageBox.Show(owner, T(text));
    public static MessageBoxResult Box(Window owner, string text, string caption) => MessageBox.Show(owner, T(text), T(caption));
    public static MessageBoxResult Box(Window owner, string text, string caption, MessageBoxButton b) => MessageBox.Show(owner, T(text), T(caption), b);
    public static MessageBoxResult Box(Window owner, string text, string caption, MessageBoxButton b, MessageBoxImage i) => MessageBox.Show(owner, T(text), T(caption), b, i);

    // Проверка самого модуля на настоящей Windows (запуск лаунчера с --selftest-loc=<папка>): точные строки, шаблоны, обрывки, пробелы, возврат на русский.
    public static string SelfTest()
    {
        var sb = new StringBuilder(); var bad = 0;
        void Check(string what, string got, string want) { if (got == want) sb.AppendLine("OK    " + what); else { bad++; sb.AppendLine("FAIL  " + what + "  получено: «" + got + "»  ожидалось: «" + want + "»"); } }
        var oldLang = Lang;
        Lang = "en";
        Check("точная строка", T("Закрыть"), "Close");
        Check("шаблон", T("Обновлено: Плагин А"), "Updated: Плагин А");
        Check("шаблон с двумя дырами", T("Обновлено 2 из 5. Что не вышло — причина в менеджере внизу и в карточке."), "Updated 2 of 5. What failed is explained in the manager below and on the card.");
        Check("обрыв-приставка", T("Не получилось: нет связи"), "Failed: нет связи");
        Check("пробелы по краям", T("  Закрыть "), "  Close ");
        Check("нет перевода — как есть", T("Совсем неизвестная строка про бобров"), "Совсем неизвестная строка про бобров");
        Check("латиница не трогается", T("Taskbar Plus"), "Taskbar Plus");
        Lang = "de";
        Check("немецкий", T("Закрыть"), "Schließen");
        Check("немецкий шаблон", T("Профиль «nl» сохранён."), "Profil „nl“ gespeichert.");
        Lang = "ru";
        Check("русский — без изменений", T("Закрыть"), "Закрыть");
        Lang = oldLang;
        sb.AppendLine(bad == 0 ? "ВСЕ ПРОВЕРКИ ПРОШЛИ (словарь: " + Count + ")" : "НЕ ПРОШЛО ПРОВЕРОК: " + bad);
        return sb.ToString();
    }
}
