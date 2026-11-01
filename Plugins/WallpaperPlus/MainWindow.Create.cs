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
    string createBackground = "";
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
        createBackground = w.Background;
        ShowPage(2);
    }

    // Открыть «Создать» с нужными обоями (и сразу редактор) — для проверки и ярлыков
    public void OpenCreate(string id, bool editor)
    {
        var w = Library.Get(id);
        if (w == null) return;
        StartCreate(w);
        if (editor) Dispatcher.BeginInvoke(new Action(OpenEditor), DispatcherPriority.Loaded);
    }

    // Предпросмотр живёт только пока открыта страница «Создать»: иначе он зря грузил бы компьютер
    void DisposePreview()
    {
        if (previewWeb != null) WallpaperView.Detach(previewWeb);
        try { previewWeb?.Dispose(); } catch { }
        previewWeb = null;
    }

    void ReleasePreview()
    {
        previewTimer.Stop();
        DisposePreview();
        if (current != 2) pages[2].Children.Clear();
    }

    void BuildCreate()
    {
        var p = pages[2];
        p.Children.Clear();
        DisposePreview();
        if (!previewTimerHooked)
        {
            previewTimerHooked = true;
            previewTimer.Tick += (_, _) => { previewTimer.Stop(); NavigatePreview(); };
        }

        var templates = Library.List().Where(w => w.Params.Count > 0).ToList();
        if (templates.Count == 0) { p.Children.Add(Note("Пока нет обоев, которые можно настраивать.")); return; }
        if (createBase == null || !templates.Any(t => t.Id == createBase.Id)) { createBase = templates[0]; createEditing = false; createTitle = ""; createValues = new(); createBackground = templates[0].Background; }
        var baseW = createBase;

        // основа
        var names = templates.Select(t => t.Title).ToArray();
        p.Children.Add(Card("1. Основа", new UIElement[]
        {
            Note(createEditing ? "Вы настраиваете свои обои. Изменения сохраняются кнопкой «Сохранить изменения»." : "Выберите, что взять за основу. Встроенные обои не меняются — получится новая копия со своими цветами."),
            Seg(names, Math.Max(0, templates.FindIndex(t => t.Id == baseW.Id)), i => { createBase = templates[i]; createEditing = !templates[i].Builtin; createTitle = createEditing ? templates[i].Title : ""; createValues = new(templates[i].Values); createBackground = templates[i].Background; BuildCreate(); }, 900),
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
            if (prm.Type == "layout") continue;   // раскладку правит редактор, а не ползунки
            if (prm.Type == "color")
                rows.Add(Row(prm.Label, "", ColorPicker(cur, hex => { createValues[key] = hex; SchedulePreview(); })));
            else if (prm.Type == "choice" && prm.Options.Count > 0)
                rows.Add(Row(prm.Label, "", Seg(prm.Options.ToArray(), Math.Max(0, prm.Options.IndexOf(cur)), i => { createValues[key] = prm.Options[i]; SchedulePreview(); BuildCreate(); })));
            else if (prm.Type == "text")
            {
                var tb = new TextBox { Text = cur, Width = 260, Padding = new Thickness(6, 4, 6, 4) };
                tb.TextChanged += (_, _) => { createValues[key] = tb.Text; SchedulePreview(); };
                rows.Add(Row(prm.Label, "", tb));
            }
            else
            {
                var dv = double.TryParse(cur, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.TryParse(prm.Default, NumberStyles.Float, CultureInfo.InvariantCulture, out var d0) ? d0 : prm.Min;
                rows.Add(SliderRow(prm.Label, "", prm.Min, prm.Max, prm.Step <= 0 ? 0.1 : prm.Step, Math.Clamp(dv, prm.Min, prm.Max), "", val => { createValues[key] = val.ToString("0.###", CultureInfo.InvariantCulture); SchedulePreview(); }));
            }
        }
        if (baseW.Overlay)
        {
            // что лежит под слоем: «Ничего» или любые другие обои из библиотеки (космос, северное сияние, видео…)
            var under = Library.List().Where(x => !x.Overlay && x.Id != baseW.Id).ToList();
            var labels = new List<string> { "Ничего (тёмная подложка)" };
            labels.AddRange(under.Select(x => x.Title));
            var sel = createBackground.Length == 0 ? 0 : Math.Max(0, under.FindIndex(x => x.Id == createBackground) + 1);
            rows.Add(Row("Что под терминалом", "Терминал рисуется поверх других обоев: можно поставить космос, сияние или своё видео. «Ничего» — тёмный фон.",
                Seg(labels.ToArray(), sel, i => { createBackground = i == 0 ? "" : under[i - 1].Id; SchedulePreview(); BuildCreate(); }, 900)));
        }
        if (baseW.Params.Any(x => x.Type == "layout"))
        {
            var ed = new StackPanel { Orientation = Orientation.Horizontal };
            ed.Children.Add(Btn("Открыть редактор панелей…", "Primary", OpenEditor, first: true));
            ed.Children.Add(Btn("Вернуть расположение по умолчанию", null, () => { createValues.Remove("layout"); SchedulePreview(); Say("Расположение панелей сброшено."); }));
            rows.Add(Row("Панели терминала", "Часы, погода, звук, музыка, курсор, журнал событий и ваши панели: двигайте мышью, добавляйте и убирайте", ed));
        }
        p.Children.Add(Card("2. Настройте", rows));

        // предпросмотр
        var holder = new System.Windows.Controls.Border
        {
            Width = baseW.Overlay ? 768 : 576, Height = baseW.Overlay ? 432 : 324, Background = System.Windows.Media.Brushes.Black, BorderBrush = Res("Line"), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left,
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
            Note("Предпросмотр идёт в окне плагина; на рабочем столе обои появятся после «Включить». Своё расположение панелей терминала делается в редакторе (кнопка в шаге 2)."),
        }));
    }

    async Task LoadPreviewAsync()
    {
        if (previewWeb == null || createBase == null) return;
        try { await WallpaperView.LoadAsync(previewWeb, createBase, settings, createValues, PreviewOpts()); }
        catch (Exception ex) { Say("Предпросмотр не запустился: " + ex.Message + " (нужен «Microsoft Edge WebView2 Runtime»)."); }
    }

    LoadOptions PreviewOpts() => new() { Background = createBase != null && createBase.Overlay ? createBackground : null };

    void SchedulePreview() { previewTimer.Stop(); previewTimer.Start(); }

    void OpenEditor()
    {
        if (createBase == null) return;
        var win = new EditorWindow(this, createBase, settings, createValues, createBackground, text => { createValues["layout"] = text; SchedulePreview(); });
        win.Show();
    }

    void NavigatePreview()
    {
        try
        {
            if (previewWeb?.CoreWebView2 != null && createBase != null) WallpaperView.Navigate(previewWeb, createBase, settings, createValues, PreviewOpts());
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
            Library.UpdateValues(createBase, createValues, title, createBackground);
            saved = Library.Get(createBase.Id);
            Say("Сохранено: " + (saved?.Title ?? title));
            if (settings.Active == createBase.Id) _ = ctl.ApplyAsync(settings);
        }
        else
        {
            var (w, msg) = Library.CreateFrom(createBase, title, createValues, createBackground);
            Say(msg);
            saved = w;
            if (w != null) { createBase = w; createEditing = true; createTitle = w.Title; createValues = new Dictionary<string, string>(w.Values); createBackground = w.Background; }
        }
        if (saved != null && activate) Activate(saved.Id);
        BuildLibrary();
    }
}
