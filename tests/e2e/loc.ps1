# Проверка языков интерфейса (English / Deutsch) на Windows-машине GitHub: каждое приложение рисует свои окна в PNG с --lang=en и --lang=de,
# и пишет список русских надписей, которые остались без перевода (loc-missing-<приложение>-<язык>.txt). Плюс самопроверка модуля перевода лаунчера.
param([string]$Out = 'results-loc')
New-Item -ItemType Directory -Force $Out | Out-Null
$log = Join-Path $Out 'report.txt'
function Say($t) { $t | Tee-Object -FilePath $log -Append }
$outFull = (Resolve-Path $Out).Path

$apps = @(
    @{ n = 'BattleLauncher'; exe = 'out-test/BattleLauncher.exe' },
    @{ n = 'GatewayPlus'; exe = 'out-test/GatewayPlus.exe' },
    @{ n = 'IconPlus'; exe = 'out-test/IconPlus.exe' },
    @{ n = 'WallpaperPlus'; exe = 'out-test/WallpaperPlus.exe' },
    @{ n = 'TaskbarPlus'; exe = 'out-test/TaskbarPlus.exe' },
    @{ n = 'SkinLoader'; exe = 'out-test/SkinLoader.exe' },
    @{ n = 'CursorPlus'; exe = 'out-test/CursorPlus.exe' },
    @{ n = 'ColorPlus'; exe = 'out-test/ColorPlus.exe' },
    @{ n = 'DnsPlus'; exe = 'out-test/DnsPlus.exe' }
)

Say '=== самопроверка модуля перевода (лаунчер) ==='
$selfDir = Join-Path $outFull 'selftest'
New-Item -ItemType Directory -Force $selfDir | Out-Null
$p = Start-Process -FilePath (Resolve-Path 'out-test/BattleLauncher.exe').Path -ArgumentList "--selftest-loc=$selfDir" -PassThru
if (-not $p.WaitForExit(120000)) { $p.Kill(); Say 'selftest-loc: не завершился за 120 с' } else { Say "selftest-loc: код $($p.ExitCode)" }
if (Test-Path (Join-Path $selfDir 'loc-selftest.txt')) { Get-Content (Join-Path $selfDir 'loc-selftest.txt') | ForEach-Object { Say $_ } } else { Say 'loc-selftest.txt не появился' }

foreach ($lang in 'en', 'de') {
    foreach ($a in $apps) {
        $exe = (Resolve-Path $a.exe -ErrorAction SilentlyContinue)
        if (-not $exe) { Say "$($a.n): exe не найден"; continue }
        $dir = Join-Path $outFull "$lang/$($a.n)"
        New-Item -ItemType Directory -Force $dir | Out-Null
        Say "=== $($a.n) --lang=$lang ==="
        $p = Start-Process -FilePath $exe.Path -ArgumentList "--screenshot=$dir", "--lang=$lang" -PassThru
        if (-not $p.WaitForExit(240000)) { $p.Kill(); Say "$($a.n) ${lang}: не завершился за 240 с" } else { Say "$($a.n) ${lang}: код $($p.ExitCode)" }
        Get-ChildItem $dir -File | ForEach-Object { Say ("  " + $_.Name + " " + $_.Length) }
        # список непереведённого — в общую сводку
        Get-ChildItem $dir -Filter 'loc-missing-*.txt' -File | ForEach-Object {
            $n = (Get-Content $_.FullName | Where-Object { $_ -and -not $_.StartsWith('---') }).Count
            Say "  непереведённых строк в $($_.Name): $n"
        }
    }
}
