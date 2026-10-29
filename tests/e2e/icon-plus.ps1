# Проверка Icon Plus на Windows-машине GitHub: окно, перекраска, наборы (zip и 7z), настоящие значки Проводника до и после.
param([string]$Out = 'results-icons')
. "$PSScriptRoot/common.ps1"
New-Item -ItemType Directory -Force $Out | Out-Null
$log = Join-Path $Out 'report.txt'
function Say($t) { $t | Tee-Object -FilePath $log -Append }
$exe = (Resolve-Path 'out-test/IconPlus.exe').Path
$outFull = (Resolve-Path $Out).Path

function Run-Exe([string]$name, [string[]]$arguments, [int]$seconds = 120) {
    $p = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru
    if (-not $p.WaitForExit($seconds * 1000)) { $p.Kill(); Say "$name : не завершился за $seconds с" } else { Say "$name : код $($p.ExitCode)" }
}

Say "=== окно настроек (снимки без показа) ==="
New-Item -ItemType Directory -Force (Join-Path $outFull 'ui') | Out-Null
Run-Exe 'screenshot' @("--screenshot=$(Join-Path $outFull 'ui')") 90
Get-ChildItem (Join-Path $outFull 'ui') | ForEach-Object { Say "  файл: $($_.Name) ($($_.Length) байт)" }

Say ""
Say "=== самопроверка: перекраска и возврат ==="
New-Item -ItemType Directory -Force (Join-Path $outFull 'self') | Out-Null
Run-Exe 'selftest' @("--selftest=$(Join-Path $outFull 'self')") 300
if (Test-Path (Join-Path $outFull 'self/report.txt')) { Get-Content (Join-Path $outFull 'self/report.txt') | ForEach-Object { Say $_ } }

