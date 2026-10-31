# Панель задач и акцентный цвет Windows: красится ли панель при «Показывать акцентный цвет на панели задач» после перезапуска Проводника.
param([string]$Out = 'results-accent')
. "$PSScriptRoot/common.ps1"
New-Item -ItemType Directory -Force $Out | Out-Null
$log = Join-Path $Out 'report.txt'
function Say($t) { $t | Tee-Object -FilePath $log -Append }
$pers = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
$acc = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent'
$dwm = 'HKCU:\Software\Microsoft\Windows\DWM'
function Get-V($p, $n) { try { (Get-ItemProperty $p -ErrorAction Stop).$n } catch { $null } }
$orig = @{
    ColorPrevalence = Get-V $pers 'ColorPrevalence'; SystemUsesLightTheme = Get-V $pers 'SystemUsesLightTheme'; AppsUseLightTheme = Get-V $pers 'AppsUseLightTheme'
    AccentColorMenu = Get-V $acc 'AccentColorMenu'; StartColorMenu = Get-V $acc 'StartColorMenu'; AccentColor = Get-V $dwm 'AccentColor'; ColorizationColor = Get-V $dwm 'ColorizationColor'
}
Say ("исходные значения: " + (($orig.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join '; '))
Say (Shot (Join-Path $Out '01-до.png'))
function Restart-Explorer { Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 12 }
function Set-Accent($prevalence, $dark, $menuToo) {
    New-Item -Path $pers -Force | Out-Null; New-Item -Path $acc -Force | Out-Null; New-Item -Path $dwm -Force | Out-Null
    Set-ItemProperty $pers 'ColorPrevalence' $prevalence -Type DWord
    Set-ItemProperty $pers 'SystemUsesLightTheme' $(if ($dark) { 0 } else { 1 }) -Type DWord
    $abgr = [int]0xFF9D2AFF   # розовый #FF2A9D в формате ABGR
    Set-ItemProperty $dwm 'AccentColor' $abgr -Type DWord
    Set-ItemProperty $dwm 'ColorizationColor' ([int]0xFFFF2A9D) -Type DWord
    if ($menuToo) { Set-ItemProperty $acc 'AccentColorMenu' $abgr -Type DWord; Set-ItemProperty $acc 'StartColorMenu' $abgr -Type DWord }
}
# 1) как сейчас делает Color Plus: меняем акцент, в том числе цвет меню и панели; в Windows включено «акцентный цвет на панели задач»
Set-Accent 1 $true $true
Restart-Explorer
Say (Shot (Join-Path $Out '02-акцент-розовый-на-панели-ВКЛ-после-перезапуска.png'))
# 2) только основной акцент, цвет меню и панели не трогаем (исходные значения)
foreach ($n in 'AccentColorMenu', 'StartColorMenu') { if ($null -ne $orig[$n]) { Set-ItemProperty $acc $n $orig[$n] -Type DWord } else { Remove-ItemProperty $acc $n -ErrorAction SilentlyContinue } }
Restart-Explorer
Say (Shot (Join-Path $Out '03-акцент-розовый-но-цвет-панели-не-тронут.png'))
# возвращаем всё
foreach ($pair in @(@($pers, 'ColorPrevalence'), @($pers, 'SystemUsesLightTheme'), @($dwm, 'AccentColor'), @($dwm, 'ColorizationColor'))) {
    $v = $orig[$pair[1]]
    if ($null -ne $v) { Set-ItemProperty $pair[0] $pair[1] $v -Type DWord } else { Remove-ItemProperty $pair[0] $pair[1] -ErrorAction SilentlyContinue }
}
Restart-Explorer
Say (Shot (Join-Path $Out '04-после-возврата.png'))
