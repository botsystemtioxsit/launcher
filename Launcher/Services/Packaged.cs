using System.Runtime.InteropServices;
using System.Text;

namespace BattleLauncher;

// Версия из Microsoft Store (MSIX) ставится, обновляется и удаляется самим Store — своя установка
// и самообновление там должны молчать.
static class Packaged
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetCurrentPackageFullName(ref int length, StringBuilder? name);

    const int AppModelErrorNoPackage = 15700;

    public static readonly bool IsPackaged = Check();

    static bool Check()
    {
        try
        {
            var len = 0;
            return GetCurrentPackageFullName(ref len, null) != AppModelErrorNoPackage;
        }
        catch { return false; }
    }
}
