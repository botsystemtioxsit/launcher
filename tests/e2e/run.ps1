# Проверка плагинов на настоящей Windows-машине GitHub: что получилось, видно по файлам и снимкам экрана в папке results.
param([string]$Out = 'results')
. "$PSScriptRoot/common.ps1"
New-Item -ItemType Directory -Force $Out | Out-Null
$log = Join-Path $Out 'report.txt'
function Say($t) { $t | Tee-Object -FilePath $log -Append }

$data = Join-Path $env:APPDATA 'BattleLauncher'
New-Item -ItemType Directory -Force $data | Out-Null

Say "=== Среда ==="
Say ("Windows: " + [System.Environment]::OSVersion.Version + "  сеанс: " + [System.Diagnostics.Process]::GetCurrentProcess().SessionId + "  пользователь: " + $env:USERNAME)
Say ("Экран: " + [System.Windows.Forms.SystemInformation]::VirtualScreen)
Say ("explorer: " + ((Get-Process explorer -ErrorAction SilentlyContinue | ForEach-Object { $_.Id }) -join ', '))
$tray = [W]::FindWindow('Shell_TrayWnd', $null)
Say ("Shell_TrayWnd: $tray")
if ($tray -eq [IntPtr]::Zero) {
    Say "панели задач нет — пробую запустить Проводник"
    Start-Process explorer.exe
    Start-Sleep -Seconds 8
    $tray = [W]::FindWindow('Shell_TrayWnd', $null)
    Say ("Shell_TrayWnd после запуска: $tray; explorer: " + ((Get-Process explorer -ErrorAction SilentlyContinue | ForEach-Object { $_.Id }) -join ', '))
}
Say "--- окна верхнего уровня (до) ---"
[W]::List() | Where-Object { $_ -match '\|vis=1\|' } | ForEach-Object { Say $_ }
Say (Shot (Join-Path $Out '01-до.png'))

# ---------- Taskbar Plus + Color Plus (фон панели) ----------
Say ""
Say "=== Taskbar Plus (движок) + Color Plus (фон панели) ==="
@{ UseXaml = $true; XamlConsentShown = $true; Shape = 'none' } | ConvertTo-Json | Set-Content (Join-Path $data 'taskbar-plus-settings.json') -Encoding UTF8
@{ Active = $true; BgMode = 'solid'; BgColor = '#FF2A9D'; BgOpacity = 100; HideStroke = $true } | ConvertTo-Json | Set-Content (Join-Path $data 'color-plus-taskbar.json') -Encoding UTF8
$tp = Start-Process -FilePath 'out-test/TaskbarPlus.exe' -ArgumentList '--background' -PassThru
Say "TaskbarPlus.exe pid=$($tp.Id)"
$status = Join-Path $data 'taskbar-plus-engine-status.txt'
$st = Wait-File $status { param($t) $t -match 'state=ready' -and $t -match 'bgfill=[1-9]' } 75
Say "--- статус движка ---"
Say $st
Start-Sleep -Seconds 4
Say (Shot (Join-Path $Out '02-панель-движок.png'))
foreach ($f in 'taskbar-plus-engine.log', 'taskbar-plus-engine.cfg', 'taskbar-plus-xaml-tree.txt', 'taskbar-plus-engine-health.json') {
    $p = Join-Path $data $f
    if (Test-Path $p) { Copy-Item $p (Join-Path $Out $f) -Force; Say "сохранён файл: $f" } else { Say "нет файла: $f" }
}
Say "--- журнал движка ---"
if (Test-Path (Join-Path $data 'taskbar-plus-engine.log')) { Get-Content (Join-Path $data 'taskbar-plus-engine.log') -Tail 40 | ForEach-Object { Say $_ } }

# вернуть как было: движок снимает свои изменения при выходе из плагина
Say "--- выход из плагина (проверка возврата) ---"
try { $ev = [System.Threading.EventWaitHandle]::OpenExisting('GhostShell.Plugin.Quit.taskbar-plus'); [void]$ev.Set() } catch { Say "событие выхода не найдено: $($_.Exception.Message)" }
Start-Sleep -Seconds 6
Say (Shot (Join-Path $Out '03-панель-после-выхода.png'))
if (-not $tp.HasExited) { Stop-Process -Id $tp.Id -Force -ErrorAction SilentlyContinue; Say "плагин пришлось закрыть принудительно" } else { Say "плагин вышел сам" }

# ---------- Color Plus (окна) ----------
Say ""
Say "=== Color Plus (окна) ==="
Remove-Item (Join-Path $data 'color-plus-taskbar.json') -ErrorAction SilentlyContinue
$form = Start-Process pwsh -ArgumentList '-NoProfile', '-Command', 'Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.Form; $f.Text = "E2E-ТЕСТ-ОКНО"; $f.Width = 640; $f.Height = 420; $f.BackColor = [System.Drawing.Color]::White; $f.StartPosition = "CenterScreen"; $f.TopMost = $false; [void]$f.ShowDialog()' -PassThru
Start-Sleep -Seconds 5
$hw = [IntPtr]::Zero
foreach ($line in [W]::List()) { if ($line -match 'E2E-ТЕСТ-ОКНО') { $hw = [IntPtr][long]($line.Split('|')[0]) } }
Say "тестовое окно: $hw"
if ($hw -ne [IntPtr]::Zero) { Say ("до: " + [W]::Describe($hw)) }
Say (Shot (Join-Path $Out '04-окно-до.png'))
@{ WindowsOn = $true; WindowOpacity = 55; Backdrop = 'mica'; DarkTitle = $true; TitleColor = '#FF2A9D'; TitleTextColor = '#FFFFFF'; TaskbarOn = $false } | ConvertTo-Json | Set-Content (Join-Path $data 'color-plus-settings.json') -Encoding UTF8
$cp = Start-Process -FilePath 'out-test/ColorPlus.exe' -ArgumentList '--background' -PassThru
Say "ColorPlus.exe pid=$($cp.Id)"
Start-Sleep -Seconds 6
if ($hw -ne [IntPtr]::Zero) { Say ("с Color Plus: " + [W]::Describe($hw)) }
Say (Shot (Join-Path $Out '05-окно-с-эффектами.png'))
try { $ev = [System.Threading.EventWaitHandle]::OpenExisting('GhostShell.Plugin.Quit.color-plus'); [void]$ev.Set() } catch { Say "событие выхода не найдено: $($_.Exception.Message)" }
Start-Sleep -Seconds 4
if ($hw -ne [IntPtr]::Zero) { Say ("после выхода: " + [W]::Describe($hw)) }
Say (Shot (Join-Path $Out '06-окно-после-выхода.png'))
if (-not $cp.HasExited) { Stop-Process -Id $cp.Id -Force -ErrorAction SilentlyContinue; Say "Color Plus пришлось закрыть принудительно" } else { Say "Color Plus вышел сам" }
Get-Process pwsh -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -eq 'E2E-ТЕСТ-ОКНО' } | Stop-Process -Force -ErrorAction SilentlyContinue
Say "=== конец ==="
