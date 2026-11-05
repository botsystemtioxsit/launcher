# Taskbar Plus 3 на настоящей Windows-машине GitHub (Windows Server 2025 с Проводником и панелью задач, экран 1024x768).
# Что проверяем: (1) расчёт плашек без Проводника (--selftest); (2) движок подключается, родной фон панели становится прозрачным, измерения значков
# записываются; (3) окно с плашками лежит СРАЗУ ПОД панелью и совпадает с ней по размеру; (4) снимки всех форм; (5) перезапуск Проводника;
# (6) выход плагина возвращает панель; (7) если плагин закрыт аварийно, движок сам возвращает панель через ~20 секунд.
param([string]$Out = 'results-taskbar')
. "$PSScriptRoot/common.ps1"
New-Item -ItemType Directory -Force $Out | Out-Null
$log = Join-Path $Out 'report.txt'
function Say($t) { $t | Tee-Object -FilePath $log -Append }
$data = Join-Path $env:APPDATA 'BattleLauncher'
New-Item -ItemType Directory -Force $data | Out-Null
$status = Join-Path $data 'taskbar-plus-engine-status.txt'
$geoFile = Join-Path $data 'taskbar-plus-geometry.txt'
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class Ov {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetLong(IntPtr h, int i);
    public static string Info() {
        IntPtr tb = FindWindow("Shell_TrayWnd", null);
        IntPtr ov = FindWindow(null, "GhostShellTaskbarPlusOverlay");
        if (tb == IntPtr.Zero) return "tb=нет";
        RECT a; GetWindowRect(tb, out a);
        string s = "tb=" + a.L + "," + a.T + "," + a.R + "," + a.B + " topmost=" + (((long)GetLong(tb, -20) & 8) != 0 ? 1 : 0);
        if (ov == IntPtr.Zero) return s + " ov=нет";
        RECT b; GetWindowRect(ov, out b);
        bool below = GetWindow(tb, 2) == ov;
        bool same = Math.Abs(a.L - b.L) <= 2 && Math.Abs(a.T - b.T) <= 2 && Math.Abs(a.R - b.R) <= 2 && Math.Abs(a.B - b.B) <= 2;
        return s + " ov=" + b.L + "," + b.T + "," + b.R + "," + b.B + " visible=" + (IsWindowVisible(ov) ? 1 : 0) + " сразу_под_панелью=" + (below ? 1 : 0) + " размер_совпадает=" + (same ? 1 : 0) + " exstyle=0x" + ((long)GetLong(ov, -20)).ToString("X");
    }
}
'@
function Quit-Plugin($proc, $id) {
    try { $ev = [System.Threading.EventWaitHandle]::OpenExisting("GhostShell.Plugin.Quit.$id"); [void]$ev.Set() } catch { Say "событие выхода $id не найдено" }
    for ($i = 0; $i -lt 20 -and -not $proc.HasExited; $i++) { Start-Sleep -Seconds 1 }
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue; Say "$id пришлось закрыть принудительно" }
}
function Kill-Explorer { Set-Content (Join-Path $data 'explorer-restart.txt') ([DateTime]::UtcNow.ToString('o')); Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue }
function StatusLine { ((Get-Content $status -Raw -ErrorAction SilentlyContinue) -replace "`r?`n", ' ' | ForEach-Object { if ($_.Length -gt 330) { $_.Substring(0, 330) } else { $_ } }) }
function Geo { if (Test-Path $geoFile) { (Get-Content $geoFile -Raw) -replace "`r?`n", ' / ' } else { '(файла измерений нет)' } }
function Settings($tp) { ($tp | ConvertTo-Json -Depth 6) | Set-Content (Join-Path $data 'taskbar-plus-settings.json') -Encoding UTF8 }
function Wait-Overlay([int]$seconds) {
    $end = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $end) { $i = [Ov]::Info(); if ($i -match 'visible=1') { return $i }; Start-Sleep -Milliseconds 500 }
    return [Ov]::Info()
}
function Start-Plugin {
    Remove-Item $status, $geoFile -ErrorAction SilentlyContinue
    $p = Start-Process -FilePath 'out-test/TaskbarPlus.exe' -ArgumentList '--background' -PassThru
    $null = Wait-File $status { param($t) $t -match 'state=ready' -and $t -match 'engine=7' -and $t -match 'bgfill=[1-9]' -and $t -match 'applied=1' -and $t -match 'geobars=[1-9]' } 90
    return $p
}

# ---------- 1. Расчёт плашек и разбор измерений: без Проводника ----------
Say '=== самопроверка: расчёт плашек ==='
$p0 = Start-Process -FilePath 'out-test/TaskbarPlus.exe' -ArgumentList "--selftest=$((Resolve-Path $Out).Path)\self" -PassThru
if (-not $p0.WaitForExit(120000)) { $p0.Kill(); Say 'selftest: не завершился' } else { Say "selftest: код $($p0.ExitCode)" }
$rep = Join-Path $Out 'self/report.txt'
if (Test-Path $rep) { Get-Content $rep | ForEach-Object { Say $_ } } else { Say 'report.txt самопроверки не появился' }

Say ''
Say ("Экран: " + [System.Windows.Forms.SystemInformation]::VirtualScreen)
Say (Shot (Join-Path $Out '00-до.png'))
Remove-Item (Join-Path $data 'color-plus-taskbar.json') -ErrorAction SilentlyContinue

