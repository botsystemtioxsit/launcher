# Игры и 3D-программы Color Plus не трогает: окно, где подгружен opengl32 (как у Minecraft), остаётся без подложки, а обычное окно получает её.
# Сверх того проверяем уборку «остатка»: подложка, поставленная на игровое окно раньше (прошлым запуском плагина), при старте снимается.
param([string]$Out = 'results-gpu')
. "$PSScriptRoot/common.ps1"
New-Item -ItemType Directory -Force $Out | Out-Null
$log = Join-Path $Out 'report.txt'
function Say($t) { $t | Tee-Object -FilePath $log -Append }
$data = Join-Path $env:APPDATA 'BattleLauncher'
New-Item -ItemType Directory -Force $data | Out-Null
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class Dw { [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
  public static void Backdrop(IntPtr h, int v) { DwmSetWindowAttribute(h, 38, ref v, 4); } }
'@
function Quit-Plugin($proc, $id) {
    try { $ev = [System.Threading.EventWaitHandle]::OpenExisting("GhostShell.Plugin.Quit.$id"); [void]$ev.Set() } catch { Say "событие выхода $id не найдено" }
    for ($i = 0; $i -lt 15 -and -not $proc.HasExited; $i++) { Start-Sleep -Seconds 1 }
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
}
$gpuScript = Join-Path (Get-Location) 'gpu-form.ps1'
@'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class Ld { [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr LoadLibraryW(string n); }'
[void][Ld]::LoadLibraryW('opengl32.dll')
$f = New-Object System.Windows.Forms.Form
$f.Text = "E2E-GPU-GAME"; $f.Width = 520; $f.Height = 340; $f.BackColor = [System.Drawing.Color]::Black; $f.StartPosition = "Manual"; $f.Left = 30; $f.Top = 40
[void]$f.ShowDialog()
'@ | Set-Content $gpuScript -Encoding UTF8
$plainScript = Join-Path (Get-Location) 'plain-form.ps1'
@'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$f = New-Object System.Windows.Forms.Form
$f.Text = "E2E-PLAIN-APP"; $f.Width = 520; $f.Height = 340; $f.BackColor = [System.Drawing.Color]::White; $f.StartPosition = "Manual"; $f.Left = 500; $f.Top = 40
[void]$f.ShowDialog()
'@ | Set-Content $plainScript -Encoding UTF8
$g = Start-Process pwsh -ArgumentList '-NoProfile', '-File', $gpuScript -PassThru
$p = Start-Process pwsh -ArgumentList '-NoProfile', '-File', $plainScript -PassThru
Start-Sleep -Seconds 8
$hg = [IntPtr]::Zero; $hp = [IntPtr]::Zero
foreach ($line in [W]::List()) {
    if ($line -match 'E2E-GPU-GAME' -and $line -match '\|vis=1\|') { $hg = [IntPtr][long]($line.Split('|')[0]) }
    if ($line -match 'E2E-PLAIN-APP' -and $line -match '\|vis=1\|') { $hp = [IntPtr][long]($line.Split('|')[0]) }
}
Say "окно «игры»: $hg   обычное окно: $hp"
# «остаток» от прошлого запуска: на игровом окне уже стоит подложка
[Dw]::Backdrop($hg, 3)
Start-Sleep -Seconds 1
Say ("до: игра " + [W]::Describe($hg) + " | обычное " + [W]::Describe($hp))
Say (Shot (Join-Path $Out '01-до.png'))

$s = @{ WindowsOn = $true; WindowMode = 'none'; WindowOpacity = 100; Backdrop = 'acrylic'; Tint = '#FF2A9D'; TintOpacity = 80; ExtendFrame = $true; Apps = @{ powershell = @{ Mode = 'skip' } } }
($s | ConvertTo-Json -Depth 6) | Set-Content (Join-Path $data 'color-plus-settings.json') -Encoding UTF8
$cp = Start-Process -FilePath 'out-test/ColorPlus.exe' -ArgumentList '--background' -PassThru
Start-Sleep -Seconds 9
Say ("Color Plus работает: игра " + [W]::Describe($hg) + " | обычное " + [W]::Describe($hp))
Say (Shot (Join-Path $Out '02-плагин-работает.png'))
Quit-Plugin $cp 'color-plus'
Start-Sleep -Seconds 3
Say ("после выхода: игра " + [W]::Describe($hg) + " | обычное " + [W]::Describe($hp))
Stop-Process -Id $g.Id, $p.Id -Force -ErrorAction SilentlyContinue

# Цвет текста выделения: «Авто» оставляет буквы как в Windows, «Свой» ставит выбранный цвет; после выхода всё возвращается
Say ""
Say "=== цвет текста выделения ==="
Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern int GetSysColor(int i);' -Name S -Namespace N
function Run-Sel($name, $auto) {
    $s = @{ SelectionOn = $true; SelectionColor = '#FF2A9D'; SelectionTextColor = '#FF2A9D'; SelectionTextAuto = $auto }
    ($s | ConvertTo-Json) | Set-Content (Join-Path $data 'color-plus-settings.json') -Encoding UTF8
    $c = Start-Process -FilePath 'out-test/ColorPlus.exe' -ArgumentList '--background' -PassThru
    Start-Sleep -Seconds 6
    Say ("$name : HIGHLIGHT=0x{0:X} HIGHLIGHTTEXT=0x{1:X}" -f [N.S]::GetSysColor(13), [N.S]::GetSysColor(14))
    Quit-Plugin $c 'color-plus'
    Start-Sleep -Seconds 3
    Say ("$name после выхода : HIGHLIGHT=0x{0:X} HIGHLIGHTTEXT=0x{1:X}" -f [N.S]::GetSysColor(13), [N.S]::GetSysColor(14))
}
Say ("до: HIGHLIGHT=0x{0:X} HIGHLIGHTTEXT=0x{1:X}" -f [N.S]::GetSysColor(13), [N.S]::GetSysColor(14))
Run-Sel 'Авто (ожидается HIGHLIGHT=0x9D2AFF, HIGHLIGHTTEXT как до)' $true
Run-Sel 'Свой розовый (ожидается HIGHLIGHT=0x9D2AFF, HIGHLIGHTTEXT=0x9D2AFF)' $false
