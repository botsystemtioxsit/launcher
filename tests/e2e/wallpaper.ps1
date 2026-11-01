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
# чтобы рабочий стол был виден: сворачиваем окна
(New-Object -ComObject Shell.Application).MinimizeAll()
Start-Sleep -Seconds 3
Say (Shot (Join-Path $Out '00-до.png'))
$ids = if ($env:WP_IDS) { $env:WP_IDS.Split(',') } else { @('b-neon-grid') }
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
