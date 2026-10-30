# Панель в тех состояниях, где у владельца пропадали часы и трей: плагин запущен, открыт «Пуск», Проводник перезапущен.
# Настройки как у владельца: плавающая панель с большим боковым отступом, острова краёв, цвет у острова значков.
param([string]$Out = 'results-taskbar')
. "$PSScriptRoot/common.ps1"
New-Item -ItemType Directory -Force $Out | Out-Null
$log = Join-Path $Out 'report.txt'
function Say($t) { $t | Tee-Object -FilePath $log -Append }
$data = Join-Path $env:APPDATA 'BattleLauncher'
New-Item -ItemType Directory -Force $data | Out-Null
$status = Join-Path $data 'taskbar-plus-engine-status.txt'
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class Rg {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern int GetWindowRgnBox(IntPtr h, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
    public static string Box() {
        IntPtr h = FindWindow("Shell_TrayWnd", null);
        if (h == IntPtr.Zero) return "панели нет";
        RECT r; int t = GetWindowRgnBox(h, out r);
        return t == 0 ? "выреза нет" : ("вырез " + r.L + "," + r.T + "," + r.R + "," + r.B);
    }
}
'@
function Quit-Plugin($proc, $id) {
    try { $ev = [System.Threading.EventWaitHandle]::OpenExisting("GhostShell.Plugin.Quit.$id"); [void]$ev.Set() } catch { Say "событие выхода $id не найдено" }
    for ($i = 0; $i -lt 15 -and -not $proc.HasExited; $i++) { Start-Sleep -Seconds 1 }
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
}
function Kill-Explorer { Set-Content (Join-Path $data 'explorer-restart.txt') ([DateTime]::UtcNow.ToString('o')); Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue }
function Mark([string]$name) { Say (Shot (Join-Path $Out "$name.png")); Say ("  $name : " + [Rg]::Box() + " | " + ((Get-Content $status -Raw -ErrorAction SilentlyContinue) -replace "`r?`n", ' ' | ForEach-Object { if ($_.Length -gt 200) { $_.Substring(0, 200) } else { $_ } })) }

$tp = @{ UseXaml = $true; XamlConsentShown = $true; Shape = 'pill'; Radius = 8; SideMargin = 260; BottomGap = 5; KeepEdges = $true
    Islands = @{ system = @{ Radius = 15; Color = '#FF2A9D'; Opacity = 90; Mode = 'solid'; Hidden = $false }; apps = @{ Radius = -1; Color = '#6C4DFF'; Opacity = 90; Mode = 'solid'; Hidden = $false } } }
($tp | ConvertTo-Json -Depth 6) | Set-Content (Join-Path $data 'taskbar-plus-settings.json') -Encoding UTF8
Remove-Item (Join-Path $data 'color-plus-taskbar.json') -ErrorAction SilentlyContinue
Say ("Экран: " + [System.Windows.Forms.SystemInformation]::VirtualScreen)
Say (Shot (Join-Path $Out '00-до.png'))

$p = Start-Process -FilePath 'out-test/TaskbarPlus.exe' -ArgumentList '--background' -PassThru
$st = Wait-File $status { param($t) $t -match 'state=ready' -and $t -match 'engine=6' -and $t -match 'bgfill=[1-9]' } 90
Start-Sleep -Seconds 6
Mark '01-плагин-работает'

Say ""; Say "=== открываем «Пуск» ==="
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.SendKeys]::SendWait('^{ESC}')
Start-Sleep -Seconds 4
Mark '02-пуск-открыт'
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Seconds 3
Mark '03-пуск-закрыт'

Say ""; Say "=== перезапуск Проводника (как делает плагин после смены значков) ==="
Kill-Explorer
foreach ($w in @(@(2, '02'), @(3, '05'), @(5, '10'), @(10, '20'), @(20, '40'))) {
    Start-Sleep -Seconds $w[0]
    Mark ("04-проводник-+" + $w[1] + "с")
}
Say ""; Say "=== пуск после перезапуска ==="
[System.Windows.Forms.SendKeys]::SendWait('^{ESC}')
Start-Sleep -Seconds 4
Mark '05-пуск-после-перезапуска'
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')

