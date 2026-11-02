using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BattleLauncher;

record FeedbackSent(string Id, string Type, string Preview, long At);
record FeedbackState(string? Status, string? Reply, long? ReplyAt, bool Deleted);

// «Написать нам»: ошибки и предложения из лаунчера уходят в общую базу (узел launcherFeedback), владелец читает их в админ-панели
// (приложение «Сообщения в лаунчере», только Супердоступ). Скриншоты лежат отдельно (launcherFeedbackShots/<id>/<n>) — список сообщений остаётся лёгким.
// База открыта на запись, поэтому всё, что приходит оттуда обратно (статус, ответ), в окне выводится только как текст.
static class Feedback
{
    public const int MaxText = 4000, MaxShots = 3, MinText = 10, MaxContact = 100;
    const int MaxDataUrl = 380_000;                    // ≈ 280 КБ картинки в base64
    static readonly string[] Types = { "bug", "idea", "other" };
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(20);

    static string SentFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BattleLauncher", "feedback-sent.json");

    public static string TypeLabel(string t) => t switch { "bug" => "Ошибка", "idea" => "Предложение", _ => "Другое" };
    public static string StatusLabel(FeedbackState s) => s.Deleted ? "Удалено" : s.Status switch { "work" => "В работе", "done" => "Готово", "rejected" => "Отклонено", _ => "Отправлено" };

    // ---------- список отправленных (только номера и начало текста — для показа статуса и ответа) ----------
    public static List<FeedbackSent> LoadSent()
    {
        try
        {
            if (!File.Exists(SentFile)) return new();
            var list = JsonSerializer.Deserialize<List<FeedbackSent>>(File.ReadAllText(SentFile)) ?? new();
            return list.Where(x => Net.SafeId.IsMatch(x.Id)).Take(30).ToList();
        }
        catch { return new(); }
    }

