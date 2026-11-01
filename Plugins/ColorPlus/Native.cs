using System.Runtime.InteropServices;

namespace ColorPlusApp;

static class Native
{
    [DllImport("user32.dll")] public static extern bool SetSysColors(int count, int[] elements, int[] colors);
    [DllImport("user32.dll")] public static extern int GetSysColor(int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, UIntPtr w, string l, uint flags, uint timeout, out UIntPtr result);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
}
