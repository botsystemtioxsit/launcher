using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CursorPlusApp;

// Прозрачное окно поверх всего экрана, в котором рисуется шлейф за настоящим курсором. Клики проходят сквозь него, фокус оно не берёт.
// Рисует только там, где есть частицы; в полноэкранных играх и видео не рисует вовсе.
sealed class TrailOverlay
{
    delegate IntPtr WndProcDelegate(IntPtr h, uint msg, IntPtr w, IntPtr l);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSEX
    {
        public uint cbSize, style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName, lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public Native.POINT pt; }

    [StructLayout(LayoutKind.Sequential)]
    struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPels, biYPels; public uint biClrUsed, biClrImportant; }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public uint bmiColors; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateWindowEx(uint exStyle, string cls, string? title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")]
    static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dstDc, ref Native.POINT pptDst, ref Native.SIZE psize, IntPtr srcDc, ref Native.POINT pptSrc, uint crKey, ref BLENDFUNCTION blend, uint flags);
    [DllImport("user32.dll")] static extern bool PeekMessage(out MSG msg, IntPtr h, uint min, uint max, uint remove);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG m);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);

    const uint WS_POPUP = 0x80000000;
    const uint WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
    const string ClassName = "GhostShellCursorTrail";
    static readonly WndProcDelegate Proc = (h, m, w, l) => DefWindowProc(h, m, w, l);
    static bool registered;

    readonly TrailEngine engine = new();
    Thread? thread;
    volatile bool run;
    public volatile bool Paused;     // можно поставить на паузу снаружи
    public bool Running => run && thread != null && thread.IsAlive;

    public void SetSpec(TrailSpec spec) => engine.SetSpec(spec);

    public void Start(TrailSpec spec)
    {
        if (Running) { engine.SetSpec(spec); return; }
        engine.Reset();
        engine.SetSpec(spec);
        run = true;
        thread = new Thread(Loop) { IsBackground = true, Name = "CursorPlus.Trail" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public void Stop()
    {
        run = false;
        try { thread?.Join(2500); } catch { }
        thread = null;
    }

    void Loop()
    {
        Native.timeBeginPeriod(1);
        var hwnd = IntPtr.Zero;
        try
        {
            var inst = GetModuleHandle(null);
            if (!registered)
            {
                var wc = new WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = Proc, hInstance = inst, lpszClassName = ClassName };
                RegisterClassEx(ref wc);
                registered = true;
            }
            hwnd = CreateWindowEx(WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, ClassName, "Ghost Shell Cursor Trail", WS_POPUP, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
            if (hwnd == IntPtr.Zero) return;

            var sw = Stopwatch.StartNew();
            double last = 0, nextCheck = 0, topAt = 0;
            var visible = false; var paused = false; var lDown = false; var rDown = false;
            var buf = new byte[1024 * 1024];
            while (run)
            {
                var frameStart = sw.Elapsed.TotalSeconds;
                while (PeekMessage(out var m, IntPtr.Zero, 0, 0, 1)) { TranslateMessage(ref m); DispatchMessage(ref m); }
                var now = frameStart; var dt = Math.Min(0.05, now - last); last = now;
                if (now >= nextCheck) { paused = Paused || Native.FullscreenBusy(); nextCheck = now + 0.5; }
                if (paused)
                {
                    if (visible) { ShowWindow(hwnd, 0); visible = false; }
                    engine.Reset();
                    Thread.Sleep(120);
                    continue;
                }
                Native.GetCursorPos(out var pt);
                engine.Move(pt.X, pt.Y);
                var l = (Native.GetAsyncKeyState(1) & 0x8000) != 0; var r = (Native.GetAsyncKeyState(2) & 0x8000) != 0;
                if ((l && !lDown) || (r && !rDown)) engine.Click(pt.X, pt.Y);
                lDown = l; rDown = r;
                engine.Step(dt);

                if (engine.Bounds(out var x0, out var y0, out var x1, out var y1))
                {
                    // не выходим за пределы всех мониторов
                    int vx = Native.GetSystemMetrics(76), vy = Native.GetSystemMetrics(77), vw = Native.GetSystemMetrics(78), vh = Native.GetSystemMetrics(79);
                    x0 = Math.Max(x0, vx); y0 = Math.Max(y0, vy); x1 = Math.Min(x1, vx + vw); y1 = Math.Min(y1, vy + vh);
                    var w = x1 - x0; var h = y1 - y0;
                    if (w > 0 && h > 0 && w <= 8192 && h <= 8192)
                    {
                        if (buf.Length < w * h * 4) buf = new byte[w * h * 4];
                        engine.Render(buf, w, h, x0, y0);
                        Present(hwnd, buf, x0, y0, w, h);
                        if (!visible) { ShowWindow(hwnd, 4); visible = true; topAt = now; }
                        else if (now - topAt > 1.5) { SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040); topAt = now; }   // снова наверх: панель или «Пуск» могли перекрыть
                    }
                }
                else if (visible) { ShowWindow(hwnd, 0); visible = false; }

                var spent = sw.Elapsed.TotalSeconds - frameStart;
                var wait = (visible ? 1.0 / 60 : 1.0 / 40) - spent;
                Thread.Sleep(wait > 0.001 ? (int)(wait * 1000) : 1);
            }
        }
        catch { }
        finally
        {
            try { if (hwnd != IntPtr.Zero) DestroyWindow(hwnd); } catch { }
            Native.timeEndPeriod(1);
        }
    }

    static void Present(IntPtr hwnd, byte[] buf, int x, int y, int w, int h)
    {
        var bi = new BITMAPINFO { bmiHeader = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 } };
        var hdc = GetDC(IntPtr.Zero);
        var mem = CreateCompatibleDC(hdc);
        var bmp = CreateDIBSection(hdc, ref bi, 0, out var bits, IntPtr.Zero, 0);
        try
        {
            if (bmp == IntPtr.Zero || bits == IntPtr.Zero) return;
            var old = SelectObject(mem, bmp);
            Marshal.Copy(buf, 0, bits, w * h * 4);
            var dst = new Native.POINT { X = x, Y = y };
            var size = new Native.SIZE { cx = w, cy = h };
            var src = new Native.POINT();
            var bf = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(hwnd, hdc, ref dst, ref size, mem, ref src, 0, ref bf, 2);
            SelectObject(mem, old);
        }
        finally
        {
            if (bmp != IntPtr.Zero) Native.DeleteObject(bmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }
}
