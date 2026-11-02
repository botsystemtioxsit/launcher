using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace SkinLoaderApp;

// Окно Skin Loader. Две страницы: «Собрать скин» (галочки — какие плагины входят, имя, картинка) и «Мои скины» (применить, вернуть, файл, удалить).
sealed class MainWindow : Window
{
    readonly StackPanel page = new();
    readonly bool demo;
    string tab = "build";
    bool busy;
    string statusText = "";
    bool statusOk = true;

    // состояние страницы «Собрать»
    string nameText = "Мой скин", authorText = "", descText = "";
    readonly HashSet<string> chosen = new();
    bool chosenInit;
    byte[]? preview;
    List<Capture.PartInfo>? scan;
    List<SavedSkin>? demoSkins;

    static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    public MainWindow(bool demo = false)
    {
        this.demo = demo;
        Title = "Skin Loader";
        Width = Math.Min(980, SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(900, SystemParameters.WorkArea.Height - 40);
        MinWidth = 660; MinHeight = 480;
        Background = Res("Bg"); Foreground = Res("Text");
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        page.Margin = new Thickness(28, 22, 28, 24);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
        authorText = DefaultAuthor();
        if (demo) MakeDemo();
        Rebuild();
    }

    // имя автора по умолчанию — имя аккаунта в лаунчере (в его файле лежит открытым текстом только то, что и так видно в базе)
    static string DefaultAuthor()
    {
        try
        {
            var f = Path.Combine(Plugins.DataDir, "launcher.json");
            if (!File.Exists(f)) return "";
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(f));
            var id = doc.RootElement.TryGetProperty("ActiveId", out var a) ? a.GetString() : null;
            if (id == null || !doc.RootElement.TryGetProperty("Accounts", out var accs)) return "";
            foreach (var acc in accs.EnumerateArray())
                if (acc.TryGetProperty("Id", out var i) && i.GetString() == id && acc.TryGetProperty("Name", out var n)) return SkinSchema.Text(n.GetString(), 40, "");
        }
        catch { }
        return "";
    }

    void MakeDemo()
    {
        var s1 = new Skin { Id = "s-demo1", Name = "Neon Night", Author = "Alex", Description = "Розовый неон: курсор с кометой, острова на панели, обои «Нео-сетка».", Created = 2 };
        s1.Parts["taskbar"] = SkinSchema.CleanPart("taskbar", new System.Text.Json.Nodes.JsonObject { ["shape"] = "islands" }, new List<string>());
        s1.Parts["color"] = SkinSchema.CleanPart("color", new System.Text.Json.Nodes.JsonObject { ["color"] = "#FF2A9D" }, new List<string>());
        s1.Parts["wallpaper"] = SkinSchema.CleanPart("wallpaper", new System.Text.Json.Nodes.JsonObject { ["id"] = "b-neon-grid" }, new List<string>());
        s1.Parts["cursor"] = SkinSchema.CleanPart("cursor", new System.Text.Json.Nodes.JsonObject { ["shape"] = "neon", ["fill"] = "#FF2A9D", ["trail"] = "comet" }, new List<string>());
        var s2 = new Skin { Id = "s-demo2", Name = "Тихий зелёный", Author = "Alex", Description = "Только цвет и значки — без обоев и курсора.", Created = 1 };
        s2.Parts["color"] = SkinSchema.CleanPart("color", new System.Text.Json.Nodes.JsonObject { ["color"] = "#2CFF9A", ["taskbar"] = false }, new List<string>());
        s2.Parts["icons"] = SkinSchema.CleanPart("icons", new System.Text.Json.Nodes.JsonObject { ["color"] = "#2CFF9A" }, new List<string>());
        demoSkins = new List<SavedSkin> { new(s1, null), new(s2, null) };
    }

