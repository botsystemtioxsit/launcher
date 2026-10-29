# Опыты на Windows-машине GitHub: можно ли сделать прозрачным ТОЛЬКО задний фон окна (не заголовок, не текст, не картинки)?
# Способ: «цветовой ключ» слоёного окна — пиксели одного цвета (цвета фона) становятся прозрачными, остальное остаётся как есть.
param([string]$Out = 'results-bg')
. "$PSScriptRoot/common.ps1"
New-Item -ItemType Directory -Force $Out | Out-Null
$log = Join-Path $Out 'report.txt'
function Say($t) { $t | Tee-Object -FilePath $log -Append }

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class X
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] struct Accent { public int State; public int Flags; public uint Color; public int Anim; }
    [StructLayout(LayoutKind.Sequential)] struct Attr { public int A; public IntPtr D; public int S; }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetL(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetL(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte a, uint f);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr h, ref Attr d);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint f);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);

    public static void Place(IntPtr h) { ShowWindow(h, 9); SetWindowPos(h, IntPtr.Zero, 40, 30, 760, 480, 0x0004 | 0x0040); SetForegroundWindow(h); }

    public static void ColorKey(IntPtr h, uint colorRef)
    {
        SetL(h, -20, (IntPtr)(GetL(h, -20).ToInt64() | 0x80000));
        SetLayeredWindowAttributes(h, colorRef, 0, 1);
    }
    public static void Alpha(IntPtr h, byte a)
    {
        SetL(h, -20, (IntPtr)(GetL(h, -20).ToInt64() | 0x80000));
        SetLayeredWindowAttributes(h, 0, a, 2);
    }
    public static void Unlayer(IntPtr h) { SetL(h, -20, (IntPtr)(GetL(h, -20).ToInt64() & ~0x80000L)); }
    public static void AccentSet(IntPtr h, int state, uint abgr)
    {
        var p = new Accent { State = state, Flags = 0, Color = abgr };
        int size = Marshal.SizeOf(p);
        IntPtr ptr = Marshal.AllocHGlobal(size);
        Marshal.StructureToPtr(p, ptr, false);
        var a = new Attr { A = 19, D = ptr, S = size };
        SetWindowCompositionAttribute(h, ref a);
        Marshal.FreeHGlobal(ptr);
    }

    // Самый частый цвет в нижних 60% окна (так обходим заголовок и вкладки) и его доля.
    public static string Dominant(IntPtr h, out uint colorRef)
    {
        colorRef = 0;
        try { return Dominant2(h, out colorRef); } catch (Exception ex) { return "ошибка: " + ex.Message; }
    }

    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(System.Drawing.Point p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, System.Text.StringBuilder s, int n);
    public static string UnderPoint(int x, int y)
    {
        IntPtr h = WindowFromPoint(new System.Drawing.Point(x, y));
        var sb = new System.Text.StringBuilder(256); GetClassNameW(h, sb, 256);
        return h + " класс " + sb;
    }

    static string Dominant2(IntPtr h, out uint colorRef)
    {
        colorRef = 0;
        RECT r; GetWindowRect(h, out r);
        int w = r.R - r.L, hh = r.B - r.T;
        if (w < 50 || hh < 50) return "окно слишком мало";
        using (var bmp = new Bitmap(w, hh, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp)) { IntPtr dc = g.GetHdc(); PrintWindow(h, dc, 2); g.ReleaseHdc(dc); }
            var bd = bmp.LockBits(new Rectangle(0, 0, w, hh), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var counts = new Dictionary<int, int>();
            int total = 0;
            for (int y = hh * 4 / 10; y < hh; y += 5)
            {
                for (int x = 0; x < w; x += 5)
                {
                    int px = Marshal.ReadInt32(bd.Scan0, y * bd.Stride + x * 4) & 0xFFFFFF;
                    int c; counts.TryGetValue(px, out c); counts[px] = c + 1; total++;
                }
            }
            bmp.UnlockBits(bd);
            int best = 0, bestN = 0;
            foreach (var kv in counts) if (kv.Value > bestN) { best = kv.Key; bestN = kv.Value; }
            int rr = (best >> 16) & 0xFF, gg = (best >> 8) & 0xFF, bb = best & 0xFF;
            colorRef = (uint)((bb << 16) | (gg << 8) | rr);
            return string.Format("#{0:X2}{1:X2}{2:X2} доля {3:P0}", rr, gg, bb, bestN / (double)Math.Max(1, total));
        }
    }
}
'@

