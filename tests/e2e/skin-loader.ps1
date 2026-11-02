# Проверка Skin Loader на Windows-машине GitHub: окно (снимки без показа), очистка чужих скинов, запись в настройки плагинов,
# библиотека и файл .ghostskin, и — с настоящими плагинами, установленными в %LOCALAPPDATA%\Programs — применение и откат скина
# (курсор, цвет, обои, панель задач). Значки здесь не применяются (долго, перезапускают Проводник) — их проверяет сценарий icons.
param([string]$Out = 'results-skins')
New-Item -ItemType Directory -Force $Out | Out-Null
$outFull = (Resolve-Path $Out).Path
$log = Join-Path $outFull 'report.txt'
function Say($t) { $t | Tee-Object -FilePath $log -Append }
$exe = (Resolve-Path 'out-test/SkinLoader.exe').Path

Say '=== окно (снимки без показа) ==='
$ui = Join-Path $outFull 'ui'
New-Item -ItemType Directory -Force $ui | Out-Null
$p = Start-Process -FilePath $exe -ArgumentList "--screenshot=$ui" -PassThru
if (-not $p.WaitForExit(120000)) { $p.Kill(); Say 'screenshot: не завершился за 2 минуты' } else { Say "screenshot: код $($p.ExitCode)" }
Get-ChildItem $ui | ForEach-Object { Say "  файл: $($_.Name) ($($_.Length) байт)" }
if (Test-Path (Join-Path $ui 'skin-loader-ui-error.txt')) { Get-Content (Join-Path $ui 'skin-loader-ui-error.txt') | ForEach-Object { Say $_ } }

Say ''
Say '=== самопроверка ==='
$self = Join-Path $outFull 'self'
New-Item -ItemType Directory -Force $self | Out-Null
$p = Start-Process -FilePath $exe -ArgumentList "--selftest=$self", '--apply-tests' -PassThru
if (-not $p.WaitForExit(900000)) { $p.Kill(); Say 'selftest: не завершился за 15 минут' } else { Say "selftest: код $($p.ExitCode)" }
$rep = Join-Path $self 'report.txt'
if (Test-Path $rep) { Get-Content $rep | ForEach-Object { Say $_ } } else { Say 'report.txt не появился' }
