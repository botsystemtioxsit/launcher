using System.IO;

namespace IconPlusApp;

// Значок папки задаётся строкой IconResource в файле desktop.ini внутри папки (так Windows хранит значки Документов, Загрузок и т. д.).
static class DesktopIni
{
    const string Section = "[.ShellClassInfo]";

    public static string? Read(string file, string key)
    {
        try
        {
            if (!File.Exists(file)) return null;
            var inSection = false;
            foreach (var line in File.ReadAllLines(file))
            {
                var t = line.Trim();
                if (t.StartsWith("[")) { inSection = t.Equals(Section, StringComparison.OrdinalIgnoreCase); continue; }
                if (inSection && t.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) return t[(key.Length + 1)..].Trim();
            }
        }
        catch { }
        return null;
    }

    // value == null — убрать строку.
    public static bool Write(string file, string key, string? value)
    {
        try
        {
            var existed = File.Exists(file);
            var attrs = existed ? File.GetAttributes(file) : FileAttributes.Hidden | FileAttributes.System;
            if (existed) File.SetAttributes(file, FileAttributes.Normal);
            var lines = existed ? File.ReadAllLines(file).ToList() : new List<string>();
            var res = new List<string>();
            var inSection = false; var seen = false; var wrote = false;
            foreach (var line in lines)
            {
                var t = line.Trim();
                if (t.StartsWith("["))
                {
                    if (inSection && !wrote && value != null) { res.Add(key + "=" + value); wrote = true; }
                    inSection = t.Equals(Section, StringComparison.OrdinalIgnoreCase);
                    if (inSection) seen = true;
                    res.Add(line);
                    continue;
                }
                if (inSection && t.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                {
                    if (value != null && !wrote) { res.Add(key + "=" + value); wrote = true; }
                    continue;
                }
                res.Add(line);
            }
            if (value != null && !wrote)
            {
                if (seen && inSection) res.Add(key + "=" + value);
                else if (seen)
                {
                    var i = res.FindIndex(l => l.Trim().Equals(Section, StringComparison.OrdinalIgnoreCase));
                    res.Insert(i + 1, key + "=" + value);
                }
                else { res.Add(Section); res.Add(key + "=" + value); }
            }
            File.WriteAllLines(file, res);
            File.SetAttributes(file, attrs | FileAttributes.Hidden | FileAttributes.System);
            return true;
        }
        catch { return false; }
    }
}
