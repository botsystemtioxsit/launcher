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

Copy-Item (Join-Path $data 'taskbar-plus-engine.log') (Join-Path $Out 'engine.log') -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $data 'taskbar-plus-engine.cfg') (Join-Path $Out 'engine.cfg') -Force -ErrorAction SilentlyContinue
Quit-Plugin $p 'taskbar-plus'
Start-Sleep -Seconds 3
Say ""; Mark '06-после-выхода'

# ----- плашка должна накрывать все значки (снимок владельца: плашка кончалась после Steam, остальные значки висели справа) -----
# На экране 1024 px отступ 486 оставляет плашке ~50 px — значки (их 5–6 штук) явно шире. Движок обязан расширить плашку.
function Cover-Check([string]$shape, [int]$side, [string]$tag) {
    $tp2 = @{ UseXaml = $true; XamlConsentShown = $true; Shape = $shape; Radius = 13; SideMargin = $side; BottomGap = 6; KeepEdges = $true; ZoneSystem = 'right'; ZoneApps = 'default'; ZoneWidgets = 'default'; Islands = @{} }
    ($tp2 | ConvertTo-Json -Depth 6) | Set-Content (Join-Path $data 'taskbar-plus-settings.json') -Encoding UTF8
    Remove-Item $status -ErrorAction SilentlyContinue
    $p2 = Start-Process -FilePath 'out-test/TaskbarPlus.exe' -ArgumentList '--background' -PassThru
    $t = Wait-File $status { param($t) $t -match 'state=ready' -and $t -match 'engine=6' -and $t -match 'bgfill=[1-9]' -and $t -match 'cover=\d' } 90
    Start-Sleep -Seconds 5
    Mark "07-$tag"
    $txt = Get-Content $status -Raw -ErrorAction SilentlyContinue
    if ($txt -match 'cover=(-?\d+),(-?\d+),(-?\d+),(-?\d+),(-?\d+)') {
        $aL = [int]$Matches[1]; $aR = [int]$Matches[2]; $bL = [int]$Matches[3]; $bR = [int]$Matches[4]; $w = [int]$Matches[5]
        $plateL = $bL; $plateR = $w - $bR
        Say ("  $tag : значки $aL..$aR, плашка $plateL..$plateR, ширина панели $w, отступ в настройках $side")
        if ($plateL -le $aL -and $plateR -ge $aR) { Say "  $tag : OK — плашка накрывает все значки" } else { Say "  $tag : ОШИБКА — значки выходят за плашку" }
        if ($side -gt $aL) { if ($plateL -lt $side) { Say "  $tag : OK — плашка расширена" } }
    } else { Say "  $tag : ОШИБКА — в статусе нет строки cover" }
    Quit-Plugin $p2 'taskbar-plus'
    Start-Sleep -Seconds 3
}
Say ""; Say "=== плашка накрывает значки ==="
Cover-Check 'pill' 486 'плашка-pill-486'
Cover-Check 'tab' 486 'плашка-tab-486'
Cover-Check 'pill' 20 'плашка-pill-20'
