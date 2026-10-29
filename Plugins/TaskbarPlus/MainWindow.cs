using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TaskbarPlusApp;

// Окно настроек Taskbar Plus. Весь интерфейс собирается кодом: при смене формы или режима перерисовывается целиком.
sealed class MainWindow : Window
{
    readonly TaskbarEngine engine;
    readonly AppearanceSettings settings;
    readonly StackPanel root = new();
    readonly TextBlock statusLine = new();
    readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    public MainWindow(TaskbarEngine engine, AppearanceSettings settings)
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
        engine.StatusChanged += () => Dispatcher.BeginInvoke(new Action(() => statusLine.Text = engine.Status));
        Build();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Форма и размытие держатся, пока работает плагин: закрытие окна просто прячет его в фон.
        if (!App.Quitting && settings.Active)
        {
            e.Cancel = true;
            Hide();
            if (!settings.BackgroundHintShown)
            {
                settings.BackgroundHintShown = true;
                settings.Save();
                MessageBox.Show("Taskbar Plus продолжает работать в фоне — иначе форма и размытие панели пропали бы.\n\n" +
                                "Чтобы открыть окно снова, запустите Taskbar Plus из лаунчера или из меню «Пуск». " +
                                "Чтобы совсем выйти и вернуть панель как была, нажмите «Выйти» внизу окна.",
                                "Taskbar Plus", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return;
        }
        base.OnClosing(e);
        Application.Current.Shutdown();
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
            Text = "Плагин Ghost Shell Launcher. Панель задач Windows по-своему: форма, острова, прозрачность, значки, автоскрытие. " +
                   "Всё, что вы поменяли, можно вернуть кнопкой «Вернуть как было».",
            Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6),
        });
        statusLine.Text = engine.Status;
        statusLine.Foreground = Res("Accent2");
        statusLine.FontSize = 12;
        statusLine.TextWrapping = TextWrapping.Wrap;
        statusLine.Margin = new Thickness(0, 0, 0, 14);
        root.Children.Add(statusLine);

        root.Children.Add(AppearanceCard());
        root.Children.Add(IconsCard());
        root.Children.Add(AutoHideCard());
        root.Children.Add(SystemCard());
    }

    // ---------- карточки ----------
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
            rows.Add(SliderRow("Скругление", "Насколько круглые углы", 0, 40, settings.Radius, "px", v => { settings.Radius = v; Changed(); }));
            if (settings.Shape is AppearanceSettings.ShapePill or AppearanceSettings.ShapeTab)
                rows.Add(SliderRow("Отступ по бокам", "Чем больше, тем уже панель: так получается небольшая панель по центру", 0, 600, settings.SideMargin, "px", v => { settings.SideMargin = v; Changed(); }));
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
                rows.Add(SliderRow("Зазор снизу", "Расстояние от панели до нижнего края экрана", 0, 60, settings.BottomGap, "px", v => { settings.BottomGap = v; Changed(); }));
            rows.Add(Note("Формы работают для панели, которая стоит внизу экрана. Пока плагин запущен, форма держится сама; при выходе из плагина панель возвращается к обычной."));
        }

        rows.Add(Row("Прозрачность и размытие", "Эксперимент: на Windows 10 работает хорошо; на Windows 11 Проводник может закрывать эффект своим фоном.",
            Seg(new[] { "Как есть", "Прозрачная", "Размытие", "Акрил" }, BackdropIndex(), i =>
            {
                settings.Backdrop = new[] { AppearanceSettings.BackdropNone, AppearanceSettings.BackdropClear, AppearanceSettings.BackdropBlur, AppearanceSettings.BackdropAcrylic }[i];
                Changed(); Build();
            })));
        if (settings.Backdrop != AppearanceSettings.BackdropNone)
        {
            rows.Add(SliderRow("Плотность цвета", "0 — совсем прозрачно, 255 — сплошной цвет", 0, 255, settings.TintOpacity, "", v => { settings.TintOpacity = v; Changed(); }));
            var colors = new[] { ("Чёрный", "#000000"), ("Тёмно-фиолетовый", "#1A0B2E"), ("Синий", "#0B1B3A"), ("Белый", "#FFFFFF") };
            var sel = Array.FindIndex(colors, c => string.Equals(c.Item2, settings.TintColor, StringComparison.OrdinalIgnoreCase));
            rows.Add(Row("Цвет", "Оттенок под размытием", Seg(colors.Select(c => c.Item1).ToArray(), sel, i => { settings.TintColor = colors[i].Item2; Changed(); Build(); })));
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
        Add("Обычная", s => { s.Shape = AppearanceSettings.ShapeNone; s.Backdrop = AppearanceSettings.BackdropNone; });
        Add("Плавающая", s => { s.Shape = AppearanceSettings.ShapePill; s.Radius = 18; s.SideMargin = 24; s.BottomGap = 8; });
        Add("Острова", s => { s.Shape = AppearanceSettings.ShapeIslands; s.IslandMode = "each"; s.IslandStyle = "float"; s.Radius = 14; s.IslandGap = 8; s.BottomGap = 6; });
        Add("Вкладка", s => { s.Shape = AppearanceSettings.ShapeTab; s.Radius = 16; s.SideMargin = 24; });
        Add("Вкладки-острова", s => { s.Shape = AppearanceSettings.ShapeIslands; s.IslandMode = "each"; s.IslandStyle = "tab"; s.Radius = 14; s.IslandGap = 8; });
        Add("Только значки", s => { s.Shape = AppearanceSettings.ShapeNone; s.Backdrop = AppearanceSettings.BackdropClear; s.TintOpacity = 0; });
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
            Row("Автоскрытие панели", "Панель прячется, пока курсор не у края экрана", Seg(new[] { "Выкл", "Вкл" }, Native.GetAutoHide() ? 1 : 0, i =>
            {
                var err = RegistrySettings.SetAutoHide(i == 1);
                Say(err ?? (i == 1 ? "Автоскрытие включено" : "Автоскрытие выключено"));
                Build();
            })),
        };
        var anim = RegistrySettings.Options.First(o => o.Id == "animations");
        rows.Add(Row(anim.Title, anim.Hint, Seg(anim.Labels, Array.IndexOf(anim.Values, RegistrySettings.Current(anim)), i => ApplyOption(anim, i))));
        var sys = Native.SystemAnimationsOn;
        var sysRow = Row("Анимации Windows", sys ? "Включены: панель при автоскрытии выезжает и уезжает плавно."
                                                 : "Выключены — из-за этого панель появляется и пропадает мгновенно, без движения.",
            sys ? new TextBlock { Text = "Включены", Foreground = Res("Green"), VerticalAlignment = VerticalAlignment.Center }
                : Btn("Включить анимации Windows", "Primary", () =>
                {
                    Say(Native.EnableSystemAnimations() ? "Анимации Windows включены" : "Не получилось включить; откройте Параметры → Специальные возможности → Визуальные эффекты");
                    Build();
                }));
        rows.Add(sysRow);
        rows.Add(Note("Собственная анимация выезда снизу вверх пока не сделана: сейчас движение — штатное, Windows."));
        return Card("Автоскрытие и анимация", rows);
    }

    UIElement SystemCard()
    {
        var rows = new List<UIElement>
        {
            Row("Запускать вместе с Windows", "Форма и размытие будут применяться после входа в систему, окно открываться не будет",
                Seg(new[] { "Нет", "Да" }, RegistrySettings.Autostart ? 1 : 0, i =>
                {
                    try { RegistrySettings.SetAutostart(i == 1); Say(i == 1 ? "Автозапуск включён" : "Автозапуск выключен"); }
                    catch (Exception ex) { Say("Не получилось: " + ex.Message); }
                    Build();
                })),
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(Btn("Перезапустить панель задач", null, () => { ConfirmRestart(null); }, first: true));
        buttons.Children.Add(Btn("Вернуть как было", "Danger", ResetAll));
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
        if (opt.NeedsRestart) ConfirmRestart("Чтобы «" + opt.Title + "» изменилось, нужно перезапустить панель задач.");
    }

    bool ConfirmRestart(string? why)
    {
        var text = (why == null ? "" : why + "\n\n") + "Панель задач и окна Проводника закроются на пару секунд и откроются снова. Перезапустить сейчас?";
        if (MessageBox.Show(this, text, "Taskbar Plus", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return false;
        var err = RegistrySettings.RestartExplorer();
        Say(err ?? "Панель задач перезапущена");
        return err == null;
    }

    void ResetAll()
    {
        var (restored, error) = RegistrySettings.Reset();
        settings.Shape = AppearanceSettings.ShapeNone;
        settings.Backdrop = AppearanceSettings.BackdropNone;
        engine.Update(settings);
        settings.Save();
        Say(error != null ? "Вернулось не всё: " + error : restored == 0 ? "Параметры Windows вы не меняли; форма и размытие сняты" : $"Возвращено параметров: {restored}; форма и размытие сняты");
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

    int BackdropIndex() => settings.Backdrop switch
    {
        AppearanceSettings.BackdropClear => 1, AppearanceSettings.BackdropBlur => 2, AppearanceSettings.BackdropAcrylic => 3, _ => 0,
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
