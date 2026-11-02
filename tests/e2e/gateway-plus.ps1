# Проверка Gateway Plus на Windows-машине GitHub: окно (снимок без показа), проверка конфигов, шифрование профиля, скрипт сервера (bash -n),
# и — после установки настоящего WireGuard — установка и снятие службы туннеля на несуществующий сервер (интернет раннера не трогается).
param([string]$Out = 'results-gateway')
. "$PSScriptRoot/common.ps1"
New-Item -ItemType Directory -Force $Out | Out-Null
$log = Join-Path $Out 'report.txt'
function Say($t) { $t | Tee-Object -FilePath $log -Append }
$exe = (Resolve-Path 'out-test/GatewayPlus.exe').Path
$outFull = (Resolve-Path $Out).Path

function Run-Exe([string]$name, [string[]]$arguments, [int]$seconds = 180) {
    $p = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru
    if (-not $p.WaitForExit($seconds * 1000)) { $p.Kill(); Say "$name : не завершился за $seconds с" } else { Say "$name : код $($p.ExitCode)" }
}

Say "=== окно (снимок без показа) ==="
New-Item -ItemType Directory -Force (Join-Path $outFull 'ui') | Out-Null
Run-Exe 'screenshot' @("--screenshot=$(Join-Path $outFull 'ui')") 120
Get-ChildItem (Join-Path $outFull 'ui') | ForEach-Object { Say "  файл: $($_.Name) ($($_.Length) байт)" }
if (Test-Path (Join-Path $outFull 'ui/gateway-plus-ui-error.txt')) { Get-Content (Join-Path $outFull 'ui/gateway-plus-ui-error.txt') | ForEach-Object { Say $_ } }

Say ""
Say "=== без WireGuard: проверки, не требующие его ==="
New-Item -ItemType Directory -Force (Join-Path $outFull 'self-no-wg') | Out-Null
Run-Exe 'selftest (без WireGuard)' @("--selftest=$(Join-Path $outFull 'self-no-wg')") 180
if (Test-Path (Join-Path $outFull 'self-no-wg/report.txt')) { Get-Content (Join-Path $outFull 'self-no-wg/report.txt') | ForEach-Object { Say $_ } }

Say ""
Say "=== скрипт сервера: проверка синтаксиса bash ==="
$sh = Join-Path $outFull 'self-no-wg/server-setup.sh'
$bash = Get-Command bash -ErrorAction SilentlyContinue
if ($bash -and (Test-Path $sh)) { & $bash.Source -n $sh; Say "bash -n: код $LASTEXITCODE" } else { Say 'bash или файл скрипта не найдены' }

Say ""
Say "=== установка настоящего WireGuard (официальный MSI) ==="
try {
    $idx = (Invoke-WebRequest 'https://download.wireguard.com/windows-client/' -UseBasicParsing).Content
    $m = [regex]::Matches($idx, 'wireguard-amd64-([0-9.]+)\.msi') | Sort-Object { [version]$_.Groups[1].Value } | Select-Object -Last 1
    Say "последняя версия на сайте: $($m.Value)"
    $msi = Join-Path $env:RUNNER_TEMP 'wireguard.msi'
    Invoke-WebRequest "https://download.wireguard.com/windows-client/$($m.Value)" -OutFile $msi -UseBasicParsing
    $sig = Get-AuthenticodeSignature $msi
    Say "подпись MSI: $($sig.Status) · $($sig.SignerCertificate.Subject)"
    $p = Start-Process msiexec.exe -ArgumentList '/i', $msi, '/qn', '/norestart' -Wait -PassThru
    Say "msiexec: код $($p.ExitCode)"
} catch { Say ("установка WireGuard не удалась: " + $_.Exception.Message) }
Say ("wireguard.exe: " + (Test-Path 'C:\Program Files\WireGuard\wireguard.exe'))

Say ""
Say "=== с WireGuard: служба туннеля ==="
New-Item -ItemType Directory -Force (Join-Path $outFull 'self') | Out-Null
Run-Exe 'selftest (с WireGuard)' @("--selftest=$(Join-Path $outFull 'self')") 300
if (Test-Path (Join-Path $outFull 'self/report.txt')) { Get-Content (Join-Path $outFull 'self/report.txt') | ForEach-Object { Say $_ } }
Say ""
Say ("остались службы WireGuard: " + ((Get-Service -Name 'WireGuardTunnel*' -ErrorAction SilentlyContinue | Out-String).Trim()))
Say ("папка ProgramData осталась: " + (Test-Path 'C:\ProgramData\GhostShell\GatewayPlus'))