$html = @'
<!doctype html><html><head><meta charset="utf-8"><title>BG-TEST-TITLE</title></head>
<body style="margin:0;background:#0F0F0F;color:#fff;font:28px Segoe UI">
<h1 style="margin:20px">ТОЛЬКО ФОН ПРОЗРАЧНЫЙ</h1>
<p style="margin:20px">Этот текст, картинка и видео должны остаться плотными.</p>
<div style="margin:20px;width:260px;height:130px;background:linear-gradient(135deg,#ff2a9d,#00f0ff,#2cff9a)"></div>
<video style="margin:20px;width:260px;height:120px;background:#223" controls></video>
</body></html>
'@

function Find-Window([string]$mask) {
    foreach ($line in [W]::List()) { if ($line -match $mask -and $line -match '\|vis=1\|') { return [IntPtr][long]($line.Split('|')[0]) } }
    return [IntPtr]::Zero
}

function Experiment([string]$name, [IntPtr]$h) {
    Say ""
    Say "=== $name : окно $h ==="
    if ($h -eq [IntPtr]::Zero) { Say "окно не найдено"; return }
    [X]::Place($h); Start-Sleep -Seconds 2
    [uint32]$key = 0
    try { Say ("главный цвет: " + [X]::Dominant($h, [ref]$key) + "  (COLORREF 0x" + $key.ToString('X6') + ")") } catch { Say ("главный цвет: ошибка " + $_.Exception.Message) }
    Say (Shot (Join-Path $Out "$name-1-до.png"))
    [X]::ColorKey($h, $key); Start-Sleep -Seconds 2
    Say (Shot (Join-Path $Out "$name-2-цветовой-ключ.png"))
    Say ("под точкой фона (ниже середины окна): " + [X]::UnderPoint(600, 420) + "   под текстом: " + [X]::UnderPoint(150, 160))
    [X]::AccentSet($h, 4, 0x80181010); Start-Sleep -Seconds 2   # акрил с оттенком
    Say (Shot (Join-Path $Out "$name-3-ключ-и-акрил.png"))
    [X]::AccentSet($h, 0, 0); [X]::Unlayer($h); Start-Sleep -Seconds 1
}

# 1) обычное окно Windows Forms (белый фон, чёрный текст)
$formScript = Join-Path (Get-Location) 'form.ps1'
@'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$f = New-Object System.Windows.Forms.Form
$f.Text = "BG-TEST-FORM"; $f.Width = 760; $f.Height = 480; $f.BackColor = [System.Drawing.Color]::White
$l = New-Object System.Windows.Forms.Label
$l.Text = "TEXT STAYS OPAQUE"; $l.Font = New-Object System.Drawing.Font("Segoe UI", 28); $l.AutoSize = $true; $l.Left = 30; $l.Top = 40
$f.Controls.Add($l)
$p = New-Object System.Windows.Forms.Panel
$p.BackColor = [System.Drawing.Color]::Crimson; $p.Left = 30; $p.Top = 140; $p.Width = 200; $p.Height = 100
$f.Controls.Add($p)
[void]$f.ShowDialog()
'@ | Set-Content $formScript -Encoding UTF8
$form = Start-Process pwsh -ArgumentList '-NoProfile', '-File', $formScript -PassThru
Start-Sleep -Seconds 5
Experiment 'форма' (Find-Window 'BG-TEST-FORM')

# 2) браузеры (Chromium): Edge и Chrome со страницей с тёмным фоном
$page = Join-Path (Get-Location) 'bg-test.html'
Set-Content $page $html -Encoding UTF8
$uri = ([System.Uri]$page).AbsoluteUri
$edge = (Get-Command msedge -ErrorAction SilentlyContinue).Source
if (-not $edge) { $edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe' }
Say "Edge: $edge  существует: $(Test-Path $edge)"
if (Test-Path $edge) {
    Start-Process $edge -ArgumentList '--new-window', '--no-first-run', '--disable-features=msEdgeFirstRunExperience', $uri
    Start-Sleep -Seconds 10
    Experiment 'edge' (Find-Window 'BG-TEST-TITLE')
}
$chrome = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
Say "Chrome: $chrome  существует: $(Test-Path $chrome)"
if (Test-Path $chrome) {
    Get-Process msedge -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    Start-Process $chrome -ArgumentList '--new-window', '--no-first-run', '--no-default-browser-check', $uri
    Start-Sleep -Seconds 10
    Experiment 'chrome' (Find-Window 'BG-TEST-TITLE')
}
Say "=== конец ==="
