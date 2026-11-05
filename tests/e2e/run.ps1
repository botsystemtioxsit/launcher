# Проверка плагинов на настоящей Windows-машине GitHub: что получилось, видно по файлам и снимкам экрана в папке results.
param([string]$Out = 'results')
. "$PSScriptRoot/common.ps1"
New-Item -ItemType Directory -Force $Out | Out-Null
$log = Join-Path $Out 'report.txt'
function Say($t) { $t | Tee-Object -FilePath $log -Append }

$data = Join-Path $env:APPDATA 'BattleLauncher'
New-Item -ItemType Directory -Force $data | Out-Null
$status = Join-Path $data 'taskbar-plus-engine-status.txt'
$cfg = Join-Path $data 'taskbar-plus-engine.cfg'

function Quit-Plugin($proc, $id) {
    try { $ev = [System.Threading.EventWaitHandle]::OpenExisting("GhostShell.Plugin.Quit.$id"); [void]$ev.Set() } catch { Say "событие выхода $id не найдено" }
    for ($i = 0; $i -lt 15 -and -not $proc.HasExited; $i++) { Start-Sleep -Seconds 1 }
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue; Say "$id пришлось закрыть принудительно" }
}

# Сценарий для панели: настройки Taskbar Plus + пожелание Color Plus -> движок -> снимок -> дерево -> выход (всё возвращается).
function Run-Taskbar([string]$name, $tp, $link, [bool]$dumpTree = $false) {
    Say ""
    Say "=== Панель: $name ==="
    ($tp | ConvertTo-Json -Depth 6) | Set-Content (Join-Path $data 'taskbar-plus-settings.json') -Encoding UTF8
    if ($link) { ($link | ConvertTo-Json) | Set-Content (Join-Path $data 'color-plus-taskbar.json') -Encoding UTF8 }
    else { Remove-Item (Join-Path $data 'color-plus-taskbar.json') -ErrorAction SilentlyContinue }
    $p = Start-Process -FilePath 'out-test/TaskbarPlus.exe' -ArgumentList '--background' -PassThru
    $st = Wait-File $status { param($t) $t -match 'state=ready' -and $t -match 'engine=7' -and $t -match 'bgfill=[1-9]' -and $t -match 'applied=1' } 60
    Start-Sleep -Seconds 5
    if ($dumpTree -and (Test-Path $cfg)) {
        # просим движок записать дерево элементов (счётчик dump в файле настроек)
        $c = Get-Content $cfg -Raw
        $c = [regex]::Replace($c, 'dump=\d+', 'dump=' + (Get-Random -Minimum 100000 -Maximum 999999))
        Set-Content $cfg $c -NoNewline
        Start-Sleep -Seconds 3
        Copy-Item (Join-Path $data 'taskbar-plus-xaml-tree.txt') (Join-Path $Out 'tree.txt') -Force -ErrorAction SilentlyContinue
    }
    Say (Shot (Join-Path $Out "$name.png"))
    Say ((Get-Content $status -Raw -ErrorAction SilentlyContinue) -replace "`r?`n", ' | ')
    Copy-Item (Join-Path $data 'taskbar-plus-engine.log') (Join-Path $Out "$name.log") -Force -ErrorAction SilentlyContinue
    Quit-Plugin $p 'taskbar-plus'
    Start-Sleep -Seconds 3
}

Say "=== Среда ==="
Say ("Windows: " + [System.Environment]::OSVersion.Version + "  сеанс: " + [System.Diagnostics.Process]::GetCurrentProcess().SessionId)
Say ("Экран: " + [System.Windows.Forms.SystemInformation]::VirtualScreen)
Say ("explorer: " + ((Get-Process explorer -ErrorAction SilentlyContinue | ForEach-Object { $_.Id }) -join ', ') + "  Shell_TrayWnd: " + [W]::FindWindow('Shell_TrayWnd', $null))
Say (Shot (Join-Path $Out '00-до.png'))

# ---------- Внешний вид окон настроек (рисуются в PNG без показа) ----------
Say ""
Say "=== Окна настроек ==="
foreach ($e in 'TaskbarPlus') {
    $pr = Start-Process -FilePath "out-test/$e.exe" -ArgumentList "--screenshot=$((Resolve-Path $Out).Path)" -PassThru
    if (-not $pr.WaitForExit(60000)) { $pr.Kill(); Say "$e --screenshot: не завершился" } else { Say "$e --screenshot: код $($pr.ExitCode)" }
}
Get-ChildItem $Out -Filter '*ui*' | ForEach-Object { Say "файл: $($_.Name) ($($_.Length) байт)" }

# ---------- Taskbar Plus 3: панель (подробные проверки — в сценарии taskbar; здесь короткий прогон вместе с остальными плагинами) ----------
Run-Taskbar '10-плавающая' (@{ UseXaml = $true; XamlConsentShown = $true; Shape = 'pill'; Radius = 18; IslandPadding = 10; KeepEdges = $true }) @{ Active = $true; BgMode = 'solid'; BgColor = '#FF2A9D'; BgOpacity = 100; HideStroke = $true } $true
Run-Taskbar '11-острова-каждый' (@{ UseXaml = $true; XamlConsentShown = $true; Shape = 'islands'; IslandMode = 'each'; IslandStyle = 'float'; Radius = 14; IslandGap = 8 }) @{ Active = $true; BgMode = 'solid'; BgColor = '#1E6BFF'; BgOpacity = 85; HideStroke = $true }
Run-Taskbar '12-вкладка' (@{ UseXaml = $true; XamlConsentShown = $true; Shape = 'tab'; Radius = 16; IslandPadding = 10; KeepEdges = $true }) @{ Active = $true; BgMode = 'solid'; BgColor = '#101018'; BgOpacity = 90; HideStroke = $true }

# после выхода из плагина панель должна вернуться как была
Say ""
Say (Shot (Join-Path $Out '19-панель-после-всех-выходов.png'))

Get-Process pwsh, powershell -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -in 'E2E-TEST-WINDOW', 'E2E-BACKDROP' } | Stop-Process -Force -ErrorAction SilentlyContinue
Say "=== конец ==="
