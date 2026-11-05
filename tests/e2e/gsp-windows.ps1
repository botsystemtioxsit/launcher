# Проверка клиента GSP на Windows-машине GitHub: настоящая сетевая карта wintun, служба Windows, адрес / маршруты / DNS,
# обмен пакетами (ping через туннель к «ответчику» на этой же машине), остановка службы и уборка за собой.
# Второго компьютера нет, поэтому сервер — `gsp echo-server` (он отвечает на ping любому адресу из разрешённой сети).
param([string]$Out = 'results-gsp')
New-Item -ItemType Directory -Force $Out | Out-Null
$log = Join-Path $Out 'report.txt'
function Say($t) { $t | Tee-Object -FilePath $log -Append }
$script:fail = 0
function Check($name, $ok, $extra = '') { if ($ok) { Say "  OK    $name" } else { $script:fail++; Say "  FAIL  $name $extra" } }

$gsp = (Resolve-Path 'out-test/gsp.exe').Path
Say "gsp: $(& $gsp version)"
Say ("wintun.dll рядом: " + (Test-Path (Join-Path (Split-Path $gsp) 'wintun.dll')))
$work = Join-Path $env:RUNNER_TEMP 'gsp-test'
New-Item -ItemType Directory -Force $work | Out-Null
$statusFile = Join-Path $work 'status.json'
$name = 'GhostShellTest'

$sk = (& $gsp genkey).Trim(); $ck = (& $gsp genkey).Trim()
$sp = ($sk | & $gsp pubkey).Trim(); $cp = ($ck | & $gsp pubkey).Trim()
Check 'ключи создаются' (($sk.Length -eq 44) -and ($sp.Length -eq 44))

$echoOut = Join-Path $work 'echo.out'
$echo = Start-Process -FilePath $gsp -ArgumentList @('echo-server', '-key', $sk, '-peer', $cp, '-allowed', '10.77.0.2/32', '-port', '7100') -PassThru -RedirectStandardOutput $echoOut -RedirectStandardError (Join-Path $work 'echo.err') -WindowStyle Hidden
Start-Sleep -Seconds 2
Say ("echo-server: " + ((Get-Content $echoOut -ErrorAction SilentlyContinue) -join ' '))

$conf = Join-Path $work 'client.conf'
@"
[Interface]
PrivateKey = $ck
Address = 10.77.0.2/32
MTU = 1380
DNS = 192.0.2.53
Name = $name
[Peer]
PublicKey = $sp
Endpoint = 127.0.0.1:7100
AllowedIPs = 10.77.0.1/32, 198.51.100.0/24
PersistentKeepalive = 5
"@ | Set-Content -Path $conf -Encoding ascii

