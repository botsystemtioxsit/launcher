using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IconPlusApp;

// Отчёт о последнем применении: что перекрасилось, а что нет и почему. Нужен, чтобы видеть, какие значки Windows не отдаёт, и подстраиваться.
static class Report
{
    static string File_ => Path.Combine(Paths.Root, "last-run.txt");

    public static string Summary(ApplyResult r)
    {
        var bad = r.Outcomes.Where(o => !o.Ok).ToList();
        var s = $"Готово: значков изменено {r.Outcomes.Count(o => o.Ok)}";
        if (bad.Count > 0)
        {
            s += $", не получилось {bad.Count}";
            var top = bad.GroupBy(o => o.Result).OrderByDescending(g => g.Count()).Take(2).Select(g => $"{g.Key} ({g.Count()})");
            s += " — " + string.Join("; ", top) + ". Подробности — на вкладке «Состояние».";
        }
        return s;
    }

    public static string Build(ApplyResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Icon Plus {typeof(Report).Assembly.GetName().Version} · Windows {Environment.OSVersion.Version} · {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"Изменено: {r.Outcomes.Count(o => o.Ok)}, не получилось: {r.Outcomes.Count(o => !o.Ok)}");
        foreach (var g in r.Outcomes.GroupBy(o => o.Group))
        {
            var ok = g.Count(o => o.Ok);
            var bad = g.Where(o => !o.Ok).ToList();
            sb.AppendLine();
            sb.AppendLine($"[{g.Key}] изменено {ok}" + (bad.Count > 0 ? $", не получилось {bad.Count}" : ""));
            foreach (var why in bad.GroupBy(o => o.Result).OrderByDescending(x => x.Count()))
            {
                var names = why.Select(o => o.Item).Take(12).ToList();
                sb.AppendLine($"  {why.Key} ({why.Count()}): " + string.Join(", ", names) + (why.Count() > names.Count ? ", …" : ""));
            }
        }
        if (r.Aumids.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"[Ярлыки с идентификатором программы (AUMID): {r.Aumids.Count}] — имя | идентификатор | результат");
            var res = r.Outcomes.GroupBy(o => o.Item).ToDictionary(g => g.Key, g => g.First().Result);
            foreach (var kv in r.Aumids.Take(150)) sb.AppendLine($"  {kv.Key} | {kv.Value} | {(res.TryGetValue(kv.Key, out var v) ? v : "?")}");
        }
        foreach (var m in r.Messages.Take(20)) sb.AppendLine("• " + m);
        return sb.ToString();
    }

    public static void Save(string text)
    {
        try { Paths.Ensure(); System.IO.File.WriteAllText(File_, text); } catch { }
    }

    public static string Load()
    {
        try { return System.IO.File.Exists(File_) ? System.IO.File.ReadAllText(File_) : ""; } catch { return ""; }
    }

    // Отправка в общую базу (как у Taskbar Plus): имя пользователя и компьютера вычищаются; нужны названия ярлыков и причины — по ним видно, что чинить.
    public static async Task<string> UploadAsync(string text)
    {
        try
        {
            var user = Environment.UserName; var pc = Environment.MachineName;
            var clean = text.Replace(user, "<user>", StringComparison.OrdinalIgnoreCase).Replace(pc, "<pc>", StringComparison.OrdinalIgnoreCase);
            if (clean.Length > 90000) clean = clean[..90000];
            var id = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(pc + "|" + user))).ToLowerInvariant()[..12];
            var body = JsonSerializer.Serialize(new { at = DateTime.UtcNow.ToString("o"), report = clean });
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var resp = await http.PutAsync($"https://zolotaya-kletka-default-rtdb.firebaseio.com/pluginReports/icon-plus/{id}.json", new StringContent(body, Encoding.UTF8, "application/json"));
            return resp.IsSuccessStatusCode ? "Отчёт отправлен." : "Отчёт не отправился: " + (int)resp.StatusCode;
        }
        catch (Exception ex) { return "Отчёт не отправился: " + ex.Message; }
    }
}
