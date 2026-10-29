# Опыт: «стекло» под окном (акрил) + чёткая шапка (DWM-миниатюра) поверх прозрачного окна Chrome.
param([string]$Out = 'results-glass')
. "$PSScriptRoot/common.ps1"
New-Item -ItemType Directory -Force $Out | Out-Null
$log = Join-Path $Out 'report.txt'
function Say($t) { $t | Tee-Object -FilePath $log -Append }

# яркая подложка позади: цветные полосы и крупный текст (размытие должно быть хорошо видно)
$backScript = Join-Path (Get-Location) 'backdrop.ps1'
@'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$f = New-Object System.Windows.Forms.Form
$f.Text = "E2E-BACKDROP"; $f.StartPosition = "Manual"; $f.Left = 0; $f.Top = 0; $f.Width = 1010; $f.Height = 700; $f.BackColor = [System.Drawing.Color]::FromArgb(255, 0, 140)
$cols = @('#00F0FF', '#FFE600', '#2CFF9A', '#6C4DFF')
for ($i = 0; $i -lt 4; $i++) { $p = New-Object System.Windows.Forms.Panel; $p.BackColor = [System.Drawing.ColorTranslator]::FromHtml($cols[$i]); $p.Left = 40 + $i * 230; $p.Top = 120; $p.Width = 120; $p.Height = 420; $f.Controls.Add($p) }
$l = New-Object System.Windows.Forms.Label; $l.Text = "ПОЗАДИ: РАБОЧИЙ СТОЛ И ЗНАЧКИ"; $l.Font = New-Object System.Drawing.Font("Segoe UI", 28, [System.Drawing.FontStyle]::Bold); $l.AutoSize = $true; $l.Left = 30; $l.Top = 540
$f.Controls.Add($l)
[void]$f.ShowDialog()
'@ | Set-Content $backScript -Encoding UTF8
Start-Process powershell -ArgumentList '-NoProfile', '-File', $backScript | Out-Null
Start-Sleep -Seconds 5

$page = Join-Path (Get-Location) 'glass-page.html'
@'
<!doctype html><html><head><meta charset="utf-8"><title>GLASS-CHROME-PAGE</title></head>
<body style="margin:0;background:#0F0F0F;color:#fff;font:26px Segoe UI">
<h1 style="margin:20px">ТЕКСТ ЧЁТКИЙ, ФОН СТЕКЛО</h1>
<p style="margin:20px">Картинка и видео остаются плотными.</p>
<div style="margin:20px;width:300px;height:130px;background:linear-gradient(135deg,#ff2a9d,#00f0ff,#2cff9a)"></div>
</body></html>
'@ | Set-Content $page -Encoding UTF8
$chrome = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
Start-Process $chrome -ArgumentList '--new-window', '--no-first-run', '--no-default-browser-check', '--window-position=120,80', '--window-size=760,520', ([System.Uri]$page).AbsoluteUri | Out-Null
Start-Sleep -Seconds 10
$ch = [IntPtr]::Zero
foreach ($line in [W]::List()) { if ($line -match 'GLASS-CHROME-PAGE' -and $line -match '\|vis=1\|') { $ch = [IntPtr][long]($line.Split('|')[0]) } }
Say "окно Chrome: $ch"
if ($ch -eq [IntPtr]::Zero) { Say "Chrome не найден"; exit 1 }
Say (Shot (Join-Path $Out '1-до.png'))

function Run-Glass([string]$name, [int]$alpha, [int]$strip, [int]$tintOpacity) {
    $p = Start-Process pwsh -ArgumentList '-NoProfile', '-File', (Join-Path $PSScriptRoot 'glass.ps1'), '-Target', ([long]$ch), '-Alpha', $alpha, '-Strip', $strip, '-TintOpacity', $tintOpacity, '-Seconds', '14' -PassThru
    Start-Sleep -Seconds 8
    Say ("$name : Chrome " + [W]::Describe($ch))
    Say (Shot (Join-Path $Out "$name.png"))
    if (-not $p.WaitForExit(30000)) { $p.Kill(); Say "$name : glass.ps1 пришлось завершить" }
    Start-Sleep -Seconds 1
}
Run-Glass '2-стекло-без-шапки' 80 0 40
Run-Glass '3-стекло-и-чёткая-шапка' 70 112 40
Say "после: Chrome " + [W]::Describe($ch)
Say (Shot (Join-Path $Out '4-после.png'))
Get-Process chrome -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process powershell -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -eq 'E2E-BACKDROP' } | Stop-Process -Force -ErrorAction SilentlyContinue
Say "=== конец ==="
