using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using BattleLauncher;

namespace TioxsitTerminal;

// Страницы Терминала. Построители страниц (…Page) принимают готовые данные — их же использует режим --screenshot.
public partial class MainWindow
{
    void Highlight(string key)
    {
        foreach (var kv in navButtons) kv.Value.Foreground = Ui.Res(kv.Key == key ? "Text" : "Muted");
    }

    UIElement ErrorPage(string title, Exception ex)
    {
        var (root, body) = Ui.Page(title);
        body.Children.Add(Ui.Text("Не удалось загрузить: " + ex.Message, 13, "Red"));
        body.Children.Add(Ui.Row(Ui.Btn("Повторить", "Ghost", () => { Navigate(navButtons.FirstOrDefault(k => k.Value.Foreground == Ui.Res("Text")).Key ?? "players"); return Task.CompletedTask; })));
        return root;
    }

    // ================= Игроки =================
    void GoPlayers(string query)
    {
        timer?.Stop();
        Highlight("players");
        ShowPlayers(query);
    }

    void ShowPlayers(string? prefill)
    {
        var (root, body) = Ui.Page("Игроки", "Найти по игровому коду (PLR-…), @нику или Telegram-id");
        var box = new TextBox { Width = 320, Text = prefill ?? "", Margin = new Thickness(0, 0, 8, 0) };
        var result = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };

