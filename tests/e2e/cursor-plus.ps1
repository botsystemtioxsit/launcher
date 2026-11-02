# Проверка Cursor Plus на Windows-машине GitHub: окно и редактор (снимки без показа), указатели Windows до/после/после возврата,
# шлейф и эффекты клика на настоящем экране (процесс сам водит курсором и снимает экран).
param([string]$Out = 'results-cursor')
. "$PSScriptRoot/common.ps1"
New-Item -ItemType Directory -Force $Out | Out-Null
$log = Join-Path $Out 'report.txt'
function Say($t) { $t | Tee-Object -FilePath $log -Append }
$exe = (Resolve-Path 'out-test/CursorPlus.exe').Path
$outFull = (Resolve-Path $Out).Path

function Run-Exe([string]$name, [string[]]$arguments, [int]$seconds = 120) {
    $p = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru
    if (-not $p.WaitForExit($seconds * 1000)) { $p.Kill(); Say "$name : не завершился за $seconds с" } else { Say "$name : код $($p.ExitCode)" }
}

Say "=== окно и редактор (снимки без показа) ==="
New-Item -ItemType Directory -Force (Join-Path $outFull 'ui') | Out-Null
Run-Exe 'screenshot' @("--screenshot=$(Join-Path $outFull 'ui')") 120
Get-ChildItem (Join-Path $outFull 'ui') | ForEach-Object { Say "  файл: $($_.Name) ($($_.Length) байт)" }
if (Test-Path (Join-Path $outFull 'ui/cursor-plus-ui-error.txt')) { Get-Content (Join-Path $outFull 'ui/cursor-plus-ui-error.txt') | ForEach-Object { Say $_ } }

Say ""
Say "=== самопроверка: указатели Windows ==="
New-Item -ItemType Directory -Force (Join-Path $outFull 'self') | Out-Null
Run-Exe 'selftest' @("--selftest=$(Join-Path $outFull 'self')") 300
if (Test-Path (Join-Path $outFull 'self/report.txt')) { Get-Content (Join-Path $outFull 'self/report.txt') | ForEach-Object { Say $_ } }

Say ""
Say "=== шлейф на настоящем экране ==="
$trail = Join-Path $outFull 'trail'
New-Item -ItemType Directory -Force $trail | Out-Null
foreach ($st in 'comet', 'sparks', 'glow', 'stars', 'bubbles', 'hearts', 'ghost') { Run-Exe "шлейф $st" @("--overlay-test=$trail", "--style=$st") 90 }
Run-Exe 'шлейф радуга' @("--overlay-test=$trail", "--style=comet", "--mode=rainbow") 90
Run-Exe 'шлейф два цвета' @("--overlay-test=$trail", "--style=comet", "--mode=two") 90
Run-Exe 'клик: круги' @("--overlay-test=$trail", "--style=none", "--click=ring") 90
Run-Exe 'клик: вспышка' @("--overlay-test=$trail", "--style=none", "--click=burst") 90
if (Test-Path (Join-Path $trail 'overlay-report.txt')) { Get-Content (Join-Path $trail 'overlay-report.txt') | ForEach-Object { Say $_ } }
Get-ChildItem $trail -Filter *.png | ForEach-Object { Say "  файл: $($_.Name) ($($_.Length) байт)" }

Say ""
Say "реестр после проверок (должен быть как до):"
Say ((Get-ItemProperty 'HKCU:\Control Panel\Cursors' -ErrorAction SilentlyContinue | Out-String).Trim())
