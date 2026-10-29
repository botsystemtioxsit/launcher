using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace IconPlusApp;

sealed partial class MainWindow
{
    readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    WrapPanel? previewPanel;
    bool busy;

    static readonly string[] PreviewIds = { "folder", "drive-fixed", "thispc", "recycle-empty", "ft-text", "ft-archive", "ft-image", "ft-audio", "ft-video", "ft-pdf" };

    void SettingsChanged(bool rebuild = false)
    {
        saveTimer.Stop();
        saveTimer.Tick -= SaveTick;
        saveTimer.Tick += SaveTick;
        saveTimer.Start();
        if (rebuild) BuildColor();
        else RefreshPreview();
    }

    void SaveTick(object? sender, EventArgs e) { saveTimer.Stop(); settings.Save(); }

    void BuildColor()
    {
        var p = pages[0];
        p.Children.Clear();

        var rows = new List<UIElement>
        {
            Note("Выберите цвет — плагин перекрасит значки Windows в него: папки, диски, «Этот компьютер», корзину, типы файлов, ярлыки на рабочем столе и закреплённые на панели задач. " +
                 "Форма и свет значков сохраняются, меняется только цвет. Значки программ внутри их окон и обычных запущенных программ плагин не трогает."),
            Row("Цвет", "Готовые цвета или свой в виде #RRGGBB", ColorPicker(settings.Color, hex => { settings.Color = hex; SettingsChanged(); })),
            Row("Способ", "«Один цвет» — все значки становятся оттенками выбранного цвета. «Сменить оттенок» — сохраняются краски, оттенок сдвигается к выбранному.",
                Seg(new[] { "Один цвет", "Сменить оттенок" }, settings.Style == Tint.Hue ? 1 : 0, i => { settings.Style = i == 1 ? Tint.Hue : Tint.Mono; SettingsChanged(true); })),
            SliderRow("Сила", "100% — полностью новый цвет; меньше — часть прежних красок остаётся.", 0, 100, settings.Strength, "%", v => { settings.Strength = v; SettingsChanged(); }),
        };
        p.Children.Add(Card("Цвет значков", rows));

        // превью
        previewPanel = new WrapPanel();
        p.Children.Add(Card("Так будет выглядеть", new UIElement[] { previewPanel }));
        RefreshPreview();

        // что перекрашивать
        var scope = new List<UIElement>();
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
            var box = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            box.Children.Add(new TextBlock { Text = group.Key, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            box.Children.Add(wrap);
            scope.Add(box);
        }
        scope.Add(Note("Ярлыки в меню «Пуск» по умолчанию не затрагиваются (их много) — включите, если нужно. Значки на рабочем столе для всех пользователей и системные ярлыки без прав администратора не меняются."));
        p.Children.Add(Card("Что перекрасить", scope));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        buttons.Children.Add(Btn("Применить цвет", "Primary", ApplyColor, first: true));
        buttons.Children.Add(Btn("Вернуть как было", "Danger", RestoreAll));
        p.Children.Add(buttons);
        p.Children.Add(Note("После применения Проводник обновляет значки сам; если где-то остался старый значок, на вкладке «Состояние» есть перезапуск Проводника."));
    }

    void RefreshPreview()
    {
        if (previewPanel == null) return;
        previewPanel.Children.Clear();
        foreach (var id in PreviewIds)
        {
            var t = Target.ById(id);
            if (t == null) continue;
            try
            {
                var (before, after) = engine.Preview(t, settings);
                var cell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 10) };
                cell.Children.Add(Thumb(before));
                cell.Children.Add(new TextBlock { Text = "→", VerticalAlignment = VerticalAlignment.Center, Foreground = Res("Muted"), Margin = new Thickness(0, 0, 8, 0) });
                cell.Children.Add(Thumb(after));
                cell.ToolTip = t.Title;
                previewPanel.Children.Add(cell);
            }
            catch { }
        }
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
            var result = await Task.Run(() => engine.ApplyColor(snapshot, t => Dispatcher.BeginInvoke(new Action(() => Say("Перекрашиваю: " + t)))));
            Report.Save(Report.Build(result));
            Say(Report.Summary(result));
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
            var n = await Task.Run(() => engine.RestoreAll());
            Say($"Всё возвращено как было (изменений отменено: {n}).");
            if (n > 0) { busy = false; BuildState(); AskExplorerRestart(); }
        }
        catch (Exception ex) { Say("Не вышло: " + ex.Message); }
        finally { busy = false; BuildState(); }
    }
}
