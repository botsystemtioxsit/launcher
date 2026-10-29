using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace IconPlusApp;

sealed partial class MainWindow
{
    void BuildState()
    {
        var p = pages[3];
        p.Children.Clear();
        var st = engine.State;
        var rows = new List<UIElement>
        {
            Note(st.Applied.Length == 0 ? "Сейчас плагин ничего не менял — значки как в Windows." : "Применено: " + (st.Applied == "color" ? "перекраска в один цвет" : "набор значков") + "."),
            Note($"Запомнено изменений: в реестре — {st.Reg.Count}, ярлыков — {st.Lnk.Count}. По этому списку «Вернуть как было» возвращает прежнее."),
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        buttons.Children.Add(Btn("Вернуть как было", "Danger", RestoreAll, first: true));
        buttons.Children.Add(Btn("Обновить значки", null, () => { Native.NotifyIconsChanged(); Say("Проводнику сказано обновить значки."); }));
        buttons.Children.Add(Btn("Перезапустить Проводник", null, RestartExplorer));
        rows.Add(buttons);
        p.Children.Add(Card("Что сделано", rows));

        var report = Report.Load();
        var repRows = new List<UIElement>
        {
            Row("Отправлять отчёт сам", "После каждого применения отчёт уходит разработчику без кнопки (названия ярлыков и причины; имя пользователя и компьютера вычищаются)",
                Seg(new[] { "Нет", "Да" }, settings.AutoReport ? 1 : 0, i => { settings.AutoReport = i == 1; settings.Save(); BuildState(); })),
            Note("Здесь видно, какие значки перекрасились, а какие Windows не отдала и почему. Если не перекрасилось то, что должно было, нажмите «Отправить отчёт разработчику» — " +
                 "в отчёт попадут названия ярлыков и причины (имя пользователя и компьютера вычищаются), и это можно будет исправить."),
        };
        if (report.Length > 0)
        {
            repRows.Add(new TextBox
            {
                Text = report, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("Mono"), FontSize = 12,
                MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 4, 0, 8),
            });
            var rb = new StackPanel { Orientation = Orientation.Horizontal };
            rb.Children.Add(Btn("Отправить отчёт разработчику", "Primary", async () => { Say("Отправляю отчёт…"); Say(await Report.UploadAsync(report)); }, first: true));
            rb.Children.Add(Btn("Скопировать отчёт", null, () => { try { Clipboard.SetText(report); Say("Отчёт скопирован."); } catch { Say("Не удалось скопировать."); } }));
            repRows.Add(rb);
        }
        else repRows.Add(Note("Отчёта пока нет — он появится после первого применения."));
        p.Children.Add(Card("Отчёт о последнем применении", repRows));

        if (st.Reg.Count > 0)
        {
            var list = st.Reg.Take(40).Select(c => (UIElement)new TextBlock
            {
                Text = c.Path.Replace(@"Software\Classes\", "").Replace(@"Software\Microsoft\Windows\CurrentVersion\Explorer\", "") + (c.Existed ? "  (было: " + c.Old + ")" : "  (не было)"),
                Foreground = Res("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 3),
            }).ToList();
            if (st.Reg.Count > 40) list.Add(Note($"… и ещё {st.Reg.Count - 40}"));
            p.Children.Add(Card("Изменённые записи", list));
        }

        p.Children.Add(Card("Где хранятся файлы", new UIElement[]
        {
            Note("Наборы и перекрашенные значки: " + Paths.Root),
            Note("Перекрашенные значки — обычные файлы .ico в папке generated. Если удалить папку, не вернув значки как было, Windows покажет пустые значки: сначала нажмите «Вернуть как было»."),
        }));
    }

    // После изменения часть значков (панель слева, «Этот компьютер», диски, папки) обновляется только после перезапуска Проводника.
    void AskExplorerRestart()
    {
        var r = MessageBox.Show(this, "Часть значков (панель слева, «Этот компьютер», диски, папки) обновится только после перезапуска Проводника.\n\n" +
                                      "Перезапустить сейчас? Открытые окна папок на секунду закроются, ваши программы не затрагиваются.",
                                "Icon Plus", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r == MessageBoxResult.Yes) RestartExplorer();
    }

    void RestartExplorer()
    {
        Say("Перезапускаю Проводник…");
        Task.Run(() =>
        {
            try
            {
                foreach (var pr in Process.GetProcessesByName("explorer")) { try { pr.Kill(); } catch { } }
                // «Пуск» помнит значки до перезапуска своей оболочки — она сама запустится снова при открытии меню
                foreach (var pr in Process.GetProcessesByName("StartMenuExperienceHost")) { try { pr.Kill(); } catch { } }
                System.Threading.Thread.Sleep(1200);
                if (Process.GetProcessesByName("explorer").Length == 0) Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            }
            catch { }
        });
    }
}
