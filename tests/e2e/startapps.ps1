# Приложения «Пуска» без ярлыков (магазинные и системные): свои ярлыки с тем же идентификатором программы — склеивает ли их «Пуск», какой значок рисует Проводник.
param([string]$Out = 'results-startapps')
New-Item -ItemType Directory -Force $Out | Out-Null
$log = Join-Path $Out 'report.txt'
function Say($t) { $t | Tee-Object -FilePath $log -Append }
$exe = (Resolve-Path 'out-test/IconPlus.exe').Path
$outFull = (Resolve-Path $Out).Path
Say "=== Get-StartApps до опыта (Windows PowerShell) ==="
try { powershell.exe -NoProfile -Command "Get-StartApps | Select-Object -First 40 | Format-Table -AutoSize | Out-String -Width 220" | ForEach-Object { Say $_ } } catch { Say $_.Exception.Message }
Say "=== опыт: свои ярлыки для приложений «Пуска» ==="
$p = Start-Process -FilePath $exe -ArgumentList "--startapps-test=$outFull" -PassThru
if (-not $p.WaitForExit(600000)) { $p.Kill(); Say 'не завершился за 10 минут' } else { Say "код $($p.ExitCode)" }
if (Test-Path (Join-Path $outFull 'report.txt')) { Get-Content (Join-Path $outFull 'report.txt') | ForEach-Object { Say $_ } }
Say "=== «Пуск»: папка ярлыков пользователя после опыта ==="
Get-ChildItem (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs') -ErrorAction SilentlyContinue | ForEach-Object { Say ("  " + $_.Name) }
