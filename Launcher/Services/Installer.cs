using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace BattleLauncher;

record InstalledApp(string? Version, string Exe, string? Uninstall);

// Установка, запуск и удаление приложений. Установщики приложений — обычные NSIS-установщики
// electron-builder (на пользователя, без прав администратора): запускаем их с /S (тихо), а
// установленную версию читаем из списка «Установка и удаление программ».
static class Installer
{
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    public static InstalledApp? FindInstalled(string productName)
    {
        var views = new[] { (RegistryHive.CurrentUser, RegistryView.Default), (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32) };
        foreach (var (hive, view) in views)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var root = baseKey.OpenSubKey(UninstallKey);
                if (root == null) continue;
                foreach (var name in root.GetSubKeyNames())
                {
                    using var k = root.OpenSubKey(name);
                    var display = k?.GetValue("DisplayName") as string;
                    if (display == null || !(display == productName || display.StartsWith(productName + " "))) continue;
                    var uninstall = (k!.GetValue("QuietUninstallString") ?? k.GetValue("UninstallString")) as string;
                    var location = (k.GetValue("InstallLocation") as string)?.Trim('"');
                    if (string.IsNullOrEmpty(location) && uninstall != null) location = Path.GetDirectoryName(ExePart(uninstall));
                    if (string.IsNullOrEmpty(location)) continue;
                    var exe = Path.Combine(location, productName + ".exe");
                    if (!File.Exists(exe)) continue;
                    return new InstalledApp(k.GetValue("DisplayVersion") as string, exe, uninstall);
                }
            }
            catch { /* нет доступа к ветке — смотрим следующую */ }
        }
        return null;
    }

    // из строки вида  "C:\...\Uninstall X.exe" /currentuser  берём путь к программе
    static string ExePart(string cmd)
    {
        cmd = cmd.Trim();
        if (cmd.StartsWith('"')) { var end = cmd.IndexOf('"', 1); return end > 1 ? cmd[1..end] : cmd.Trim('"'); }
        var i = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return i > 0 ? cmd[..(i + 4)] : cmd;
    }

    // percent == null — скачано, идёт установка
    public static async Task Install(AppDef app, Release release, IProgress<int?> progress, CancellationToken ct = default)
    {
        if (!Net.IsTrustedDownload(release.Url, app.Repo)) throw new InvalidOperationException("Недоверенный адрес загрузки");
        var dir = Directory.CreateTempSubdirectory("battle-launcher-").FullName;
        var file = Path.Combine(dir, "setup.exe");
        try
        {
            await Download(release.Url, file, release.Sha512, progress, ct);
            progress.Report(null);
            await RunAndWait(file, "/S");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
        if (FindInstalled(app.ProductName) == null) throw new InvalidOperationException("Установщик отработал, но приложение не найдено");
    }

    // sha512 из latest.yml (base64) — защита от битого или подменённого файла
    public static async Task Download(string url, string file, string expectedSha512Base64, IProgress<int?> progress, CancellationToken ct)
    {
        using var res = await Net.Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        var total = res.Content.Headers.ContentLength ?? 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        await using (var src = await res.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(file))
        {
            var buf = new byte[81920];
            long got = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                hash.AppendData(buf, 0, n);
                got += n;
                if (total > 0) progress.Report((int)Math.Min(99, got * 100 / total));
            }
        }
        if (Convert.ToBase64String(hash.GetHashAndReset()) != expectedSha512Base64)
            throw new InvalidDataException("Контрольная сумма не совпала — файл повреждён");
    }

    static async Task RunAndWait(string file, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true })
                      ?? throw new InvalidOperationException("Не удалось запустить установщик");
        await p.WaitForExitAsync();
        if (p.ExitCode != 0) throw new InvalidOperationException($"Установщик завершился с кодом {p.ExitCode}");
    }

    public static void Launch(string exe) =>
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! });

    public static async Task Uninstall(InstalledApp app)
    {
        if (app.Uninstall == null) throw new InvalidOperationException("Не нашёл программу удаления");
        var exe = ExePart(app.Uninstall);
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(exe)) throw new InvalidOperationException("Программа удаления не найдена");
        var rest = app.Uninstall.Trim().StartsWith('"') ? app.Uninstall.Trim()[(exe.Length + 2)..].Trim() : "";
        await RunAndWait(exe, (rest + " /S").Trim());
    }
}
