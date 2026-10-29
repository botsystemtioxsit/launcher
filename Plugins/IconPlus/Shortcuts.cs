using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace IconPlusApp;

// Значок ярлыка (.lnk) через настоящий объект «ярлык» Windows: остальное содержимое ярлыка (в том числе идентификатор программы) не теряется.
static class Shortcuts
{
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLink { }

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

    public static bool IsUrl(string file) => file.EndsWith(".url", StringComparison.OrdinalIgnoreCase);

    public static bool ReadIcon(string lnk, out string path, out int index)
    {
        path = ""; index = 0;
        if (IsUrl(lnk)) return ReadUrlIcon(lnk, out path, out index);
        object? o = null;
        try
        {
            o = new ShellLink();
            ((IPersistFile)o).Load(lnk, 0);
            var sb = new StringBuilder(520);
            ((IShellLinkW)o).GetIconLocation(sb, sb.Capacity, out index);
            path = sb.ToString();
            return true;
        }
        catch { return false; }
        finally { if (o != null) Marshal.ReleaseComObject(o); }
    }

    // Интернет-ярлыки (.url, в том числе ярлыки Steam-игр): значок задаётся строками IconFile / IconIndex в разделе [InternetShortcut].
    static bool ReadUrlIcon(string file, out string path, out int index)
    {
        path = ""; index = 0;
        try
        {
            foreach (var line in File.ReadAllLines(file))
            {
                var t = line.Trim();
                if (t.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase)) path = t[9..].Trim();
                else if (t.StartsWith("IconIndex=", StringComparison.OrdinalIgnoreCase)) int.TryParse(t[10..].Trim(), out index);
            }
            return true;
        }
        catch { return false; }
    }

    // iconPath пустой — убрать строки значка (вернуть как было, если их не было).
    static bool WriteUrlIcon(string file, string iconPath, int index)
    {
        try
        {
            var attrs = File.GetAttributes(file);
            if ((attrs & FileAttributes.ReadOnly) != 0) File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
            var res = new List<string>();
            var done = false;
            foreach (var line in File.ReadAllLines(file))
            {
                var t = line.TrimStart();
                if (t.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase) || t.StartsWith("IconIndex=", StringComparison.OrdinalIgnoreCase)) continue;
                res.Add(line);
                if (!done && iconPath.Length > 0 && t.TrimEnd().Equals("[InternetShortcut]", StringComparison.OrdinalIgnoreCase))
                {
                    res.Add("IconIndex=" + index);
                    res.Add("IconFile=" + iconPath);
                    done = true;
                }
            }
            if (!done && iconPath.Length > 0) { res.Add("[InternetShortcut]"); res.Add("IconIndex=" + index); res.Add("IconFile=" + iconPath); }
            File.WriteAllLines(file, res);
            if ((attrs & FileAttributes.ReadOnly) != 0) File.SetAttributes(file, attrs);
            return true;
        }
        catch { return false; }
    }

    // Объект оболочки, на который указывает ярлык (адрес для IShellItem). Освободить CoTaskMemFree.
    public static IntPtr ReadIdList(string lnk)
    {
        object? o = null;
        try
        {
            o = new ShellLink();
            ((IPersistFile)o).Load(lnk, 0);
            ((IShellLinkW)o).GetIDList(out var pidl);
            return pidl;
        }
        catch { return IntPtr.Zero; }
        finally { if (o != null) Marshal.ReleaseComObject(o); }
    }

    // Куда ведёт ярлык (пусто для программ из магазина и некоторых системных ярлыков).
    public static string ReadTarget(string lnk)
    {
        object? o = null;
        try
        {
            o = new ShellLink();
            ((IPersistFile)o).Load(lnk, 0);
            var sb = new StringBuilder(520);
            ((IShellLinkW)o).GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            return sb.ToString();
        }
        catch { return ""; }
        finally { if (o != null) Marshal.ReleaseComObject(o); }
    }

    public static bool WriteIcon(string lnk, string iconPath, int index)
    {
        if (IsUrl(lnk)) return WriteUrlIcon(lnk, iconPath, index);
        object? o = null;
        try
        {
            o = new ShellLink();
            ((IPersistFile)o).Load(lnk, 2 /* STGM_READWRITE */);
            ((IShellLinkW)o).SetIconLocation(iconPath, index);
            ((IPersistFile)o).Save(lnk, true);
            return true;
        }
        catch { return false; }
        finally { if (o != null) Marshal.ReleaseComObject(o); }
    }

    // Ярлыки (.lnk и .url) в папке: рекурсивно, не больше limit штук.
    public static List<string> In(string dir, bool recursive, int limit = 4000)
    {
        var list = new List<string>();
        try
        {
            if (!Directory.Exists(dir)) return list;
            var opt = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System | FileAttributes.Offline };
            foreach (var f in Directory.EnumerateFiles(dir, "*.*", opt))
            {
                if (!f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && !f.EndsWith(".url", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(f);
                if (list.Count >= limit) break;
            }
        }
        catch { }
        return list;
    }
}
