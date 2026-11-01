# Color Plus 2 на Windows-машине GitHub: цвет окон и выделения (средствами Windows), цвет островов Taskbar Plus, возврат «как было».
param([string]$Out = 'results-colorplus')
. "$PSScriptRoot/common.ps1"
New-Item -ItemType Directory -Force $Out | Out-Null
$log = Join-Path $Out 'report.txt'
function Say($t) { $t | Tee-Object -FilePath $log -Append }
$cp = (Resolve-Path 'out-test/ColorPlus.exe').Path
$data = Join-Path $env:APPDATA 'BattleLauncher'
New-Item -ItemType Directory -Force $data | Out-Null
$status = Join-Path $data 'taskbar-plus-engine-status.txt'
Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern int GetSysColor(int i);' -Name S -Namespace N
function Quit-Plugin($proc, $id) {
    try { $ev = [System.Threading.EventWaitHandle]::OpenExisting("GhostShell.Plugin.Quit.$id"); [void]$ev.Set() } catch { Say "событие выхода $id не найдено" }
    for ($i = 0; $i -lt 15 -and -not $proc.HasExited; $i++) { Start-Sleep -Seconds 1 }
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
}
function Dump([string]$name) {
    $d = Get-ItemProperty 'HKCU:\SOFTWARE\Microsoft\Windows\DWM' -ErrorAction SilentlyContinue
    $p = Get-ItemProperty 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize' -ErrorAction SilentlyContinue
    $a = Get-ItemProperty 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent' -ErrorAction SilentlyContinue
    $c = Get-ItemProperty 'HKCU:\Control Panel\Colors' -ErrorAction SilentlyContinue
    $pal = if ($a.AccentPalette) { ($a.AccentPalette | ForEach-Object { '{0:X2}' -f $_ }) -join '' } else { '(нет)' }
    Say ("[$name] DWM: AccentColor={0:X} Inactive={1:X} Prevalence={2} | Personalize.ColorPrevalence={3} | Palette=$pal | Hilight='{4}' HilightText='{5}' | GetSysColor(13)=0x{6:X6}" -f $d.AccentColor, $d.AccentColorInactive, $d.ColorPrevalence, $p.ColorPrevalence, $c.Hilight, $c.HilightText, [N.S]::GetSysColor(13))
}

# ---- 1. вид окна (без показа) ----
$ui = Join-Path $Out 'ui'; New-Item -ItemType Directory -Force $ui | Out-Null
$pu = Start-Process -FilePath $cp -ArgumentList "--screenshot=$((Resolve-Path $ui).Path)" -PassThru
if (-not $pu.WaitForExit(90000)) { $pu.Kill(); Say 'ui: окно не нарисовалось за 90 секунд' }
Get-ChildItem $ui | ForEach-Object { Say ("ui: " + $_.Name + " " + $_.Length) }
if (Test-Path (Join-Path $ui 'color-plus-ui-error.txt')) { Get-Content (Join-Path $ui 'color-plus-ui-error.txt') | ForEach-Object { Say $_ } }

# ---- 2. окна Windows до / после / после возврата ----
(New-Object -ComObject Shell.Application).MinimizeAll()
Start-Sleep -Seconds 2
Start-Process explorer 'C:\Windows'
Start-Process notepad
Start-Sleep -Seconds 6
Dump 'до'
Say (Shot (Join-Path $Out '01-до.png'))

Say ""; Say "=== применить #FF2A9D (окна и выделение, без панели) ==="
$p1 = Start-Process -FilePath $cp -ArgumentList '--apply=#FF2A9D', '--no-taskbar' -PassThru   # остаётся работать в фоне и красит заголовки
Start-Sleep -Seconds 9
if (Test-Path (Join-Path $env:TEMP 'color-plus-error.txt')) { Say ('ошибка: ' + (Get-Content (Join-Path $env:TEMP 'color-plus-error.txt') -Raw)) }
Dump 'после'
Say "фоновый процесс работает: $(-not $p1.HasExited)"
Say (Shot (Join-Path $Out '02-после-применить.png'))
# окно, открытое после применения, тоже должно покраситься
Start-Process notepad -ArgumentList 'C:\Windows\win.ini'
Start-Sleep -Seconds 5
Say (Shot (Join-Path $Out '03-новое-окно.png'))

