# Разведка: можно ли поменять картинки значков внутри магазинных приложений (WindowsApps) с резервной копией и возвратом?
param([string]$Out = 'results-store')
New-Item -ItemType Directory -Force $Out | Out-Null
$log = Join-Path $Out 'report.txt'
function Say($t) { $t | Tee-Object -FilePath $log -Append }
Add-Type -AssemblyName System.Drawing

$repo = 'HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages'
Say "=== пакеты в реестре пользователя ==="
$pkgs = @()
if (Test-Path $repo) {
    foreach ($k in Get-ChildItem $repo) {
        $root = (Get-ItemProperty $k.PSPath -ErrorAction SilentlyContinue).PackageRootFolder
        $pkgs += [pscustomobject]@{ Name = $k.PSChildName; Root = $root }
    }
}
Say "найдено пакетов: $($pkgs.Count)"
$pkgs | Select-Object -First 60 | ForEach-Object { Say ("  {0}  ->  {1}" -f $_.Name, $_.Root) }

Say ""
Say "=== манифесты: читаются ли и есть ли значки-файлы ==="
$cands = @()
foreach ($p in $pkgs) {
    if (-not $p.Root) { continue }
    $mf = Join-Path $p.Root 'AppxManifest.xml'
    $canRead = $false; $logos = @()
    try {
        if (Test-Path $mf) {
            [xml]$x = Get-Content $mf -Raw -ErrorAction Stop
            $canRead = $true
            $ve = $x.GetElementsByTagName('*') | Where-Object { $_.LocalName -eq 'VisualElements' }
            foreach ($v in $ve) { foreach ($a in 'Square44x44Logo', 'Square150x150Logo') { if ($v.GetAttribute($a)) { $logos += $v.GetAttribute($a) } } }
        }
    } catch { }
    if ($canRead -and $logos.Count -gt 0) {
        $first = $logos[0]
        $abs = Join-Path $p.Root $first
        $dir = Split-Path $abs -Parent
        $base = [System.IO.Path]::GetFileNameWithoutExtension($abs)
        $files = @(Get-ChildItem $dir -Filter "$base*.png" -ErrorAction SilentlyContinue)
        Say ("  {0}: логотипы {1}; файлов-вариантов {2}" -f $p.Name, ($logos -join ', '), $files.Count)
        if ($files.Count -gt 0) { $cands += [pscustomobject]@{ Name = $p.Name; Dir = $dir; Files = $files } }
    } else { Say ("  {0}: манифест читается: {1}" -f $p.Name, $canRead) }
}
Say "подходящих для опыта: $($cands.Count)"

Say ""
Say "=== опыт на двух пакетах ==="
$bak = Join-Path $Out 'acl'
New-Item -ItemType Directory -Force $bak | Out-Null
$n = 0
foreach ($c in ($cands | Select-Object -First 2)) {
    $n++
    Say "--- $($c.Name) : $($c.Dir)"
    $f = $c.Files[0].FullName
    $h0 = (Get-FileHash $f -Algorithm SHA256).Hash
    $owner0 = (Get-Acl $f).Owner
    Say "файл: $f  владелец: $owner0  sha256: $($h0.Substring(0, 12))"
    $aclFile = Join-Path $bak "acl-$n.txt"
    & icacls $c.Dir /save $aclFile /t /c | Out-Null
    Say "ACL сохранены: $((Test-Path $aclFile))"
    $orig = [System.IO.File]::ReadAllBytes($f)
    # попытка записи без прав
    $plain = $true
    try { [System.IO.File]::WriteAllBytes($f, $orig); } catch { $plain = $false }
    Say "запись без смены владельца: $plain"
    & takeown /f $c.Dir /r /d y | Out-Null
    & icacls $c.Dir /grant '*S-1-5-32-544:(OI)(CI)F' /t /c | Out-Null
    $owner1 = (Get-Acl $f).Owner
    Say "владелец после takeown: $owner1"
    # подмена: розовая картинка того же размера
    $img = [System.Drawing.Image]::FromFile($f); $w = $img.Width; $h = $img.Height; $img.Dispose()
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.Clear([System.Drawing.Color]::FromArgb(255, 42, 157)); $g.Dispose()
    $tmp = Join-Path $Out "tmp-$n.png"; $bmp.Save($tmp, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    try { [System.IO.File]::Copy($tmp, $f, $true); Say "запись после смены владельца: ok" } catch { Say ("запись не вышла: " + $_.Exception.Message) }
    $h1 = (Get-FileHash $f -Algorithm SHA256).Hash
    Say "sha256 после подмены: $($h1.Substring(0, 12))  изменился: $($h1 -ne $h0)"
    # возврат
    [System.IO.File]::WriteAllBytes($f, $orig)
    & icacls (Split-Path $c.Dir -Parent) /restore $aclFile /c | Out-Null
    $h2 = (Get-FileHash $f -Algorithm SHA256).Hash
    $owner2 = (Get-Acl $f).Owner
    Say "после возврата: sha256 совпал: $($h2 -eq $h0)  владелец: $owner2 (было $owner0)"
}
Say "=== конец ==="