    // ---------- элементы ----------
    static TextBlock Note(string text, Thickness? margin = null) => new() { Text = text, Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0, 4, 0, 10) };

    static Border Card(string title, IEnumerable<UIElement> children)
    {
        var st = new StackPanel();
        st.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), FontFamily = (FontFamily)Application.Current.FindResource("Mono"), FontWeight = FontWeights.Bold, Foreground = Res("Accent2"), Margin = new Thickness(0, 0, 0, 10) });
        foreach (var c in children) st.Children.Add(c);
        return new Border { Background = Res("Panel"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(20), Margin = new Thickness(0, 0, 0, 14), Child = st };
    }

    static Border Badge(string text, Brush fg) => new()
    {
        Background = Res("Panel2"), CornerRadius = new CornerRadius(99), Padding = new Thickness(9, 2, 9, 3), Margin = new Thickness(0, 0, 6, 0),
        Child = new TextBlock { Text = text, FontSize = 12, Foreground = fg },
    };

    Button Btn(string text, string? style, Func<Task> click, bool first = false)
    {
        var b = new Button { Content = text, Margin = new Thickness(first ? 0 : 8, 0, 0, 0), IsEnabled = !busy };
        if (style != null) b.Style = (Style)Application.Current.FindResource(style);
        b.Click += async (_, _) => { try { await click(); } catch (Exception ex) { SetStatus("Не получилось: " + ex.Message, false); } };
        return b;
    }

    void SetStatus(string text, bool ok) { statusText = text; statusOk = ok; Rebuild(); }

    // ---------- страницы ----------
    void Rebuild()
    {
        page.Children.Clear();
        page.Children.Add(new TextBlock { Text = "SKIN LOADER", FontFamily = (FontFamily)Application.Current.FindResource("Mono"), FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Res("Accent") });
        page.Children.Add(Note("Скин — это настройки ваших плагинов (панель задач, цвет, значки, обои, курсор) в одном файле: собрали один раз — применяете одной кнопкой, а ещё можно отдать другу."));
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 14) };
        nav.Children.Add(TabBtn("build", "Собрать скин"));
        nav.Children.Add(TabBtn("mine", "Мои скины"));
        page.Children.Add(nav);
        if (statusText.Length > 0) page.Children.Add(new TextBlock { Text = statusText, Foreground = Res(statusOk ? "Green" : "Red"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        if (tab == "build") BuildPage(); else MinePage();
    }

    Button TabBtn(string id, string text)
    {
        var b = new Button { Content = text, Margin = new Thickness(id == "build" ? 0 : 8, 0, 0, 0), Style = (Style)Application.Current.FindResource(tab == id ? "Primary" : "Ghost") };
        b.Click += (_, _) => { tab = id; statusText = ""; Rebuild(); };
        return b;
    }

    // ----- «Собрать скин» -----
    void BuildPage()
    {
        scan ??= demo ? DemoScan() : Capture.Scan();
        if (!chosenInit) { foreach (var i in scan.Where(i => i.Installed && i.InUse && i.Settings != null)) chosen.Add(i.Part); chosenInit = true; }

        var name = new TextBox { Text = nameText, MaxLength = 40, Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
        name.TextChanged += (_, _) => nameText = name.Text;
        var author = new TextBox { Text = authorText, MaxLength = 40, Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
        author.TextChanged += (_, _) => authorText = author.Text;
        var desc = new TextBox { Text = descText, MaxLength = 300, MinHeight = 64, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        desc.TextChanged += (_, _) => descText = desc.Text;
        page.Children.Add(Card("Название", new UIElement[]
        {
            Label("Как назвать скин"), name, Label("Автор", 10), author, Label("Описание (необязательно)", 10), desc,
        }));

        var rows = new List<UIElement> { Note("Отметьте, что должно войти в скин. Берутся настройки, которые сейчас выбраны в каждом плагине.", new Thickness(0, 0, 0, 8)) };
        foreach (var info in scan)
        {
            var part = info.Part;
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var cb = new CheckBox { IsChecked = chosen.Contains(part), IsEnabled = info.Settings != null, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 12, 0) };
            cb.Click += (_, _) => { if (cb.IsChecked == true) chosen.Add(part); else chosen.Remove(part); };
            row.Children.Add(cb);
            var text = new StackPanel();
            Grid.SetColumn(text, 1);
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new TextBlock { Text = SkinSchema.PartNames[part], FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(Badge(SkinSchema.PartPlugins[part], Res("Muted")));
            head.Children.Add(info.Installed ? Badge(info.InUse ? "используется" : "не применялось", info.InUse ? Res("Green") : Res("Muted")) : Badge("плагин не установлен", Res("Red")));
            text.Children.Add(head);
            text.Children.Add(new TextBlock { Text = info.Summary, Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            if (info.Note != null) text.Children.Add(new TextBlock { Text = info.Note, Foreground = Res("Accent2"), FontSize = 12, TextWrapping = TextWrapping.Wrap });
            row.Children.Add(text);
            rows.Add(row);
        }
        var rescan = Btn("Прочитать настройки заново", null, () => { scan = null; Rebuild(); return Task.CompletedTask; }, first: true);
        rescan.HorizontalAlignment = HorizontalAlignment.Left;
        rows.Add(rescan);
        page.Children.Add(Card("Что войдёт в скин", rows));

        var pv = new List<UIElement> { Note("Картинка помогает узнать скин в списке. Лучше всего — снимок рабочего стола с уже применёнными настройками.", new Thickness(0, 0, 0, 8)) };
        var pvRow = new StackPanel { Orientation = Orientation.Horizontal };
        pvRow.Children.Add(Btn("Снять экран", null, CaptureScreen, first: true));
        pvRow.Children.Add(Btn("Выбрать файл…", null, PickPreview));
        if (preview != null) pvRow.Children.Add(Btn("Убрать", null, () => { preview = null; Rebuild(); return Task.CompletedTask; }));
        pv.Add(pvRow);
        if (Shots.Load(preview) is { } bmp) pv.Add(new Border { BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0), Child = new Image { Source = bmp, Width = 320, Margin = new Thickness(1) } });
        page.Children.Add(Card("Картинка для превью", pv));

        var make = Btn("Собрать скин", "Primary", BuildSkin, first: true);
        make.HorizontalAlignment = HorizontalAlignment.Left;
        page.Children.Add(make);
    }

    static TextBlock Label(string text, double top = 0) => new() { Text = text, Foreground = Res("Muted"), Margin = new Thickness(0, top, 0, 4) };

    List<Capture.PartInfo> DemoScan() => new()
    {
        new("taskbar", true, true, "форма панели: острова", new System.Text.Json.Nodes.JsonObject(), null),
        new("color", true, true, "цвет #FF2A9D, окна и выделение, острова панели", new System.Text.Json.Nodes.JsonObject(), null),
        new("icons", true, false, "все значки цвета #00F0FF", new System.Text.Json.Nodes.JsonObject(), null),
        new("wallpaper", false, false, "настроек пока нет — откройте плагин и выберите", null, null),
        new("cursor", true, true, "курсор «neon» #FF2A9D, шлейф «comet»", new System.Text.Json.Nodes.JsonObject(), null),
    };

    async Task CaptureScreen()
    {
        WindowState = WindowState.Minimized;
        try
        {
            await Task.Delay(900);
            var bmp = await Task.Run(() => Shots.CaptureScreen());
            preview = Shots.ToJpeg(bmp);
        }
        finally { WindowState = WindowState.Normal; Activate(); }
        Rebuild();
    }

    Task PickPreview()
    {
        var dlg = new OpenFileDialog { Title = "Картинка для превью", Filter = "Картинки (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dlg.ShowDialog(this) != true) return Task.CompletedTask;
        preview = Shots.Sanitize(File.ReadAllBytes(dlg.FileName));
        Rebuild();
        return Task.CompletedTask;
    }

    Task BuildSkin()
    {
        if (chosen.Count == 0) { SetStatus("Отметьте хотя бы одну часть.", false); return Task.CompletedTask; }
        var skin = Capture.Build(nameText, authorText, descText, chosen, out var warnings);
        Library.Save(skin, preview);
        tab = "mine";
        statusText = $"Скин «{skin.Name}» собран и лежит в «Моих скинах»." + (warnings.Count > 0 ? " Замечания: " + string.Join("; ", warnings) : "");
        statusOk = true;
        Rebuild();
        return Task.CompletedTask;
    }

    // ----- «Мои скины» -----
    void MinePage()
    {
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        top.Children.Add(Btn("Открыть файл скина…", null, ImportSkin, first: true));
        top.Children.Add(Btn("Вернуть как было", "Danger", RevertAll));
        page.Children.Add(top);
        var applied = demo ? null : Applier.AppliedName();
        if (applied != null) page.Children.Add(Note($"Сейчас применён скин «{applied}»."));

        var skins = demo ? demoSkins! : Library.List();
        if (skins.Count == 0) { page.Children.Add(Note("Скинов пока нет. Соберите свой на странице «Собрать скин» или откройте файл .ghostskin.")); return; }
        foreach (var s in skins) page.Children.Add(SkinCard(s));
    }

    UIElement SkinCard(SavedSkin saved)
    {
        var skin = saved.Skin;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var img = Shots.Load(saved.Preview);
        var thumb = new Border { Width = 200, Height = 120, Background = Res("Panel2"), CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Top };
        thumb.Child = img != null ? new Image { Source = img, Stretch = Stretch.UniformToFill } : new TextBlock { Text = "без картинки", Foreground = Res("Muted"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        thumb.ClipToBounds = true;
        grid.Children.Add(thumb);

        var st = new StackPanel();
        Grid.SetColumn(st, 1);
        st.Children.Add(new TextBlock { Text = skin.Name, FontSize = 17, FontWeight = FontWeights.SemiBold });
        if (skin.Author.Length > 0) st.Children.Add(new TextBlock { Text = "автор: " + skin.Author, Foreground = Res("Muted"), FontSize = 12 });
        if (skin.Description.Length > 0) st.Children.Add(new TextBlock { Text = skin.Description, Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        var badges = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var p in skin.PartIds) badges.Children.Add(Badge(SkinSchema.PartNames[p], Res("Accent")));
        st.Children.Add(badges);
        var btns = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        btns.Children.Add(Btn("Применить", "Primary", () => ApplySkin(skin), first: true));
        btns.Children.Add(Btn("Сохранить файл…", null, () => ExportSkin(saved)));
        if (CodeEngineInstalled()) btns.Children.Add(Btn("Настроить кодом", null, () => OpenCode(skin)));
        btns.Children.Add(Btn("Удалить", "Danger", () => DeleteSkin(skin)));
        st.Children.Add(btns);
        grid.Children.Add(st);
        return new Border { Background = Res("Panel"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 12), Child = grid };
    }

    static string? CodeEnginePath()
    {
        var p = Path.Combine(Plugins.ProgramsDir, "CodeEnginePlus", "CodeEnginePlus.exe");
        return File.Exists(p) ? p : null;
    }
    static bool CodeEngineInstalled() => CodeEnginePath() != null;

    Task OpenCode(Skin skin)
    {
        var exe = CodeEnginePath();
        if (exe == null) return Task.CompletedTask;
        Process.Start(new ProcessStartInfo(exe, "--skin=\"" + Path.Combine(Library.Root, skin.Id, "skin.json") + "\"") { UseShellExecute = false });
        return Task.CompletedTask;
    }

    async Task ApplySkin(Skin skin)
    {
        var plan = Applier.Plan(skin);
        var can = plan.Where(p => p.Available).Select(p => p.Part).ToList();
        if (can.Count == 0) { SetStatus("Ни одного из нужных плагинов нет на этом компьютере — установите их в лаунчере (вкладка «Плагины»).", false); return; }
        var lines = string.Join("\n", plan.Select(p => (p.Available ? "• " : "✗ ") + SkinSchema.PartNames[p.Part] + ": " + p.Text));
        var extra = new List<string>();
        if (can.Contains("taskbar") && skin.Part("taskbar") is { } tb && (SkinSchema.Str(tb, "shape") != "none"))
            extra.Add("Форма панели: Taskbar Plus подключит свой движок к Проводнику — так же, как в самом плагине.");
        if (can.Contains("icons")) extra.Add("Значки: Проводник может перезапуститься, Windows может спросить разрешение для значков «для всех».");
        var text = $"Применить скин «{skin.Name}»?\n\n{lines}" + (extra.Count > 0 ? "\n\n" + string.Join("\n", extra) : "") + "\n\nВернуть всё как было можно кнопкой «Вернуть как было».";
        if (MessageBox.Show(this, text, "Применить скин", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        busy = true; SetStatus("Применяю…", true);
        try
        {
            var results = await Applier.Apply(skin, can, p => { Dispatcher.Invoke(() => { statusText = "Применяю: " + p; Rebuild(); }); });
            busy = false;
            var ok = results.All(r => r.Ok);
            SetStatus((ok ? "Готово. " : "Применено не всё. ") + string.Join("; ", results.Select(r => SkinSchema.PartNames[r.Part] + " — " + (r.Ok ? "ок" : r.Message))), ok);
        }
        finally { busy = false; }
    }

    async Task RevertAll()
    {
        if (MessageBox.Show(this, "Вернуть всё как было?\n\nКаждый плагин вернёт то, что менял: курсор, цвета окон, значки, обои и вид панели задач.", "Вернуть как было", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        busy = true; SetStatus("Возвращаю…", true);
        try
        {
            var results = await Applier.RevertAll(null, p => { Dispatcher.Invoke(() => { statusText = "Возвращаю: " + p; Rebuild(); }); });
            busy = false;
            SetStatus(results.Count == 0 ? "Нечего возвращать — нужные плагины не установлены." : string.Join("; ", results.Select(r => SkinSchema.PartNames[r.Part] + " — " + (r.Ok ? "ок" : r.Message))), results.All(r => r.Ok));
        }
        finally { busy = false; }
    }

    Task ImportSkin()
    {
        var dlg = new OpenFileDialog { Title = "Файл скина", Filter = "Скин (*.ghostskin;*.json)|*.ghostskin;*.json" };
        if (dlg.ShowDialog(this) != true) return Task.CompletedTask;
        var warnings = new List<string>();
        var saved = Library.Import(dlg.FileName, warnings);
        saved.Skin.Id = "";
        Library.Save(saved.Skin, saved.Preview);
        SetStatus($"Скин «{saved.Skin.Name}» добавлен." + (warnings.Count > 0 ? " Замечания: " + string.Join("; ", warnings) : ""), true);
        return Task.CompletedTask;
    }

    Task ExportSkin(SavedSkin saved)
    {
        var dlg = new SaveFileDialog { Title = "Сохранить скин", Filter = "Скин (*.ghostskin)|*.ghostskin", FileName = SafeFileName(saved.Skin.Name) + Library.Extension };
        if (dlg.ShowDialog(this) != true) return Task.CompletedTask;
        Library.Export(saved.Skin, saved.Preview, dlg.FileName);
        SetStatus("Файл скина сохранён: " + dlg.FileName, true);
        return Task.CompletedTask;
    }

    static string SafeFileName(string s)
    {
        var bad = Path.GetInvalidFileNameChars();
        var clean = new string(s.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim();
        return clean.Length == 0 ? "skin" : clean;
    }

    Task DeleteSkin(Skin skin)
    {
        if (MessageBox.Show(this, $"Удалить скин «{skin.Name}» из библиотеки?\n\nУже применённые настройки это не отменяет.", "Удаление", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return Task.CompletedTask;
        Library.Delete(skin.Id);
        Rebuild();
        return Task.CompletedTask;
    }

    // Для проверок в CI: рисует страницу окна в PNG без показа
    public void RenderTo(string file, string which)
    {
        tab = which; statusText = ""; Rebuild();
        var sv = (ScrollViewer)Content;
        sv.Content = null;
        System.Windows.Documents.TextElement.SetForeground(page, Foreground);
        System.Windows.Documents.TextElement.SetFontFamily(page, FontFamily);
        System.Windows.Documents.TextElement.SetFontSize(page, FontSize);
        page.Measure(new Size(940, double.PositiveInfinity));
        var w = 940; var h = Math.Max(200, (int)Math.Ceiling(page.DesiredSize.Height));
        page.Arrange(new Rect(0, 0, w, h));
        page.UpdateLayout();
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen()) dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));
        rtb.Render(bg);
        rtb.Render(page);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(file);
        enc.Save(fs);
        sv.Content = page;
    }
}
