using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TaskbarPlusApp;

// Окно Taskbar Plus (2.0): один экран. Сверху живой предпросмотр панели, ниже — форма, подстройка, где что стоит и кнопка «Применить».
// Пока вы не нажали «Применить», с панелью ничего не происходит — меняется только предпросмотр. Редкие настройки спрятаны в «Ещё настройки».
sealed class MainWindow : Window
{
    readonly Engines engine;
    readonly AppearanceSettings settings;   // применённое
    AppearanceSettings draft;                // выбранное, ещё не применённое
    readonly Preview preview = new();
    readonly StackPanel root = new();
    readonly TextBlock statusLine = new();
    bool moreOpen;

    static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    // Состояние автоскрытия спрашивает у Проводника, а он может отвечать с задержкой (особенно сразу после перезапуска) —
    // поэтому читаем его в фоне, а окно рисует то, что уже известно.
    bool autoHide;

    void RefreshAutoHide()
    {
        Task.Run(Native.GetAutoHide).ContinueWith(t =>
        {
            if (t.IsFaulted) return;
            Dispatcher.BeginInvoke(new Action(() => { if (autoHide != t.Result) { autoHide = t.Result; if (moreOpen) Build(); } }));
        });
    }

    public MainWindow(Engines engine, AppearanceSettings settings)
    {
        this.engine = engine;
        this.settings = settings;
        draft = settings.Clone();
        Title = "Taskbar Plus";
        Width = Math.Min(960, SystemParameters.WorkArea.Width - 40); Height = Math.Min(900, SystemParameters.WorkArea.Height - 40); MinWidth = 600; MinHeight = 480;
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
        engine.StatusChanged += () => Dispatcher.BeginInvoke(new Action(() => { MaybeAskRestart(); }));
        Build();
        RefreshAutoHide();
        Dispatcher.BeginInvoke(new Action(MaybeAskRestart));
    }

