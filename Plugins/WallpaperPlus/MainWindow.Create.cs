using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;

namespace WallpaperPlusApp;

// Страница «Создать»: свои обои на основе шаблона. Выбираете основу, меняете цвета и скорость — рядом сразу видно результат, потом сохраняете в «Мои обои».
// Здесь же будут появляться новые инструменты для создания обоев (свой текст, картинки, слои): основа любых обоев — папка со страницей и манифестом.
sealed partial class MainWindow
{
    Wallpaper? createBase;
    bool createEditing;
    string createTitle = "";
    Dictionary<string, string> createValues = new();
    WebView2? previewWeb;
    readonly DispatcherTimer previewTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    bool previewTimerHooked;

    // Открыть «Создать» с этими обоями за основу (для встроенных — новые свои обои, для созданных — правка)
    void StartCreate(Wallpaper w)
    {
        createBase = w;
        createEditing = !w.Builtin;
        createTitle = createEditing ? w.Title : "";
        createValues = new Dictionary<string, string>(w.Values);
        ShowPage(2);
    }

    // Предпросмотр живёт только пока открыта страница «Создать»: иначе он зря грузил бы компьютер
    void ReleasePreview()
    {
        previewTimer.Stop();
        try { previewWeb?.Dispose(); } catch { }
        previewWeb = null;
        if (current != 2) pages[2].Children.Clear();
    }

    void BuildCreate()
    {
        var p = pages[2];
        p.Children.Clear();
        try { previewWeb?.Dispose(); } catch { }
        previewWeb = null;
        if (!previewTimerHooked)
        {
            previewTimerHooked = true;
            previewTimer.Tick += (_, _) => { previewTimer.Stop(); NavigatePreview(); };
        }

        var templates = Library.List().Where(w => w.Params.Count > 0).ToList();
        if (templates.Count == 0) { p.Children.Add(Note("Пока нет обоев, которые можно настраивать.")); return; }
        if (createBase == null || !templates.Any(t => t.Id == createBase.Id)) { createBase = templates[0]; createEditing = false; createTitle = ""; createValues = new(); }
        var baseW = createBase;

        // основа
        var names = templates.Select(t => t.Title).ToArray();
        p.Children.Add(Card("1. Основа", new UIElement[]
        {
            Note(createEditing ? "Вы настраиваете свои обои. Изменения сохраняются кнопкой «Сохранить изменения»." : "Выберите, что взять за основу. Встроенные обои не меняются — получится новая копия со своими цветами."),
            Seg(names, Math.Max(0, templates.FindIndex(t => t.Id == baseW.Id)), i => { createBase = templates[i]; createEditing = !templates[i].Builtin; createTitle = createEditing ? templates[i].Title : ""; createValues = new(templates[i].Values); BuildCreate(); }, 900),
        }));

        // настройки
        var rows = new List<UIElement>();
        var titleBox = new TextBox { Text = createTitle.Length > 0 ? createTitle : baseW.Title + " (мои)", Width = 260, Padding = new Thickness(6, 4, 6, 4) };
        titleBox.TextChanged += (_, _) => createTitle = titleBox.Text;
        if (createTitle.Length == 0) createTitle = titleBox.Text;
        rows.Add(Row("Название", "Так обои будут называться в «Моих обоях»", titleBox));
        foreach (var prm in baseW.Params)
        {
            var key = prm.Key;
            var cur = createValues.TryGetValue(key, out var v) && v.Length > 0 ? v : prm.Default;
            if (prm.Type == "color")
                rows.Add(Row(prm.Label, "", ColorPicker(cur, hex => { createValues[key] = hex; SchedulePreview(); })));
            else
            {
                var dv = double.TryParse(cur, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.TryParse(prm.Default, NumberStyles.Float, CultureInfo.InvariantCulture, out var d0) ? d0 : prm.Min;
                rows.Add(SliderRow(prm.Label, "", prm.Min, prm.Max, prm.Step <= 0 ? 0.1 : prm.Step, Math.Clamp(dv, prm.Min, prm.Max), "", val => { createValues[key] = val.ToString("0.###", CultureInfo.InvariantCulture); SchedulePreview(); }));
            }
        }
        p.Children.Add(Card("2. Настройте", rows));

        // предпросмотр
        var holder = new System.Windows.Controls.Border
        {
            Width = 576, Height = 324, Background = System.Windows.Media.Brushes.Black, BorderBrush = Res("Line"), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left,
        };
        if (!screenshotMode)
        {
            previewWeb = new WebView2();
            holder.Child = previewWeb;
            Dispatcher.BeginInvoke(new Action(async () => await LoadPreviewAsync()), DispatcherPriority.Loaded);
        }
        else holder.Child = new TextBlock { Text = "Предпросмотр", Foreground = Res("Muted"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var save = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        if (createEditing)
        {
            save.Children.Add(Btn("Сохранить изменения", "Primary", () => SaveCreated(false, true), first: true));
            save.Children.Add(Btn("Сохранить и включить", null, () => SaveCreated(true, true)));
            save.Children.Add(Btn("Сохранить как новые", null, () => SaveCreated(false, false)));
        }
        else
        {
            save.Children.Add(Btn("Сохранить", "Primary", () => SaveCreated(false, false), first: true));
            save.Children.Add(Btn("Сохранить и включить", null, () => SaveCreated(true, false)));
        }
        save.Children.Add(Btn("Сбросить значения", null, () => { createValues = new(); BuildCreate(); }));
        p.Children.Add(Card("3. Предпросмотр и сохранение", new UIElement[]
        {
            holder, save,
            Note("Предпросмотр идёт в окне плагина; на рабочем столе обои появятся после «Включить». Позже здесь появятся и другие способы создать обои: свой текст, картинки, слои."),
        }));
    }

    async Task LoadPreviewAsync()
    {
        if (previewWeb == null || createBase == null) return;
        try { await WallpaperView.LoadAsync(previewWeb, createBase, settings, createValues); }
        catch (Exception ex) { Say("Предпросмотр не запустился: " + ex.Message + " (нужен «Microsoft Edge WebView2 Runtime»)."); }
    }

    void SchedulePreview() { previewTimer.Stop(); previewTimer.Start(); }

    void NavigatePreview()
    {
        try
        {
            if (previewWeb?.CoreWebView2 != null && createBase != null) previewWeb.CoreWebView2.Navigate(WallpaperView.EntryUrl(createBase, settings, createValues));
        }
        catch { }
    }

    void SaveCreated(bool activate, bool overwrite)
    {
        if (createBase == null) return;
        var title = createTitle.Trim();
        Wallpaper? saved;
        if (overwrite && !createBase.Builtin)
        {
            Library.UpdateValues(createBase, createValues, title);
            saved = Library.Get(createBase.Id);
            Say("Сохранено: " + (saved?.Title ?? title));
            if (settings.Active == createBase.Id) _ = ctl.ApplyAsync(settings);
        }
        else
        {
            var (w, msg) = Library.CreateFrom(createBase, title, createValues);
            Say(msg);
            saved = w;
            if (w != null) { createBase = w; createEditing = true; createTitle = w.Title; createValues = new Dictionary<string, string>(w.Values); }
        }
        if (saved != null && activate) Activate(saved.Id);
        BuildLibrary();
    }
}
