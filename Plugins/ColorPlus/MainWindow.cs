using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ColorPlusApp;

// Окно настроек Color Plus. Весь интерфейс собирается кодом: при смене режима перерисовывается целиком.
sealed class MainWindow : Window
{
    readonly Controller ctl;
    readonly ColorSettings settings;
    readonly StackPanel root = new();
    readonly TextBlock statusLine = new();
    readonly DispatcherTimer applyTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    readonly HashSet<string> openApps = new();

    static readonly string[] SwatchColors = { "#000000", "#101018", "#202020", "#F2F2F2", "#00F0FF", "#FF2A9D", "#6C4DFF", "#1E6BFF", "#20C27A" };
    static readonly string[] Backdrops = { ColorSettings.BdNone, ColorSettings.BdBlur, ColorSettings.BdAcrylic, ColorSettings.BdMica, ColorSettings.BdTabbed };
    static readonly string[] BackdropLabels = { "Нет", "Размытие", "Акрил", "Слюда", "Вкладки" };
    static readonly string[] WinModes = { ColorSettings.WmBackground, ColorSettings.WmAlpha, ColorSettings.WmNone };
    static readonly string[] TaskbarModes = { ColorSettings.TbDefault, ColorSettings.TbClear, ColorSettings.TbBlur, ColorSettings.TbSolid };

    static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    public MainWindow(Controller ctl, ColorSettings settings)
    {
        this.ctl = ctl;
        this.settings = settings;
        Title = "Color Plus";
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
        applyTimer.Tick += (_, _) => { applyTimer.Stop(); settings.Save(); ctl.Apply(settings); ShowStatus(); };
        ctl.Effects.Changed += () => Dispatcher.BeginInvoke(new Action(ShowStatus));
        Build();
    }

