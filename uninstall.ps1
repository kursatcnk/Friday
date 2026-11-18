<#
  Friday'i bu bilgisayardan tamamen kaldırır; önce kişisel verilerini (ayarlar, hafıza, sohbetler, hatırlatmalar, notlar,
  ritim, gizlilik defteri) tek bir zip'e yedekler. Yeniden kurmak için GitHub'daki sürümü indirip
  .\setup.ps1 -Restore <yedek.zip> demen yeter; modeller yeniden indirilir, verilerin yerine konur.

  Silinenler:  D:\Friday (araçlar, modeller, veriler)  ·  D:\CodexCache\Friday (derleme önbellekleri)
  Sorulur:     D:\LinguaFlowModels (Whisper; LinguaFlow da kullanır)
  Dokunulmaz:  C: (Friday oraya bir şey yazmaz; yalnızca "Windows açılınca başlat" açıksa tek bir kayıt defteri satırı silinir)

  Kullanım: .\uninstall.ps1 [-BackupTo D:\Friday-yedekler] [-Force]
#>
param(
    [string]$FridayHome = $(if ($env:FRIDAY_HOME) { $env:FRIDAY_HOME } else { 'D:\Friday' }),
    [string]$BackupTo = '',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$drive = Split-Path -Qualifier $FridayHome
if (-not $BackupTo) { $BackupTo = Join-Path $drive 'Friday-yedekler' }
$cache = Join-Path $drive 'CodexCache\Friday'
$whisper = Join-Path $drive 'LinguaFlowModels'

function Size($path) {
    if (-not (Test-Path $path)) { return 0 }
    (Get-ChildItem $path -Recurse -File -Force -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
}

# 1) Friday ve başlattığı yardımcılar kapanır.
Get-Process Friday -ErrorAction SilentlyContinue | Stop-Process -Force
Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($FridayHome, [StringComparison]::OrdinalIgnoreCase) } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 1

# 2) Kişisel veriler yedeklenir (modeller ve önbellekler hariç; birkaç MB).
$data = Join-Path $FridayHome 'data'
if (Test-Path $data) {
    New-Item -ItemType Directory -Force $BackupTo | Out-Null
    $zip = Join-Path $BackupTo ("friday-yedek-{0:yyyy-MM-dd-HHmm}.zip" -f (Get-Date))
    $skip = 'uv-cache', 'tmp', 'xtts-home', 'ollama-home', 'logs', 'captures', 'exports', 'restore-pending'
    $stage = Join-Path $BackupTo '.stage'
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $stage | Out-Null
    Get-ChildItem $data -Force | Where-Object { $skip -notcontains $_.Name -and $_.Name -notlike 'before-restore-*' -and $_.Extension -ne '.log' } |
        ForEach-Object { Copy-Item $_.FullName (Join-Path $stage $_.Name) -Recurse -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
    Remove-Item $stage -Recurse -Force
    Write-Host ("Yedek: {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
}

# 3) Ne silineceği gösterilir ve onay alınır.
$targets = @($FridayHome, $cache) | Where-Object { Test-Path $_ }
$total = ($targets | ForEach-Object { Size $_ } | Measure-Object -Sum).Sum
$targets | ForEach-Object { Write-Host ("  {0,-34} {1,8:N1} GB" -f $_, ((Size $_) / 1GB)) }
if (-not $Force) {
    $answer = Read-Host ("Toplam {0:N1} GB silinsin mi? (e/h)" -f ($total / 1GB))
    if ($answer -notin 'e', 'E', 'evet', 'y') { Write-Host 'Vazgeçildi; hiçbir şey silinmedi.'; exit 0 }
}

foreach ($target in $targets) { Remove-Item $target -Recurse -Force }

if (Test-Path $whisper) {
    $answer = if ($Force) { 'h' } else { Read-Host ("{0} ({1:N1} GB) LinguaFlow da kullanıyor. O da silinsin mi? (e/h)" -f $whisper, ((Size $whisper) / 1GB)) }
    if ($answer -in 'e', 'E', 'evet', 'y') { Remove-Item $whisper -Recurse -Force }
}

# 4) "Windows açılınca başlat" satırı varsa kaldırılır.
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'Friday' -ErrorAction SilentlyContinue

Write-Host ""
Write-Host ("Friday kaldırıldı, {0:N1} GB açıldı. Geri getirmek için: sürümü indir, .\setup.ps1 -Restore <yedek.zip>" -f ($total / 1GB))
