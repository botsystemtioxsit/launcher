# Разведка: как устроен сайт с иконками (только чтение открытых страниц, несколько запросов, без обхода защиты и без скачивания архивов).
param([string]$Out = 'results-site')
New-Item -ItemType Directory -Force $Out | Out-Null
$log = Join-Path $Out 'report.txt'
function Say($t) { $t | Tee-Object -FilePath $log -Append }
$ua = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36'
function Get-Page([string]$url, [string]$file) {
    try {
        $r = Invoke-WebRequest -Uri $url -UserAgent $ua -MaximumRedirection 5 -TimeoutSec 40
        $path = Join-Path $Out $file
        [System.IO.File]::WriteAllText($path, $r.Content)
        Say ("OK  $url -> $($r.StatusCode), $($r.Content.Length) символов, тип $($r.Headers['Content-Type'])")
        return $r
    } catch { Say ("ОШИБКА $url : " + $_.Exception.Message); return $null }
}
Say "=== robots.txt ==="
$r = Get-Page 'https://vsthemes.org/robots.txt' 'robots.txt'
if ($r) { Say $r.Content }
Say "=== главная ==="
$home = Get-Page 'https://vsthemes.org/ru/' 'home.html'
if ($home) {
    $links = $home.Links | Where-Object { $_.href } | Select-Object -ExpandProperty href -Unique | Select-Object -First 120
    Say "ссылки на главной:"; $links | ForEach-Object { Say "  $_" }
}
Say "=== раздел иконок (угадываем адреса) ==="
foreach ($u in 'https://vsthemes.org/ru/icons/', 'https://vsthemes.org/ru/icon/', 'https://vsthemes.org/ru/icons/windows/') {
    $p = Get-Page $u ('list-' + ($u -replace '[^a-z0-9]', '_') + '.html')
    if ($p) {
        $items = $p.Links | Where-Object { $_.href -match '\.html$' } | Select-Object -ExpandProperty href -Unique | Select-Object -First 15
        Say "  ссылки на материалы:"; $items | ForEach-Object { Say "    $_" }
        if ($items -and -not $script:item) { $script:item = ($items | Select-Object -First 1) }
    }
}
if ($script:item) {
    Say "=== страница материала: $($script:item) ==="
    $ip = Get-Page $script:item 'item.html'
    if ($ip) {
        $dl = $ip.Links | Where-Object { $_.href -match 'download|скач|file|\.7z|\.zip|\.rar' -or $_.outerHTML -match 'download|Скачать' } | Select-Object -ExpandProperty href -Unique | Select-Object -First 15
        Say "  ссылки про скачивание:"; $dl | ForEach-Object { Say "    $_" }
    }
}
Say "=== конец ==="
