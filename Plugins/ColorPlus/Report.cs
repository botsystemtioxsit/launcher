using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace ColorPlusApp;

// Отчёт о состоянии для разбора (владелец не нажимает кнопки ради отчётов, поэтому он уходит сам): настройки плагина, соседние плагины,
// цвета Windows, окно панели задач и цвета пикселей вдоль панели — по ним видно, чем именно залита панель. Имя пользователя и компьютера вычищаются.
static class Report
{
    const string ReportUrl = "https://zolotaya-kletka-default-rtdb.firebaseio.com/pluginReports/color-plus/";
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BattleLauncher");
    static DateTime lastSent = DateTime.MinValue;

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc, int x, int y);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);

    static string ReadFile(string name, int max = 4000)
    {
        try
        {
            var path = Path.Combine(Dir, name);
            if (!File.Exists(path)) return "(нет файла)";
            var t = File.ReadAllText(path);
            return t.Length > max ? t[..max] + "…" : t;
        }
        catch (Exception ex) { return "(не прочитан: " + ex.Message + ")"; }
    }

    static string Reg(string path, string name)
    {
        try { using var k = Registry.CurrentUser.OpenSubKey(path); var v = k?.GetValue(name); return v == null ? "—" : v is int i ? "0x" + i.ToString("X8") : v.ToString() ?? "—"; }
        catch { return "?"; }
    }

    static string Procs(params string[] names)
    {
        var sb = new StringBuilder();
        foreach (var n in names)
        {
            try
            {
                var list = Process.GetProcessesByName(n);
                if (list.Length == 0) continue;
                sb.Append(n).Append(':').Append(list.Length);
                if (n == "explorer") { try { sb.Append(" запущен ").Append(list[0].StartTime.ToString("HH:mm:ss")); } catch { } }
                sb.Append("; ");
            }
            catch { }
        }
        return sb.Length == 0 ? "—" : sb.ToString();
    }

    // Цвета нескольких точек вдоль панели задач (там, где у обычной панели фон, и в местах между островами)
    static string TaskbarPixels()
    {
        try
        {
            var w = GetSystemMetrics(0); var h = GetSystemMetrics(1);
            var dc = GetDC(IntPtr.Zero);
            try
            {
                var sb = new StringBuilder();
                foreach (var y in new[] { h - 4, h - 24, h - 44 })
                {
                    sb.Append("y=").Append(y).Append(": ");
                    foreach (var f in new[] { 0.005, 0.03, 0.08, 0.15, 0.25, 0.4, 0.5, 0.6, 0.75, 0.85, 0.92, 0.97, 0.995 })
                    {
                        var c = GetPixel(dc, (int)(w * f), y);
                        sb.Append($"#{c & 0xFF:X2}{(c >> 8) & 0xFF:X2}{(c >> 16) & 0xFF:X2} ");
                    }
                    sb.AppendLine();
                }
                return sb.ToString();
            }
            finally { ReleaseDC(IntPtr.Zero, dc); }
        }
        catch (Exception ex) { return "(пиксели не прочитаны: " + ex.Message + ")"; }
    }

    static string TrayWindow()
    {
        try
        {
            var h = Native.FindWindow("Shell_TrayWnd", null);
            if (h == IntPtr.Zero) return "панели нет";
            Native.GetWindowRect(h, out var r);
            return $"handle={h} rect={r.Left},{r.Top},{r.Right},{r.Bottom} ex=0x{Native.GetLong(h, Native.GWL_EXSTYLE):X}";
        }
        catch (Exception ex) { return "(ошибка: " + ex.Message + ")"; }
    }

    public static string Build(ColorSettings s, string reason, string effectsStatus)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Color Plus — отчёт");
        sb.AppendLine("Время: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "   повод: " + reason);
        sb.AppendLine("Плагин: " + (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?") + "   Windows: " + Environment.OSVersion.Version);
        sb.AppendLine("Состояние: " + effectsStatus);
        sb.AppendLine();
        sb.AppendLine("--- настройки Color Plus ---");
        try { sb.AppendLine(JsonSerializer.Serialize(s)); } catch { }
        sb.AppendLine("--- связь с Taskbar Plus (color-plus-taskbar.json) ---");
        sb.AppendLine(ReadFile("color-plus-taskbar.json"));
        sb.AppendLine("--- настройки Taskbar Plus ---");
        sb.AppendLine(ReadFile("taskbar-plus-settings.json"));
        sb.AppendLine("--- движок Taskbar Plus ---");
        sb.AppendLine(ReadFile("taskbar-plus-engine-status.txt", 1500));
        sb.AppendLine("острова включены (по мнению Color Plus): " + TaskbarLink.IslandsActive());
        sb.AppendLine();
        sb.AppendLine("--- процессы ---");
        sb.AppendLine(Procs("explorer", "TaskbarPlus", "IconPlus", "ColorPlus", "BattleLauncher", "wallpaper32", "wallpaper64", "lively", "rainmeter", "TranslucentTB"));
        sb.AppendLine("--- цвета Windows ---");
        const string pers = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        const string acc = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
        const string dwm = @"Software\Microsoft\Windows\DWM";
        sb.AppendLine($"ColorPrevalence={Reg(pers, "ColorPrevalence")} SystemUsesLightTheme={Reg(pers, "SystemUsesLightTheme")} AppsUseLightTheme={Reg(pers, "AppsUseLightTheme")} EnableTransparency={Reg(pers, "EnableTransparency")}");
        sb.AppendLine($"AccentColorMenu={Reg(acc, "AccentColorMenu")} StartColorMenu={Reg(acc, "StartColorMenu")} AutoColorization={Reg(@"Control Panel\Desktop", "AutoColorization")}");
        sb.AppendLine($"DWM AccentColor={Reg(dwm, "AccentColor")} ColorizationColor={Reg(dwm, "ColorizationColor")} ColorPrevalence={Reg(dwm, "ColorPrevalence")}");
        sb.AppendLine("--- что запомнено для возврата (color-plus-originals.json) ---");
        sb.AppendLine(ReadFile("color-plus-originals.json", 1500));
        sb.AppendLine("--- окно панели задач ---");
        sb.AppendLine(TrayWindow());
        sb.AppendLine("--- цвета пикселей вдоль нижнего края экрана ---");
        sb.Append(TaskbarPixels());
        return sb.ToString();
    }

    static string Sanitize(string text)
    {
        foreach (var (what, with) in new[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%"),
            (Environment.UserName, "user"),
            (Environment.MachineName, "pc"),
        })
            if (what.Length >= 3) text = text.Replace(what, with, StringComparison.OrdinalIgnoreCase);
        return text;
    }

    // Отправка в общую базу проекта (узел pluginReports); не чаще раза в 30 секунд. Возвращает null, если отправилось, иначе текст ошибки.
    public static async Task<string?> UploadAsync(ColorSettings s, string reason, string effectsStatus)
    {
        try
        {
            if (DateTime.UtcNow - lastSent < TimeSpan.FromSeconds(30)) return "слишком часто";
            lastSent = DateTime.UtcNow;
            var text = Sanitize(Build(s, reason, effectsStatus));
            if (text.Length > 60_000) text = text[..60_000] + "\n…(обрезано)";
            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.MachineName + "|" + Environment.UserName)))[..8].ToLowerInvariant();
            var body = JsonSerializer.Serialize(new { at = DateTime.UtcNow.ToString("o"), plugin = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?", windows = Environment.OSVersion.Version.ToString(), report = text });
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            using var resp = await http.PutAsync(ReportUrl + id + ".json", new StringContent(body, Encoding.UTF8, "application/json"));
            return resp.IsSuccessStatusCode ? null : "сервер ответил " + (int)resp.StatusCode;
        }
        catch (Exception ex) { return ex.Message; }
    }
}
