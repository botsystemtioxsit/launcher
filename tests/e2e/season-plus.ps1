# Проверка Season Plus на Windows-машине GitHub: окно (снимки без показа), самопроверка логики (сугробы, календарь, закрытые куски окон,
# рисование) со снимками сцен всех сезонов и праздников, и проба на настоящем экране: прозрачное окно поверх рабочего стола, сканер окон, снимок экрана.
param([string]$Out = 'results-season')
New-Item -ItemType Directory -Force $Out | Out-Null
$outFull = (Resolve-Path $Out).Path
$log = Join-Path $outFull 'report.txt'
function Say($t) { $t | Tee-Object -FilePath $log -Append }
$exe = (Resolve-Path 'out-test/SeasonPlus.exe').Path

Say '=== окно (снимки без показа) ==='
$ui = Join-Path $outFull 'ui'
New-Item -ItemType Directory -Force $ui | Out-Null
$p = Start-Process -FilePath $exe -ArgumentList "--screenshot=$ui" -PassThru
if (-not $p.WaitForExit(180000)) { $p.Kill(); Say 'screenshot: не завершился за 3 минуты' } else { Say "screenshot: код $($p.ExitCode)" }
Get-ChildItem $ui | ForEach-Object { Say "  файл: $($_.Name) ($($_.Length) байт)" }
if (Test-Path (Join-Path $ui 'season-plus-ui-error.txt')) { Get-Content (Join-Path $ui 'season-plus-ui-error.txt') | ForEach-Object { Say $_ } }

Say ''
Say '=== самопроверка ==='
$self = Join-Path $outFull 'self'
New-Item -ItemType Directory -Force $self | Out-Null
$p = Start-Process -FilePath $exe -ArgumentList "--selftest=$self" -PassThru
if (-not $p.WaitForExit(600000)) { $p.Kill(); Say 'selftest: не завершился за 10 минут' } else { Say "selftest: код $($p.ExitCode)" }
$rep = Join-Path $self 'selftest.txt'
if (Test-Path $rep) { Get-Content $rep | ForEach-Object { Say $_ } } else { Say 'selftest.txt не появился' }
Get-ChildItem $self -Filter *.png | ForEach-Object { Say "  снимок: $($_.Name) ($($_.Length) байт)" }

Say ''
Say '=== настоящий экран: прозрачное окно поверх рабочего стола ==='
$ov = Join-Path $outFull 'overlay'
New-Item -ItemType Directory -Force $ov | Out-Null
# несколько окон, на которые должен лечь снег
$notes = @(); 1..2 | ForEach-Object { $notes += Start-Process notepad -PassThru }
Start-Sleep -Seconds 3
$p = Start-Process -FilePath $exe -ArgumentList "--overlay-test=$ov" -PassThru
Start-Sleep -Seconds 5
Add-Type -AssemblyName System.Windows.Forms 2>$null
try {
    Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices; using System.Text;
public static class ShotS {
  [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
  [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
  [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
  [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
  [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
  [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
  [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, uint rop);
  [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
  [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
  [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, byte[] bits, byte[] bi, uint usage);
  public static string Shot(string file) {
    int w = GetSystemMetrics(0), h = GetSystemMetrics(1);
    IntPtr sdc = GetDC(IntPtr.Zero), mdc = CreateCompatibleDC(sdc), bmp = CreateCompatibleBitmap(sdc, w, h);
    IntPtr old = SelectObject(mdc, bmp);
    BitBlt(mdc, 0, 0, w, h, sdc, 0, 0, 0x00CC0020 | 0x40000000);
    byte[] bi = new byte[40]; BitConverter.GetBytes(40).CopyTo(bi, 0); BitConverter.GetBytes(w).CopyTo(bi, 4); BitConverter.GetBytes(-h).CopyTo(bi, 8);
    BitConverter.GetBytes((short)1).CopyTo(bi, 12); BitConverter.GetBytes((short)32).CopyTo(bi, 14);
    byte[] px = new byte[w * h * 4]; GetDIBits(mdc, bmp, 0, (uint)h, px, bi, 0);
    SelectObject(mdc, old); DeleteObject(bmp); DeleteDC(mdc); ReleaseDC(IntPtr.Zero, sdc);
    // сохраняем как BMP (PowerShell потом переведёт в PNG средствами .NET)
    using (var fs = new System.IO.FileStream(file, System.IO.FileMode.Create)) {
      var hdr = new byte[54]; hdr[0]=66; hdr[1]=77; BitConverter.GetBytes(54 + px.Length).CopyTo(hdr, 2); BitConverter.GetBytes(54).CopyTo(hdr, 10);
      BitConverter.GetBytes(40).CopyTo(hdr, 14); BitConverter.GetBytes(w).CopyTo(hdr, 18); BitConverter.GetBytes(-h).CopyTo(hdr, 22);
      BitConverter.GetBytes((short)1).CopyTo(hdr, 26); BitConverter.GetBytes((short)32).CopyTo(hdr, 28);
      fs.Write(hdr, 0, 54); fs.Write(px, 0, px.Length);
    }
    return w + "x" + h;
  }
}
'@
    Say ("снимок экрана: " + [ShotS]::Shot((Join-Path $ov 'screen.bmp')))
} catch { Say ("снимок экрана не удался: " + $_) }
if (-not $p.WaitForExit(60000)) { $p.Kill(); Say 'overlay-test: не завершился за минуту' } else { Say "overlay-test: код $($p.ExitCode)" }
$orep = Join-Path $ov 'overlay-test.txt'
if (Test-Path $orep) { Get-Content $orep | ForEach-Object { Say $_ } } else { Say 'overlay-test.txt не появился' }
$notes | ForEach-Object { try { $_.Kill() } catch { } }
Get-ChildItem $outFull -Recurse -File | ForEach-Object { Say ("  итого файл: " + $_.FullName.Substring($outFull.Length) + " " + $_.Length) }