# ---------- 2-4. Формы: подключение, прозрачность, окно под панелью, снимок ----------
$forms = @(
    @{ Name = '01-плавающая'; S = @{ Shape = 'pill'; Radius = 18; IslandPadding = 10; KeepEdges = $true } },
    @{ Name = '02-плавающая-одна-плашка'; S = @{ Shape = 'pill'; Radius = 18; IslandPadding = 10; KeepEdges = $false } },
    @{ Name = '03-вкладка'; S = @{ Shape = 'tab'; Radius = 16; IslandPadding = 10; KeepEdges = $true } },
    @{ Name = '04-острова-каждый'; S = @{ Shape = 'islands'; IslandMode = 'each'; IslandStyle = 'float'; Radius = 14; IslandGap = 8; BottomGap = 6 } },
    @{ Name = '05-острова-группа-вкладки'; S = @{ Shape = 'islands'; IslandMode = 'group'; IslandStyle = 'tab'; Radius = 14; IslandPadding = 8 } }
)
$first = $true
foreach ($f in $forms) {
    Say ''
    Say "=== $($f.Name) ==="
    $tp = @{ UseXaml = $true; XamlConsentShown = $true }
    foreach ($k in $f.S.Keys) { $tp[$k] = $f.S[$k] }
    Settings $tp
    if ($first) { Add-Content (Join-Path $data 'color-plus-taskbar.json') '' -ErrorAction SilentlyContinue; Set-Content (Join-Path $data 'color-plus-taskbar.json') '{"Active":true,"BgMode":"solid","BgColor":"#6C4DFF","BgOpacity":90,"HideStroke":true}' -Encoding UTF8 }
    $p = Start-Plugin
    $info = Wait-Overlay 30
    Start-Sleep -Seconds 3
    Say (Shot (Join-Path $Out "$($f.Name).png"))
    Say ("  окна: " + [Ov]::Info())
    Say ("  статус: " + (StatusLine))
    Say ("  измерения: " + (Geo))
    $i2 = [Ov]::Info()
    Say ("  " + $(if ($i2 -match 'visible=1' -and $i2 -match 'сразу_под_панелью=1' -and $i2 -match 'размер_совпадает=1') { 'OK — окно с плашками видно, лежит сразу под панелью и совпадает с ней по размеру' } else { 'ОШИБКА — окно плашек не там, где нужно' }))
    if ($first) {
        # дерево элементов: пригодится, если на другой сборке Windows что-то не найдётся
        $cfg = Join-Path $data 'taskbar-plus-engine.cfg'
        $c = Get-Content $cfg -Raw
        Set-Content $cfg ([regex]::Replace($c, 'dump=\d+', 'dump=' + (Get-Random -Minimum 100000 -Maximum 999999))) -NoNewline
        Start-Sleep -Seconds 3
        Copy-Item (Join-Path $data 'taskbar-plus-xaml-tree.txt') (Join-Path $Out 'tree.txt') -Force -ErrorAction SilentlyContinue
        # «Пуск» поверх: плашки не должны вылезать
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.SendKeys]::SendWait('^{ESC}')
        Start-Sleep -Seconds 3
        Say (Shot (Join-Path $Out '01б-пуск-открыт.png'))
        Say ("  окна при открытом «Пуске»: " + [Ov]::Info())
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Start-Sleep -Seconds 2
    }
    Quit-Plugin $p 'taskbar-plus'
    Start-Sleep -Seconds 3
    Say ("  после выхода: " + [Ov]::Info() + " | " + (StatusLine))
    $first = $false
}
Copy-Item (Join-Path $data 'taskbar-plus-engine.log') (Join-Path $Out 'engine.log') -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $data 'taskbar-plus-engine.cfg') (Join-Path $Out 'engine.cfg') -Force -ErrorAction SilentlyContinue
Say (Shot (Join-Path $Out '06-после-выходов.png'))

# ---------- 5. Перезапуск Проводника при работающем плагине ----------
Say ''
Say '=== перезапуск Проводника при работающем плагине ==='
Settings @{ UseXaml = $true; XamlConsentShown = $true; Shape = 'pill'; Radius = 18; IslandPadding = 10; KeepEdges = $true }
$p = Start-Plugin
Say ("  до: " + [Ov]::Info())
Kill-Explorer
foreach ($w in @(3, 7, 15, 25)) {
    Start-Sleep -Seconds $w
    Say ("  +$w с: " + [Ov]::Info() + " | " + (StatusLine))
}
$null = Wait-File $status { param($t) $t -match 'state=ready' -and $t -match 'applied=1' -and $t -match 'geobars=[1-9]' } 90
$i3 = Wait-Overlay 60
Start-Sleep -Seconds 3
Say (Shot (Join-Path $Out '07-после-перезапуска-проводника.png'))
Say ("  итог: " + [Ov]::Info() + " | " + (StatusLine))
Say ("  " + $(if ($i3 -match 'visible=1' -and $i3 -match 'сразу_под_панелью=1') { 'OK — плашки вернулись после перезапуска Проводника' } else { 'ОШИБКА — плашки не вернулись' }))

# ---------- 6. Аварийное закрытие плагина: «выключатель мёртвой руки» ----------
Say ''
Say '=== плагин закрыт аварийно: панель должна вернуться сама ==='
Stop-Process -Name TaskbarPlus -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 5
Say ("  через 5 с: " + (StatusLine))
Say (Shot (Join-Path $Out '08-сразу-после-аварии.png'))
Start-Sleep -Seconds 25
Say ("  через 30 с: " + (StatusLine))
Say (Shot (Join-Path $Out '09-через-30-секунд.png'))
$st = Get-Content $status -Raw -ErrorAction SilentlyContinue
Say ("  " + $(if ($st -match 'applied=0' -and $st -match 'alive=0') { 'OK — движок сам вернул родной фон панели' } else { 'ОШИБКА — панель осталась прозрачной без плагина' }))
Say '=== конец ==='
