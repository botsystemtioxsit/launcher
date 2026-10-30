using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace IconPlusApp;

sealed record StartApp(string Name, string AppId);

// Приложения из списка «Пуск», у которых нет своего ярлыка: магазинные (Блокнот, Терминал, Claude…) и системные (Безопасность Windows…).
// Их значок лежит в самом пакете приложения, куда Windows писать не даёт, поэтому делаем иначе: кладём в «Пуск» свой ярлык с тем же идентификатором
// программы (AppUserModelID) — «Пуск» склеивает их в одну запись и берёт значок из ярлыка (так уже работает WhatsApp и другие).
static class StartApps
{
    [StructLayout(LayoutKind.Sequential)] struct PROPERTYKEY { public Guid fmtid; public uint pid; }
    [StructLayout(LayoutKind.Sequential)] struct PROPVARIANT { public ushort vt, r1, r2, r3; public IntPtr p1, p2; }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint i, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr fd, uint flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int cch, out int icon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int icon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relPath, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
    interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string file, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string? file, [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string file);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string file);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHParseDisplayName(string name, IntPtr bindCtx, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
    [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr p);
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT pv);

    public static string ProgramsFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");

    public static string AppsFolderPath(string appId) => "shell:AppsFolder\\" + appId;

    // Всё, что «Пуск» показывает в списке «Все приложения» (имя и идентификатор). Спрашиваем у самого Windows (Get-StartApps).
    public static List<StartApp> List()
    {
        var res = new List<StartApp>();
        try
        {
            var script = "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; Get-StartApps | Select-Object Name,AppID | ConvertTo-Json -Compress";
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8,
            };
            using var p = Process.Start(psi);
            if (p == null) return res;
            var outTask = p.StandardOutput.ReadToEndAsync();
            p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(90000)) { try { p.Kill(true); } catch { } return res; }
            var json = outTask.Result.Trim();
            if (json.Length == 0) return res;
            using var doc = JsonDocument.Parse(json);
            var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new List<JsonElement> { doc.RootElement };
            foreach (var it in items)
            {
                var name = it.TryGetProperty("Name", out var n) ? n.GetString() : null;
                var id = it.TryGetProperty("AppID", out var a) ? a.GetString() : null;
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(id)) res.Add(new StartApp(name!.Trim(), id!.Trim()));
            }
        }
        catch { }
        return res;
    }

    // Идентификаторы программ, которые уже есть у ярлыков в меню «Пуск» (чужие ярлыки трогает обычная перекраска ярлыков, им свой не нужен).
    static Dictionary<string, string> LnkAumids()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in new[] { ProgramsFolder, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs") })
            foreach (var f in Shortcuts.In(dir, true))
            {
                var au = Shortcuts.ReadAumid(f);
                if (au.Length > 0 && !map.ContainsKey(au)) map[au] = f;
            }
        return map;
    }

    // Кому нужен свой ярлык: нет ярлыка с таким идентификатором (или он наш), и это не путь к программе (такие идут из обычных ярлыков).
    public static List<StartApp> Candidates(IEnumerable<string> ours, List<StartApp>? all = null)
    {
        all ??= List();
        var mine = new HashSet<string>(ours, StringComparer.OrdinalIgnoreCase);
        var have = LnkAumids();
        var res = new List<StartApp>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in all)
        {
            var id = a.AppId;
            if (id.Contains('\\') || id.Contains('/') || id.Contains(':') || id.StartsWith("{", StringComparison.Ordinal) || id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
            if (!seen.Add(id)) continue;
            if (have.TryGetValue(id, out var lnk) && !mine.Contains(lnk)) continue;
            res.Add(a);
            if (res.Count >= 150) break;
        }
        return res;
    }

    public static string FileName(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name) sb.Append(Path.GetInvalidFileNameChars().Contains(c) ? ' ' : c);
        var s = sb.ToString().Trim().TrimEnd('.');
        if (s.Length > 90) s = s[..90].TrimEnd();
        return s.Length == 0 ? "App" : s;
    }

    // Ярлык на приложение из списка «Пуск»: цель — сам объект «Пуска», идентификатор программы тот же, значок — наш.
    public static void CreateLink(string lnk, string appId, string iconPath)
    {
        object? o = null;
        var pidl = IntPtr.Zero;
        try
        {
            o = new ShellLink();
            var link = (IShellLinkW)o;
            var hr = SHParseDisplayName(AppsFolderPath(appId), IntPtr.Zero, out pidl, 0, out _);
            if (hr != 0 || pidl == IntPtr.Zero) throw new IOException("«Пуск» не отдаёт это приложение (код " + hr + ")");
            link.SetIDList(pidl);
            link.SetIconLocation(iconPath, 0);
            var ps = (IPropertyStore)o;
            var key = new PROPERTYKEY { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };
            // InitPropVariantFromString в новых Windows не экспортируется из propsys.dll («Entry point was not found», так провалились все 26 приложений в 1.3.0/1.3.1) —
            // строку (VT_LPWSTR = 31) собираем сами; PropVariantClear освободит её
            var pv = new PROPVARIANT { vt = 31, p1 = Marshal.StringToCoTaskMemUni(appId) };
            try
            {
                var r1 = ps.SetValue(ref key, ref pv);
                var r2 = ps.Commit();
                if (r1 != 0 || r2 != 0) throw new IOException("идентификатор программы не записался (код " + r1 + "/" + r2 + ")");
            }
            finally { PropVariantClear(ref pv); }
            ((IPersistFile)o).Save(lnk, true);
        }
        finally
        {
            if (pidl != IntPtr.Zero) CoTaskMemFree(pidl);
            if (o != null) Marshal.ReleaseComObject(o);
        }
    }

    // Какие из наших ярлыков «Пуск» показывает дважды (значит склейка не сработала): считаем записи с тем же идентификатором и с тем же именем.
    public static List<string> Duplicates(IEnumerable<StartApp> created)
    {
        var list = List();
        var bad = new List<string>();
        foreach (var c in created)
        {
            var byId = list.Count(a => string.Equals(a.AppId, c.AppId, StringComparison.OrdinalIgnoreCase));
            var byName = list.Count(a => string.Equals(a.Name, c.Name, StringComparison.OrdinalIgnoreCase));
            if (byId > 1 || byName > 1) bad.Add(c.AppId);
        }
        return bad;
    }
}
