# Проверка установщика расширений для браузера (вкладка «Плагины → Для браузера») на Windows-машине GitHub:
# скачивает настоящий релиз лаунчера, проверяет сумму и файлы, повторную установку, отказ от опасных архивов, удаление.
# Запускать ПОСЛЕ слияния в main (нужен релиз с MaskPlus-extension.zip).
param([string]$Out = 'results-browser')
New-Item -ItemType Directory -Force $Out | Out-Null
$outFull = (Resolve-Path $Out).Path
$log = Join-Path $outFull 'run.txt'
function Say($t) { $t | Tee-Object -FilePath $log -Append }
$exe = (Resolve-Path 'out-test/BattleLauncher.exe').Path

$p = Start-Process -FilePath $exe -ArgumentList "--selftest-browser=$(Join-Path $outFull 'self')" -PassThru
if (-not $p.WaitForExit(240000)) { $p.Kill(); Say 'selftest-browser: не завершился за 4 минуты' } else { Say "selftest-browser: код $($p.ExitCode)" }
$rep = Join-Path $outFull 'self/report.txt'
if (Test-Path $rep) { Get-Content $rep | ForEach-Object { Say $_ } } else { Say 'report.txt не появился' }

# Обновление плагина не должно его выключать: настоящий релиз Icon Plus ставится, запускается, обновляется поверх работающего и запускается снова
Say ''
Say '=== обновление работающего плагина ==='
$upd = Join-Path $outFull 'update'
$p2 = Start-Process -FilePath $exe -ArgumentList "--selftest-update=$upd" -PassThru
if (-not $p2.WaitForExit(300000)) { $p2.Kill(); Say 'selftest-update: не завершился за 5 минут' } else { Say "selftest-update: код $($p2.ExitCode)" }
$rep2 = Join-Path $upd 'report.txt'
if (Test-Path $rep2) { Get-Content $rep2 | ForEach-Object { Say $_ } } else { Say 'report.txt обновления не появился' }