Say ""; Say "=== Color Plus: цвет панели «сплошной» (розовый) поверх островов ==="
$csettings = @{ TaskbarOn = $true; TaskbarMode = 'solid'; TaskbarColor = '#FF2A9D'; TaskbarOpacity = 90 }
($csettings | ConvertTo-Json) | Set-Content (Join-Path $data 'color-plus-settings.json') -Encoding UTF8
$cp = Start-Process -FilePath 'out-test/ColorPlus.exe' -ArgumentList '--background' -PassThru
Start-Sleep -Seconds 12
Mark '07-цвет-панели-розовый'
Get-Content (Join-Path $data 'color-plus-taskbar.json') -ErrorAction SilentlyContinue | ForEach-Object { Say "  link: $_" }
Say ""; Say "=== перезапуск Проводника при работающем Color Plus: панель не должна вспыхнуть сплошным цветом ==="
Kill-Explorer
Start-Sleep -Seconds 1
Mark '08-проводник-+1с-цвет'
Start-Sleep -Seconds 2
Mark '08-проводник-+3с-цвет'
Start-Sleep -Seconds 6
Mark '08-проводник-+9с-цвет'
Quit-Plugin $cp 'color-plus'
Remove-Item (Join-Path $data 'color-plus-settings.json') -ErrorAction SilentlyContinue
Start-Sleep -Seconds 3

Say ""; Say "=== Color Plus: размытая панель (розовый тон) + обрезка по островам ==="
$csettings = @{ TaskbarOn = $true; TaskbarMode = 'blur'; TaskbarColor = '#FF2A9D'; TaskbarOpacity = 90 }
($csettings | ConvertTo-Json) | Set-Content (Join-Path $data 'color-plus-settings.json') -Encoding UTF8
$cp = Start-Process -FilePath 'out-test/ColorPlus.exe' -ArgumentList '--background' -PassThru
Start-Sleep -Seconds 12
Mark '09-размытая-панель-обрезка-по-островам'
Get-ChildItem $data -Filter 'taskbar-plus-engine-rects-*.txt' | ForEach-Object { Say ("  " + $_.Name); Get-Content $_.FullName | ForEach-Object { Say ("    " + $_) } }
Say "  окна у нижнего края экрана:"
foreach ($line in [W]::List()) {
    $parts = $line.Split('|')
    if ($parts.Count -ge 6 -and $parts[5] -match '^(-?\d+),(\d+),(-?\d+),(\d+)$') {
        $top = [int]$Matches[2]
        if ($top -ge 690 -and $parts[4] -eq 'vis=1') { Say ("    " + $parts[1] + " | " + $parts[5] + " | " + $parts[3] + " | " + $parts[4]) }
    }
}
Start-Sleep -Seconds 3
Say (Shot (Join-Path $Out '09b-через-3-секунды.png'))
Say ("  " + [Rg]::Box())
Quit-Plugin $cp 'color-plus'
Remove-Item (Join-Path $data 'color-plus-settings.json') -ErrorAction SilentlyContinue
Start-Sleep -Seconds 3

Get-ChildItem $data -Filter 'taskbar-plus-engine-*' -ErrorAction SilentlyContinue | ForEach-Object { Copy-Item $_.FullName (Join-Path $Out ('data-' + $_.Name)) -Force -ErrorAction SilentlyContinue }
Copy-Item (Join-Path $data 'taskbar-plus-engine.log') (Join-Path $Out 'engine.log') -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $data 'taskbar-plus-engine.cfg') (Join-Path $Out 'engine.cfg') -Force -ErrorAction SilentlyContinue
Quit-Plugin $p 'taskbar-plus'
Start-Sleep -Seconds 3
Say ""; Mark '06-после-выхода'
