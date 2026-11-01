# Wallpaper Plus на Windows-машине GitHub: обои рисуются за значками рабочего стола, двигаются, пропадают после выхода.
param([string]$Out = 'results-wallpaper')
. "$PSScriptRoot/common.ps1"
New-Item -ItemType Directory -Force $Out | Out-Null
$log = Join-Path $Out 'report.txt'
function Say($t) { $t | Tee-Object -FilePath $log -Append }
$exe = (Resolve-Path 'out-test/WallpaperPlus.exe').Path
$outFull = (Resolve-Path $Out).Path
$data = Join-Path $env:LOCALAPPDATA 'GhostShell\WallpaperPlus'
function Quit-Plugin($proc, $id) {
    try { $ev = [System.Threading.EventWaitHandle]::OpenExisting("GhostShell.Plugin.Quit.$id"); [void]$ev.Set() } catch { Say "событие выхода $id не найдено" }
    for ($i = 0; $i -lt 15 -and -not $proc.HasExited; $i++) { Start-Sleep -Seconds 1 }
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue; Say "$id пришлось закрыть принудительно" }
}
# ---- 1. вид окна плагина (без показа) ----
$ui = Join-Path $Out 'ui'; New-Item -ItemType Directory -Force $ui | Out-Null
$pu = Start-Process -FilePath $exe -ArgumentList "--screenshot=$((Resolve-Path $ui).Path)" -PassThru
if (-not $pu.WaitForExit(90000)) { $pu.Kill(); Say 'ui: окно не нарисовалось за 90 секунд' }
Get-ChildItem $ui | ForEach-Object { Say ("ui: " + $_.Name + " " + $_.Length) }
if (Test-Path (Join-Path $ui 'wallpaper-plus-ui-error.txt')) { Get-Content (Join-Path $ui 'wallpaper-plus-ui-error.txt') | ForEach-Object { Say $_ } }

# ---- 2. ввоз файлов, создание своих обоев (без окна) ----
$st = Join-Path $Out 'selftest'; $samples = Join-Path $st 'samples'
New-Item -ItemType Directory -Force $samples | Out-Null
'<!doctype html><html><body style="margin:0;background:#c0f"><h1 style="color:#fff">single</h1></body></html>' | Set-Content (Join-Path $samples 'single.html') -Encoding UTF8
New-Item -ItemType Directory -Force (Join-Path $st 'pack') | Out-Null
'<!doctype html><html><body style="margin:0;background:#0c8"><h1>zip pack</h1></body></html>' | Set-Content (Join-Path $st 'pack\index.html') -Encoding UTF8
'{"Title":"Пакет из Lively","Type":0,"FileName":"index.html","Desc":"проверка","Author":"тест"}' | Set-Content (Join-Path $st 'pack\LivelyInfo.json') -Encoding UTF8
Compress-Archive -Path (Join-Path $st 'pack\*') -DestinationPath (Join-Path $samples 'pack.zip') -Force
[IO.File]::WriteAllBytes((Join-Path $samples 'tiny.png'), [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=='))
[IO.File]::WriteAllBytes((Join-Path $samples 'tiny.gif'), [Convert]::FromBase64String('R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7'))
[IO.File]::WriteAllBytes((Join-Path $samples 'clip.mp4'), [byte[]](0,0,0,24,102,116,121,112,109,112,52,50))
'MZ not a wallpaper' | Set-Content (Join-Path $samples 'bad.exe')
'x' | Set-Content (Join-Path $samples 'archive.7z')
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$evil = Join-Path $samples 'evil.zip'
$za = [IO.Compression.ZipFile]::Open($evil, 'Create')
foreach ($n in @('index.html', '../evil-outside.txt', '..\..\evil2.txt')) {
    $en = $za.CreateEntry($n); $sw = New-Object IO.StreamWriter($en.Open()); $sw.Write('<html>evil</html>'); $sw.Dispose()
}
$za.Dispose()
$ps = Start-Process -FilePath $exe -ArgumentList "--selftest=$((Resolve-Path $st).Path)" -PassThru
if (-not $ps.WaitForExit(90000)) { $ps.Kill(); Say 'selftest: не завершился' }
if (Test-Path (Join-Path $st 'selftest.txt')) { Get-Content (Join-Path $st 'selftest.txt') | ForEach-Object { Say $_ } } else { Say 'selftest.txt не создан' }
Say ("за пределы библиотеки записано: " + ((Get-ChildItem $data -Recurse -Filter 'evil*' -ErrorAction SilentlyContinue | Where-Object { $_.Name -like 'evil-outside*' -or $_.Name -like 'evil2*' } | Measure-Object).Count))

# чтобы рабочий стол был виден: сворачиваем окна
(New-Object -ComObject Shell.Application).MinimizeAll()
Start-Sleep -Seconds 3
Say (Shot (Join-Path $Out '00-до.png'))
$created = if (Test-Path (Join-Path $st 'created-id.txt')) { (Get-Content (Join-Path $st 'created-id.txt') -Raw).Trim() } else { '' }
$ids = if ($env:WP_IDS) { $env:WP_IDS.Split(',') } else { @('b-neon-grid', 'b-digital-rain', 'b-starfield', 'b-aurora', 'b-neon-clock') }
if ($created) { $ids += $created }
$first = $true
foreach ($id in $ids) {
    Say ""; Say "=== обои $id ==="
    $p = Start-Process -FilePath $exe -ArgumentList "--apply=$id" -PassThru
    Start-Sleep -Seconds 12
    Say (Shot (Join-Path $Out "01-$id-a.png"))
    Start-Sleep -Seconds 3
    Say (Shot (Join-Path $Out "01-$id-b.png"))
    if ($first) {
        Say "--- окна рабочего стола (из журнала) ---"
        if (Test-Path (Join-Path $data 'log.txt')) { Get-Content (Join-Path $data 'log.txt') | ForEach-Object { Say $_ } }
        $first = $false
    }
    Quit-Plugin $p 'wallpaper-plus'
    Start-Sleep -Seconds 3
    Say (Shot (Join-Path $Out "02-$id-после-выхода.png"))
}