try {
    Say ""
    Say "=== установка службы ==="
    $o = & $gsp install-service -c $conf -name test -status $statusFile 2>&1
    Say ("install-service: код $LASTEXITCODE; " + ($o -join ' '))
    Check 'служба установилась и работает' (($LASTEXITCODE -eq 0) -and ((& $gsp service-state -name test) -eq 'running'))
    Check 'служба видна системе (sc query)' ((sc.exe query 'GhostShellGsp$test' | Out-String) -match 'RUNNING')
    Start-Sleep -Seconds 3

    Say ""
    Say "=== сетевая карта ==="
    $ad = Get-NetAdapter -Name $name -ErrorAction SilentlyContinue
    Check 'карта шлюза появилась' ($null -ne $ad)
    if ($ad) { Say ("  карта: " + ($ad | Format-List Name, InterfaceDescription, Status, ifIndex | Out-String)) }
    $ip = Get-NetIPAddress -InterfaceAlias $name -AddressFamily IPv4 -ErrorAction SilentlyContinue
    Check 'адрес 10.77.0.2 на карте' (@($ip | Where-Object { $_.IPAddress -eq '10.77.0.2' }).Count -eq 1)
    $ifc = Get-NetIPInterface -InterfaceAlias $name -AddressFamily IPv4 -ErrorAction SilentlyContinue
    Check 'размер пакета 1380' ($ifc -and $ifc.NlMtu -eq 1380) "(NlMtu=$($ifc.NlMtu))"
    Check 'метрика карты 0 (она главная)' ($ifc -and $ifc.InterfaceMetric -le 5) "(InterfaceMetric=$($ifc.InterfaceMetric))"
    $r1 = Get-NetRoute -DestinationPrefix '198.51.100.0/24' -InterfaceAlias $name -ErrorAction SilentlyContinue
    $r2 = Get-NetRoute -DestinationPrefix '10.77.0.1/32' -InterfaceAlias $name -ErrorAction SilentlyContinue
    Check 'маршрут 198.51.100.0/24 через карту' ($null -ne $r1)
    Check 'маршрут 10.77.0.1/32 через карту' ($null -ne $r2)
    $dns = Get-DnsClientServerAddress -InterfaceAlias $name -AddressFamily IPv4 -ErrorAction SilentlyContinue
    Check 'DNS 192.0.2.53 на карте' (@($dns.ServerAddresses) -contains '192.0.2.53') "($($dns.ServerAddresses -join ','))"

    Say ""
    Say "=== пакеты ходят через туннель ==="
    $p1 = ping.exe -n 4 -w 2000 10.77.0.1 | Out-String
    Say $p1
    Check 'ping 10.77.0.1 (адрес сервера в туннеле)' ($p1 -match 'TTL=' -and $p1 -match '(Received|Получено) = [1-4]')
    $p2 = ping.exe -n 3 -w 2000 198.51.100.7 | Out-String
    Check 'ping 198.51.100.7 (другая сеть шлюза)' ($p2 -match 'TTL=')
    Start-Sleep -Seconds 2
    if (Test-Path $statusFile) {
        $st = Get-Content $statusFile -Raw | ConvertFrom-Json
        Say ("  состояние: " + (Get-Content $statusFile -Raw))
        Check 'файл состояния: соединено' ($st.peers[0].connected -eq $true)
        Check 'файл состояния: есть трафик' (($st.peers[0].txPackets -gt 3) -and ($st.peers[0].rxPackets -gt 3))
    } else { Check 'файл состояния существует' $false }

    Say ""
    Say "=== маршрут к серверу мимо туннеля (на безопасном адресе) ==="
    $rt = Start-Process -FilePath $gsp -ArgumentList @('selftest-route', '-ip', '203.0.113.9', '-hold', '8') -PassThru -RedirectStandardOutput (Join-Path $work 'route.out') -RedirectStandardError (Join-Path $work 'route.err') -WindowStyle Hidden
    Start-Sleep -Seconds 4
    $hr = Get-NetRoute -DestinationPrefix '203.0.113.9/32' -ErrorAction SilentlyContinue
    Check 'узкий маршрут /32 поставлен' ($null -ne $hr)
    if ($hr) { Say ("  " + (($hr | Format-Table DestinationPrefix, NextHop, InterfaceAlias, RouteMetric -AutoSize | Out-String).Trim())) }
    $rt.WaitForExit(30000) | Out-Null
    Say ("  вывод: " + ((Get-Content (Join-Path $work 'route.out') -ErrorAction SilentlyContinue) -join ' | ') + " " + ((Get-Content (Join-Path $work 'route.err') -ErrorAction SilentlyContinue) -join ' | '))
    Check 'узкий маршрут /32 убран' ($null -eq (Get-NetRoute -DestinationPrefix '203.0.113.9/32' -ErrorAction SilentlyContinue))

    Say ""
    Say "=== повторная установка поверх работающей ==="
    $o = & $gsp install-service -c $conf -name test -status $statusFile 2>&1
    Check 'повторная установка прошла' (($LASTEXITCODE -eq 0) -and ((& $gsp service-state -name test) -eq 'running')) ($o -join ' ')
    Start-Sleep -Seconds 3
    $p3 = ping.exe -n 3 -w 2000 10.77.0.1 | Out-String
    Check 'после переустановки ping идёт' ($p3 -match 'TTL=')

    Say ""
    Say "=== остановка и уборка ==="
    $o = & $gsp remove-service -name test 2>&1
    Say ("remove-service: код $LASTEXITCODE; " + ($o -join ' '))
    Start-Sleep -Seconds 2
    Check 'службы больше нет' ((& $gsp service-state -name test) -eq 'none')
    Check 'карта шлюза исчезла' ($null -eq (Get-NetAdapter -Name $name -ErrorAction SilentlyContinue))
    Check 'маршрут шлюза исчез' ($null -eq (Get-NetRoute -DestinationPrefix '198.51.100.0/24' -ErrorAction SilentlyContinue))
    Check 'файл состояния убран' (-not (Test-Path $statusFile))

    Say ""
    Say "=== плохие настройки: служба не остаётся, ошибка видна ==="
    $bad = Join-Path $work 'bad.conf'
    "[Interface]`nPrivateKey = $ck`nName = $name`n" | Set-Content -Path $bad -Encoding ascii
    $o = & $gsp install-service -c $bad -name bad -status $statusFile 2>&1
    Say ("install-service (плохой): код $LASTEXITCODE; " + ($o -join ' '))
    Check 'с плохими настройками установка не удалась' ($LASTEXITCODE -ne 0)
    Check 'служба с плохими настройками не осталась' ((& $gsp service-state -name bad) -eq 'none')
    if (Test-Path $statusFile) { Say ("  состояние после ошибки: " + (Get-Content $statusFile -Raw)); Check 'причина записана в файл состояния' ((Get-Content $statusFile -Raw) -match '"error"') }
    else { Check 'причина записана в файл состояния' $false }
}
finally {
    & $gsp remove-service -name test 2>&1 | Out-Null
    & $gsp remove-service -name bad 2>&1 | Out-Null
    if ($echo -and -not $echo.HasExited) { $echo.Kill() }
}
Say ""
Say ("ИТОГ: " + $(if ($script:fail -eq 0) { 'всё прошло' } else { "провалено проверок: $script:fail" }))
exit $script:fail
