using System.Net.Http;
using System.Text.Json;

namespace GatewayPlusApp;

// Каким вас видят сайты: внешний адрес и страна. Спрашиваем сервис ipwho.is (он видит только тот адрес, с которого приходит запрос).
static class IpInfo
{
    public sealed record Info(bool Ok, string Ip, string Country, string City, string Zone, string Error);

    public static async Task<Info> GetAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var json = await http.GetStringAsync("https://ipwho.is/");
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False) return new Info(false, "", "", "", "", "сервис проверки отказал");
            string S(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            var zone = r.TryGetProperty("timezone", out var tz) && tz.ValueKind == JsonValueKind.Object && tz.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
            return new Info(true, S("ip"), S("country"), S("city"), zone, "");
        }
        catch (Exception ex) { return new Info(false, "", "", "", "", ex.Message); }
    }
}