    // Служебный режим для проверки внешнего вида: рисует окно целиком (без прокрутки) в PNG. Запускается из CI: --screenshot=<папка>.
    public void RenderTo(string path)
    {
        Build();
        var scroll = (ScrollViewer)Content;
        var content = (FrameworkElement)scroll.Content;
        scroll.Content = null;
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
        // Эффекты держатся, пока работает плагин: закрытие окна просто прячет его в фон.
        if (!App.Quitting && settings.Active)
        {
            e.Cancel = true;
            Hide();
            if (!settings.BackgroundHintShown)
            {
                settings.BackgroundHintShown = true;
                settings.Save();
                MessageBox.Show("Color Plus продолжает работать в фоне — иначе эффекты пропали бы.\n\n" +
                                "Чтобы открыть окно снова, запустите Color Plus из лаунчера или из меню «Пуск». " +
                                "Чтобы совсем выйти и вернуть всё как было, нажмите «Выйти» внизу окна.",
                                "Color Plus", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return;
        }
        base.OnClosing(e);
        Application.Current.Shutdown();
    }

    // Применение откладывается на 0,3 с: пока человек двигает ползунок или вводит цвет, Windows не засыпается изменениями.
    void Changed()
    {
        applyTimer.Stop();
        applyTimer.Start();
    }

    void ShowStatus()
    {
        var text = ctl.Effects.Status;
        if (ctl.SystemError != null) text += (text.Length > 0 ? " · " : "") + "Не вышло: " + ctl.SystemError;
        statusLine.Text = text;
    }

    void Say(string text) => statusLine.Text = text;

    // ---------- сборка окна ----------
    void Build()
    {
        root.Children.Clear();
        root.Children.Add(new TextBlock { Text = "COLOR PLUS", FontFamily = (FontFamily)Application.Current.FindResource("Mono"), FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Res("Accent") });
        root.Children.Add(new TextBlock
        {
            Text = "Плагин Ghost Shell Launcher. Прозрачность, размытие и цвет для окон всех программ, панели задач и выделения. " +
                   "Всё, что вы поменяли, возвращается кнопкой «Вернуть всё как было» и при выходе из плагина.",
            Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6),
        });
        statusLine.Foreground = Res("Accent2");
        statusLine.FontSize = 12;
        statusLine.TextWrapping = TextWrapping.Wrap;
        statusLine.Margin = new Thickness(0, 0, 0, 14);
        ShowStatus();
        root.Children.Add(statusLine);

        root.Children.Add(WindowsCard());
        if (settings.WindowsOn) root.Children.Add(AppsCard());
        root.Children.Add(TaskbarCard());
        root.Children.Add(SelectionCard());
        root.Children.Add(SystemCard());
    }

    // ---------- карточки ----------
    UIElement WindowsCard()
    {
        var rows = new List<UIElement>
        {
            Row("Эффекты для окон", "Включить прозрачность, размытие и цвет у окон всех открытых программ. Новые окна подхватываются сами.",
                Seg(new[] { "Выкл", "Вкл" }, settings.WindowsOn ? 1 : 0, i => { settings.WindowsOn = i == 1; Changed(); Build(); })),
        };
        if (!settings.WindowsOn) return Card("Окна всех программ", rows);

        rows.Add(Row("Что делать прозрачным",
            "«Только фон» — становится прозрачным фон за текстом, картинками и значками, а заголовок, текст, картинки и видео остаются плотными. «Окно целиком» — прозрачно всё, включая видео и заголовок. «Ничего» — окна не просвечивают, работают только цвет заголовка и подложка.",
            Seg(new[] { "Только фон", "Окно целиком", "Ничего" }, Math.Max(0, Array.IndexOf(WinModes, settings.WindowMode)), i => { settings.WindowMode = WinModes[i]; Changed(); Build(); })));
        if (settings.WindowMode == ColorSettings.WmBackground)
        {
            rows.Add(Row("Если «только фон» невозможен", "Chrome, Edge, Discord, VS Code и магазинные приложения рисуют окно видеокартой — Windows не даёт сделать у них прозрачным один фон. Что с ними делать:",
                Seg(new[] { "Не трогать окно", "Сделать целиком прозрачным" }, settings.FallbackAlpha ? 1 : 0, i => { settings.FallbackAlpha = i == 1; Changed(); Build(); })));
        }
        if (settings.WindowMode == ColorSettings.WmAlpha || (settings.WindowMode == ColorSettings.WmBackground && settings.FallbackAlpha))
        {
            rows.Add(SliderRow("Прозрачность окна целиком", "100% — окна как обычно. Меньше — сквозь окно виден рабочий стол и то, что за ним.", 20, 100, settings.WindowOpacity, "%",
                v => { settings.WindowOpacity = v; Changed(); }));
        }

        var bdIdx = Math.Max(0, Array.IndexOf(Backdrops, settings.Backdrop));
        rows.Add(Row("Что за окном", "Размытие и акрил — матовое стекло; слюда (Mica) и вкладки — мягкий фон Windows 11 за заголовком окна.",
            Seg(BackdropLabels, bdIdx, i => { settings.Backdrop = Backdrops[i]; Changed(); Build(); })));
        if (settings.Backdrop is ColorSettings.BdBlur or ColorSettings.BdAcrylic)
        {
            rows.Add(Row("Оттенок стекла", "Цвет, которым подкрашено размытие", ColorPicker(settings.Tint, hex => { settings.Tint = hex; Changed(); })));
            rows.Add(SliderRow("Сила оттенка", "Чем меньше, тем прозрачнее стекло", 0, 100, settings.TintOpacity, "%", v => { settings.TintOpacity = v; Changed(); }));
        }
        if (settings.Backdrop != ColorSettings.BdNone)
            rows.Add(Row("Растянуть на всё окно", "Показать размытие не только у рамки, но и под содержимым. Некоторые программы после этого выглядят криво — тогда выключите.",
                Seg(new[] { "Нет", "Да" }, settings.ExtendFrame ? 1 : 0, i => { settings.ExtendFrame = i == 1; Changed(); })));

        rows.Add(Row("Тёмные заголовки", "Тёмная строка заголовка у окон (Windows 11)",
            Seg(new[] { "Нет", "Да" }, settings.DarkTitle ? 1 : 0, i => { settings.DarkTitle = i == 1; Changed(); })));
        rows.Add(Row("Цвет заголовков", "Свой цвет строки заголовка и рамки окон (Windows 11)",
            Seg(new[] { "Как в Windows", "Свой" }, settings.TitleColor == null ? 0 : 1, i =>
            {
                settings.TitleColor = i == 1 ? (settings.TitleColor ?? "#101018") : null;
                settings.TitleTextColor = i == 1 ? (settings.TitleTextColor ?? "#F2F2F2") : null;
                Changed(); Build();
            })));
        if (settings.TitleColor != null)
        {
            rows.Add(Row("Цвет заголовка", "Фон строки заголовка", ColorPicker(settings.TitleColor, hex => { settings.TitleColor = hex; Changed(); })));
            rows.Add(Row("Цвет текста заголовка", "Цвет названия окна", ColorPicker(settings.TitleTextColor ?? "#F2F2F2", hex => { settings.TitleTextColor = hex; Changed(); })));
        }

        rows.Add(Note("Честно о возможностях. «Только фон» работает цветом: прозрачным становится ровно тот цвет, которым залит фон окна (его плагин находит сам). " +
                      "Поэтому: (1) если шапка окна залита тем же цветом, она тоже станет прозрачной; (2) по прозрачным местам мышь «проваливается» — клик попадёт в то, что лежит за окном; " +
                      "(3) у краёв букв остаётся тонкая кайма; (4) окна, которые рисуются видеокартой (Chrome, Edge, Telegram, Discord, VS Code, магазинные приложения), так не умеют — снаружи Windows им фон не отдаёт. " +
                      "Размытие и «слюда» видны там, где окно само прозрачно, у остальных — у рамки и заголовка. Полноэкранные окна (игры, видео), системные окна и окна самого плагина не трогаются."));
        return Card("Окна всех программ", rows);
    }

    UIElement AppsCard()
    {
        var rows = new List<UIElement>
        {
            Note("Для каждой программы можно оставить общие настройки, не трогать её окна вовсе или задать свою прозрачность и размытие. " +
                 "В списке программы, у которых сейчас есть окна."),
        };
        var names = ctl.Effects.RunningApps.Concat(settings.Apps.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count == 0) rows.Add(Note("Пока нет окон. Откройте программы и нажмите «Обновить список»."));
        foreach (var name in names) rows.Add(AppRow(name));
        rows.Add(Btn("Обновить список", null, () => Build(), first: true));
        return Card("Программы", rows);
    }

    UIElement AppRow(string name)
    {
        settings.Apps.TryGetValue(name, out var rule);
        var mode = rule?.Mode ?? "inherit";
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        ctl.Effects.UnsupportedApps.TryGetValue(name, out var noKey);
        panel.Children.Add(Row(name, (mode == "skip" ? "Не трогаем" : mode == "custom" ? "Свои настройки" : "Как у всех") + (noKey != null ? " · «только фон» невозможен: " + noKey : ""),
            Seg(new[] { "Как у всех", "Не трогать", "Свои" }, mode == "skip" ? 1 : mode == "custom" ? 2 : 0, i =>
            {
                if (i == 0) settings.Apps.Remove(name);
                else settings.Apps[name] = new AppRule { Mode = i == 1 ? "skip" : "custom", WinMode = rule?.WinMode, Opacity = rule?.Opacity ?? settings.WindowOpacity, Backdrop = rule?.Backdrop };
                Changed(); Build();
            })));
        if (mode == "custom" && rule != null)
        {
            var body = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
            var wmIdx = rule.WinMode == null ? 0 : Array.IndexOf(WinModes, rule.WinMode) + 1;
            body.Children.Add(Row("Что делать прозрачным", "Для этой программы",
                Seg(new[] { "Как у всех", "Только фон", "Окно целиком", "Ничего" }, Math.Max(0, wmIdx), i => { rule.WinMode = i == 0 ? null : WinModes[i - 1]; Changed(); Build(); })));
            if ((rule.WinMode ?? settings.WindowMode) == ColorSettings.WmAlpha || (rule.WinMode == null && settings.FallbackAlpha && settings.WindowMode == ColorSettings.WmBackground))
                body.Children.Add(SliderRow("Прозрачность окна целиком", "Для этой программы", 20, 100, rule.Opacity, "%", v => { rule.Opacity = v; Changed(); }));
            var idx = rule.Backdrop == null ? 0 : Array.IndexOf(Backdrops, rule.Backdrop) + 1;
            body.Children.Add(Row("Что за окном", "Для этой программы",
                Seg(new[] { "Как у всех" }.Concat(BackdropLabels).ToArray(), Math.Max(0, idx), i => { rule.Backdrop = i == 0 ? null : Backdrops[i - 1]; Changed(); Build(); })));
            panel.Children.Add(body);
        }
        return panel;
    }

    UIElement TaskbarCard()
    {
        var rows = new List<UIElement>
        {
            Row("Панель задач", "Прозрачный, размытый или цветной фон панели. Форма и расположение значков — в плагине Taskbar Plus.",
                Seg(new[] { "Выкл", "Вкл" }, settings.TaskbarOn ? 1 : 0, i => { settings.TaskbarOn = i == 1; Changed(); Build(); })),
        };
        if (!settings.TaskbarOn) return Card("Панель задач", rows);

        rows.Add(Row("Фон панели", "Как в Windows — не трогать. Прозрачная — без фона. Размытая — матовое стекло. Свой цвет — заливка выбранным цветом.",
            Seg(new[] { "Как в Windows", "Прозрачная", "Размытая", "Свой цвет" }, Math.Max(0, Array.IndexOf(TaskbarModes, settings.TaskbarMode)), i =>
            {
                settings.TaskbarMode = TaskbarModes[i];
                Changed(); Build();
            })));
        if (settings.TaskbarMode is ColorSettings.TbBlur or ColorSettings.TbSolid)
        {
            rows.Add(Row("Цвет", "Готовые цвета или свой в виде #RRGGBB", ColorPicker(settings.TaskbarColor, hex => { settings.TaskbarColor = hex; Changed(); })));
            rows.Add(SliderRow("Непрозрачность", "Чем меньше, тем прозрачнее", 0, 100, settings.TaskbarOpacity, "%", v => { settings.TaskbarOpacity = v; Changed(); }));
        }
        if (settings.TaskbarMode != ColorSettings.TbDefault)
            rows.Add(Row("Тонкая линия панели", "У панели Windows 11 есть тонкая обводка. Её можно убрать.",
                Seg(new[] { "Оставить", "Убрать" }, settings.TaskbarHideStroke ? 1 : 0, i => { settings.TaskbarHideStroke = i == 1; Changed(); })));

        var (_, text) = TaskbarLink.EngineState();
        rows.Add(Note(text));
        rows.Add(Note("Фон панели Windows 11 рисует сама панель, поэтому прозрачность и цвет здесь делает движок Taskbar Plus, а размытие за прозрачной панелью — Windows."));
        return Card("Панель задач", rows);
    }

    UIElement SelectionCard()
    {
        var rows = new List<UIElement>
        {
            Row("Цвет выделения", "Цвет, которым подсвечивается текст и строки, выделенные мышкой (в программах, которые берут цвет у Windows).",
                Seg(new[] { "Выкл", "Вкл" }, settings.SelectionOn ? 1 : 0, i => { settings.SelectionOn = i == 1; Changed(); Build(); })),
        };
        if (settings.SelectionOn)
        {
            rows.Add(Row("Цвет подсветки", "Фон выделенного текста", ColorPicker(settings.SelectionColor, hex => { settings.SelectionColor = hex; Changed(); })));
            rows.Add(Row("Цвет текста", "Цвет выделенных букв", ColorPicker(settings.SelectionTextColor, hex => { settings.SelectionTextColor = hex; Changed(); })));
        }
        rows.Add(Row("Акцентный цвет Windows", "Тот же, что в Параметрах → Персонализация → Цвета: выделенные строки в Проводнике, кнопки, меню «Пуск».",
            Seg(new[] { "Выкл", "Вкл" }, settings.AccentOn ? 1 : 0, i => { settings.AccentOn = i == 1; Changed(); Build(); })));
        if (settings.AccentOn)
            rows.Add(Row("Акцентный цвет", "Часть программ подхватит его только после перезапуска", ColorPicker(settings.AccentColor, hex => { settings.AccentColor = hex; Changed(); })));
        rows.Add(Note("Браузеры и некоторые современные программы подсвечивают выделение своим цветом — у них он не поменяется. Цвета возвращаются при выключении, выходе из плагина и при следующем запуске, если плагин закрылся аварийно."));
        return Card("Выделение и акцент", rows);
    }

    UIElement SystemCard()
    {
        var rows = new List<UIElement>
        {
            Row("Запускать вместе с Windows", "Эффекты будут применяться после входа в систему, окно открываться не будет",
                Seg(new[] { "Нет", "Да" }, Autostart ? 1 : 0, i =>
                {
                    try { SetAutostart(i == 1); Say(i == 1 ? "Автозапуск включён" : "Автозапуск выключен"); }
                    catch (Exception ex) { Say("Не получилось: " + ex.Message); }
                    Build();
                })),
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(Btn("Вернуть всё как было", "Danger", ResetAll, first: true));
        buttons.Children.Add(Btn("Выйти", null, () => { App.Quitting = true; Close(); }));
        rows.Add(buttons);
        return Card("Система", rows);
    }

    // ---------- действия ----------
    void ResetAll()
    {
        settings.WindowsOn = false;
        settings.TaskbarOn = false;
        settings.SelectionOn = false;
        settings.AccentOn = false;
        applyTimer.Stop();
        settings.Save();
        ctl.Apply(settings);
        Say("Всё возвращено как было");
        Build();
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "ColorPlus";

    static bool Autostart
    {
        get { try { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue(RunName) != null; } catch { return false; } }
    }

    static void SetAutostart(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue(RunName, "\"" + Environment.ProcessPath + "\" --background");
        else k.DeleteValue(RunName, false);
    }

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
    WrapPanel Seg(string[] labels, int selected, Action<int> choose)
    {
        var seg = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = 460 };
        for (var i = 0; i < labels.Length; i++)
        {
            var idx = i;
            var b = new Button { Content = labels[i], Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 4) };
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
}