Say ""
Say "значок папок после самопроверки (должен быть чист): " + ((Get-ItemProperty 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons' -ErrorAction SilentlyContinue) | Out-String).Trim()
Say ""
Say "=== набор значков: архивы ==="
Add-Type -AssemblyName System.Drawing
$packDir = Join-Path $env:RUNNER_TEMP 'icon-pack-src'
New-Item -ItemType Directory -Force $packDir | Out-Null
$names = @{
    'folder' = '#F5B400'; 'hard drive' = '#4D7CFF'; 'usb drive' = '#20C27A'; 'dvd' = '#C65DFF'; 'computer' = '#00B7C3'; 'recycle bin empty' = '#8B86B8'
    'recycle bin full' = '#FF8A3D'; 'network' = '#3DDC97'; 'user' = '#E85D9E'; 'txt' = '#CFCFCF'; 'doc' = '#2B7BFF'; 'xls' = '#27AE60'; 'pdf' = '#E74C3C'
    'zip' = '#C9A227'; 'image' = '#9B59B6'; 'music' = '#1ABC9C'; 'video' = '#F39C12'; 'html' = '#E67E22'; 'code' = '#34495E'; 'script' = '#7F8C8D'
}
foreach ($n in $names.Keys) {
    $bmp = New-Object System.Drawing.Bitmap 128, 128
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($names[$n]))
    $g.FillEllipse($brush, 6, 6, 116, 116)
    $font = New-Object System.Drawing.Font('Segoe UI', 40, [System.Drawing.FontStyle]::Bold)
    $g.DrawString($n.Substring(0, 1).ToUpper(), $font, [System.Drawing.Brushes]::White, 34, 30)
    $g.Dispose()
    $bmp.Save((Join-Path $packDir "$n.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}
$zip = Join-Path $env:RUNNER_TEMP 'TestPack.zip'
Compress-Archive -Path (Join-Path $packDir '*') -DestinationPath $zip -Force
$sevenZ = 'C:\Program Files\7-Zip\7z.exe'
$z7 = Join-Path $env:RUNNER_TEMP 'TestPack7.7z'
if (Test-Path $sevenZ) { & $sevenZ a -t7z $z7 (Join-Path $packDir '*') | Out-Null } else { Say '7-Zip не найден' }
New-Item -ItemType Directory -Force (Join-Path $outFull 'pack') | Out-Null
Run-Exe 'pack-test zip' @("--pack-test=$zip", "--out=$(Join-Path $outFull 'pack')") 300
if (Test-Path $z7) { Run-Exe 'pack-test 7z' @("--pack-test=$z7", "--out=$(Join-Path $outFull 'pack')") 300 }
foreach ($f in Get-ChildItem (Join-Path $outFull 'pack') -Filter 'pack-*.txt') { Say "--- $($f.Name)"; Get-Content $f.FullName | ForEach-Object { Say $_ } }

function Restart-Explorer {
    Get-Process explorer -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 4
    if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer.exe | Out-Null; Start-Sleep -Seconds 4 }
}
function Open-Box { Start-Process explorer.exe -ArgumentList $box | Out-Null; Start-Sleep -Seconds 6 }

Say ""
Say "=== Проводник: значки до и после перекраски ==="
$box = Join-Path $env:TEMP 'IconPlusTest'
Open-Box
Say (Shot (Join-Path $Out 'explorer-1-до.png'))
Run-Exe 'apply-color' @('--apply-color=#FF2A9D') 240
Start-Sleep -Seconds 8
Say (Shot (Join-Path $Out 'explorer-2-перекрашено.png'))
Restart-Explorer; Open-Box
Say (Shot (Join-Path $Out 'explorer-2b-после-перезапуска-проводника.png'))
Run-Exe 'restore' @('--restore') 120
Restart-Explorer; Open-Box
Say (Shot (Join-Path $Out 'explorer-3-вернули.png'))

Say ""
Say "=== опыт: значок обычной папки без прав администратора? ==="
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap 32, 32
$g = [System.Drawing.Graphics]::FromImage($bmp); $g.Clear([System.Drawing.Color]::FromArgb(255, 0, 140)); $g.Dispose()
$ico = Join-Path $env:TEMP 'folder-test.ico'
$ic = [System.Drawing.Icon]::FromHandle($bmp.GetHicon())
$fs = [System.IO.File]::Create($ico); $ic.Save($fs); $fs.Close()
$probeFile = Join-Path $outFull 'folder-probe.txt'
function Probe-Folder([string]$label) {
    $pp = Start-Process -FilePath $exe -ArgumentList "--probe=$probeFile" -PassThru
    [void]$pp.WaitForExit(60000)
    $line = (Get-Content $probeFile | Where-Object { $_ -like 'folder=*' })
    Say "$label : $line"
}
Probe-Folder 'до'
foreach ($variant in @(
    @{ Name = 'HKCU Shell Icons 3'; Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons' },
    @{ Name = 'HKLM Shell Icons 3 (нужны права администратора)'; Path = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons' })) {
    try {
        New-Item -Path $variant.Path -Force | Out-Null
        Set-ItemProperty -Path $variant.Path -Name '3' -Value "$ico,0" -Type String
        Restart-Explorer; Open-Box
        Probe-Folder $variant.Name
        Say (Shot (Join-Path $Out ("folder-" + ($variant.Name -replace '[^A-Za-z]', '') + '.png')))
    } catch { Say ("$($variant.Name): ошибка " + $_.Exception.Message) }
    finally { try { Remove-ItemProperty -Path $variant.Path -Name '3' -ErrorAction SilentlyContinue; if (-not (Get-Item $variant.Path -ErrorAction SilentlyContinue).GetValueNames()) { Remove-Item $variant.Path -ErrorAction SilentlyContinue } } catch { } }
}
Restart-Explorer

Say ""
Say "=== окно плагина на экране (вкладка «Сайт с иконками») ==="
$p = Start-Process -FilePath $exe -ArgumentList '--tab=2' -PassThru
Start-Sleep -Seconds 14
Say (Shot (Join-Path $Out 'window-site.png'))
$p | Stop-Process -Force -ErrorAction SilentlyContinue
$p = Start-Process -FilePath $exe -ArgumentList '--tab=0' -PassThru
Start-Sleep -Seconds 8
Say (Shot (Join-Path $Out 'window-color.png'))
$p | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process explorer -ErrorAction SilentlyContinue | Out-Null
Say "=== конец ==="
