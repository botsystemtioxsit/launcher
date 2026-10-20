using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace BattleLauncher;

static class Net
{
    public static readonly HttpClient Http = Create();

    // Любой путь к базе, собранный из чужого текста, сначала проходит через это.
    public static readonly Regex SafeId = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled);

    static HttpClient Create()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("battle-launcher", "1"));
        return c;
    }

    public static async Task<string> GetText(string url, CancellationToken ct = default)
    {
        using var res = await Http.GetAsync(url, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsStringAsync(ct);
    }

    // Скачивать разрешено только с github.com из релизов репозиториев проекта.
    public static bool IsTrustedDownload(string url, string repo)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        return u.Scheme == Uri.UriSchemeHttps && u.Host == "github.com" &&
               u.AbsolutePath.StartsWith($"/{Catalog.Owner}/{repo}/releases/", StringComparison.Ordinal);
    }

    // «1.2.10» > «1.2.9»
    public static int CompareVersions(string a, string b)
    {
        static int[] Parse(string s) => s.Split('.').Select(p => int.TryParse(new string(p.TakeWhile(char.IsDigit).ToArray()), out var n) ? n : 0).ToArray();
        var pa = Parse(a); var pb = Parse(b);
        for (var i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            var d = (i < pa.Length ? pa[i] : 0) - (i < pb.Length ? pb[i] : 0);
            if (d != 0) return d > 0 ? 1 : -1;
        }
        return 0;
    }
}
