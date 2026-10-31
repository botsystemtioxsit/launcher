using System.Text;

namespace WallpaperPlusApp;

// Прикрепление окна к рабочему столу: за значками, перед системными обоями.
//  • Windows 10 и Windows 11 до 24H2: сообщение 0x052C заставляет Progman создать окно WorkerW позади значков — делаем наше окно его дочерним.
//  • Windows 11 24H2 и новее: WorkerW становится дочерним окном самого Progman; наше окно — тоже дочернее Progman, сразу под окном значков (SHELLDLL_DefView).
static class Desktop
{
    public sealed record Target(IntPtr Parent, IntPtr InsertAfter, string Mode);

    public static Target Find()
    {
        var progman = Native.FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return new Target(IntPtr.Zero, IntPtr.Zero, "нет Progman");
        // «создай окно для обоев позади значков»; повторяем — Проводник иногда не успевает с первого раза
        for (var i = 0; i < 2; i++)
        {
            Native.SendMessageTimeout(progman, 0x052C, new UIntPtr(0xD), new IntPtr(1), 0, 1000, out _);
            Thread.Sleep(80);
        }

        var childWorker = Native.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
        var defView = Native.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (childWorker != IntPtr.Zero && defView != IntPtr.Zero) return new Target(progman, defView, "Progman (24H2)");

        IntPtr worker = IntPtr.Zero;
        Native.EnumWindows((top, _) =>
        {
            var dv = Native.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (dv != IntPtr.Zero) worker = Native.FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
            return true;
        }, IntPtr.Zero);
        if (worker != IntPtr.Zero) return new Target(worker, Native.HWND_TOP, "WorkerW");

        return new Target(progman, defView != IntPtr.Zero ? defView : Native.HWND_BOTTOM, "Progman (запасной способ)");
    }

    // Превращает окно в дочернее окно рабочего стола и ставит его на нужный монитор. Возвращает способ или null, если рабочий стол не найден.
    public static string? Attach(IntPtr hwnd, Native.RECT monitor)
    {
        var t = Find();
        if (t.Parent == IntPtr.Zero) { Paths.Log("рабочий стол не найден: " + t.Mode); return null; }

        var style = Native.GetLong(hwnd, Native.GWL_STYLE);
        style &= ~(Native.WS_POPUP | Native.WS_CAPTION | Native.WS_THICKFRAME | Native.WS_SYSMENU | Native.WS_MINIMIZEBOX | Native.WS_MAXIMIZEBOX);
        style |= Native.WS_CHILD | Native.WS_VISIBLE | Native.WS_CLIPSIBLINGS | Native.WS_CLIPCHILDREN;
        Native.SetLong(hwnd, Native.GWL_STYLE, style);
        var ex = Native.GetLong(hwnd, Native.GWL_EXSTYLE);
        ex = (ex & ~Native.WS_EX_APPWINDOW) | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
        Native.SetLong(hwnd, Native.GWL_EXSTYLE, ex);

        Native.SetParent(hwnd, t.Parent);
        Native.GetWindowRect(t.Parent, out var pr);
        var x = monitor.Left - pr.Left;
        var y = monitor.Top - pr.Top;
        Native.SetWindowPos(hwnd, t.InsertAfter, x, y, monitor.Width, monitor.Height, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW | Native.SWP_FRAMECHANGED);
        Paths.Log($"окно {hwnd} прикреплено: {t.Mode}, родитель {t.Parent}, место {x},{y} {monitor.Width}x{monitor.Height} (родитель {pr.Left},{pr.Top} {pr.Width}x{pr.Height})");
        return t.Mode;
    }

    // Текущий родитель окна — тот ли это рабочий стол, что сейчас (Проводник мог перезапуститься).
    public static bool StillAttached(IntPtr hwnd)
    {
        if (!Native.IsWindow(hwnd)) return false;
        var p = Native.GetParent(hwnd);
        if (p == IntPtr.Zero || !Native.IsWindow(p)) return false;
        var cls = Native.ClassName(p);
        return cls is "Progman" or "WorkerW";
    }

    // Дерево окон рабочего стола — для журнала (по нему подстраиваемся под сборку Windows).
    public static string Dump()
    {
        var sb = new StringBuilder();
        var progman = Native.FindWindow("Progman", null);
        sb.AppendLine("Progman " + progman + " " + Rect(progman));
        void Children(IntPtr parent, string indent)
        {
            var c = IntPtr.Zero;
            while ((c = Native.FindWindowEx(parent, c, null, null)) != IntPtr.Zero)
            {
                sb.AppendLine($"{indent}{Native.ClassName(c)} {c} {Rect(c)} {(Native.IsWindowVisible(c) ? "видимо" : "скрыто")}");
                if (indent.Length < 8) Children(c, indent + "  ");
            }
        }
        Children(progman, "  ");
        Native.EnumWindows((top, _) =>
        {
            if (Native.ClassName(top) == "WorkerW") { sb.AppendLine($"WorkerW верхнего уровня {top} {Rect(top)}"); Children(top, "  "); }
            return true;
        }, IntPtr.Zero);
        return sb.ToString();
    }

    static string Rect(IntPtr h) => Native.GetWindowRect(h, out var r) ? $"[{r.Left},{r.Top} {r.Width}x{r.Height}]" : "[?]";

    // После выхода просим перерисовать рабочий стол (на случай, если на месте окна обоев осталось «пятно»). Системные обои не трогаем.
    public static void Repaint()
    {
        try
        {
            var progman = Native.FindWindow("Progman", null);
            if (progman != IntPtr.Zero) Native.RedrawWindow(progman, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0400 /* INVALIDATE | ERASE | ALLCHILDREN | FRAME */);
            Native.RedrawWindow(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080);
        }
        catch { }
    }
}
