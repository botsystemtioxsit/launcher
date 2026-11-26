using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SeasonPlusApp;

// Прозрачное окно поверх всего рабочего стола: клики проходят сквозь него, фокус оно не берёт.
// В нём движок рисует то, что падает, лежит на окнах и висит гирляндами. В полноэкранных играх и видео — не рисует.
sealed class SeasonOverlay
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
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);

    const uint WS_POPUP = 0x80000000;
    const uint WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
    const string ClassName = "GhostShellSeasonPlusOverlay";
    static readonly WndProcDelegate Proc = (h, m, w, l) => DefWindowProc(h, m, w, l);
    static bool registered;

    SeasonSettings settings = new();
    Thread? thread;
    volatile bool run;
    public volatile bool Paused;
    public bool Running => run && thread != null && thread.IsAlive;
    public volatile bool Healthy = true;
    public int LastParticles;
    public string LastError = "";
    SeasonEngine? current;
    volatile bool fireworks;

    public void SetSettings(SeasonSettings s) { settings = s.Clone(); }
    public void Fireworks() { fireworks = true; }

    public void Start(SeasonSettings s)
    {
        settings = s.Clone();
        if (Running) return;
        run = true;
        thread = new Thread(Loop) { IsBackground = true, Name = "SeasonPlus.Overlay" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public void Stop()
    {
        run = false;
        try { thread?.Join(3000); } catch { }
        thread = null;
    }

    void Loop()
    {
        Native.timeBeginPeriod(1);
        var hwnd = IntPtr.Zero; IntPtr mem = IntPtr.Zero, dib = IntPtr.Zero, bits = IntPtr.Zero, hdc = IntPtr.Zero;
        try
        {
            var inst = GetModuleHandle(null);
            if (!registered)
            {
                var wc = new WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = Proc, hInstance = inst, lpszClassName = ClassName };
                RegisterClassEx(ref wc);
                registered = true;
            }
            hwnd = CreateWindowEx(WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, ClassName, "Ghost Shell Season Plus", WS_POPUP, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
            if (hwnd == IntPtr.Zero) { LastError = "не удалось создать окно"; Healthy = false; return; }

            var engine = new SeasonEngine(Environment.TickCount) { S = settings };
            current = engine;
            var raster = new Raster();
            var sw = Stopwatch.StartNew();
            var pid = (uint)Environment.ProcessId;
            double last = 0, nextScan = 0, nextCheck = 0, nextMetrics = 0, topAt = 0;
            var visible = false; var paused = false; var warmed = false;
            int vx = 0, vy = 0, vw = 0, vh = 0, dibW = 0, dibH = 0;
            while (run)
            {
                var frameStart = sw.Elapsed.TotalSeconds;
                while (PeekMessage(out var m, IntPtr.Zero, 0, 0, 1)) { TranslateMessage(ref m); DispatchMessage(ref m); }
                var now = frameStart; var dt = Math.Min(0.05, now - last); last = now;
                engine.S = settings;
                if (fireworks) { fireworks = false; engine.StartFireworks(); }

                if (now >= nextMetrics)
                {
                    nextMetrics = now + 2;
                    var nx = Native.GetSystemMetrics(76); var ny = Native.GetSystemMetrics(77); var nw = Native.GetSystemMetrics(78); var nh = Native.GetSystemMetrics(79);
                    nw = Math.Clamp(nw, 100, 8192); nh = Math.Clamp(nh, 100, 4320);
                    if (nx != vx || ny != vy || nw != vw || nh != vh)
                    {
                        vx = nx; vy = ny; vw = nw; vh = nh;
                        engine.OX = vx; engine.OY = vy; engine.W = vw; engine.H = vh;
                        raster.Resize(vw, vh);
                    }
                }
                if (now >= nextCheck) { paused = Paused || Native.FullscreenBusy(); nextCheck = now + 0.5; }
                if (paused)
                {
                    if (visible) { ShowWindow(hwnd, 0); visible = false; }
                    Thread.Sleep(150);
                    continue;
                }
                if (now >= nextScan)
                {
                    nextScan = now + 0.12;
                    engine.SetSurfaces(WindowScanner.Scan(pid, vx, vy, vw, vh));
                    if (!warmed) { engine.Warm(8); warmed = true; }
                }
                engine.StepTo(dt);
                LastParticles = engine.Count;

                raster.Clear();
                engine.Render(raster);
                if (vw > 0 && vh > 0)
                {
                    if (dibW != vw || dibH != vh || dib == IntPtr.Zero)
                    {
                        if (dib != IntPtr.Zero) { DeleteObject(dib); dib = IntPtr.Zero; }
                        if (mem != IntPtr.Zero) { DeleteDC(mem); mem = IntPtr.Zero; }
                        if (hdc != IntPtr.Zero) { ReleaseDC(IntPtr.Zero, hdc); hdc = IntPtr.Zero; }
                        hdc = GetDC(IntPtr.Zero);
                        mem = CreateCompatibleDC(hdc);
                        var bi = new BITMAPINFO { bmiHeader = new BITMAPINFOHEADER { biSize = 40, biWidth = vw, biHeight = -vh, biPlanes = 1, biBitCount = 32 } };
                        dib = CreateDIBSection(hdc, ref bi, 0, out bits, IntPtr.Zero, 0);
                        if (dib != IntPtr.Zero) SelectObject(mem, dib);
                        dibW = vw; dibH = vh;
                    }
                    if (dib != IntPtr.Zero && bits != IntPtr.Zero)
                    {
                        Marshal.Copy(raster.Px, 0, bits, vw * vh * 4);
                        var dst = new Native.POINT { X = vx, Y = vy };
                        var size = new Native.SIZE { cx = vw, cy = vh };
                        var src = new Native.POINT();
                        var bf = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                        UpdateLayeredWindow(hwnd, hdc, ref dst, ref size, mem, ref src, 0, ref bf, 2);
                        if (!visible) { ShowWindow(hwnd, 4); visible = true; topAt = now; }
                        else if (now - topAt > 1.5) { SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040); topAt = now; }
                    }
                }
                var spent = sw.Elapsed.TotalSeconds - frameStart;
                var wait = 1.0 / 30 - spent;
                Thread.Sleep(wait > 0.001 ? (int)(wait * 1000) : 1);
            }
        }
        catch (Exception ex) { LastError = ex.Message; Healthy = false; }
        finally
        {
            try { if (hwnd != IntPtr.Zero) DestroyWindow(hwnd); } catch { }
            try { if (dib != IntPtr.Zero) DeleteObject(dib); } catch { }
            try { if (mem != IntPtr.Zero) DeleteDC(mem); } catch { }
            try { if (hdc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, hdc); } catch { }
            current = null;
            Native.timeEndPeriod(1);
        }
    }
}
