# Проверка нативного (C++) Season Plus на Windows-машине GitHub: логика (тот же набор проверок), снимки сцен и окна, прозрачное окно поверх рабочего стола
# на настоящем экране (DirectComposition), нагрузка процессора и снимок экрана.
param([string]$Out = 'results-native')
New-Item -ItemType Directory -Force $Out | Out-Null
$outFull = (Resolve-Path $Out).Path
$log = Join-Path $outFull 'report.txt'
function Say($t) { $t | Tee-Object -FilePath $log -Append }
$exe = (Resolve-Path 'native-dist/GhostStyle.exe').Path
$env:GS_TRACE = Join-Path $outFull 'trace.log'
$len = (Get-Item $exe).Length
Say "GhostStyle.exe: $len байт"
Say ("Windows: " + [Environment]::OSVersion.Version)

Say ''
Say '=== логика (под Windows) ==='
$lt = Join-Path (Split-Path $exe) 'sp_logic_test.exe'
& $lt 2>$null | Tee-Object -FilePath (Join-Path $outFull 'logic-test.txt') | ForEach-Object { Say $_ }

Say ''
Say '=== снимки сцен и окна ==='
$ui = Join-Path $outFull 'ui'
New-Item -ItemType Directory -Force $ui | Out-Null
$p = Start-Process -FilePath $exe -ArgumentList "--screenshot=$ui" -PassThru
if (-not $p.WaitForExit(240000)) { $p.Kill(); Say 'screenshot: не завершился за 4 минуты' } else { Say "screenshot: код $($p.ExitCode)" }
Get-ChildItem $ui | ForEach-Object { Say "  файл: $($_.Name) ($($_.Length) байт)" }
if (Test-Path (Join-Path $ui 'screenshot-log.txt')) { Get-Content (Join-Path $ui 'screenshot-log.txt') | ForEach-Object { Say $_ } }

Say ''
Say '=== настоящий экран: прозрачное окно поверх рабочего стола ==='
$ov = Join-Path $outFull 'overlay'
New-Item -ItemType Directory -Force $ov | Out-Null
$notes = @(); 1..2 | ForEach-Object { $notes += Start-Process notepad -PassThru }
Start-Sleep -Seconds 3
$env:GS_DUMP = Join-Path $ov 'overlay-frame.png'
$p = Start-Process -FilePath $exe -ArgumentList "--overlay-test=$ov" -PassThru
Start-Sleep -Seconds 6
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class ShotN {
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
try { Say ("снимок экрана: " + [ShotN]::Shot((Join-Path $ov 'screen.bmp'))) } catch { Say ("снимок экрана не удался: " + $_) }
if (-not $p.WaitForExit(90000)) { $p.Kill(); Say 'overlay-test: не завершился за 90 секунд' } else { Say "overlay-test: код $($p.ExitCode)" }
$orep = Join-Path $ov 'overlay-test.txt'
if (Test-Path $orep) { Get-Content $orep | ForEach-Object { Say $_ } } else { Say 'overlay-test.txt не появился' }
$notes | ForEach-Object { try { $_.Kill() } catch { } }

Say ''
Say '=== одна копия и выход по просьбе лаунчера ==='
$env:APPDATA = Join-Path $outFull 'appdata'
New-Item -ItemType Directory -Force $env:APPDATA | Out-Null
$a = Start-Process -FilePath $exe -ArgumentList '--background' -PassThru
Start-Sleep -Seconds 2
Say ("без включённых эффектов фоновый запуск сразу выходит: " + $a.HasExited)
if (Test-Path $env:GS_TRACE) { Say '=== журнал шагов ==='; Get-Content $env:GS_TRACE | Select-Object -Last 40 | ForEach-Object { Say $_ } }
Get-ChildItem $outFull -Recurse -File | ForEach-Object { Say ("  итого файл: " + $_.FullName.Substring($outFull.Length) + " " + $_.Length) }
