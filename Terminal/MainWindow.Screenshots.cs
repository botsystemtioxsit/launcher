using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BattleLauncher;

namespace TioxsitTerminal;

// Служебный режим --screenshot: настоящие окна Терминала с выдуманными данными (сети нет) сохраняются в PNG.
public partial class MainWindow
{
    async Task RunScreenshots(string dir)
    {
        Directory.CreateDirectory(dir);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var me = new Account { Id = "500600700", Name = "Alex", Role = "superadmin", PlayerId = "PLR-ADM001" };
        session = new Session(me, "demo-device", true);

        await Capture(Path.Combine(dir, "terminal-1-login.png"), () => ShowSetup("Вход не найден. Войди в TIOXSIT Launcher или подтверди это устройство кодом ниже.", null));

        BuildShell();
        await Capture(Path.Combine(dir, "terminal-2-players.png"), () =>
        {
            Highlight("players");
            var (root, body) = Ui.Page("Игроки", "Найти по игровому коду (PLR-…), @нику или Telegram-id");
            body.Children.Add(PlayerCard(new PlayerInfo("30001", "PLR-FX15QZ", "MegaTroll", "@megatroll", "user", 1540, 130, "restricted", now + 3 * 3600000L, now - 40 * 86400000L, 50, 25, 24, 1, 8, 90)));
            Show(root);
        });

        await Capture(Path.Combine(dir, "terminal-3-journal.png"), () =>
        {
            Highlight("journal");
            var reports = new List<ReportEntry>
            {
                new("r1", now - 12 * 60000L, "discrimination", "PLR-AX10QZ", "PLR-FX15QZ", "MegaTroll", "K7Q2MX", false),
                new("r2", now - 95 * 60000L, "discrimination", "PLR-BX11QZ", "PLR-GX16QZ", "Nikita_77", "P9ZL4A", false),
                new("r3", now - 26 * 3600000L, "discrimination", "PLR-CX12QZ", "PLR-FX15QZ", "MegaTroll", "T3VB8N", true),
            };
            var logs = new List<LogEntry>
            {
                new(now - 6 * 60000L, "ban_temp", "Alex (Терминал)", "500600700", "MegaTroll", "PLR-FX15QZ", "Ограничение на 7 дней"),
                new(now - 55 * 60000L, "role_changed", "Alex", "500600700", "Katya_Burn", "PLR-DX13QZ", "Роль: игрок → админ"),
                new(now - 70 * 60000L, "report_closed", "Alex (Терминал)", "500600700", "MegaTroll", "PLR-FX15QZ", "Жалоба закрыта: Дискриминация"),
            };
            var errors = new List<ErrorEntry> { new("e1", now - 3 * 3600000L, "Nikita_77 (PLR-GX16QZ)", "[22:11:05] Голос (LiveKit): подключение прервано", "Chrome / Windows") };
            Show(JournalPage(reports, logs, errors));
        });

        await Capture(Path.Combine(dir, "terminal-4-stats.png"), () =>
        {
            Highlight("stats");
            Show(StatsPage(new StatsData(1284, 37, 12, 4310, 9, 2, 3, 1, 4, 1)));
        });

        await Capture(Path.Combine(dir, "terminal-5-staff.png"), () =>
        {
            Highlight("staff");
            var staff = new List<StaffEntry>
            {
                new("500600700", "PLR-ADM001", "Alex", "superadmin", ""),
                new("500600701", "PLR-DX13QZ", "Katya_Burn", "admin", "bans, clans, app_troll"),
            };
            var tokens = new List<TokenEntry> { new("t1", "Проверка Microsoft Store", now + 5 * 86400000L, now - 86400000L, "app_troll, app_videochat") };
            Show(StaffPage(staff, tokens, null));
        });

        await Capture(Path.Combine(dir, "terminal-6-system.png"), () =>
        {
            Highlight("system");
            Show(SystemPage(false, "", false, ""));
        });

        Application.Current.Shutdown();
    }

    async Task Capture(string path, Action prepare)
    {
        prepare();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        await Task.Delay(500);
        UpdateLayout();
        var content = (FrameworkElement)Content;
        double w = content.ActualWidth, h = content.ActualHeight;
        const double k = 2; // удвоенное разрешение: картинка чёткая и без растяжения
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(content) { Viewbox = new Rect(0, 0, w, h), ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill }, null, new Rect(0, 0, w, h));
        }
        var bmp = new RenderTargetBitmap((int)(w * k), (int)(h * k), 96 * k, 96 * k, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        await using var fs = File.Create(path);
        enc.Save(fs);
    }
}