    // Служебный режим для проверки внешнего вида: рисует окно целиком (без прокрутки) в PNG. Запускается из CI: --screenshot=<папка>.
    public void RenderTo(string path, params string[] open)
    {
        moreOpen = open.Length > 0;
        Build();
        var scroll = (ScrollViewer)Content;
        var content = (FrameworkElement)scroll.Content;
        scroll.Content = null;
        // вне окна свойства текста не наследуются — задаём вручную, чтобы снимок выглядел как настоящее окно
        System.Windows.Documents.TextElement.SetForeground(content, Foreground);
        System.Windows.Documents.TextElement.SetFontFamily(content, FontFamily);
        System.Windows.Documents.TextElement.SetFontSize(content, FontSize);
        content.Measure(new Size(900, double.PositiveInfinity));
        var w = 900;
        var h = (int)Math.Ceiling(content.DesiredSize.Height);
        content.Arrange(new Rect(0, 0, w, h));
        content.UpdateLayout();
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen()) dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));
        rtb.Render(bg);
        rtb.Render(content);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        using var fs = System.IO.File.Create(path);
        enc.Save(fs);
        scroll.Content = content;
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
                                "Чтобы совсем выйти и вернуть панель как была, нажмите «Выйти» в «Ещё настройки».",
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

    // ---------- сборка окна ----------
    static readonly string[] PresetNames = { "Обычная", "Плавающая", "Острова", "Вкладка", "Вкладки-острова" };
    static readonly string[] Zones = { AppearanceSettings.ZoneDefault, AppearanceSettings.ZoneLeft, AppearanceSettings.ZoneCenter, AppearanceSettings.ZoneRight };
    static readonly string[] ZoneNames = { "Как в Windows", "Слева", "По центру", "Справа" };

    void Build()
    {
        root.Children.Clear();
        root.Children.Add(new TextBlock { Text = "TASKBAR PLUS", FontFamily = (FontFamily)Application.Current.FindResource("Mono"), FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Res("Accent") });
        root.Children.Add(Note("Выберите форму панели — внизу сразу видно, как будет. Понравилось — нажмите «Применить». Пока не нажали, с панелью ничего не происходит."));

        // предпросмотр (один и тот же холст переезжает в новую рамку при каждой перерисовке)
        if (preview.Parent is Border old) old.Child = null;
        var holder = new Border { Width = Preview.W, Height = Preview.H, BorderBrush = Res("Line"), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, Child = preview, Margin = new Thickness(0, 0, 0, 10) };
        root.Children.Add(holder);
        preview.Update(draft);

        // форма
        root.Children.Add(Card("Форма панели", new UIElement[]
        {
            Seg(PresetNames, PresetIndex(draft), i => { ApplyPreset(draft, i); Build(); }),
            Note("Обычная — как в Windows. Плавающая — отдельная скруглённая панель. Острова — значки на отдельных плитках. Вкладка — скруглён только верх, низ прижат к краю."),
        }));

        // подстройка (только если форма не «обычная»)
        if (draft.Shape != AppearanceSettings.ShapeNone)
        {
            var rows = new List<UIElement>
            {
                SliderRow("Скругление", "Насколько круглые углы", 0, 30, draft.Radius, "px", v => { draft.Radius = v; preview.Update(draft); }),
            };
            if (draft.Shape is AppearanceSettings.ShapePill or AppearanceSettings.ShapeTab)
            {
                rows.Add(SliderRow("Отступ по бокам", "Чем больше, тем уже панель — получается небольшая панель по центру", 0, 600, draft.SideMargin, "px", v => { draft.SideMargin = v; preview.Update(draft); }));
                rows.Add(Row("Часы и трей", "Оставить отдельными островами по краям; иначе при большом отступе они пропадут вместе с часами",
                    Seg(new[] { "Оставить", "Убрать" }, draft.KeepEdges ? 0 : 1, i => { draft.KeepEdges = i == 0; Build(); })));
            }
            if (draft.Shape == AppearanceSettings.ShapeIslands)
            {
                rows.Add(Row("Что окружать", "Каждый значок — свой остров, или один остров на группу",
                    Seg(new[] { "Каждый значок", "Группу" }, draft.IslandMode == "group" ? 1 : 0, i => { draft.IslandMode = i == 1 ? "group" : "each"; Build(); })));
                rows.Add(draft.IslandMode == "group"
                    ? SliderRow("Поля острова", "Расстояние от значков до края острова", 0, 40, draft.IslandPadding, "px", v => { draft.IslandPadding = v; preview.Update(draft); })
                    : SliderRow("Зазор между островами", "Чем больше, тем дальше острова друг от друга", 0, 40, draft.IslandGap, "px", v => { draft.IslandGap = v; preview.Update(draft); }));
            }
            var floating = draft.Shape == AppearanceSettings.ShapePill || (draft.Shape == AppearanceSettings.ShapeIslands && draft.IslandStyle == "float");
            if (floating) rows.Add(SliderRow("Зазор снизу", "Расстояние от панели до нижнего края экрана", 0, 12, draft.BottomGap, "px", v => { draft.BottomGap = v; preview.Update(draft); }));
            root.Children.Add(Card("Подстройка", rows));
        }

        // где что стоит
        root.Children.Add(Card("Где что стоит", new UIElement[]
        {
            Row("Значки программ", "«Пуск», поиск и значки запущенных программ", Seg(ZoneNames, Math.Max(0, Array.IndexOf(Zones, draft.ZoneApps)), i => { draft.ZoneApps = Zones[i]; Build(); })),
            Row("Часы, сеть и трей", "Всегда двигаются одним целым", Seg(ZoneNames, Math.Max(0, Array.IndexOf(Zones, draft.ZoneSystem)), i => { draft.ZoneSystem = Zones[i]; Build(); })),
            Note("Нажатия идут за значками. Окна Windows (календарь, быстрые настройки, «Пуск») могут открываться на прежнем месте. Цвет панели — в плагине Color Plus."),
        }));

        // действия
        var reason = XamlEngine.UnavailableReason;
        if (reason != null) root.Children.Add(Note("Плавные формы и острова работают в Windows 11: " + reason + " Обычный способ формы тоже доступен, но проще."));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        buttons.Children.Add(Btn("Применить", "Primary", () => { _ = ApplyDraftAsync(); }, first: true));
        buttons.Children.Add(Btn("Вернуть как было", "Danger", () => { _ = ResetAllAsync(); }));
        root.Children.Add(buttons);
        statusLine.Foreground = Res("Accent2"); statusLine.FontSize = 13; statusLine.TextWrapping = TextWrapping.Wrap; statusLine.Margin = new Thickness(0, 12, 0, 8);
        if (statusLine.Text.Length == 0) statusLine.Text = settings.Active ? engine.Status : "Ничего не применено — панель как в Windows.";
        if (statusLine.Parent is Panel oldP) oldP.Children.Remove(statusLine);
        root.Children.Add(statusLine);

        // редкое
        var more = Btn(moreOpen ? "Ещё настройки ▾" : "Ещё настройки ▸", null, () => { moreOpen = !moreOpen; Build(); }, first: true);
        more.HorizontalAlignment = HorizontalAlignment.Left; more.Margin = new Thickness(0, 4, 0, 10);
        root.Children.Add(more);
        if (moreOpen) root.Children.Add(MoreCard());
    }

    // ---------- формы ----------
    static int PresetIndex(AppearanceSettings s) => s.Shape switch
    {
        AppearanceSettings.ShapePill => 1,
        AppearanceSettings.ShapeIslands => s.IslandStyle == "tab" ? 4 : 2,
        AppearanceSettings.ShapeTab => 3,
        _ => 0,
    };

    // Готовые варианты — те же формы, что на картинках владельца.
    static void ApplyPreset(AppearanceSettings s, int i)
    {
        switch (i)
        {
            case 0: s.Shape = AppearanceSettings.ShapeNone; break;
            case 1: s.Shape = AppearanceSettings.ShapePill; s.Radius = 18; s.SideMargin = 24; s.BottomGap = 8; break;
            case 2: s.Shape = AppearanceSettings.ShapeIslands; s.IslandMode = "each"; s.IslandStyle = "float"; s.Radius = 14; s.IslandGap = 8; s.BottomGap = 6; break;
            case 3: s.Shape = AppearanceSettings.ShapeTab; s.Radius = 16; s.SideMargin = 24; break;
            default: s.Shape = AppearanceSettings.ShapeIslands; s.IslandMode = "each"; s.IslandStyle = "tab"; s.Radius = 14; s.IslandGap = 8; break;
        }
    }

    // «Применить»: выбранное из предпросмотра становится настоящим
    async Task ApplyDraftAsync()
    {
        var wantEngine = XamlEngine.UnavailableReason == null && (draft.Shape != AppearanceSettings.ShapeNone || draft.LayoutActive || settings.UseXaml);
        if (wantEngine && !settings.XamlConsentShown)
        {
            var ok = MessageBox.Show(this,
                "Для плавных форм и островов Taskbar Plus подключается к панели задач внутри Проводника Windows (так работают и другие программы для оформления панели).\n\n" +
                "Если Проводник закроется сразу после подключения два раза подряд, движок сам отключится.\n" +
                "Вернуть всё как было можно кнопкой «Вернуть как было».\n\nПродолжить?",
                "Taskbar Plus", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            if (!ok) { Say("Не применено: без движка плавные формы недоступны."); return; }
            settings.XamlConsentShown = true;
        }
        settings.Shape = draft.Shape; settings.Radius = draft.Radius; settings.SideMargin = draft.SideMargin; settings.BottomGap = draft.BottomGap;
        settings.IslandStyle = draft.IslandStyle; settings.IslandMode = draft.IslandMode; settings.IslandPadding = draft.IslandPadding; settings.IslandGap = draft.IslandGap;
        settings.KeepEdges = draft.KeepEdges;
        settings.ZoneApps = draft.ZoneApps; settings.ZoneSystem = draft.ZoneSystem;
        settings.UseXaml = wantEngine;
        settings.Islands.Clear();   // цвет и вид островов задаёт Color Plus; свои стили отдельных островов в простом окне не настраиваются
        engine.Update(settings);
        settings.Save();
        Say("Применено. " + (wantEngine ? "Движок подключается к панели — это занимает несколько секунд." : ""));
        await Task.Delay(1500);
        if (statusLine.Text.StartsWith("Применено")) Say(engine.Status.Length > 0 ? "Применено. " + engine.Status : "Применено.");
    }

    UIElement MoreCard()
    {
        var rows = new List<UIElement>
        {
            Note("Эти настройки применяются сразу, без кнопки «Применить»."),
            Row("Автоскрытие панели", "Панель прячется, пока курсор не у края экрана", Seg(new[] { "Выкл", "Вкл" }, autoHide ? 1 : 0, i => SetAutoHideAsync(i == 1))),
        };
        rows.Add(Row("Тонкая линия по краю панели", "Светлая линия сверху панели. По умолчанию она убрана.",
            Seg(new[] { "Убрана", "Показывать" }, settings.ShowStroke ? 1 : 0, i =>
            {
                settings.ShowStroke = i == 1;
                engine.Update(settings);
                settings.Save();
                Say(i == 1 ? "Линия по краю панели показывается." : "Линия по краю панели убрана.");
                Build();
            })));
        foreach (var o in RegistrySettings.Options.Where(RegistrySettings.Available))
        {
            var opt = o;
            rows.Add(Row(opt.Title, opt.Hint, Seg(opt.Labels, Array.IndexOf(opt.Values, RegistrySettings.Current(opt)), i => ApplyOption(opt, i))));
        }
        var sys = Native.SystemAnimationsOn;
        rows.Add(Row("Анимации Windows", sys ? "Включены" : "Выключены — из-за этого панель при автоскрытии появляется без движения.",
            sys ? new TextBlock { Text = "Включены", Foreground = Res("Green"), VerticalAlignment = VerticalAlignment.Center }
                : Btn("Включить анимации Windows", "Primary", () =>
                {
                    Say(Native.EnableSystemAnimations() ? "Анимации Windows включены" : "Не получилось включить; откройте Параметры → Специальные возможности → Визуальные эффекты");
                    Build();
                })));
        rows.Add(Row("Запускать вместе с Windows", "Форма будет применяться после входа в систему, окно открываться не будет",
            Seg(new[] { "Нет", "Да" }, RegistrySettings.Autostart ? 1 : 0, i =>
            {
                try { RegistrySettings.SetAutostart(i == 1); Say(i == 1 ? "Автозапуск включён" : "Автозапуск выключен"); }
                catch (Exception ex) { Say("Не получилось: " + ex.Message); }
                Build();
            })));
        var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        void Add(Button b) { b.Margin = new Thickness(0, 0, 8, 8); buttons.Children.Add(b); }
        Add(Btn("Перезапустить панель задач", null, () => { _ = ConfirmRestartAsync(null); }, first: true));
        Add(Btn("Отправить отчёт разработчику", null, () => { _ = UploadReportAsync(); }, first: true));
        Add(Btn("Сохранить отчёт на рабочий стол", null, () => { _ = SaveReportAsync(); }, first: true));
        if (engine.Xaml.Blocked) Add(Btn("Попробовать снова", "Primary", () => { engine.Xaml.ClearBlock(); engine.Update(settings); Build(); }, first: true));
        Add(Btn("Выйти", null, () => { App.Quitting = true; Close(); }, first: true));
        rows.Add(buttons);
        return Card("Ещё настройки", rows);
    }

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
        engine.Update(settings);
        settings.Save();
        Build();
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
        var (restored, error) = await Task.Run(RegistrySettings.Reset);
        engine.Update(settings);
        settings.Save();
        draft = settings.Clone();
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
