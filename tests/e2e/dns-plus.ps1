# Проверка DNS Plus на Windows-машине GitHub: разбор ID и настоящее включение / повторное включение / выключение DNS через HTTPS (NextDNS).
# Сравнивается состояние адаптеров до, во время и после: автоматический DNS должен остаться автоматическим, прописанный вручную — вручную.
# На машине меняется DNS на несколько секунд; в конце страховка сбрасывает всё, что могло остаться.
param([string]$Out = 'results-dns')
New-Item -ItemType Directory -Force $Out | Out-Null
$log = Join-Path $Out 'report.txt'
function Say($t) { $t | Tee-Object -FilePath $log -Append }
$exe = (Resolve-Path 'out-test/DnsPlus.exe').Path
$outFull = (Resolve-Path $Out).Path
$ips = @('45.90.28.0', '45.90.30.0', '2a07:a8c0::', '2a07:a8c1::')

Say "=== адаптеры и DNS до проверки ==="
Get-NetAdapter | Select-Object Name, Status, HardwareInterface, ifIndex, InterfaceGuid | Format-Table -AutoSize | Out-String | ForEach-Object { Say $_ }
Get-DnsClientServerAddress | Where-Object { $_.ServerAddresses } | Format-Table InterfaceAlias, AddressFamily, ServerAddresses -AutoSize | Out-String | ForEach-Object { Say $_ }
Say ("есть Add-DnsClientDohServerAddress: " + [bool](Get-Command Add-DnsClientDohServerAddress -ErrorAction SilentlyContinue))

Say ""
Say "=== самопроверка DnsPlus.exe ==="
$self = Join-Path $outFull 'self'
New-Item -ItemType Directory -Force $self | Out-Null
try {
    $p = Start-Process -FilePath $exe -ArgumentList "--selftest=$self" -PassThru
    if (-not $p.WaitForExit(300000)) { $p.Kill(); Say 'selftest: не завершился за 300 с' } else { Say "selftest: код $($p.ExitCode)" }
}
finally {
    # страховка: если самопроверка оборвалась посреди включения, не оставляем DNS машины в руках NextDNS
    foreach ($a in (Get-NetAdapter | Where-Object { $_.Status -eq 'Up' })) {
        $c = Get-DnsClientServerAddress -InterfaceIndex $a.ifIndex -ErrorAction SilentlyContinue
        if ($c -and (@($c.ServerAddresses | Where-Object { $ips -contains $_ }).Count -gt 0)) {
            Say "страховка: сбрасываю DNS на $($a.Name)"
            try { Set-DnsClientServerAddress -InterfaceIndex $a.ifIndex -ResetServerAddresses } catch { }
        }
    }
    foreach ($ip in $ips) { Remove-DnsClientDohServerAddress -ServerAddress $ip -ErrorAction SilentlyContinue }
}
if (Test-Path (Join-Path $self 'report.txt')) { Get-Content (Join-Path $self 'report.txt') | ForEach-Object { Say $_ } } else { Say 'report.txt самопроверки не появился' }

Say ""
Say "=== DNS после проверки ==="
Get-DnsClientServerAddress | Where-Object { $_.ServerAddresses } | Format-Table InterfaceAlias, AddressFamily, ServerAddresses -AutoSize | Out-String | ForEach-Object { Say $_ }
Say ("остались наши записи DoH: " + (@(Get-DnsClientDohServerAddress -ErrorAction SilentlyContinue | Where-Object { $ips -contains $_.ServerAddress }).Count))
Say ("остался файл состояния: " + (Test-Path 'C:\ProgramData\GhostShell\DnsPlus\state.json'))
