using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace PcStats;

/// <summary>
/// Установка и обновление приложения Lock Screen Plus на телефоне по кабелю (отладка по USB, adb).
/// Всё через обычные окна «Да / Нет» — мышкой.
/// </summary>
internal static class Updater
{
    private const string Package = "com.tioxsit.lockscreenplus";
    private const string Repo = "botsystemtioxsit/launcher";
    private const string Tag = "lockscreen-plus-latest";

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private static bool Ask(string text)
    {
        // MB_YESNO | MB_ICONQUESTION | MB_TOPMOST
        return MessageBoxW(IntPtr.Zero, text, "Lock Screen PC Stats", 0x4 | 0x20 | 0x40000) == 6;
    }

    private static void Info(string text)
    {
        MessageBoxW(IntPtr.Zero, text, "Lock Screen PC Stats", 0x0 | 0x40 | 0x40000);
    }

    /// <summary>Ждёт телефон по кабелю с «Отладкой по USB», потом один раз предлагает поставить или обновить приложение.</summary>
    public static void RunOnce(Adb adb)
    {
        try
        {
            string? exe = null;
            for (int i = 0; i < 40; i++)
            {
                exe = adb.Ensure();
                if (exe != null)
                {
                    var devs = DeviceLines(adb);
                    if (devs.Any(l => l.EndsWith("device", StringComparison.Ordinal)))
                    {
                        break;
                    }
                }
                Thread.Sleep(3000);
                exe = null;
            }
            if (exe == null)
            {
                return;
            }
            string installed = InstalledVersion(adb);
            string latest = LatestVersion();
            if (latest.Length == 0)
            {
                return;
            }
            if (installed.Length > 0 && CompareVersions(installed, latest) >= 0)
            {
                return;
            }
            string q = installed.Length == 0
                ? $"На телефоне нет приложения Lock Screen Plus.\n\nУстановить версию {latest} по кабелю?"
                : $"Вышла новая версия Lock Screen Plus: {latest} (у вас {installed}).\n\nОбновить на телефоне по кабелю?";
            if (!Ask(q))
            {
                return;
            }
            string apk = Download();
            string res = Install(adb, apk);
            if (res.Contains("INSTALL_FAILED_UPDATE_INCOMPATIBLE") || res.Contains("INSTALL_FAILED_VERSION_DOWNGRADE") || res.Contains("signatures do not match"))
            {
                if (Ask("Подпись приложения на телефоне другая (старая сборка), поверх оно не ставится.\n\nУдалить старое приложение и поставить новое? Его настройки на телефоне пропадут."))
                {
                    adb.Run("uninstall " + Package);
                    res = Install(adb, apk);
                }
                else
                {
                    return;
                }
            }
            if (res.Contains("Success"))
            {
                adb.Run($"shell am start -n {Package}/com.lockscreen.customplus.MainActivity");
                Info($"Готово: на телефоне Lock Screen Plus {latest}.");
            }
            else
            {
                Info("Не получилось поставить приложение на телефон:\n\n" + res.Trim());
            }
        }
        catch (Exception e)
        {
            Info("Обновление приложения не удалось: " + e.Message);
        }
    }

    private static List<string> DeviceLines(Adb adb)
    {
        return adb.Run("devices").Split('\n').Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("List of") && !l.StartsWith("*")).ToList();
    }

    private static string InstalledVersion(Adb adb)
    {
        string o = adb.Run("shell dumpsys package " + Package);
        var m = Regex.Match(o, @"versionName=([0-9][0-9.]*)");
        return m.Success ? m.Groups[1].Value : "";
    }

    private static string LatestVersion()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        string g = http.GetStringAsync($"https://raw.githubusercontent.com/{Repo}/main/LockScreenPlus/app/build.gradle").GetAwaiter().GetResult();
        var m = Regex.Match(g, "versionName \"([0-9][0-9.]*)\"");
        return m.Success ? m.Groups[1].Value : "";
    }

    private static int CompareVersions(string a, string b)
    {
        int[] pa = a.Split('.').Select(x => int.TryParse(x, out var n) ? n : 0).ToArray();
        int[] pb = b.Split('.').Select(x => int.TryParse(x, out var n) ? n : 0).ToArray();
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            int x = i < pa.Length ? pa[i] : 0;
            int y = i < pb.Length ? pb[i] : 0;
            if (x != y)
            {
                return x.CompareTo(y);
            }
        }
        return 0;
    }

    private static string Download()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LockScreenPcStats");
        Directory.CreateDirectory(dir);
        string apk = Path.Combine(dir, "LockScreenPlus.apk");
        string baseUrl = $"https://github.com/{Repo}/releases/download/{Tag}/";
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        string want = http.GetStringAsync(baseUrl + "LockScreenPlus.apk.sha256").GetAwaiter().GetResult().Trim().ToLowerInvariant();
        if (!Regex.IsMatch(want, "^[0-9a-f]{64}$"))
        {
            throw new InvalidOperationException("не удалось получить контрольную сумму приложения");
        }
        byte[] bytes = http.GetByteArrayAsync(baseUrl + "LockScreenPlus.apk").GetAwaiter().GetResult();
        string got = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (got != want)
        {
            throw new InvalidOperationException("скачанный файл не совпал с контрольной суммой — установку отменил");
        }
        File.WriteAllBytes(apk, bytes);
        return apk;
    }

    private static string Install(Adb adb, string apk)
    {
        return adb.Run($"install -r \"{apk}\"", 240000);
    }
}
