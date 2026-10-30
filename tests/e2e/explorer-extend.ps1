# Окно Проводника при «Растянуть на всё окно» + подложка: у владельца пропадал весь обычный текст (панель слева, подписи дисков).
param([string]$Out = 'results-extend')
. "$PSScriptRoot/common.ps1"
New-Item -ItemType Directory -Force $Out | Out-Null
$log = Join-Path $Out 'report.txt'
function Say($t) { $t | Tee-Object -FilePath $log -Append }
$data = Join-Path $env:APPDATA 'BattleLauncher'
New-Item -ItemType Directory -Force $data | Out-Null
function Quit-Plugin($proc, $id) {
    try { $ev = [System.Threading.EventWaitHandle]::OpenExisting("GhostShell.Plugin.Quit.$id"); [void]$ev.Set() } catch { Say "событие выхода $id не найдено" }
    for ($i = 0; $i -lt 15 -and -not $proc.HasExited; $i++) { Start-Sleep -Seconds 1 }
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
}
# закрываем всё лишнее, чтобы Проводник был на виду
Get-Process WindowsTerminal, conhost -ErrorAction SilentlyContinue | Out-Null
Start-Process explorer.exe 'shell:MyComputerFolder'
Start-Sleep -Seconds 6
$hx = [IntPtr]::Zero
foreach ($line in [W]::List()) { if ($line -match '\|CabinetWClass\|' -and $line -match '\|vis=1\|') { $hx = [IntPtr][long]($line.Split('|')[0]) } }
Say "окно Проводника: $hx"
Say (Shot (Join-Path $Out '01-проводник-обычный.png'))
if ($hx -ne [IntPtr]::Zero) { Say ("до: " + [W]::Describe($hx)) }
function Run-Cp($name, $extend) {
    $s = @{ WindowsOn = $true; WindowMode = 'none'; WindowOpacity = 100; Backdrop = 'acrylic'; Tint = '#FF2A9D'; TintOpacity = 80; ExtendFrame = $extend }
    ($s | ConvertTo-Json) | Set-Content (Join-Path $data 'color-plus-settings.json') -Encoding UTF8
    $c = Start-Process -FilePath 'out-test/ColorPlus.exe' -ArgumentList '--background' -PassThru
    Start-Sleep -Seconds 9
    if ($hx -ne [IntPtr]::Zero) { Say ("$name : " + [W]::Describe($hx)) }
    Say (Shot (Join-Path $Out "$name.png"))
    Quit-Plugin $c 'color-plus'
    Start-Sleep -Seconds 3
}
Run-Cp '02-проводник-подложка-растянуть-ДА' $true
Run-Cp '03-проводник-подложка-растянуть-НЕТ' $false
Get-Process explorer -ErrorAction SilentlyContinue | Out-Null
