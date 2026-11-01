using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace IconPlusApp;

// Главный экран: сверху — как будут выглядеть настоящие значки (закреплённые на панели задач и основные типы), ниже цвет и кнопки «Применить» / «Вернуть как было».
// Всё остальное (способ, сила, что перекрашивать) спрятано в «Тонкая настройка».
sealed partial class MainWindow
{
    readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    readonly object engineGate = new();                  // движок один, а к нему обращаются и превью, и «Применить» — по очереди
    StackPanel? previewHost;
    bool busy, moreOpen;
    int previewGen;
    List<IconEngine.PreviewItem>? taskbarBase;           // закреплённые на панели задач: значок «до» (или причина, почему его не взять)
    List<(Target Target, BitmapSource? Before)>? typeBase;   // основные типы значков «до»

    static readonly string[] PreviewIds = { "folder", "drive-fixed", "thispc", "recycle-empty", "ft-text", "ft-archive", "ft-image", "ft-audio", "ft-video", "ft-pdf" };

    void SettingsChanged(bool rebuild = false)
    {
        saveTimer.Stop();
        saveTimer.Tick -= SaveTick;
        saveTimer.Tick += SaveTick;
        saveTimer.Start();
        if (rebuild) BuildColor();
        else RenderPreview();
    }

    void SaveTick(object? sender, EventArgs e) { saveTimer.Stop(); settings.Save(); }

    void BuildColor()
    {
        var p = pages[0];
        p.Children.Clear();

        // 1. как будет выглядеть
        previewHost = new StackPanel();
        p.Children.Add(Card("1. Так будет выглядеть", new UIElement[]
        {
            Note("Слева — значок сейчас, справа — каким станет. Показаны настоящие значки с вашей панели задач и самые частые типы. " +
                 "Крестик значит, что у такого значка Windows не отдаёт картинку, — под ним написано, почему."),
            previewHost,
        }));
        if (taskbarBase == null) LoadPreviewBase(); else RenderPreview();

        // 2. цвет
        p.Children.Add(Card("2. Выберите цвет", new UIElement[] { ColorStrip() }));

        // 3. применить
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var apply = Btn("Применить цвет", "Primary", ApplyColor, first: true);
        apply.Padding = new Thickness(26, 11, 26, 11); apply.FontSize = 16;
        buttons.Children.Add(apply);
        var revert = Btn("Вернуть как было", "Danger", RestoreAll);
        revert.Padding = new Thickness(18, 11, 18, 11);
        buttons.Children.Add(revert);
        p.Children.Add(buttons);
        p.Children.Add(Note("Пока вы не нажали «Применить цвет», в Windows ничего не меняется. После применения Проводник обновит значки сам (на пару секунд закроются окна папок)."));

        // 4. тонкая настройка
        var more = Btn(moreOpen ? "Тонкая настройка ▾" : "Тонкая настройка ▸", null, () => { moreOpen = !moreOpen; BuildColor(); }, first: true);
        more.HorizontalAlignment = HorizontalAlignment.Left; more.Margin = new Thickness(0, 6, 0, 10);
        p.Children.Add(more);
        if (moreOpen) p.Children.Add(FineTuneCard());
    }