        async Task Search()
        {
            result.Children.Clear();
            result.Children.Add(Ui.Muted("Ищу…"));
            try
            {
                var p = await Data.FindPlayer(box.Text);
                result.Children.Clear();
                result.Children.Add(p == null ? Ui.Muted("Игрок не найден.") : PlayerCard(p));
            }
            catch (Exception ex)
            {
                result.Children.Clear();
                result.Children.Add(Ui.Text("Ошибка: " + ex.Message, 13, "Red"));
            }
        }

        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) _ = Search(); };
        body.Children.Add(Ui.Row(box, Ui.Btn("Найти", "Primary", Search, 14)));
        body.Children.Add(result);
        Show(root);
        if (!string.IsNullOrWhiteSpace(prefill)) _ = Search();
    }

    UIElement PlayerCard(PlayerInfo p)
    {
        var host = new Border();
        void Render(PlayerInfo x) => host.Child = PlayerCardBody(x, Render);
        Render(p);
        return host;
    }

    UIElement PlayerCardBody(PlayerInfo p, Action<PlayerInfo> rerender)
    {
        var stack = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(Ui.Avatar(p.Name.Length > 0 ? p.Name : p.PlayerId, 56));
        var who = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        who.Children.Add(Ui.Text(p.Name.Length > 0 ? p.Name : "Без имени", 18, bold: true));
        who.Children.Add(Ui.Muted(p.PlayerId + (p.Username.Length > 0 ? " · " + p.Username : "") + " · id " + p.Id));
        head.Children.Add(who);
        stack.Children.Add(head);

        var badges = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        badges.Children.Add(Ui.Badge(Texts.Role(p.Role), p.Role is "admin" or "superadmin" ? "OnAccent" : "Muted", p.Role is "admin" or "superadmin" ? "Accent" : null));
        var statusText = Texts.Status(p.Status);
        if (p.Status == "restricted" && p.RestrictedUntil > 0) statusText += " до " + Texts.Time(p.RestrictedUntil);
        badges.Children.Add(Ui.Badge(statusText, p.Status == "active" ? "Green" : "Red"));
        stack.Children.Add(badges);

        var tiles = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        tiles.Children.Add(Ui.Tile(p.Points.ToString("N0"), "Трофеи"));
        tiles.Children.Add(Ui.Tile(p.Stars.ToString("N0"), "Stars"));
        tiles.Children.Add(Ui.Tile(p.Battles.ToString(), "Боёв"));
        tiles.Children.Add(Ui.Tile(p.Wins.ToString(), "Побед"));
        tiles.Children.Add(Ui.Tile(p.Losses.ToString(), "Поражений"));
        tiles.Children.Add(Ui.Tile(p.Draws.ToString(), "Ничьих"));
        tiles.Children.Add(Ui.Tile(p.Referee.ToString(), "Судил боёв"));
        tiles.Children.Add(Ui.Tile(p.Likes.ToString(), "Симпатий"));
        stack.Children.Add(tiles);
        stack.Children.Add(Ui.Muted("Аккаунт создан: " + Texts.Time(p.CreatedAt)));

        var s = session;
        if (s != null && s.CanBan && p.Id != s.Me.Id && p.Role is not ("admin" or "superadmin"))
        {
            stack.Children.Add(Ui.Text("Бан и ограничения", 15, bold: true).WithMargin(0, 18, 0, 0));
            long Until(int hours) => DateTimeOffset.UtcNow.AddHours(hours).ToUnixTimeMilliseconds();
            stack.Children.Add(Ui.Wrap(
                Ui.Btn("Ограничить на 1 час", null, () => SetStatus(p, "Ограничение на 1 час", "restricted", Until(1), rerender)),
                Ui.Btn("На 24 часа", null, () => SetStatus(p, "Ограничение на 24 часа", "restricted", Until(24), rerender)),
                Ui.Btn("На 7 дней", null, () => SetStatus(p, "Ограничение на 7 дней", "restricted", Until(24 * 7), rerender)),
                Ui.Btn("Забанить навсегда", "Danger", () => SetStatus(p, "Бан навсегда", "banned", null, rerender)),
                Ui.Btn("Снять бан / ограничение", null, () => SetStatus(p, "Снятие бана", "active", null, rerender))));
        }
        else if (s != null && p.Id != s.Me.Id && p.Role is not ("admin" or "superadmin"))
        {
            stack.Children.Add(Ui.Muted("Банить может админ с правом «Бан и ограничения» или Супердоступ.").WithMargin(0, 14, 0, 0));
        }
        return Ui.Card(stack);
    }

    async Task SetStatus(PlayerInfo p, string label, string status, long? until, Action<PlayerInfo> rerender)
    {
        var name = p.Name.Length > 0 ? p.Name : p.PlayerId;
        if (MessageBox.Show(this, $"{label}: «{name}» ({p.PlayerId})?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await Safe(async () =>
        {
            var s = session!;
            await Db.SetStatus(s.Me, s.DeviceId, p.Id, status, until);
            var type = status == "banned" ? "ban_forever" : status == "restricted" ? "ban_temp" : "unban";
            await Db.Log(s.Me, type, p.Id, p.Name, p.PlayerId, label);
            var fresh = await Data.LoadPlayer(p.Id);
            if (fresh != null) rerender(fresh);
            Toast("Готово: " + label);
        });
    }

    // ================= Журнал =================
    void ShowJournal() => _ = LoadJournal();

    async Task LoadJournal()
    {
        Show(Loading("Журнал"));
        try
        {
            var rep = Data.Reports(100); var logs = Data.Logs(100); var errs = Data.Errors(50);
            await Task.WhenAll(rep, logs, errs);
            Show(JournalPage(rep.Result, logs.Result, errs.Result));
        }
        catch (Exception ex) { Show(ErrorPage("Журнал", ex)); }
    }

    UIElement JournalPage(List<ReportEntry> reports, List<LogEntry> logs, List<ErrorEntry> errors)
    {
        var (root, body) = Ui.Page("Журнал", "Жалобы игроков, действия админов и ошибки, которые прислали игроки");

        body.Children.Add(SectionTitle($"Жалобы игроков ({reports.Count(r => !r.Closed)} новых)"));
        if (reports.Count == 0) body.Children.Add(Ui.Muted("Жалоб нет."));
        foreach (var r in reports)
        {
            var st = new StackPanel();
            st.Children.Add(Ui.Text($"{Texts.Reason(r.Reason)} — на {(r.TargetName.Length > 0 ? r.TargetName : "—")} ({r.TargetId})", 14, bold: true));
            st.Children.Add(Ui.Muted($"Пожаловался: {r.ReporterId}" + (r.BattleCode.Length > 0 ? " · бой " + r.BattleCode : "")));
            st.Children.Add(Ui.Muted(Texts.Time(r.Time) + (r.Closed ? " · закрыта" : ""), 12));
            var row = Ui.Row(Ui.Btn("Найти игрока", null, () => { GoPlayers(r.TargetId); return Task.CompletedTask; }, 12));
            if (!r.Closed)
                row.Children.Add(Ui.Btn("Закрыть жалобу", null, () => Safe(async () =>
                {
                    await Db.Put($"reports/{r.Id}/status", "closed");
                    await Db.Log(session!.Me, "report_closed", r.TargetId, r.TargetName, r.TargetId, "Жалоба закрыта: " + Texts.Reason(r.Reason));
                    await LoadJournal();
                }), 12));
            st.Children.Add(row);
            var card = Ui.Card(st, 8);
            if (r.Closed) card.Opacity = 0.55;
            body.Children.Add(card);
        }

        body.Children.Add(SectionTitle("Журнал действий"));
        if (logs.Count == 0) body.Children.Add(Ui.Muted("Записей пока нет."));
        foreach (var l in logs)
        {
            var st = new StackPanel();
            st.Children.Add(Ui.Text(Texts.Action(l.Type), 14, bold: true));
            st.Children.Add(Ui.Muted($"{l.Actor} ({l.ActorId}) → {(l.Target.Length > 0 ? l.Target : "—")} ({(l.TargetId.Length > 0 ? l.TargetId : "—")})"));
            if (l.Details.Length > 0) st.Children.Add(Ui.Muted(l.Details));
            st.Children.Add(Ui.Muted(Texts.Time(l.Time), 12));
            body.Children.Add(Ui.Card(st, 8));
        }

        body.Children.Add(SectionTitle($"Присланные ошибки ({errors.Count})"));
        if (errors.Count == 0) body.Children.Add(Ui.Muted("Пока никто ничего не присылал."));
        foreach (var e in errors)
        {
            var st = new StackPanel();
            st.Children.Add(Ui.Text(e.Who, 14, bold: true));
            st.Children.Add(Ui.Muted(Texts.Short(e.Text, 700)));
            st.Children.Add(Ui.Muted(Texts.Time(e.Time) + (e.Agent.Length > 0 ? " · " + Texts.Short(e.Agent, 80) : ""), 12));
            st.Children.Add(Ui.Row(Ui.Btn("Удалить", "Ghost", () => Safe(async () =>
            {
                await Db.Delete($"errorReports/{e.Id}");
                await LoadJournal();
            }), 12)));
            body.Children.Add(Ui.Card(st, 8));
        }
        return root;
    }

    static TextBlock SectionTitle(string t)
    {
        var tb = Ui.Text(t.ToUpperInvariant(), 12, "Muted", true);
        tb.Margin = new Thickness(0, 14, 0, 8);
        return tb;
    }

    // ================= Заявки на вход (в мой аккаунт) =================
    void ShowRequests()
    {
        _ = LoadRequests(true);
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        timer.Tick += async (_, _) => await LoadRequests(false);
        timer.Start();
    }

    async Task LoadRequests(bool first)
    {
        if (first) Show(Loading("Заявки на вход"));
        try
        {
            var list = await Data.LoginRequests(session!.Me.Id);
            Show(RequestsPage(list));
        }
        catch (Exception ex) { if (first) Show(ErrorPage("Заявки на вход", ex)); }
    }

    UIElement RequestsPage(List<LoginRequestEntry> list)
    {
        var (root, body) = Ui.Page("Заявки на вход", "Кто-то просит войти в твой аккаунт. Разрешай только то, что запросил ты сам. Список обновляется сам.");
        if (list.Count == 0) body.Children.Add(Ui.Muted("Сейчас заявок нет."));
        foreach (var r in list)
        {
            var st = new StackPanel();
            var where = r.App switch { "game" => "в игру", "admin" => "в админ-панель", "launcher" => "в лаунчер", _ => "в аккаунт" };
            st.Children.Add(Ui.Text($"Вход {where}", 15, bold: true));
            st.Children.Add(Ui.Muted($"Устройство: {Texts.Short(r.Device.Length > 0 ? r.Device : "неизвестное", 40)} · {Texts.Time(r.At)}"));
            st.Children.Add(Ui.Row(
                Ui.Btn("Разрешить", "Primary", () => AnswerRequest(r, "approved")),
                Ui.Btn("Отклонить", "Danger", () => AnswerRequest(r, "denied"))));
            body.Children.Add(Ui.Card(st, 8));
        }
        return root;
    }

    async Task AnswerRequest(LoginRequestEntry r, string status) => await Safe(async () =>
    {
        await Db.Patch($"users/{session!.Me.Id}/loginRequests/{r.Id}", new { status });
        Toast(status == "approved" ? "Вход разрешён" : "Вход отклонён");
        await LoadRequests(false);
    });

    // ================= Эфир =================
    void ShowLive()
    {
        var (root, body) = Ui.Page("Эфир", "Живая лента: новые действия, жалобы и ошибки. Обновляется раз в 8 секунд.");
        var online = Ui.Text("Онлайн: …", 15, "Accent", true);
        body.Children.Add(online);
        var feed = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        body.Children.Add(feed);
        Show(root);
        var seen = new HashSet<string>();

        async Task Poll()
        {
            try
            {
                var logs = Data.Logs(30); var reps = Data.Reports(30); var errs = Data.Errors(15); var on = Db.Count("presence");
                await Task.WhenAll(logs, reps, errs, on);
                online.Text = $"Онлайн: {on.Result}";
                var items = new List<(long Time, string Key, string Kind, string Text)>();
                foreach (var l in logs.Result) items.Add((l.Time, $"L{l.Time}{l.Type}{l.TargetId}", "действие", $"{Texts.Action(l.Type)} — {l.Actor} → {l.Target} {l.Details}"));
                foreach (var r in reps.Result) items.Add((r.Time, "R" + r.Id, "жалоба", $"{Texts.Reason(r.Reason)} — на {r.TargetName} ({r.TargetId})"));
                foreach (var e in errs.Result) items.Add((e.Time, "E" + e.Id, "ошибка", $"{e.Who}: {Texts.Short(e.Text, 160)}"));
                foreach (var it in items.OrderBy(i => i.Time))
                {
                    if (!seen.Add(it.Key)) continue;
                    var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                    line.Children.Add(Ui.Text(DateTimeOffset.FromUnixTimeMilliseconds(it.Time).ToLocalTime().ToString("HH:mm:ss"), 12, "Muted", wrap: false).WithMargin(0, 2, 10, 0));
                    line.Children.Add(Ui.Badge(it.Kind, it.Kind == "ошибка" ? "Red" : it.Kind == "жалоба" ? "Accent" : "Muted"));
                    var t = Ui.Text(it.Text, 13);
                    t.MaxWidth = 760;
                    line.Children.Add(t);
                    feed.Children.Insert(0, line);
                }
                while (feed.Children.Count > 300) feed.Children.RemoveAt(feed.Children.Count - 1);
            }
            catch { online.Text = "Нет связи… пробую снова"; }
        }

        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        timer.Tick += async (_, _) => await Poll();
        timer.Start();
        _ = Poll();
    }

    // ================= Статистика =================
    void ShowStats() => _ = LoadStats();

    async Task LoadStats()
    {
        Show(Loading("Статистика"));
        try { Show(StatsPage(await Data.Stats())); }
        catch (Exception ex) { Show(ErrorPage("Статистика", ex)); }
    }

    UIElement StatsPage(StatsData s)
    {
        var (root, body) = Ui.Page("Статистика", "Сводка по игре. Считается по ключам базы, поэтому быстро и почти не тратит трафик.");
        body.Children.Add(SectionTitle("Игроки"));
        body.Children.Add(Ui.Wrap(Ui.Tile(s.Users.ToString("N0"), "Всего аккаунтов"), Ui.Tile(s.Online.ToString("N0"), "Онлайн сейчас"),
            Ui.Tile(s.Admins.ToString(), "Админов"), Ui.Tile(s.Superadmins.ToString(), "Супердоступ"), Ui.Tile(s.Moderators.ToString(), "Модераторов (устар.)")));
        body.Children.Add(SectionTitle("Бои и сообщество"));
        body.Children.Add(Ui.Wrap(Ui.Tile(s.Battles.ToString("N0"), "Записей battles"), Ui.Tile(s.Matches.ToString("N0"), "Записей matches"), Ui.Tile(s.Clans.ToString("N0"), "Кланов")));
        body.Children.Add(SectionTitle("Модерация"));
        body.Children.Add(Ui.Wrap(Ui.Tile(s.NewReports.ToString(), "Новых жалоб"), Ui.Tile(s.Errors.ToString(), "Присланных ошибок")));
        return root;
    }

    // ================= Администрация (только Супердоступ) =================
    void ShowStaff() => _ = LoadStaff();

    async Task LoadStaff()
    {
        Show(Loading("Администрация"));
        try
        {
            var staff = Data.Staff(); var tokens = Data.Tokens();
            await Task.WhenAll(staff, tokens);
            Show(StaffPage(staff.Result, tokens.Result, null));
        }
        catch (Exception ex) { Show(ErrorPage("Администрация", ex)); }
    }

    static readonly (string Id, string Label)[] TokenPerms =
    {
        ("app_troll", "Приложение «Тролль-Баттл»"), ("app_videochat", "Приложение «ВидеоЧат»"), ("bans", "Бан и ограничения"),
        ("clans", "Модерация кланов"), ("stats", "Ивент и статистика игрока"), ("badges", "Значки-награды"),
        ("beta", "Заявки на бета-тест"), ("danger", "Система (кроме сброса)"), ("backups", "Бэкапы (обычно только Супердоступ)"), ("game", "Доступ к игре"),
    };

    UIElement StaffPage(List<StaffEntry> staff, List<TokenEntry> tokens, string? createdToken)
    {
        var (root, body) = Ui.Page("Администрация", "Кто имеет доступ к админ-панели и токены доступа. Роли здесь только смотрим; менять их можно в веб-админке.");
        body.Children.Add(SectionTitle("Админы и Супердоступ"));
        if (staff.Count == 0) body.Children.Add(Ui.Muted("Список пуст."));
        foreach (var m in staff)
        {
            var st = new StackPanel();
            st.Children.Add(Ui.Text($"{(m.Name.Length > 0 ? m.Name : "Без имени")} · {Texts.Role(m.Role)}", 14, bold: true));
            st.Children.Add(Ui.Muted($"{m.PlayerId} · id {m.Id}"));
            if (m.Permissions.Length > 0) st.Children.Add(Ui.Muted("Права: " + m.Permissions, 12));
            body.Children.Add(Ui.Card(st, 8));
        }

        body.Children.Add(SectionTitle("Токены доступа"));
        if (tokens.Count == 0) body.Children.Add(Ui.Muted("Токенов нет."));
        foreach (var t in tokens)
        {
            var st = new StackPanel();
            st.Children.Add(Ui.Text(t.Label.Length > 0 ? t.Label : "Без названия", 14, bold: true));
            st.Children.Add(Ui.Muted($"Создан {Texts.Time(t.CreatedAt)} · {(t.ExpiresAt > 0 ? "до " + Texts.Time(t.ExpiresAt) : "бессрочный")}"));
            st.Children.Add(Ui.Muted("Права: " + (t.Permissions.Length > 0 ? t.Permissions : "без прав"), 12));
            st.Children.Add(Ui.Row(Ui.Btn("Отозвать", "Danger", () => RevokeToken(t), 12)));
            body.Children.Add(Ui.Card(st, 8));
        }

        // создание токена
        var form = new StackPanel();
        form.Children.Add(Ui.Text("Новый токен доступа", 15, bold: true));
        var label = new TextBox { Margin = new Thickness(0, 10, 0, 8), MaxLength = 40 };
        form.Children.Add(Ui.Muted("Название (чтобы потом понять, чей он)"));
        form.Children.Add(label);
        var dur = new ComboBox { Margin = new Thickness(0, 0, 0, 8), Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var d in new[] { "1 день", "7 дней", "30 дней", "Бессрочный" }) dur.Items.Add(d);
        dur.SelectedIndex = 1;
        form.Children.Add(Ui.Muted("Срок"));
        form.Children.Add(dur);
        var checks = new List<(string Id, CheckBox Box)>();
        foreach (var (id, text) in TokenPerms)
        {
            var cb = new CheckBox { Content = text, Foreground = Ui.Res("Text"), Margin = new Thickness(0, 2, 0, 2) };
            checks.Add((id, cb));
            form.Children.Add(cb);
        }
        var created = new TextBox { IsReadOnly = true, Margin = new Thickness(0, 10, 0, 0), Visibility = createdToken == null ? Visibility.Collapsed : Visibility.Visible, Text = createdToken ?? "" };
        form.Children.Add(Ui.Row(Ui.Btn("Создать токен", "Primary", () => CreateToken(label.Text, dur.SelectedIndex, checks.Where(c => c.Box.IsChecked == true).Select(c => c.Id).ToList()), 13)));
        if (createdToken != null)
        {
            form.Children.Add(Ui.Text("Токен создан. Скопируй его сейчас и передай человеку лично — он даёт доступ к панели:", 13, "Accent").WithMargin(0, 10, 0, 0));
            form.Children.Add(created);
        }
        body.Children.Add(Ui.Card(form));
        return root;
    }

    async Task RevokeToken(TokenEntry t)
    {
        if (MessageBox.Show(this, $"Отозвать токен «{(t.Label.Length > 0 ? t.Label : "без названия")}»? Тот, у кого он есть, потеряет доступ.", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await Safe(async () =>
        {
            await Db.Delete($"accessTokens/{t.Token}");
            await Db.Log(session!.Me, "access_token_revoked", "—", t.Label, "—", "Токен отозван");
            await LoadStaff();
        });
    }

    async Task CreateToken(string label, int durationIndex, List<string> perms)
    {
        if (!session!.IsSuper) return;
        if (MessageBox.Show(this, "Создать токен доступа с выбранными правами? Он войдёт в админ-панель без аккаунта.", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await Safe(async () =>
        {
            var token = Db.NewToken();
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long? exp = durationIndex switch { 0 => now + 86400000L, 1 => now + 7 * 86400000L, 2 => now + 30 * 86400000L, _ => null };
            var permissions = perms.ToDictionary(p => p, _ => true);
            await Db.Put($"accessTokens/{token}", new { label = label.Trim().Length > 0 ? label.Trim() : null, createdAt = now, createdBy = session.Me.Id, permissions, expiresAt = exp });
            // сам токен в журнал не пишем
            await Db.Log(session.Me, "access_token_created", "—", label.Trim(), "—", perms.Count == 0 ? "без прав" : string.Join(", ", perms));
            var staff = Data.Staff(); var tokens = Data.Tokens();
            await Task.WhenAll(staff, tokens);
            Show(StaffPage(staff.Result, tokens.Result, token));
        });
    }

    // ================= Система (только Супердоступ) =================
    void ShowSystem() => _ = LoadSystem();

    async Task LoadSystem()
    {
        Show(Loading("Система"));
        try
        {
            var p = await Data.Platform();
            Show(SystemPage(p.Maintenance, p.MaintenanceText, p.Lockdown, p.LockdownText));
        }
        catch (Exception ex) { Show(ErrorPage("Система", ex)); }
    }

    UIElement SystemPage(bool maintenance, string maintenanceText, bool lockdown, string lockdownText)
    {
        var (root, body) = Ui.Page("Система", "Режимы платформы. Действуют сразу на всех игроков, поэтому каждое переключение — с подтверждением.");

        var m = new StackPanel();
        m.Children.Add(Ui.Text("Режим обслуживания", 16, bold: true));
        m.Children.Add(Ui.Muted("Игроки без роли видят заглушку; админы и Супердоступ работают как обычно."));
        m.Children.Add(Ui.Row(Ui.Badge(maintenance ? "Включён" : "Выключен", maintenance ? "OnAccent" : "Muted", maintenance ? "Accent" : null)));
        var mText = new TextBox { Text = maintenanceText, Margin = new Thickness(0, 10, 0, 0), MaxLength = 300 };
        m.Children.Add(Ui.Muted("Сообщение для игроков (необязательно)"));
        m.Children.Add(mText);
        m.Children.Add(Ui.Row(
            Ui.Btn(maintenance ? "Выключить" : "Включить", maintenance ? null : "Primary", () => ToggleMaintenance(!maintenance, mText.Text)),
            Ui.Btn("Сохранить сообщение", "Ghost", () => Safe(async () => { await Db.Put("config/maintenanceMessage", mText.Text.Trim().Length > 0 ? mText.Text.Trim() : null); Toast("Сообщение сохранено"); }))));
        body.Children.Add(Ui.Card(m));

        var l = new StackPanel();
        l.Children.Add(Ui.Text("Аварийный режим", 16, "Red", true));
        l.Children.Add(Ui.Muted("Закрывает игру и панель для ВСЕХ, кроме Супердоступа, и останавливает автобэкапы. Только для настоящих происшествий."));
        l.Children.Add(Ui.Row(Ui.Badge(lockdown ? "Включён" : "Выключен", lockdown ? "Red" : "Muted")));
        var lText = new TextBox { Text = lockdownText, Margin = new Thickness(0, 10, 0, 0), MaxLength = 300 };
        l.Children.Add(Ui.Muted("Сообщение для игроков (необязательно)"));
        l.Children.Add(lText);
        l.Children.Add(Ui.Row(Ui.Btn(lockdown ? "Выключить аварийный режим" : "Включить аварийный режим", lockdown ? null : "Danger", () => ToggleLockdown(!lockdown, lText.Text))));
        body.Children.Add(Ui.Card(l));
        return root;
    }

    async Task ToggleMaintenance(bool on, string message)
    {
        var q = on ? "Включить режим обслуживания?" : "Выключить режим обслуживания?";
        if (MessageBox.Show(this, q, "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await Safe(async () =>
        {
            if (!session!.IsSuper) return;
            await Db.Put("config/maintenanceMessage", message.Trim().Length > 0 ? message.Trim() : null);
            await Db.Put("config/maintenanceMode", on);
            await Db.Log(session.Me, "maintenance_toggled", "—", "—", "—", on ? "Включён" : "Выключен");
            await LoadSystem();
        });
    }

    async Task ToggleLockdown(bool on, string message)
    {
        var q = on
            ? "Включить АВАРИЙНЫЙ РЕЖИМ? Игра и панель закроются для всех, кроме Супердоступа, автобэкапы остановятся."
            : "Выключить аварийный режим, вернуть доступ всем и возобновить автобэкапы?";
        if (MessageBox.Show(this, q, "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await Safe(async () =>
        {
            if (!session!.IsSuper) return;
            await Db.Put("config/emergencyMessage", message.Trim().Length > 0 ? message.Trim() : null);
            await Db.Put("config/emergencyLockdown", on);
            await Db.Put("config/backupsDisabled", on ? true : null); // как в веб-админке: режим и автобэкапы переключаются вместе
            await Db.Log(session.Me, "emergency_lockdown_toggled", "—", "—", "—", on ? "Включён (+ автобэкапы остановлены)" : "Выключен (+ автобэкапы возобновлены)");
            await LoadSystem();
        });
    }
}

static class UiExt
{
    public static T WithMargin<T>(this T e, double l, double t, double r, double b) where T : FrameworkElement
    {
        e.Margin = new Thickness(l, t, r, b);
        return e;
    }
}
