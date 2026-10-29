using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TaskbarPlusApp;

// Окно настроек Taskbar Plus. Весь интерфейс собирается кодом: при смене формы или режима перерисовывается целиком.
sealed class MainWindow : Window
{
    readonly Engines engine;
    readonly AppearanceSettings settings;
    readonly StackPanel root = new();
    readonly TextBlock statusLine = new();
    readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    // Состояние автоскрытия спрашивает у Проводника, а он может отвечать с задержкой (особенно сразу после перезапуска) —
    // поэтому читаем его в фоне, а окно рисует то, что уже известно.
    bool autoHide;

    void RefreshAutoHide()
    {
        Task.Run(Native.GetAutoHide).ContinueWith(t =>
        {
            if (t.IsFaulted) return;
            Dispatcher.BeginInvoke(new Action(() => { if (autoHide != t.Result) { autoHide = t.Result; Build(); } }));
        });
    }

    public MainWindow(Engines engine, AppearanceSettings settings)
    {
        this.engine = engine;
        this.settings = settings;
        Title = "Taskbar Plus";
        Width = 900; Height = 780; MinWidth = 600; MinHeight = 480;
        Background = Res("Bg");
        Foreground = Res("Text");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border { Padding = new Thickness(28, 24, 28, 28), Child = root },
        };
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); settings.Save(); };
        engine.StatusChanged += () => Dispatcher.BeginInvoke(new Action(() => { statusLine.Text = engine.Status; MaybeAskRestart(); }));
        Build();
        RefreshAutoHide();
        Dispatcher.BeginInvoke(new Action(MaybeAskRestart));
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Форма держится, пока работает плагин: закрытие окна просто прячет его в фон.
        if (!App.Quitting && settings.Active)
        {
            e.Cancel = true;
            Hide();
            if (!settings.BackgroundHintShown)
            {
                settings.BackgroundHintShown = true;
                settings.Save();
                MessageBox.Show("Taskbar Plus продолжает работать в фоне — иначе форма панели пропала бы.\n\n" +
                                "Чтобы открыть окно снова, запустите Taskbar Plus из лаунчера или из меню «Пуск». " +
                                "Чтобы совсем выйти и вернуть панель как была, нажмите «Выйти» внизу окна.",
                                "Taskbar Plus", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return;
        }
        base.OnClosing(e);
        Application.Current.Shutdown();
    }

    // Старая версия движка остаётся в Проводнике, пока его не перезапустить: предлагаем сделать это сразу, один раз за запуск.
    bool restartAsked;

    void MaybeAskRestart()
    {
        if (restartAsked || !settings.UseXaml || !engine.Xaml.NeedsRestart) return;
        restartAsked = true;
        _ = ConfirmRestartAsync("Движок панели задач обновился. Чтобы заработала новая версия, панель нужно перезапустить.");
    }

    void Changed()
    {
        engine.Update(settings);
        saveTimer.Stop();
        saveTimer.Start();
    }

    // ---------- сборка окна ----------
    void Build()
    {
        root.Children.Clear();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new TextBlock { Text = "TASKBAR PLUS", FontFamily = (FontFamily)Application.Current.FindResource("Mono"), FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Res("Accent") });
        root.Children.Add(head);
        root.Children.Add(new TextBlock
        {
            Text = "Плагин Ghost Shell Launcher. Панель задач Windows по-своему: форма, острова, значки, автоскрытие. " +
                   "Всё, что вы поменяли, можно вернуть кнопкой «Вернуть как было».",
            Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6),
        });
        statusLine.Text = engine.Status;
        statusLine.Foreground = Res("Accent2");
        statusLine.FontSize = 12;
        statusLine.TextWrapping = TextWrapping.Wrap;
        statusLine.Margin = new Thickness(0, 0, 0, 14);
        root.Children.Add(statusLine);

        root.Children.Add(EngineCard());
        if (settings.UseXaml && XamlEngine.UnavailableReason == null)
        {
            root.Children.Add(LayoutCard());
            root.Children.Add(IslandsCard());
        }
        root.Children.Add(AppearanceCard());
        root.Children.Add(IconsCard());
        root.Children.Add(AutoHideCard());
        root.Children.Add(SystemCard());
    }

    // ---------- карточки ----------
    // ---------- новый движок (Windows 11) ----------
    static readonly string[] SwatchColors = { "#000000", "#202020", "#F2F2F2", "#00F0FF", "#FF2A9D", "#6C4DFF", "#1E6BFF", "#20C27A" };

    UIElement EngineCard()
    {
        var rows = new List<UIElement>();
        var reason = XamlEngine.UnavailableReason;
        rows.Add(Note("Подключается к панели задач Windows 11 и меняет её настоящие элементы: края получаются гладкими, фон — прозрачным, цветным или размытым. " +
                      "Пока он не заработал (или если Windows устроена иначе), действует обычный способ ниже."));
        if (reason != null)
        {
            rows.Add(Note(reason));
            return Card("Новый движок (Windows 11)", rows);
        }

        rows.Add(Row("Новый движок", "Включить гладкие края, прозрачность и размытие. При выходе из плагина панель возвращается как была.",
            Seg(new[] { "Выкл", "Вкл" }, settings.UseXaml ? 1 : 0, i => SetUseXaml(i == 1))));

        if (settings.UseXaml)
        {
            rows.Add(Row("Фон панели", "Как в Windows — родной фон. Свой цвет — заливка выбранным цветом. Прозрачный — без фона, видны только значки. Размытие — матовое стекло.",
                Seg(new[] { "Как в Windows", "Свой цвет", "Прозрачный", "Размытие" }, BgIndex(), i =>
                {
                    settings.BgMode = new[] { AppearanceSettings.BgDefault, AppearanceSettings.BgSolid, AppearanceSettings.BgClear, AppearanceSettings.BgAcrylic }[i];
                    Changed(); Build();
                })));
            if (settings.BgMode is AppearanceSettings.BgSolid or AppearanceSettings.BgAcrylic || settings.Shape == AppearanceSettings.ShapeIslands)
            {
                rows.Add(Row("Цвет", "Готовые цвета или свой в виде #RRGGBB", ColorPicker(settings.BgColor, hex => { settings.BgColor = hex; Changed(); })));
                rows.Add(SliderRow("Непрозрачность", "Чем меньше, тем прозрачнее", 0, 100, settings.BgOpacity, "%", v => { settings.BgOpacity = v; Changed(); }));
            }
            rows.Add(Row("Тонкая линия панели", "У панели Windows 11 есть тонкая обводка. Её можно убрать.",
                Seg(new[] { "Оставить", "Убрать" }, settings.HideStroke ? 1 : 0, i => { settings.HideStroke = i == 1; Changed(); Build(); })));
            rows.Add(Row("Только значки", "Панель без фона и обводки: значки просто лежат на рабочем столе.", Btn("Применить", null, () =>
            {
                settings.Shape = AppearanceSettings.ShapeNone;
                settings.BgMode = AppearanceSettings.BgClear;
                settings.HideStroke = true;
                Changed(); Build();
            })));

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            buttons.Children.Add(Btn("Отправить отчёт разработчику", "Primary", () => { _ = UploadReportAsync(); }, first: true));
            buttons.Children.Add(Btn("Сохранить отчёт на рабочий стол", null, () => { _ = SaveReportAsync(); }));
            buttons.Children.Add(Btn("Перезапустить панель задач", null, () => { _ = ConfirmRestartAsync(null); }));
            if (engine.Xaml.Blocked) buttons.Children.Add(Btn("Попробовать снова", "Primary", () => { engine.Xaml.ClearBlock(); Changed(); Build(); }));
            rows.Add(buttons);
            rows.Add(Note("Если что-то выглядит не так или не работает, нажмите «Отправить отчёт разработчику». В отчёте версия Windows, настройки плагина и названия элементов панели — ничего личного. " +
                          "Он уходит в общую базу проекта и попадает в репозиторий. Можно и сохранить его файлом на рабочий стол."));
        }
        return Card("Новый движок (Windows 11)", rows);
    }

    int BgIndex() => settings.BgMode switch
    {
        AppearanceSettings.BgSolid => 1, AppearanceSettings.BgClear => 2, AppearanceSettings.BgAcrylic => 3, _ => 0,
    };

    void SetUseXaml(bool on)
    {
        if (on && !settings.XamlConsentShown)
        {
            var ok = MessageBox.Show(this,
                "Новый движок подключается к панели задач внутри Проводника Windows (так работают и другие программы для оформления панели).\n\n" +
                "Если Проводник закроется сразу после подключения два раза подряд, движок сам отключится.\n" +
                "Вернуть всё как было можно кнопкой «Вернуть как было» или перезапуском панели задач.\n\nВключить?",
                "Taskbar Plus", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            if (!ok) { Build(); return; }
            settings.XamlConsentShown = true;
        }
        settings.UseXaml = on;
        Changed();
        Build();
    }

    StackPanel ColorPicker(string current, Action<string> set)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var box = new TextBox { Text = current, Width = 86, Padding = new Thickness(6, 4, 6, 4), VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        box.TextChanged += (_, _) =>
        {
            var t = box.Text.Trim();
            if (System.Text.RegularExpressions.Regex.IsMatch(t, "^#[0-9A-Fa-f]{6}$")) set(t.ToUpperInvariant());
        };
        panel.Children.Add(box);
        foreach (var hex in SwatchColors)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var sw = new Button
            {
                Width = 24, Height = 24, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(0),
                Background = new SolidColorBrush(color), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), ToolTip = hex,
            };
            sw.Click += (_, _) => { box.Text = hex; set(hex); };
            panel.Children.Add(sw);
        }
        return panel;
    }

    // ---------- расположение ----------
    static readonly string[] GroupIds = { "widgets", "system", "apps" };
    static readonly string[] Zones = { AppearanceSettings.ZoneDefault, AppearanceSettings.ZoneLeft, AppearanceSettings.ZoneCenter, AppearanceSettings.ZoneRight };

    static string GroupTitle(string id) => id switch { "widgets" => "Виджеты", "system" => "Часы, сеть и трей", "apps" => "Значки приложений", _ => id };
    static string GroupHint(string id) => id switch
    {
        "widgets" => "Погода и другие виджеты слева на панели",
        "system" => "Часы, язык ввода, сеть, звук, скрытые значки — всё это одно целое и всегда остаётся одним объектом",
        _ => "«Пуск», поиск и все значки программ",
    };

    string GetZone(string id) => id switch { "widgets" => settings.ZoneWidgets, "system" => settings.ZoneSystem, _ => settings.ZoneApps };

    void SetZone(string id, string zone)
    {
        if (id == "widgets") settings.ZoneWidgets = zone;
        else if (id == "system") settings.ZoneSystem = zone;
        else settings.ZoneApps = zone;
    }

    UIElement LayoutCard()
    {
        var rows = new List<UIElement>
        {
            Note("Каждую часть панели можно поставить слева, по центру или справа и поменять местами. «Как в Windows» — не трогать. " +
                 "Часы, сеть, трей и скрытые значки всегда двигаются и выглядят одним целым."),
        };
        foreach (var id in GroupIds)
        {
            var gid = id;
            rows.Add(Row(GroupTitle(gid), GroupHint(gid),
                Seg(new[] { "Как в Windows", "Слева", "По центру", "Справа" }, Math.Max(0, Array.IndexOf(Zones, GetZone(gid))), i => { SetZone(gid, Zones[i]); Changed(); Build(); })));
        }
        rows.Add(Row("Порядок слева направо", "Если несколько частей стоят в одном месте, они выстраиваются в этом порядке", OrderEditor()));
        rows.Add(SliderRow("Отступ от края", "Для частей «слева» и «справа»", 0, 60, settings.EdgeMargin, "px", v => { settings.EdgeMargin = v; Changed(); }));
        rows.Add(Note("Нажатия идут за значками. Окна Windows (календарь, быстрые настройки, «Пуск») могут открываться на прежнем месте."));
        return Card("Расположение", rows);
    }

    StackPanel OrderEditor()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < settings.LayoutOrder.Length; i++)
        {
            var idx = i;
            var cell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(i == 0 ? 0 : 10, 0, 0, 0) };
            var left = Btn("◀", null, () => MoveGroup(idx, -1), first: true);
            left.IsEnabled = idx > 0;
            var right = Btn("▶", null, () => MoveGroup(idx, 1), first: false);
            right.IsEnabled = idx < settings.LayoutOrder.Length - 1;
            cell.Children.Add(left);
            cell.Children.Add(new TextBlock { Text = GroupTitle(settings.LayoutOrder[i]), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
            cell.Children.Add(right);
            panel.Children.Add(cell);
        }
        return panel;
    }

    void MoveGroup(int index, int delta)
    {
        var to = index + delta;
        if (to < 0 || to >= settings.LayoutOrder.Length) return;
        (settings.LayoutOrder[index], settings.LayoutOrder[to]) = (settings.LayoutOrder[to], settings.LayoutOrder[index]);
        Changed(); Build();
    }

    // ---------- острова по отдельности ----------
    readonly HashSet<string> openIslands = new();

    UIElement IslandsCard()
    {
        var rows = new List<UIElement>();
        var islandsOn = settings.Shape == AppearanceSettings.ShapeIslands;
        var edgesOn = islandsOn || (settings.Shape is AppearanceSettings.ShapePill or AppearanceSettings.ShapeTab && settings.KeepEdges);
        if (!edgesOn)
        {
            rows.Add(Note("Острова сейчас не рисуются. Выберите форму «Острова» в карточке «Внешний вид панели» — тогда у каждого острова можно задать свой вид. " +
                          "С формами «Плавающая» и «Вкладка» отдельно настраиваются только часы с треем и виджеты."));
            return Card("Острова по отдельности", rows);
        }
        rows.Add(Note("Нажмите на остров, чтобы поменять именно его: углы (прямые или круглые), цвет, прозрачность, размытие — или скрыть. «Как у всех» берётся из общих настроек выше."));
        rows.Add(IslandRow("system", "Часы, сеть и трей (одним объектом)"));
        rows.Add(IslandRow("widgets", "Виджеты"));
        if (islandsOn)
        {
            rows.Add(IslandRow("apps", settings.IslandMode == "each" ? "Все значки приложений (кроме настроенных отдельно)" : "Группы значков"));
            if (settings.IslandMode == "each")
            {
                var apps = XamlEngine.ReadIslands().Where(x => x.Kind == "app").ToList();
                if (apps.Count == 0) rows.Add(Note("Список значков появится, когда движок их найдёт. Нажмите «Обновить список»."));
                foreach (var a in apps) rows.Add(IslandRow(a.Key, a.Name.Length > 0 ? a.Name : "Значок"));
            }
            else rows.Add(Note("Отдельные значки настраиваются в режиме «Каждый значок» (карточка «Внешний вид панели»)."));
        }
        rows.Add(Btn("Обновить список", null, () => Build(), first: true));
        return Card("Острова по отдельности", rows);
    }

    static string StyleSummary(IslandStyle? st)
    {
        if (st == null || st.IsEmpty) return "как у всех";
        var parts = new List<string>();
        if (st.Hidden) parts.Add("скрыт");
        if (st.Radius == 0) parts.Add("прямые углы");
        else if (st.Radius >= 0) parts.Add("углы " + st.Radius);
        if (st.Mode != null) parts.Add(st.Mode switch { AppearanceSettings.BgSolid => "цвет", AppearanceSettings.BgClear => "прозрачный", AppearanceSettings.BgAcrylic => "размытие", _ => "как в Windows" });
        if (st.Color != null) parts.Add(st.Color);
        if (st.Opacity >= 0) parts.Add(st.Opacity + "%");
        return string.Join(", ", parts);
    }

    UIElement IslandRow(string key, string title)
    {
        settings.Islands.TryGetValue(key, out var st);
        var open = openIslands.Contains(key);
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        var head = new Button
        {
            Content = (open ? "▾  " : "▸  ") + title + "   —   " + StyleSummary(st),
            HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 7, 10, 7),
        };
        head.Click += (_, _) => { if (!openIslands.Remove(key)) openIslands.Add(key); Build(); };
        panel.Children.Add(head);
        if (!open) return panel;

        IslandStyle Get() { if (!settings.Islands.TryGetValue(key, out var x)) settings.Islands[key] = x = new IslandStyle(); return x; }
        void Touch() { if (settings.Islands.TryGetValue(key, out var x) && x.IsEmpty) settings.Islands.Remove(key); Changed(); }

        var body = new StackPanel { Margin = new Thickness(14, 10, 0, 4) };

        var radius = st?.Radius ?? -1;
        var shapeIdx = radius < 0 ? 0 : radius == 0 ? 1 : radius >= 100 ? 2 : 3;
        body.Children.Add(Row("Углы", "Прямые, круглые, свои или как у остальных",
            Seg(new[] { "Как у всех", "Прямые", "Круглые", "Свои" }, shapeIdx, i =>
            {
                Get().Radius = i switch { 0 => -1, 1 => 0, 2 => 100, _ => 12 };
                Touch(); Build();
            })));
        if (shapeIdx == 3)
            body.Children.Add(SliderRow("Скругление", "Насколько круглые углы", 1, 40, radius, "px", v => { Get().Radius = v; Touch(); }));

        var modeIdx = st?.Mode switch { AppearanceSettings.BgSolid => 1, AppearanceSettings.BgClear => 2, AppearanceSettings.BgAcrylic => 3, _ => 0 };
        body.Children.Add(Row("Фон", "Как у остальных, свой цвет, прозрачный (видны только значки) или размытие",
            Seg(new[] { "Как у всех", "Цвет", "Прозрачный", "Размытие" }, modeIdx, i =>
            {
                Get().Mode = i switch { 1 => AppearanceSettings.BgSolid, 2 => AppearanceSettings.BgClear, 3 => AppearanceSettings.BgAcrylic, _ => null };
                Touch(); Build();
            })));
        if (modeIdx != 2)
        {
            body.Children.Add(Row("Цвет", "Например, фиолетовый. Пусто — общий цвет",
                ColorPicker(st?.Color ?? settings.BgColor, hex => { Get().Color = hex; Touch(); })));
            body.Children.Add(SliderRow("Непрозрачность", "Чем меньше, тем прозрачнее", 0, 100, st != null && st.Opacity >= 0 ? st.Opacity : settings.BgOpacity, "%", v => { Get().Opacity = v; Touch(); }));
        }
        body.Children.Add(Row("Остров", "Можно убрать остров совсем — значки останутся на панели",
            Seg(new[] { "Показывать", "Скрыть" }, st is { Hidden: true } ? 1 : 0, i => { Get().Hidden = i == 1; Touch(); Build(); })));
        body.Children.Add(Btn("Сбросить этот остров", null, () => { settings.Islands.Remove(key); Changed(); Build(); }, first: true));
        panel.Children.Add(body);
        return panel;
    }

    async Task UploadReportAsync()
    {
        var ok = MessageBox.Show(this,
            "Отчёт уйдёт разработчику: в общую базу проекта и в репозиторий на GitHub.\n\n" +
            "В нём версия Windows, настройки плагина, статус движка и названия элементов панели задач. Ничего личного, имя пользователя вычищается.\n\nОтправить?",
            "Taskbar Plus", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        if (!ok) return;
        Say("Отправляю отчёт…");
        var err = await engine.Xaml.UploadReportAsync();
        Say(err == null ? "Отчёт отправлен. Спасибо! Напишите разработчику, что отправили." : "Не получилось отправить отчёт: " + err + ". Можно сохранить его файлом на рабочий стол.");
    }

    async Task SaveReportAsync()
    {
        Say("Собираю отчёт…");
        try
        {
            var path = await engine.Xaml.SaveReportAsync();
            Say("Отчёт сохранён: " + path);
        }
        catch (Exception ex) { Say("Не получилось сохранить отчёт: " + ex.Message); }
    }

    UIElement AppearanceCard()
    {
        var rows = new List<UIElement>();
        rows.Add(Row("Готовые варианты", "Нажмите — применится сразу, потом можно подстроить ползунками ниже.", Presets()));
        rows.Add(Row("Форма панели", "Обычная — как в Windows. Плавающая — отдельная скруглённая панель с отступами. Вкладка — скруглён только верх, низ прижат к краю экрана. Острова — отдельные группы значков.",
            Seg(new[] { "Обычная", "Плавающая", "Вкладка", "Острова" }, ShapeIndex(), i =>
            {
                settings.Shape = new[] { AppearanceSettings.ShapeNone, AppearanceSettings.ShapePill, AppearanceSettings.ShapeTab, AppearanceSettings.ShapeIslands }[i];
                Changed(); Build();
            })));
        if (settings.Shape != AppearanceSettings.ShapeNone)
        {
            rows.Add(SliderRow("Скругление", "Насколько круглые углы (у маленьких островов сдерживается, чтобы остров не стал кругом)", 0, 30, settings.Radius, "px", v => { settings.Radius = v; Changed(); }));
            if (settings.Shape is AppearanceSettings.ShapePill or AppearanceSettings.ShapeTab)
            {
                rows.Add(SliderRow("Отступ по бокам", "Чем больше, тем уже панель: так получается небольшая панель по центру", 0, 600, settings.SideMargin, "px", v => { settings.SideMargin = v; Changed(); }));
                rows.Add(Row("Часы, трей и виджеты", "Оставить отдельными островами по краям, иначе при большом отступе они пропадут с экрана вместе с часами.",
                    Seg(new[] { "Оставить", "Скрыть" }, settings.KeepEdges ? 0 : 1, i => { settings.KeepEdges = i == 0; Changed(); Build(); })));
            }
            if (settings.Shape == AppearanceSettings.ShapeIslands)
            {
                rows.Add(Row("Что окружать", "Каждый значок — свой остров. Группу — один остров на несколько соседних значков.",
                    Seg(new[] { "Каждый значок", "Группу значков" }, settings.IslandMode == "group" ? 1 : 0, i => { settings.IslandMode = i == 1 ? "group" : "each"; Changed(); Build(); })));
                rows.Add(Row("Вид островов", "Плавающие — отдельные скруглённые плитки. Вкладки — скруглены сверху и прижаты к низу экрана.",
                    Seg(new[] { "Плавающие", "Вкладки" }, settings.IslandStyle == "tab" ? 1 : 0, i => { settings.IslandStyle = i == 1 ? "tab" : "float"; Changed(); Build(); })));
                if (settings.IslandMode == "group")
                    rows.Add(SliderRow("Поля острова", "Расстояние от значков до края острова", 0, 40, settings.IslandPadding, "px", v => { settings.IslandPadding = v; Changed(); }));
                else
                    rows.Add(SliderRow("Зазор между островами", "Чем больше, тем дальше острова друг от друга", 0, 40, settings.IslandGap, "px", v => { settings.IslandGap = v; Changed(); }));
            }
            var floating = settings.Shape == AppearanceSettings.ShapePill || (settings.Shape == AppearanceSettings.ShapeIslands && settings.IslandStyle == "float");
            if (floating)
                rows.Add(SliderRow("Зазор снизу", "Расстояние от панели до нижнего края экрана (значки остаются на месте, поэтому много не нужно)", 0, 12, settings.BottomGap, "px", v => { settings.BottomGap = v; Changed(); }));
            rows.Add(Note("Формы работают для панели, которая стоит внизу экрана. Пока плагин запущен, форма держится сама; при выходе из плагина панель возвращается к обычной."));
        }

        return Card("Внешний вид панели", rows);
    }

    // Готовые варианты — те же формы, что на картинках владельца.
    WrapPanel Presets()
    {
        var wrap = new WrapPanel { MaxWidth = 460 };
        void Add(string name, Action<AppearanceSettings> apply)
        {
            var b = new Button { Content = name, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 6) };
            b.Click += (_, _) => { apply(settings); Changed(); Build(); };
            wrap.Children.Add(b);
        }
        Add("Обычная", s => { s.Shape = AppearanceSettings.ShapeNone; });
        Add("Плавающая", s => { s.Shape = AppearanceSettings.ShapePill; s.Radius = 18; s.SideMargin = 24; s.BottomGap = 8; });
        Add("Острова", s => { s.Shape = AppearanceSettings.ShapeIslands; s.IslandMode = "each"; s.IslandStyle = "float"; s.Radius = 14; s.IslandGap = 8; s.BottomGap = 6; });
        Add("Вкладка", s => { s.Shape = AppearanceSettings.ShapeTab; s.Radius = 16; s.SideMargin = 24; });
        Add("Вкладки-острова", s => { s.Shape = AppearanceSettings.ShapeIslands; s.IslandMode = "each"; s.IslandStyle = "tab"; s.Radius = 14; s.IslandGap = 8; });
        return wrap;
    }

    UIElement IconsCard()
    {
        var rows = new List<UIElement>();
        foreach (var o in RegistrySettings.Options.Where(RegistrySettings.Available).Where(o => o.Id != "animations"))
        {
            var opt = o;
            rows.Add(Row(opt.Title, opt.Hint, Seg(opt.Labels, Array.IndexOf(opt.Values, RegistrySettings.Current(opt)), i => ApplyOption(opt, i))));
        }
        return Card("Значки и кнопки", rows);
    }

    UIElement AutoHideCard()
    {
        var rows = new List<UIElement>
        {
            Row("Автоскрытие панели", "Панель прячется, пока курсор не у края экрана", Seg(new[] { "Выкл", "Вкл" }, autoHide ? 1 : 0, i => SetAutoHideAsync(i == 1))),
        };
        var anim = RegistrySettings.Options.First(o => o.Id == "animations");
        rows.Add(Row(anim.Title, anim.Hint, Seg(anim.Labels, Array.IndexOf(anim.Values, RegistrySettings.Current(anim)), i => ApplyOption(anim, i))));
        var sys = Native.SystemAnimationsOn;
        var sysRow = Row("Анимации Windows", sys ? "Включены. Если панель при автоскрытии всё равно появляется без движения — Windows 11 у вас так и делает."
                                                 : "Выключены — из-за этого панель появляется и пропадает мгновенно, без движения.",
            sys ? new TextBlock { Text = "Включены", Foreground = Res("Green"), VerticalAlignment = VerticalAlignment.Center }
                : Btn("Включить анимации Windows", "Primary", () =>
                {
                    Say(Native.EnableSystemAnimations() ? "Анимации Windows включены" : "Не получилось включить; откройте Параметры → Специальные возможности → Визуальные эффекты");
                    Build();
                }));
        rows.Add(sysRow);
        rows.Add(Note("Собственная плавная анимация выезда снизу вверх пока не сделана: двигать саму панель Windows 11 программам не даёт, нужен другой приём."));
        return Card("Автоскрытие и анимация", rows);
    }

    UIElement SystemCard()
    {
        var rows = new List<UIElement>
        {
            Row("Запускать вместе с Windows", "Форма будет применяться после входа в систему, окно открываться не будет",
                Seg(new[] { "Нет", "Да" }, RegistrySettings.Autostart ? 1 : 0, i =>
                {
                    try { RegistrySettings.SetAutostart(i == 1); Say(i == 1 ? "Автозапуск включён" : "Автозапуск выключен"); }
                    catch (Exception ex) { Say("Не получилось: " + ex.Message); }
                    Build();
                })),
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(Btn("Перезапустить панель задач", null, () => { _ = ConfirmRestartAsync(null); }, first: true));
        buttons.Children.Add(Btn("Вернуть как было", "Danger", () => { _ = ResetAllAsync(); }));
        buttons.Children.Add(Btn("Выйти", null, () => { App.Quitting = true; Close(); }));
        rows.Add(buttons);
        return Card("Система", rows);
    }

    // ---------- действия ----------
    void ApplyOption(RegOption opt, int index)
    {
        var err = RegistrySettings.Set(opt, opt.Values[index]);
        if (err != null)
        {
            Say(err);
            if (MessageBox.Show(this, err + "\n\nОткрыть настройки панели задач Windows, чтобы поменять это вручную?", "Taskbar Plus",
                    MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                OpenWindowsSettings();
            Build();
            return;
        }
        Say("Применено: " + opt.Title + " — " + opt.Labels[index]);
        Build();
        if (opt.NeedsRestart) _ = ConfirmRestartAsync("Чтобы «" + opt.Title + "» изменилось, нужно перезапустить панель задач.");
    }

    // Перезапуск Проводника и обращения к нему идут в фоне, чтобы окно плагина не зависало вместе с панелью.
    async Task<bool> ConfirmRestartAsync(string? why)
    {
        var text = (why == null ? "" : why + "\n\n") + "Панель задач и окна Проводника закроются на пару секунд и откроются снова. Перезапустить сейчас?";
        if (MessageBox.Show(this, text, "Taskbar Plus", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return false;
        Say("Перезапускаю панель задач…");
        engine.Xaml.NoteExplorerRestart();
        var err = await Task.Run(RegistrySettings.RestartExplorer);
        await Task.Delay(2500); // ждём, пока Проводник поднимется
        Say(err ?? "Панель задач перезапущена");
        RefreshAutoHide();
        return err == null;
    }

    async void SetAutoHideAsync(bool on)
    {
        Say("Применяю…");
        var err = await Task.Run(() => RegistrySettings.SetAutoHide(on));
        if (err == null) autoHide = on;
        Say(err ?? (on ? "Автоскрытие включено" : "Автоскрытие выключено"));
        Build();
    }

    async Task ResetAllAsync()
    {
        Say("Возвращаю…");
        settings.Shape = AppearanceSettings.ShapeNone;
        settings.UseXaml = false;
        settings.BgMode = AppearanceSettings.BgDefault;
        settings.HideStroke = false;
        var (restored, error) = await Task.Run(RegistrySettings.Reset);
        engine.Update(settings);
        settings.Save();
        Say(error != null ? "Вернулось не всё: " + error : restored == 0 ? "Параметры Windows вы не меняли; форма снята" : $"Возвращено параметров: {restored}; форма снята");
        RefreshAutoHide();
        Build();
    }

    static void OpenWindowsSettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:taskbar") { UseShellExecute = true }); }
        catch { /* нет такого раздела — ничего страшного */ }
    }

    void Say(string text) => statusLine.Text = text;

    int ShapeIndex() => settings.Shape switch
    {
        AppearanceSettings.ShapePill => 1, AppearanceSettings.ShapeTab => 2, AppearanceSettings.ShapeIslands => 3, _ => 0,
    };

    // ---------- кирпичики интерфейса ----------
    Border Card(string title, IEnumerable<UIElement> rows)
    {
        var col = new StackPanel();
        col.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        foreach (var r in rows) col.Children.Add(r);
        return new Border
        {
            Background = Res("Panel"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(22), Margin = new Thickness(0, 0, 0, 14), Child = col,
        };
    }

    Grid Row(string title, string hint, UIElement control)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = hint, Foreground = Res("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap });
        row.Children.Add(text);
        if (control is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    TextBlock Note(string text) => new()
    {
        Text = text, Foreground = Res("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
    };

    Button Btn(string text, string? style, Action click, bool first = false)
    {
        var b = new Button { Content = text, Margin = new Thickness(first ? 0 : 8, 0, 0, 0), Padding = new Thickness(12, 6, 12, 6) };
        if (style != null) b.Style = (Style)FindResource(style);
        b.Click += (_, _) => click();
        return b;
    }

    // Переключатель из нескольких кнопок: выбранная подсвечена.
    StackPanel Seg(string[] labels, int selected, Action<int> choose)
    {
        var seg = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < labels.Length; i++)
        {
            var idx = i;
            var b = new Button { Content = labels[i], Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0) };
            if (i == selected) b.Style = (Style)FindResource("Primary");
            b.Click += (_, _) => { if (idx != selected) choose(idx); };
            seg.Children.Add(b);
        }
        return seg;
    }

    Grid SliderRow(string title, string hint, int min, int max, int value, string unit, Action<int> changed)
    {
        var valueText = new TextBlock { Text = value + (unit.Length > 0 ? " " + unit : ""), Width = 64, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = Res("Accent") };
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, Width = 220, SmallChange = 1, LargeChange = 5, IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, e) =>
        {
            var v = (int)Math.Round(e.NewValue);
            valueText.Text = v + (unit.Length > 0 ? " " + unit : "");
            changed(v);
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(slider);
        panel.Children.Add(valueText);
        return Row(title, hint, panel);
    }
}