    // Цвета: крупные образцы (выбранный обведён) и поле для своего цвета в виде #RRGGBB.
    UIElement ColorStrip()
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var box = new TextBox { Text = settings.Color, Width = 96, Padding = new Thickness(8, 6, 8, 6), VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 8) };
        box.TextChanged += (_, _) =>
        {
            var t = box.Text.Trim();
            if (System.Text.RegularExpressions.Regex.IsMatch(t, "^#[0-9A-Fa-f]{6}$")) { settings.Color = t.ToUpperInvariant(); SettingsChanged(); }
        };
        wrap.Children.Add(box);
        foreach (var hex in SwatchColors)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var selected = string.Equals(hex, settings.Color, StringComparison.OrdinalIgnoreCase);
            var sw = new Button
            {
                Width = 36, Height = 36, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(0),
                Background = new SolidColorBrush(color), BorderBrush = selected ? Res("Text") : Res("Line"), BorderThickness = new Thickness(selected ? 3 : 1), ToolTip = hex,
            };
            sw.Click += (_, _) => { settings.Color = hex; SettingsChanged(true); };
            wrap.Children.Add(sw);
        }
        return wrap;
    }

    UIElement FineTuneCard()
    {
        var rows = new List<UIElement>
        {
            Row("Способ", "«Один цвет» — все значки становятся оттенками выбранного цвета. «Сменить оттенок» — сохраняются краски, оттенок сдвигается к выбранному.",
                Seg(new[] { "Один цвет", "Сменить оттенок" }, settings.Style == Tint.Hue ? 1 : 0, i => { settings.Style = i == 1 ? Tint.Hue : Tint.Mono; SettingsChanged(true); })),
            SliderRow("Сила", "100% — полностью новый цвет; меньше — часть прежних красок остаётся.", 0, 100, settings.Strength, "%", v => { settings.Strength = v; SettingsChanged(); }),
        };
        foreach (var group in Target.All.GroupBy(t => t.Group))
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            foreach (var t in group)
            {
                var id = t.Id;
                wrap.Children.Add(Toggle(t.Title, settings.Enabled.Contains(id), on =>
                {
                    if (on) { if (!settings.Enabled.Contains(id)) settings.Enabled.Add(id); } else settings.Enabled.Remove(id);
                    SettingsChanged(true);
                }));
            }
            var box = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
            box.Children.Add(new TextBlock { Text = "Что перекрасить: " + group.Key, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            box.Children.Add(wrap);
            rows.Add(box);
        }
        rows.Add(Note("Значок обычных папок и ярлыки «для всех» (общий рабочий стол и общий «Пуск») меняются с правами администратора — Windows спросит один раз. " +
                      "Приложениям «Пуска» без ярлыков (Блокнот, Терминал, Безопасность Windows…) плагин кладёт в «Пуск» свой ярлык с нужным значком; «Вернуть как было» его убирает."));
        return Card("Тонкая настройка", rows);
    }

    // Значки «до»: закреплённые на панели задач и основные типы (с запоминанием исходников в движке).
    (List<IconEngine.PreviewItem> Taskbar, List<(Target Target, BitmapSource? Before)> Types) ComputePreviewBase(IconSettings snapshot)
    {
        lock (engineGate)
        {
            var taskbar = engine.PreviewShortcuts("lnk-taskbar", snapshot, 14);
            var types = new List<(Target Target, BitmapSource? Before)>();
            foreach (var id in PreviewIds)
            {
                var t = Target.ById(id);
                if (t == null) continue;
                BitmapSource? before = null;
                try { before = engine.Preview(t, snapshot).Before; } catch { }
                types.Add((t, before));
            }
            return (taskbar, types);
        }
    }

    // Один раз загружаем значки «до» (это бывает долго — Проводник рисует значки ярлыков), потом при смене цвета только пересчитываем «после».
    async void LoadPreviewBase()
    {
        var gen = ++previewGen;
        if (previewHost != null) { previewHost.Children.Clear(); previewHost.Children.Add(Note("Загружаю значки…")); }
        var snapshot = settings;
        try
        {
            var loaded = await Task.Run(() => ComputePreviewBase(snapshot));
            if (gen != previewGen) return;
            taskbarBase = loaded.Taskbar; typeBase = loaded.Types;
        }
        catch { taskbarBase = new(); typeBase = new(); }
        RenderPreview();
    }

    BitmapSource Tinted(BitmapSource before) => IconData.FromPixels(Tint.Apply(IconData.Pixels(before), settings.Color, settings.Style, settings.Strength), before.PixelWidth, before.PixelHeight);

    UIElement PreviewCell(string name, BitmapSource? before, string problem)
    {
        var cell = new StackPanel { Margin = new Thickness(0, 0, 18, 12), Width = 150 };
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        if (before != null)
        {
            line.Children.Add(Thumb(before));
            line.Children.Add(new TextBlock { Text = "→", VerticalAlignment = VerticalAlignment.Center, Foreground = Res("Muted"), Margin = new Thickness(0, 0, 8, 0) });
            line.Children.Add(Thumb(Tinted(before)));
        }
        else
        {
            line.Children.Add(new Border
            {
                Width = 40, Height = 40, Margin = new Thickness(0, 0, 8, 0), BorderBrush = Res("Line"), BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = "✕", Foreground = Res("Accent2"), FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
        }
        cell.Children.Add(line);
        cell.Children.Add(new TextBlock { Text = name, FontSize = 12, Foreground = Res("Text"), TextTrimming = TextTrimming.CharacterEllipsis });
        if (problem.Length > 0) cell.Children.Add(new TextBlock { Text = problem, FontSize = 11, Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap });
        cell.ToolTip = problem.Length > 0 ? name + ": " + problem : name;
        return cell;
    }

    void RenderPreview()
    {
        if (previewHost == null || taskbarBase == null || typeBase == null) return;
        previewHost.Children.Clear();
        if (taskbarBase.Count > 0)
        {
            previewHost.Children.Add(new TextBlock { Text = "Закреплено на панели задач", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
            var wrap = new WrapPanel();
            foreach (var it in taskbarBase) wrap.Children.Add(PreviewCell(it.Name, it.Before, it.Problem));
            previewHost.Children.Add(wrap);
        }
        previewHost.Children.Add(new TextBlock { Text = "Папки, диски и файлы", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 8) });
        var types = new WrapPanel();
        foreach (var (t, before) in typeBase) types.Children.Add(PreviewCell(t.Title.Split('(')[0].Trim(), before, before == null ? "значок не читается" : ""));
        previewHost.Children.Add(types);
    }

    async void ApplyColor()
    {
        if (busy) return;
        busy = true;
        settings.Save();
        Say("Перекрашиваю значки…");
        try
        {
            var snapshot = settings;
            var result = await Task.Run(() => { lock (engineGate) return engine.ApplyColor(snapshot, t => Dispatcher.BeginInvoke(new Action(() => Say("Перекрашиваю: " + t)))); });
            var reportText = Report.Build(result);
            Report.Save(reportText);
            Say(Report.Summary(result));
            if (settings.AutoReport) Say(Report.Summary(result) + " " + await Report.UploadAsync(reportText));
            taskbarBase = null;
            if (result.Outcomes.Any(o => o.Ok)) { busy = false; BuildState(); AskExplorerRestart(); }
        }
        catch (Exception ex) { Say("Не вышло: " + ex.Message); }
        finally { busy = false; BuildState(); }
    }

    async void RestoreAll()
    {
        if (busy) return;
        busy = true;
        try
        {
            var n = await Task.Run(() => { lock (engineGate) return engine.RestoreAll(); });
            Say($"Всё возвращено как было (изменений отменено: {n}).");
            taskbarBase = null;
            if (n > 0) { busy = false; BuildState(); AskExplorerRestart(); }
        }
        catch (Exception ex) { Say("Не вышло: " + ex.Message); }
        finally { busy = false; BuildState(); }
    }
}
