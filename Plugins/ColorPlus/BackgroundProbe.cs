using System.Runtime.InteropServices;

namespace ColorPlusApp;

// «Только фон»: Windows умеет делать прозрачным у чужого окна лишь один точный цвет (цветовой ключ слоёного окна).
// Здесь решаем, подходит ли окно, и находим цвет его фона — самый частый цвет в нижней части окна (шапку с вкладками и меню обходим).
//
// Честные границы:
//  • окна, которые рисуются видеокартой через DirectComposition (Chrome, Edge, Electron-программы вроде Discord и VS Code, магазинные приложения),
//    ключ игнорируют — снаружи их фон прозрачным не сделать (проверено на Windows 11: у Chrome и Edge после включения ключа ничего не менялось);
//  • прозрачные места пропускают нажатия мыши — клик по пустому месту попадёт в то, что лежит за окном;
//  • у сглаженного текста по краям букв остаётся тонкая кайма — это пиксели, смешанные с цветом фона.
static class BackgroundProbe
{
    public const double MinShare = 0.30;   // цвет считается фоном, только если занимает не меньше трети нижней части окна

    // Класс окна, которые заведомо рисуются мимо обычной поверхности окна.
    static readonly string[] UnsupportedClassPrefixes =
    {
        "Chrome_WidgetWin", "ApplicationFrameWindow", "Windows.UI.", "WinUIDesktopWin32WindowClass", "MozillaWindowClass", "XamlWindow",
    };

    // null — подходит; иначе причина.
    public static string? WhyUnsupported(IntPtr h, string cls)
    {
        foreach (var p in UnsupportedClassPrefixes)
            if (cls.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return "окно рисуется видеокартой (Chrome, Edge, Electron, магазинные приложения), Windows не даёт сделать только его фон прозрачным";
        if ((Native.GetLong(h, Native.GWL_EXSTYLE) & Native.WS_EX_NOREDIRECTIONBITMAP) != 0)
            return "окно рисуется видеокартой, Windows не даёт сделать только его фон прозрачным";
        return null;
    }

    // Самый частый цвет в нижних 60% окна. Возвращает COLORREF (BGR) и долю пикселей этого цвета.
    public static bool TrySample(IntPtr h, out uint colorRef, out double share)
    {
        colorRef = 0; share = 0;
        if (!Native.GetWindowRect(h, out var r)) return false;
        int w = r.Width, hh = r.Height;
        if (w < 80 || hh < 80 || w > 8000 || hh > 8000) return false;

        var sdc = Native.GetDC(IntPtr.Zero);
        var dc = Native.CreateCompatibleDC(sdc);
        var bi = new Native.BITMAPINFOHEADER { Size = Marshal.SizeOf<Native.BITMAPINFOHEADER>(), Width = w, Height = -hh, Planes = 1, BitCount = 32 };
        var bmp = Native.CreateDIBSection(sdc, ref bi, 0, out var bits, IntPtr.Zero, 0);
        if (bmp == IntPtr.Zero || bits == IntPtr.Zero) { Native.DeleteDC(dc); Native.ReleaseDC(IntPtr.Zero, sdc); return false; }
        var old = Native.SelectObject(dc, bmp);
        try
        {
            if (!Native.PrintWindow(h, dc, 2 /* PW_RENDERFULLCONTENT */)) return false;
            var counts = new Dictionary<int, int>();
            var total = 0;
            for (var y = hh * 4 / 10; y < hh - 2; y += 4)
            {
                for (var x = 2; x < w - 2; x += 4)
                {
                    var px = Marshal.ReadInt32(bits, (y * w + x) * 4) & 0xFFFFFF;
                    counts.TryGetValue(px, out var c);
                    counts[px] = c + 1;
                    total++;
                }
            }
            if (total == 0) return false;
            int best = 0, bestN = 0;
            foreach (var kv in counts) if (kv.Value > bestN) { best = kv.Key; bestN = kv.Value; }
            int rr = (best >> 16) & 0xFF, gg = (best >> 8) & 0xFF, bb = best & 0xFF;
            colorRef = (uint)((bb << 16) | (gg << 8) | rr);
            share = bestN / (double)total;
            return true;
        }
        finally
        {
            Native.SelectObject(dc, old);
            Native.DeleteObject(bmp);
            Native.DeleteDC(dc);
            Native.ReleaseDC(IntPtr.Zero, sdc);
        }
    }
}