Say ""; Say "=== вернуть как было ==="
$p2 = Start-Process -FilePath $cp -ArgumentList '--revert' -PassThru
if (-not $p2.WaitForExit(60000)) { $p2.Kill(); Say 'revert не завершился' }
Start-Sleep -Seconds 4
Say "фоновый процесс после возврата работает: $(-not $p1.HasExited)"
Dump 'после возврата'
Say (Shot (Join-Path $Out '04-после-возврата.png'))
Get-Process notepad -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# ---- 3. панель: острова Taskbar Plus получают цвет, обычная панель Windows — нет ----
Say ""; Say "=== панель: Taskbar Plus (острова) + Color Plus ==="
$tp = @{ UseXaml = $true; XamlConsentShown = $true; Shape = 'islands'; IslandMode = 'each'; IslandStyle = 'float'; Radius = 14; IslandGap = 8 }
($tp | ConvertTo-Json -Depth 6) | Set-Content (Join-Path $data 'taskbar-plus-settings.json') -Encoding UTF8
Remove-Item (Join-Path $data 'color-plus-taskbar.json') -ErrorAction SilentlyContinue
$t = Start-Process -FilePath 'out-test/TaskbarPlus.exe' -ArgumentList '--background' -PassThru
$st = Wait-File $status { param($x) $x -match 'state=ready' -and $x -match 'bgfill=[1-9]' } 90
Start-Sleep -Seconds 5
Say (Shot (Join-Path $Out '05-панель-до.png'))
$p3 = Start-Process -FilePath $cp -ArgumentList '--apply=#FF2A9D', '--no-windows' -PassThru
if (-not $p3.WaitForExit(60000)) { $p3.Kill() }
Start-Sleep -Seconds 6
Say (Shot (Join-Path $Out '06-панель-цвет-на-островах.png'))
Get-Content (Join-Path $data 'color-plus-taskbar.json') -ErrorAction SilentlyContinue | ForEach-Object { Say "  link: $_" }
$p4 = Start-Process -FilePath $cp -ArgumentList '--revert' -PassThru
if (-not $p4.WaitForExit(60000)) { $p4.Kill() }
Start-Sleep -Seconds 5
Say (Shot (Join-Path $Out '07-панель-после-возврата.png'))
Quit-Plugin $t 'taskbar-plus'
Remove-Item (Join-Path $data 'color-plus-taskbar.json') -ErrorAction SilentlyContinue

# ---- 4. «Новый движок» у Taskbar Plus выключен (как у владельца: серая панель) — Color Plus включает его при «Применить» ----
Say ""; Say "=== Taskbar Plus с ВЫКЛЮЧЕННЫМ новым движком: Color Plus включает его сам ==="
# чистый Проводник: движок прошлого шага остаётся загруженным в нём после выхода плагина
Set-Content (Join-Path $data 'explorer-restart.txt') ([DateTime]::UtcNow.ToString('o'))
Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 12
$progDir = Join-Path $env:LOCALAPPDATA 'Programs\TaskbarPlus'
New-Item -ItemType Directory -Force $progDir | Out-Null
Copy-Item 'out-test/TaskbarPlus.exe' (Join-Path $progDir 'TaskbarPlus.exe') -Force
$tp2 = @{ UseXaml = $false; XamlConsentShown = $false; Shape = 'pill'; Radius = 7; SideMargin = 300; BottomGap = 6 }
($tp2 | ConvertTo-Json) | Set-Content (Join-Path $data 'taskbar-plus-settings.json') -Encoding UTF8
Remove-Item $status -ErrorAction SilentlyContinue
Remove-Item (Join-Path $data 'color-plus-taskbar.json') -ErrorAction SilentlyContinue
$t2 = Start-Process -FilePath (Join-Path $progDir 'TaskbarPlus.exe') -ArgumentList '--background' -PassThru
Start-Sleep -Seconds 12
Say (Shot (Join-Path $Out '08-движок-выключен-серая-панель.png'))
$p5 = Start-Process -FilePath $cp -ArgumentList '--apply=#FF2A9D', '--no-windows' -PassThru
if (-not $p5.WaitForExit(170000)) { $p5.Kill(); Say 'apply не завершился за 170 секунд' }
if (Test-Path (Join-Path $env:TEMP 'color-plus-error.txt')) { Say ('ошибка: ' + (Get-Content (Join-Path $env:TEMP 'color-plus-error.txt') -Raw)) }
Start-Sleep -Seconds 6
Say ("настройки Taskbar Plus после: " + (Get-Content (Join-Path $data 'taskbar-plus-settings.json') -Raw))
Say ("статус движка: " + ((Get-Content $status -Raw -ErrorAction SilentlyContinue) -replace "`r?`n", ' | '))
Say (Shot (Join-Path $Out '09-движок-включён-цвет.png'))
$p6 = Start-Process -FilePath $cp -ArgumentList '--revert' -PassThru
if (-not $p6.WaitForExit(60000)) { $p6.Kill() }
Get-Process TaskbarPlus -ErrorAction SilentlyContinue | ForEach-Object { try { $ev = [System.Threading.EventWaitHandle]::OpenExisting('GhostShell.Plugin.Quit.taskbar-plus'); [void]$ev.Set() } catch { } }
Start-Sleep -Seconds 4
Get-Process TaskbarPlus -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $data 'color-plus-taskbar.json') -ErrorAction SilentlyContinue
Say "=== конец ==="
