using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace GatewayPlusApp;

// Отправка сведений для поддержки в общую базу проекта (узел pluginReports/gateway-plus/<номер компьютера>), как у Taskbar Plus и Icon Plus:
// оттуда их забирает служебный процесс GitHub (sync-plugin-reports.yml) в admin-logs/plugin-reports.json.
// В отчёте нет ключей, паролей и полного адреса сервера (их не кладёт BuildDiagnostics); имя пользователя и компьютера дополнительно вычищаются.
static class SupportReport
{
    public static async Task<string> UploadAsync(string text)
    {
        try
        {
            var user = Environment.UserName; var pc = Environment.MachineName;
            var clean = text;
            if (user.Length > 1) clean = clean.Replace(user, "<user>", StringComparison.OrdinalIgnoreCase);
            if (pc.Length > 1) clean = clean.Replace(pc, "<pc>", StringComparison.OrdinalIgnoreCase);
            if (clean.Length > 60000) clean = clean[..60000];
            var id = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(pc + "|" + user))).ToLowerInvariant()[..12];
            var body = JsonSerializer.Serialize(new { at = DateTime.UtcNow.ToString("o"), report = clean });
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var resp = await http.PutAsync($"https://zolotaya-kletka-default-rtdb.firebaseio.com/pluginReports/gateway-plus/{id}.json", new StringContent(body, Encoding.UTF8, "application/json"));
            return resp.IsSuccessStatusCode ? "Отчёт отправлен разработчику. Спасибо!" : "Отчёт не отправился: " + (int)resp.StatusCode;
        }
        catch (Exception ex) { return "Отчёт не отправился: " + ex.Message; }
    }
}
