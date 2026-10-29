using System.Runtime.InteropServices;
using System.Text;

namespace IconPlusApp;

// Вызовы Windows для значков. Всё, что тут делается, — только в настройках текущего пользователя, права администратора не нужны.
static class Native
{
    public const uint SHCNE_ASSOCCHANGED = 0x08000000, SHCNF_IDLIST = 0;
    public const uint SHGFI_ICONLOCATION = 0x1000, SHGFI_USEFILEATTRIBUTES = 0x10, SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0, SHGFI_PIDL = 8;
    public const uint FILE_ATTRIBUTE_NORMAL = 0x80, FILE_ATTRIBUTE_DIRECTORY = 0x10;
    public const int DRIVE_REMOVABLE = 2, DRIVE_FIXED = 3, DRIVE_REMOTE = 4, DRIVE_CDROM = 5;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SHGetFileInfo(string path, uint attrs, ref SHFILEINFO fi, uint size, uint flags);

    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW")]
    static extern IntPtr SHGetFileInfoPidl(IntPtr pidl, uint attrs, ref SHFILEINFO fi, uint size, uint flags);

    [DllImport("shell32.dll")] public static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, uint[]? ids, uint count, uint flags);

    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    static extern int AssocQueryString(uint flags, int str, string assoc, string? extra, StringBuilder? output, ref uint len);

    // Значок типа файла по версии Windows («путь,номер»), с учётом выбранной пользователем программы. assoc — «.txt» или имя типа.
    public static string AssocDefaultIcon(string assoc)
    {
        var sb = new StringBuilder(520);
        var n = (uint)sb.Capacity;
        var hr = AssocQueryString(0, 15 /* ASSOCSTR_DEFAULTICON */, assoc, null, sb, ref n);
        return hr == 0 ? Environment.ExpandEnvironmentVariables(sb.ToString()) : "";
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern int GetDriveType(string root);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHParseDisplayName(string name, IntPtr bind, out IntPtr pidl, uint attrsIn, out uint attrsOut);

    [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr p);

    // Где лежит значок файла или папки по версии Проводника: «путь,номер». Пусто, если Windows не сказала.
    public static string IconLocation(string path, bool useAttributes, bool directory = false)
    {
        var fi = new SHFILEINFO();
        var flags = SHGFI_ICONLOCATION | (useAttributes ? SHGFI_USEFILEATTRIBUTES : 0);
        var attrs = directory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
        SHGetFileInfo(path, attrs, ref fi, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        return string.IsNullOrEmpty(fi.szDisplayName) ? "" : fi.szDisplayName + "," + fi.iIcon;
    }

    [DllImport("shell32.dll")]
    static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr token, out IntPtr path);

    // Путь известной папки Windows (например, «Загрузки»). Пусто, если не нашлась.
    public static string KnownFolder(Guid id)
    {
        try
        {
            if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var p) != 0 || p == IntPtr.Zero) return "";
            try { return Marshal.PtrToStringUni(p) ?? ""; } finally { CoTaskMemFree(p); }
        }
        catch { return ""; }
    }

    // Значок объекта оболочки (например, «::{20D04FE0-3AEA-1069-A2D8-08002B30309D}» — «Этот компьютер»).
    public static string IconLocationOfShellItem(string parsingName)
    {
        if (SHParseDisplayName(parsingName, IntPtr.Zero, out var pidl, 0, out _) != 0 || pidl == IntPtr.Zero) return "";
        try
        {
            var fi = new SHFILEINFO();
            SHGetFileInfoPidl(pidl, 0, ref fi, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_PIDL | SHGFI_ICONLOCATION);
            return string.IsNullOrEmpty(fi.szDisplayName) ? "" : fi.szDisplayName + "," + fi.iIcon;
        }
        finally { CoTaskMemFree(pidl); }
    }

    // Один значок нужного размера из файла (.ico, .exe, .dll, .icl). Отрицательный номер — идентификатор ресурса.
    public static IntPtr ExtractIcon(string file, int index, int size)
    {
        var icons = new IntPtr[1];
        var n = PrivateExtractIcons(file, index, size, size, icons, null, 1, 0);
        return n >= 1 && n != 0xFFFFFFFF ? icons[0] : IntPtr.Zero;
    }

    // Попросить Windows пересобрать кэш значков (его не видно, но без этого часть значков остаётся старыми до перезапуска Проводника).
    public static void RefreshIconCache()
    {
        var exe = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ie4uinit.exe");
        if (!System.IO.File.Exists(exe)) return;
        foreach (var arg in new[] { "-ClearIconCache", "-show" })
        {
            try
            {
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, arg) { UseShellExecute = false, CreateNoWindow = true });
                p?.WaitForExit(8000);
            }
            catch { }
        }
    }

    // Сказать Проводнику, что значки изменились.
    public static void NotifyIconsChanged()
    {
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        // часть программ обновляет значки только после этого сообщения
        System.Threading.Tasks.Task.Run(() =>
        {
            try { SendMessageTimeout((IntPtr)0xFFFF, 0x1A, UIntPtr.Zero, "Environment", 2, 1500, out _); } catch { }
            try { RefreshIconCache(); } catch { }
        });
    }
}
