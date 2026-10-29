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
# за тестовым окном — большое ярко-розовое окно другой программы (powershell), чтобы прозрачность была хорошо видна
$backScript = Join-Path (Get-Location) 'backdrop.ps1'
@'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$f = New-Object System.Windows.Forms.Form
$f.Text = "E2E-BACKDROP"; $f.BackColor = [System.Drawing.Color]::FromArgb(255, 0, 140); $f.StartPosition = "Manual"; $f.Left = 0; $f.Top = 0; $f.Width = 1000; $f.Height = 700
[void]$f.ShowDialog()
'@ | Set-Content $backScript -Encoding UTF8
$backdrop = Start-Process powershell -ArgumentList '-NoProfile', '-File', $backScript -PassThru
Start-Sleep -Seconds 5
# тестовое окно: белый фон, тёмно-синий текст, красная плашка
$formScript = Join-Path (Get-Location) 'e2e-form.ps1'
@'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$f = New-Object System.Windows.Forms.Form
$f.Text = "E2E-TEST-WINDOW"; $f.Width = 560; $f.Height = 360; $f.BackColor = [System.Drawing.Color]::White; $f.StartPosition = "Manual"; $f.Left = 200; $f.Top = 150
$l = New-Object System.Windows.Forms.Label
$l.Text = "TEXT STAYS OPAQUE"; $l.Font = New-Object System.Drawing.Font("Segoe UI", 24); $l.ForeColor = [System.Drawing.Color]::FromArgb(0, 20, 120); $l.AutoSize = $true; $l.Left = 30; $l.Top = 30
$f.Controls.Add($l)
$p = New-Object System.Windows.Forms.Panel
$p.BackColor = [System.Drawing.Color]::Crimson; $p.Left = 30; $p.Top = 120; $p.Width = 160; $p.Height = 80
$f.Controls.Add($p)
[void]$f.ShowDialog()
'@ | Set-Content $formScript -Encoding UTF8
$form = Start-Process pwsh -ArgumentList '-NoProfile', '-File', $formScript -PassThru
Start-Sleep -Seconds 6
$hw = [IntPtr]::Zero
foreach ($line in [W]::List()) { if ($line -match 'E2E-TEST-WINDOW') { $hw = [IntPtr][long]($line.Split('|')[0]) } }
Say "тестовое окно: $hw"
if ($hw -ne [IntPtr]::Zero) { Say ("до: " + [W]::Describe($hw)) }
Say (Shot (Join-Path $Out '20-окно-до.png'))

function Run-ColorPlus([string]$name, $s) {
    if (-not $s.Apps) { $s.Apps = @{} }
    $s.Apps['powershell'] = @{ Mode = 'skip' }
    ($s | ConvertTo-Json -Depth 6) | Set-Content (Join-Path $data 'color-plus-settings.json') -Encoding UTF8
    $cp = Start-Process -FilePath 'out-test/ColorPlus.exe' -ArgumentList '--background' -PassThru
    Start-Sleep -Seconds 7
    if ($hw -ne [IntPtr]::Zero) { Say ("$name : " + [W]::Describe($hw)) }
    Say (Shot (Join-Path $Out "$name.png"))
    Quit-Plugin $cp 'color-plus'
    Start-Sleep -Seconds 3
    if ($hw -ne [IntPtr]::Zero) { Say ("$name после выхода: " + [W]::Describe($hw)) }
}
Run-ColorPlus '21-окна-прозрачность-слюда-заголовок' @{ WindowsOn = $true; WindowMode = 'alpha'; WindowOpacity = 55; Backdrop = 'mica'; DarkTitle = $true; TitleColor = '#FF2A9D'; TitleTextColor = '#FFFFFF' }
Run-ColorPlus '22-окна-размытие-акрил-на-всё-окно' @{ WindowsOn = $true; WindowMode = 'none'; WindowOpacity = 100; Backdrop = 'acrylic'; Tint = '#6C4DFF'; TintOpacity = 70; ExtendFrame = $true }
Run-ColorPlus '23-окна-не-трогать-программу' @{ WindowsOn = $true; WindowMode = 'alpha'; WindowOpacity = 40; Apps = @{ pwsh = @{ Mode = 'skip' } } }

Run-ColorPlus '24-окна-только-фон-форма' @{ WindowsOn = $true; WindowMode = 'background' }

# Chrome: «только фон» невозможен (рисуется видеокартой) — проверяем, что окно не ломается, и что запасной вариант «целиком» работает
$page = Join-Path (Get-Location) 'e2e-page.html'
@'
<!doctype html><html><head><meta charset="utf-8"><title>E2E-CHROME-PAGE</title></head>
<body style="margin:0;background:#0F0F0F;color:#fff;font:26px Segoe UI">
<h1 style="margin:20px">ТОЛЬКО ФОН ПРОЗРАЧНЫЙ</h1>
<p style="margin:20px">Текст, картинка и видео остаются плотными.</p>
<div style="margin:20px;width:260px;height:120px;background:linear-gradient(135deg,#ff2a9d,#00f0ff,#2cff9a)"></div>
</body></html>
'@ | Set-Content $page -Encoding UTF8
$chromePath = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
if (Test-Path $chromePath) {
    Start-Process $chromePath -ArgumentList '--new-window', '--no-first-run', '--no-default-browser-check', '--window-position=120,80', '--window-size=760,520', ([System.Uri]$page).AbsoluteUri | Out-Null
    Start-Sleep -Seconds 10
    $ch = [IntPtr]::Zero
    foreach ($line in [W]::List()) { if ($line -match 'E2E-CHROME-PAGE' -and $line -match '\|vis=1\|') { $ch = [IntPtr][long]($line.Split('|')[0]) } }
    Say "окно Chrome: $ch"
    if ($ch -ne [IntPtr]::Zero) { Say ("Chrome до: " + [W]::Describe($ch)) }
    Say (Shot (Join-Path $Out '25-хром-до.png'))
    Run-ColorPlus '26-хром-только-фон-запасного-нет' @{ WindowsOn = $true; WindowMode = 'background'; FallbackAlpha = $false }
    if ($ch -ne [IntPtr]::Zero) { Say ("Chrome после (без запасного): " + [W]::Describe($ch)) }
    Run-ColorPlus '27-хром-только-фон-запасной-целиком' @{ WindowsOn = $true; WindowMode = 'background'; FallbackAlpha = $true; WindowOpacity = 60 }
    if ($ch -ne [IntPtr]::Zero) { Say ("Chrome после (с запасным): " + [W]::Describe($ch)) }
    Get-Process chrome -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
} else { Say "Chrome не найден" }

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

Get-Process pwsh, powershell -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -in 'E2E-TEST-WINDOW', 'E2E-BACKDROP' } | Stop-Process -Force -ErrorAction SilentlyContinue
Say "=== конец ==="
