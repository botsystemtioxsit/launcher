using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GatewayPlusApp;

// Аккаунт Gateway Plus: свой, отдельный от игрового. Без пароля и почты: человеку выдаётся ключ (160 случайных бит), номер аккаунта в базе — первые 24 знака sha256 от ключа,
// поэтому ключ в базу не попадает, а по номеру его не подобрать. Ключ хранится на компьютере под DPAPI (читает только этот пользователь Windows).
static class Account
{
    public sealed class Info
    {
        public string Key { get; set; } = "";     // 32 знака A-Z2-7 (без «GW-» и тире)
        public string Name { get; set; } = "";
        public int TermsV { get; set; }           // версия принятых правил (Terms.Version)
        [JsonIgnore] public string Id => IdOf(Key);
    }

    static string FilePath => Path.Combine(Store.Dir, "account.dat");
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GhostShell.GatewayPlus.Account.v1");
    const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    // 20 случайных байт → 32 знака base32.
    public static string NewKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(20);
        var sb = new StringBuilder(32);
        int bits = 0, val = 0;
        foreach (var b in bytes)
        {
            val = (val << 8) | b; bits += 8;
            while (bits >= 5) { sb.Append(Alphabet[(val >> (bits - 5)) & 31]); bits -= 5; }
            val &= (1 << bits) - 1;
        }
        return sb.ToString();
    }

    // Для показа: GW-XXXX-XXXX-… (8 групп по 4 знака).
    public static string Format(string raw) => "GW-" + string.Join("-", Enumerable.Range(0, raw.Length / 4).Select(i => raw.Substring(i * 4, 4)));

    // Ключ, как его ввёл человек (с «GW-», тире, пробелами, любым регистром) → 32 знака или null.
    public static string? Normalize(string? input)
    {
        if (input == null) return null;
        var t = new string(input.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (t.Length == 34 && t.StartsWith("GW", StringComparison.Ordinal)) t = t[2..];
        return Regex.IsMatch(t, "^[A-Z2-7]{32}$") ? t : null;
    }

    // Номер аккаунта в базе: 24 знака 0-9a-f.
    public static string IdOf(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("ghostshell.gateway.v1:" + raw)), 0, 12).ToLowerInvariant();

    // Обезличенный номер этого компьютера (32 знака 0-9a-f): из MachineGuid Windows с солью — по нему не узнать сам MachineGuid.
    public static string DeviceHash()
    {
        string guid;
        try { guid = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null) as string ?? ""; }
        catch { guid = ""; }
        if (guid.Length == 0) guid = "no-guid:" + Environment.MachineName;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("ghostshell.gateway.dev.v1:" + guid)), 0, 16).ToLowerInvariant();
    }

    // Имя: одна строка без управляющих и «перевёрнутых» знаков, до 30 знаков.
    public static string CleanName(string? s)
    {
        var t = Regex.Replace(s ?? "", @"[\p{C}‎‏‪-‮⁦-⁩]", " ");
        t = Regex.Replace(t, @"\s+", " ").Trim();
        return t.Length > 30 ? t[..30].Trim() : t;
    }

    // Причина блокировки из базы: одна строка, до 200 знаков.
    public static string CleanReason(string? s)
    {
        var t = Regex.Replace(s ?? "", @"[\p{C}‎‏  ‪-‮⁦-⁩]", " ");
        t = Regex.Replace(t, @"\s+", " ").Trim();
        return t.Length > 200 ? t[..200] : t;
    }

    public static Info? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var json = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser));
            var info = JsonSerializer.Deserialize<Info>(json);
            return info != null && Normalize(info.Key) == info.Key ? info : null;
        }
        catch { return null; }
    }

    public static void Save(Info info)
    {
        Directory.CreateDirectory(Store.Dir);
        File.WriteAllBytes(FilePath, ProtectedData.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(info)), Entropy, DataProtectionScope.CurrentUser));
    }

    public static void Clear()
    {
        try { File.Delete(FilePath); } catch { }
        try { File.Delete(Path.Combine(Store.Dir, "access.json")); } catch { }
    }
}
