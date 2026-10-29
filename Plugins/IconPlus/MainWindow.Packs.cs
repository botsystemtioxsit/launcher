using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace IconPlusApp;

sealed partial class MainWindow
{
    IconPack? selectedPack;
    Dictionary<string, string> packMap = new();
    readonly TextBox passwordBox = new() { Width = 170, Padding = new Thickness(8, 5, 8, 5) };

    void BuildPacks()
    {
        var p = pages[1];
        p.Children.Clear();

        var add = new List<UIElement>
        {
            Note("Готовые наборы значков (папки, диски, корзина, типы файлов) можно скачать на сайте с иконками — вкладка «Сайт с иконками»: нажали «Скачать», плагин сам добавит набор. " +
                 "Или добавьте набор, который у вас уже есть: архив (zip, 7z, rar) либо папку с файлами .ico или .png. Из архива берутся только значки, ничего не запускается."),
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        buttons.Children.Add(Btn("Добавить архив или значок…", "Primary", PickArchive, first: true));
        buttons.Children.Add(Btn("Добавить папку…", null, PickFolder));
        add.Add(buttons);
        var pw = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        pw.Children.Add(new TextBlock { Text = "Пароль архива (если он есть):", Foreground = Res("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        pw.Children.Add(passwordBox);
        add.Add(pw);
        p.Children.Add(Card("Добавить набор", add));

        var packs = Packs.List();
        if (packs.Count == 0)
            p.Children.Add(Card("Мои наборы", new UIElement[] { Note("Пока наборов нет. Скачайте набор на вкладке «Сайт с иконками» или добавьте свой выше.") }));
        foreach (var pack in packs) p.Children.Add(PackCard(pack));

        if (selectedPack != null && packs.Any(x => x.Name == selectedPack.Name)) p.Children.Add(MappingCard(selectedPack));
    }

    UIElement PackCard(IconPack pack)
    {
        var rows = new List<UIElement>();
        var thumbs = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var f in pack.Icons.Take(14)) thumbs.Children.Add(Thumb(IconData.Load(f, 32), 36));
        rows.Add(thumbs);
        rows.Add(Note($"Значков в наборе: {pack.Icons.Count}"));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Btn("Настроить", null, () => { SelectPack(pack); }, first: true));
        buttons.Children.Add(Btn("Применить набор", "Primary", () => { SelectPack(pack); ApplyPack(); }));
        buttons.Children.Add(Btn("Удалить набор", "Danger", () => { DeletePack(pack); }));
        rows.Add(buttons);
        return Card(pack.Name, rows);
    }

    void SelectPack(IconPack pack)
    {
        selectedPack = pack;
        packMap = settings.Maps.TryGetValue(pack.Name, out var saved)
            ? saved.Where(kv => File.Exists(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value)
            : Packs.Suggest(pack);
        BuildPacks();
    }

    UIElement MappingCard(IconPack pack)
    {
        var rows = new List<UIElement>
        {
            Note("Программа сама подобрала значки из набора по названиям файлов. Если что-то не так или значка не нашлось — нажмите «Выбрать…» у нужной строки. " +
                 "Строки без значка остаются как в Windows."),
        };
        foreach (var t in Target.All.Where(x => x.Keywords.Length > 0))
        {
            var id = t.Id;
            var cell = new StackPanel { Orientation = Orientation.Horizontal };
            packMap.TryGetValue(id, out var file);
            cell.Children.Add(Thumb(file != null ? IconData.Load(file, 32) : null, 32));
            cell.Children.Add(new TextBlock { Text = file != null ? Path.GetFileName(file) : "— нет —", Foreground = Res("Muted"), Width = 170, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            cell.Children.Add(Btn("Выбрать…", null, () =>
            {
                var chosen = PickIcon(pack);
                if (chosen == null) return;
                packMap[id] = chosen; settings.Maps[pack.Name] = new Dictionary<string, string>(packMap); settings.Save(); BuildPacks();
            }, first: true));
            cell.Children.Add(Btn("Убрать", "Ghost", () => { packMap.Remove(id); settings.Maps[pack.Name] = new Dictionary<string, string>(packMap); settings.Save(); BuildPacks(); }));
            rows.Add(Row(t.Title, "", cell));
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(Btn("Применить набор", "Primary", ApplyPack, first: true));
        buttons.Children.Add(Btn("Подобрать заново", null, () => { packMap = Packs.Suggest(pack); settings.Maps.Remove(pack.Name); BuildPacks(); }));
        rows.Add(buttons);
        return Card("Какой значок куда — «" + pack.Name + "»", rows);
    }

    string? PickIcon(IconPack pack)
    {
        var w = new Window { Title = "Выберите значок", Owner = this, Width = 720, Height = 520, Background = Res("Bg"), Foreground = Res("Text"), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var wrap = new WrapPanel { Margin = new Thickness(14) };
        string? result = null;
        foreach (var f in pack.Icons.Take(400))
        {
            var file = f;
            var b = new Button { Content = Thumb(IconData.Load(file, 32), 40), ToolTip = Path.GetFileName(file), Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(8, 8, 0, 8) };
            b.Click += (_, _) => { result = file; w.DialogResult = true; };
            wrap.Children.Add(b);
        }
        w.Content = new ScrollViewer { Content = wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        return w.ShowDialog() == true ? result : null;
    }

    async void ApplyPack()
    {
        if (busy || selectedPack == null) return;
        busy = true;
        Say("Ставлю значки из набора…");
        try
        {
            var map = new Dictionary<string, string>(packMap);
            var result = await Task.Run(() => engine.ApplyPack(map, t => Dispatcher.BeginInvoke(new Action(() => Say("Ставлю: " + t)))));
            Report.Save(Report.Build(result));
            Say(Report.Summary(result));
            if (result.Outcomes.Any(o => o.Ok)) { busy = false; BuildState(); AskExplorerRestart(); }
        }
        catch (Exception ex) { Say("Не вышло: " + ex.Message); }
        finally { busy = false; BuildState(); }
    }

    void DeletePack(IconPack pack)
    {
        if (MessageBox.Show(this, $"Удалить набор «{pack.Name}»? Если он сейчас применён, сначала верните значки как было.", "Icon Plus", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { Packs.Delete(pack.Name); settings.Maps.Remove(pack.Name); settings.Save(); if (selectedPack?.Name == pack.Name) selectedPack = null; }
        catch (Exception ex) { Say("Не удалось удалить: " + ex.Message); }
        BuildPacks();
    }

    void PickArchive()
    {
        var dlg = new OpenFileDialog { Title = "Архив с набором значков", Filter = "Архивы и значки|*.zip;*.7z;*.rar;*.tar;*.gz;*.ico;*.png|Все файлы|*.*" };
        if (dlg.ShowDialog(this) == true) ImportPath(dlg.FileName);
    }

    void PickFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Папка со значками" };
        if (dlg.ShowDialog(this) == true) ImportPath(dlg.FolderName);
    }

    public async void ImportPath(string path)
    {
        Say("Добавляю набор…");
        var pw = passwordBox.Text;
        try
        {
            var name = await Task.Run(() => Packs.Import(path, pw));
            Say($"Набор «{name}» добавлен.");
            // скачанное в плагин (папка «downloads») после добавления больше не нужно
            try
            {
                if (path.StartsWith(Paths.Downloads, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                {
                    File.Delete(path);
                    var d = Path.GetDirectoryName(path);
                    if (d != null && d.StartsWith(Paths.Downloads, StringComparison.OrdinalIgnoreCase) && !Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);
                }
            }
            catch { }
            selectedPack = Packs.List().FirstOrDefault(x => x.Name == name);
            if (selectedPack != null) packMap = Packs.Suggest(selectedPack);
            BuildPacks();
            ShowPage(1);
        }
        catch (Exception ex) { Say("Набор не добавлен: " + ex.Message); }
    }
}
