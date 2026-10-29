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
    $st = Wait-File $status { param($t) $t -match 'state=ready' -and $t -match 'engine=3' -and $t -match 'bgfill=[1-9]' } 60
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
foreach ($e in 'TaskbarPlus', 'ColorPlus') {
    $pr = Start-Process -FilePath "out-test/$e.exe" -ArgumentList "--screenshot=$((Resolve-Path $Out).Path)" -PassThru
    if (-not $pr.WaitForExit(60000)) { $pr.Kill(); Say "$e --screenshot: не завершился" } else { Say "$e --screenshot: код $($pr.ExitCode)" }
}
Get-ChildItem $Out -Filter '*ui*' | ForEach-Object { Say "файл: $($_.Name) ($($_.Length) байт)" }

# ---------- Taskbar Plus + Color Plus: панель ----------
$base = @{ UseXaml = $true; XamlConsentShown = $true; Shape = 'none' }
Run-Taskbar '10-фон-цвет' $base @{ Active = $true; BgMode = 'solid'; BgColor = '#FF2A9D'; BgOpacity = 100; HideStroke = $true } $true
Run-Taskbar '11-фон-прозрачный' $base @{ Active = $true; BgMode = 'clear'; BgColor = '#101018'; BgOpacity = 60; HideStroke = $true }
Run-Taskbar '12-острова-каждый' (@{ UseXaml = $true; XamlConsentShown = $true; Shape = 'islands'; IslandMode = 'each'; IslandStyle = 'float'; Radius = 14; IslandGap = 8 }) @{ Active = $true; BgMode = 'solid'; BgColor = '#1E6BFF'; BgOpacity = 85; HideStroke = $true }
Run-Taskbar '13-острова-система-фиолетовый-прямой' (@{ UseXaml = $true; XamlConsentShown = $true; Shape = 'islands'; IslandMode = 'each'; IslandStyle = 'float'; Radius = 14; IslandGap = 8; Islands = @{ system = @{ Radius = 0; Color = '#6C4DFF'; Opacity = 90; Mode = 'solid'; Hidden = $false } } }) @{ Active = $true; BgMode = 'solid'; BgColor = '#1E6BFF'; BgOpacity = 70; HideStroke = $true }
Run-Taskbar '14-расположение-система-слева-значки-справа' (@{ UseXaml = $true; XamlConsentShown = $true; Shape = 'none'; ZoneSystem = 'left'; ZoneApps = 'right'; EdgeMargin = 20 }) $null
Run-Taskbar '15-вкладка-края' (@{ UseXaml = $true; XamlConsentShown = $true; Shape = 'tab'; Radius = 16; SideMargin = 120; KeepEdges = $true }) @{ Active = $true; BgMode = 'solid'; BgColor = '#101018'; BgOpacity = 90; HideStroke = $true }

# после выхода из плагина панель должна вернуться как была
Say ""
Say (Shot (Join-Path $Out '19-панель-после-всех-выходов.png'))

# ---------- Color Plus: окна ----------
Say ""
Say "=== Color Plus (окна) ==="
Remove-Item (Join-Path $data 'color-plus-taskbar.json') -ErrorAction SilentlyContinue
$form = Start-Process pwsh -ArgumentList '-NoProfile', '-Command', 'Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.Form; $f.Text = "E2E-TEST-WINDOW"; $f.Width = 560; $f.Height = 360; $f.BackColor = [System.Drawing.Color]::White; $f.StartPosition = "CenterScreen"; [void]$f.ShowDialog()' -PassThru
Start-Sleep -Seconds 6
$hw = [IntPtr]::Zero
foreach ($line in [W]::List()) { if ($line -match 'E2E-TEST-WINDOW') { $hw = [IntPtr][long]($line.Split('|')[0]) } }
Say "тестовое окно: $hw"
if ($hw -ne [IntPtr]::Zero) { Say ("до: " + [W]::Describe($hw)) }
Say (Shot (Join-Path $Out '20-окно-до.png'))

function Run-ColorPlus([string]$name, $s) {
    ($s | ConvertTo-Json -Depth 6) | Set-Content (Join-Path $data 'color-plus-settings.json') -Encoding UTF8
    $cp = Start-Process -FilePath 'out-test/ColorPlus.exe' -ArgumentList '--background' -PassThru
    Start-Sleep -Seconds 7
    if ($hw -ne [IntPtr]::Zero) { Say ("$name : " + [W]::Describe($hw)) }
    Say (Shot (Join-Path $Out "$name.png"))
    Quit-Plugin $cp 'color-plus'
    Start-Sleep -Seconds 3
    if ($hw -ne [IntPtr]::Zero) { Say ("$name после выхода: " + [W]::Describe($hw)) }
}
Run-ColorPlus '21-окна-прозрачность-слюда-заголовок' @{ WindowsOn = $true; WindowOpacity = 55; Backdrop = 'mica'; DarkTitle = $true; TitleColor = '#FF2A9D'; TitleTextColor = '#FFFFFF' }
Run-ColorPlus '22-окна-размытие-акрил-на-всё-окно' @{ WindowsOn = $true; WindowOpacity = 100; Backdrop = 'acrylic'; Tint = '#6C4DFF'; TintOpacity = 70; ExtendFrame = $true }
Run-ColorPlus '23-окна-не-трогать-программу' @{ WindowsOn = $true; WindowOpacity = 40; Apps = @{ pwsh = @{ Mode = 'skip' } } }

# Цвет выделения и акцент: проверяем системные значения до / во время / после
Say ""
Say "=== Color Plus: выделение и акцент ==="
Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern int GetSysColor(int i);' -Name S -Namespace N
$hl0 = [N.S]::GetSysColor(13)
$ac0 = (Get-ItemProperty 'HKCU:\SOFTWARE\Microsoft\Windows\DWM' -ErrorAction SilentlyContinue).AccentColor
($s = @{ SelectionOn = $true; SelectionColor = '#FF0000'; SelectionTextColor = '#FFFFFF'; AccentOn = $true; AccentColor = '#20C27A' }) | Out-Null
($s | ConvertTo-Json) | Set-Content (Join-Path $data 'color-plus-settings.json') -Encoding UTF8
$cp = Start-Process -FilePath 'out-test/ColorPlus.exe' -ArgumentList '--background' -PassThru
Start-Sleep -Seconds 6
$hl1 = [N.S]::GetSysColor(13)
$ac1 = (Get-ItemProperty 'HKCU:\SOFTWARE\Microsoft\Windows\DWM' -ErrorAction SilentlyContinue).AccentColor
Quit-Plugin $cp 'color-plus'
Start-Sleep -Seconds 3
$hl2 = [N.S]::GetSysColor(13)
$ac2 = (Get-ItemProperty 'HKCU:\SOFTWARE\Microsoft\Windows\DWM' -ErrorAction SilentlyContinue).AccentColor
Say ("COLOR_HIGHLIGHT: до=0x{0:X} во время=0x{1:X} после=0x{2:X}  (ожидается во время 0x0000FF)" -f $hl0, $hl1, $hl2)
Say ("DWM AccentColor: до=$ac0 во время=$ac1 после=$ac2")

Get-Process pwsh -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -eq 'E2E-TEST-WINDOW' } | Stop-Process -Force -ErrorAction SilentlyContinue
Say "=== конец ==="