    static void SaveSent(List<FeedbackSent> list)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SentFile)!);
            File.WriteAllText(SentFile, JsonSerializer.Serialize(list.Take(30).ToList()));
        }
        catch { /* список — удобство, без него всё работает */ }
    }

    // ---------- сведения о системе: то, что показывается человеку перед отправкой и уходит вместе с сообщением ----------
    public static string SystemInfo()
    {
        var v = Environment.OSVersion.Version;
        var sb = new StringBuilder();
        sb.Append(v.Build >= 22000 ? "Windows 11" : "Windows 10").Append(" (сборка ").Append(v.Build).Append("), ")
          .Append(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit").Append("; лаунчер ").Append(SelfUpdate.CurrentVersion);
        var parts = new List<string>();
        foreach (var def in Catalog.Plugins)
        {
            try { var n = NativeInstaller.Find(def); if (n != null) parts.Add(def.Name + " " + n.Version); } catch { }
        }
        foreach (var def in Catalog.BrowserPlugins)
        {
            try { var b = BrowserInstaller.Find(def); if (b != null) parts.Add(def.Name + " (браузер) " + b.Version); } catch { }
        }
        if (parts.Count > 0) sb.Append("; плагины: ").Append(string.Join(", ", parts));
        var s = sb.ToString();
        return s.Length > 480 ? s[..480] : s;
    }

    // ---------- картинки: любой снимок сжимается в JPEG так, чтобы поместиться в запись базы ----------
    public static string? ToJpegDataUrl(BitmapSource src)
    {
        // сначала в непрозрачный формат: у PNG бывает прозрачность, которую JPEG не умеет
        BitmapSource flat = new FormatConvertedBitmap(src, PixelFormats.Bgr24, null, 0);
        foreach (var (side, quality) in new[] { (1600, 78), (1400, 68), (1200, 58), (1000, 48), (800, 40) })
        {
            var k = Math.Min(1.0, (double)side / Math.Max(flat.PixelWidth, flat.PixelHeight));
            BitmapSource scaled = k < 1 ? new TransformedBitmap(flat, new ScaleTransform(k, k)) : flat;
            var enc = new JpegBitmapEncoder { QualityLevel = quality };
            enc.Frames.Add(BitmapFrame.Create(scaled));
            using var ms = new MemoryStream();
            enc.Save(ms);
            var url = "data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray());
            if (url.Length <= MaxDataUrl) return url;
        }
        return null;
    }

    public static BitmapSource LoadImage(string file)
    {
        var info = new FileInfo(file);
        if (!info.Exists || info.Length > 25L * 1024 * 1024) throw new InvalidDataException("Файл слишком большой (больше 25 МБ)");
        using var fs = File.OpenRead(file);
        var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var f = dec.Frames[0];
        f.Freeze();
        return f;
    }

    public static BitmapSource Thumb(BitmapSource src)
    {
        var k = Math.Min(1.0, 160.0 / Math.Max(src.PixelWidth, src.PixelHeight));
        BitmapSource t = k < 1 ? new TransformedBitmap(src, new ScaleTransform(k, k)) : src;
        var c = new WriteableBitmap(t);
        c.Freeze();
        return c;
    }

    // ---------- отправка ----------
    // 1) запись с текстом → 2) скриншоты → 3) число скриншотов и отметка «новое». Если скриншот не загрузился, сообщение всё равно дойдёт.
    public static async Task<string> Send(string type, string text, string? contact, Account? account, bool withSystem, IReadOnlyList<string> shots, CancellationToken ct = default)
    {
        if (Array.IndexOf(Types, type) < 0) throw new ArgumentException("Неизвестный тип сообщения");
        text = text.Trim();
        if (text.Length < MinText) throw new ArgumentException("Напишите хотя бы пару слов");
        if (text.Length > MaxText) text = text[..MaxText];
        var rec = new Dictionary<string, object?>
        {
            ["type"] = type,
            ["text"] = text,
            ["createdAt"] = new Dictionary<string, string> { [".sv"] = "timestamp" },   // время ставит сервер
            ["status"] = "new",
            ["shots"] = 0,
        };
        if (!string.IsNullOrWhiteSpace(contact)) rec["contact"] = contact.Trim()[..Math.Min(contact.Trim().Length, MaxContact)];
        if (withSystem) rec["system"] = SystemInfo();
        if (account != null && !account.IsToken && Net.SafeId.IsMatch(account.Id))
            rec["account"] = new Dictionary<string, string> { ["id"] = account.Id, ["name"] = account.Name.Length > 64 ? account.Name[..64] : account.Name, ["playerId"] = account.PlayerId };
        var id = await Rtdb.PostJson("launcherFeedback", JsonSerializer.Serialize(rec), ct);
        var sent = 0;
        for (var i = 0; i < shots.Count && i < MaxShots; i++)
        {
            try { await Rtdb.PutJson($"launcherFeedbackShots/{id}/{i}", JsonSerializer.Serialize(shots[i]), ct); sent++; }
            catch { break; }
        }
        if (sent > 0) await Rtdb.PatchJson("launcherFeedback/" + id, "{\"shots\":" + sent + "}", ct);
        await Rtdb.PutJson("launcherFeedbackNew/" + id, "true", ct);

        var list = LoadSent();
        list.Insert(0, new FeedbackSent(id, type, text.Length > 90 ? text[..90] + "…" : text, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        if (App.ScreenshotDir == null) SaveSent(list);
        return id;
    }

    // Статус и ответ на уже отправленное сообщение (читается по точному номеру)
    public static async Task<FeedbackState> GetState(string id, CancellationToken ct = default)
    {
        var text = await Rtdb.GetJson("launcherFeedback/" + id, ct);
        using var doc = JsonDocument.Parse(text);
        var r = doc.RootElement;
        if (r.ValueKind != JsonValueKind.Object) return new FeedbackState(null, null, null, true);
        string? Str(string k, int max) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() is { } s && s.Length > max ? s[..max] : v.GetString()) : null;
        long? replyAt = r.TryGetProperty("replyAt", out var ra) && ra.ValueKind == JsonValueKind.Number && ra.TryGetInt64(out var l) ? l : null;
        return new FeedbackState(Str("status", 20), Str("reply", 2000), replyAt, false);
    }
}

// Снимок всего экрана (все мониторы) средствами GDI — без внешних библиотек
static class ScreenGrab
{
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);

    public static BitmapSource Capture()
    {
        int x = GetSystemMetrics(76), y = GetSystemMetrics(77), w = GetSystemMetrics(78), h = GetSystemMetrics(79); // виртуальный экран
        if (w <= 0 || h <= 0) { x = 0; y = 0; w = GetSystemMetrics(0); h = GetSystemMetrics(1); }
        var screen = GetDC(IntPtr.Zero);
        var mem = CreateCompatibleDC(screen);
        var bmp = CreateCompatibleBitmap(screen, w, h);
        var old = SelectObject(mem, bmp);
        try
        {
            if (!BitBlt(mem, 0, 0, w, h, screen, x, y, 0x00CC0020 | 0x40000000)) throw new InvalidOperationException("Не удалось снять экран");   // SRCCOPY | CAPTUREBLT
            var src = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally
        {
            SelectObject(mem, old);
            DeleteObject(bmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
