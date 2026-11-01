using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace WallpaperPlusApp;

// Погода для «Терминала»: бесплатный открытый сервис Open-Meteo (без ключа и регистрации). Запросы идут только к api.open-meteo.com и
// geocoding-api.open-meteo.com и только с названием города или его координатами; страница обоев в интернет по-прежнему не выходит.
static class Weather
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    // Поиск города по названию: (широта, долгота, красивое название)
    public static async Task<(double Lat, double Lon, string Label)?> FindAsync(string query)
    {
        query = query.Trim();
        if (query.Length < 2 || query.Length > 80) return null;
        var url = "https://geocoding-api.open-meteo.com/v1/search?count=1&language=ru&format=json&name=" + Uri.EscapeDataString(query);
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        if (!doc.RootElement.TryGetProperty("results", out var res) || res.GetArrayLength() == 0) return null;
        var r = res[0];
        var name = r.TryGetProperty("name", out var n) ? n.GetString() ?? query : query;
        var country = r.TryGetProperty("country", out var c) ? c.GetString() : null;
        return (r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble(), country != null ? name + ", " + country : name);
    }

    // Текущая погода и время рассвета/заката для сообщения странице
    public static async Task<object> FetchAsync(double lat, double lon, string city)
    {
        try
        {
            var inv = CultureInfo.InvariantCulture;
            var url = "https://api.open-meteo.com/v1/forecast?latitude=" + lat.ToString("0.####", inv) + "&longitude=" + lon.ToString("0.####", inv) +
                      "&current=temperature_2m,weather_code,precipitation,wind_speed_10m&hourly=precipitation_probability&daily=sunrise,sunset&timezone=auto&wind_speed_unit=ms&forecast_days=1";
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var root = doc.RootElement;
            var cur = root.GetProperty("current");
            var offset = root.TryGetProperty("utc_offset_seconds", out var o) ? o.GetInt32() : 0;
            double prec = 0;
            if (root.TryGetProperty("hourly", out var h) && cur.TryGetProperty("time", out var ct))
            {
                var hour = (ct.GetString() ?? "").PadRight(13)[..13];
                var times = h.GetProperty("time"); var probs = h.GetProperty("precipitation_probability");
                for (var i = 0; i < times.GetArrayLength(); i++)
                    if ((times[i].GetString() ?? "").StartsWith(hour, StringComparison.Ordinal) && probs[i].ValueKind == JsonValueKind.Number) { prec = probs[i].GetDouble(); break; }
            }
            long rise = 0, set = 0;
            if (root.TryGetProperty("daily", out var d))
            {
                rise = ToMs(d.GetProperty("sunrise")[0].GetString(), offset);
                set = ToMs(d.GetProperty("sunset")[0].GetString(), offset);
            }
            return new
            {
                ok = true, city,
                temp = Math.Round(cur.GetProperty("temperature_2m").GetDouble(), 1),
                code = cur.GetProperty("weather_code").GetInt32(),
                prec,
                wind = Math.Round(cur.GetProperty("wind_speed_10m").GetDouble(), 1),
                sunrise = rise, sunset = set, err = "",
            };
        }
        catch (Exception ex)
        {
            Paths.Log("погода: " + ex.Message);
            return new { ok = false, city, temp = 0.0, code = 0, prec = 0.0, wind = 0.0, sunrise = 0L, sunset = 0L, err = "нет связи с сервисом погоды" };
        }
    }

    static long ToMs(string? local, int offsetSeconds)
    {
        if (!DateTime.TryParse(local, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) return 0;
        return new DateTimeOffset(dt, TimeSpan.FromSeconds(offsetSeconds)).ToUnixTimeMilliseconds();
    }
}
