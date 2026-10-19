<#
  Doğal ve tamamen yerel Türkçe ses (XTTS v2) için gerekenleri D: üzerine kurar. C: sürücüsüne hiçbir şey
  yazılmaz: Python, uv ile D:\Friday\tools\python'a kurulur; kayıt defterine ve PATH'e eklenmez. Ortam
  değişkenleri yalnızca bu betiğin süreci içindir.

  D:\Friday\tools\uv          uv (Python paket yöneticisi, tek dosya)
  D:\Friday\tools\python      Python 3.12 (taşınabilir)
  D:\Friday\tools\xtts-env    PyTorch (CUDA 12.8) + coqui-tts
  D:\Friday\models\xtts       XTTS v2 modeli (~1,8 GB)

  XTTS v2 modelinin lisansı (Coqui Public Model License) yalnızca ticari olmayan kullanıma izin verir.
#>
param(
    [string]$FridayHome = $(if ($env:FRIDAY_HOME) { $env:FRIDAY_HOME } else { 'D:\Friday' })
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

if ($FridayHome.StartsWith('C:', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Friday C: sürücüsüne kurulmaz. FRIDAY_HOME için D: gibi başka bir sürücü seçin."
}

$UvVersion = '0.13.1'
$tools = Join-Path $FridayHome 'tools'
$data = Join-Path $FridayHome 'data'
$downloads = Join-Path $FridayHome 'downloads'
$uvDir = Join-Path $tools 'uv'
$uv = Join-Path $uvDir 'uv.exe'
$envDir = Join-Path $tools 'xtts-env'
$modelDir = Join-Path $FridayHome 'models\xtts'
foreach ($dir in $tools, $data, $downloads, $modelDir) { New-Item -ItemType Directory -Force $dir | Out-Null }

# uv ve Python'un önbellekleri, kurulumları ve geçici dosyaları D: üzerinde kalır.
$env:UV_CACHE_DIR = Join-Path $data 'uv-cache'
$env:UV_PYTHON_INSTALL_DIR = Join-Path $tools 'python'
$env:UV_PYTHON_BIN_DIR = Join-Path $tools 'python\bin'
$env:UV_TOOL_DIR = Join-Path $tools 'uv-tools'
$env:UV_NO_CONFIG = '1'
$env:TMP = Join-Path $data 'tmp'
$env:TEMP = $env:TMP
New-Item -ItemType Directory -Force $env:TMP | Out-Null

function Get-File([string]$Url, [string]$Destination) {
    if (Test-Path $Destination) { Write-Host "  zaten var: $Destination"; return }
    $part = "$Destination.part"
    Write-Host "  indiriliyor: $Url"
    curl.exe -L --fail --retry 5 --retry-delay 3 -C - -o $part $Url
    if ($LASTEXITCODE -ne 0) { throw "İndirme başarısız: $Url" }
    Move-Item $part $Destination
}

Write-Host "[1/4] uv $UvVersion"
if (-not (Test-Path $uv)) {
    $zip = Join-Path $downloads "uv-$UvVersion.zip"
    Get-File "https://github.com/astral-sh/uv/releases/download/$UvVersion/uv-x86_64-pc-windows-msvc.zip" $zip
    New-Item -ItemType Directory -Force $uvDir | Out-Null
    tar.exe -xf $zip -C $uvDir
    if ($LASTEXITCODE -ne 0) { throw "uv arşivi açılamadı." }
    Remove-Item $zip
}

Write-Host "[2/4] Python 3.12 (taşınabilir, kayıt defterine yazılmaz)"
& $uv python install 3.12 --no-bin --no-registry
if ($LASTEXITCODE -ne 0) { throw "Python kurulamadı." }
if (-not (Test-Path (Join-Path $envDir 'Scripts\python.exe'))) {
    & $uv venv $envDir --python 3.12 --managed-python
    if ($LASTEXITCODE -ne 0) { throw "Sanal ortam oluşturulamadı." }
}
$python = Join-Path $envDir 'Scripts\python.exe'

Write-Host "[3/4] PyTorch (CUDA 12.8) ve coqui-tts"
& $uv pip install --python $python torch==2.8.0 torchaudio==2.8.0 --index-url https://download.pytorch.org/whl/cu128
if ($LASTEXITCODE -ne 0) { throw "PyTorch kurulamadı." }
# transformers 5, coqui-tts 0.27.5'in kullandığı isin_mps_friendly işlevini kaldırdı; 4.x sabitlenir.
& $uv pip install --python $python coqui-tts==0.27.5 "transformers>=4.57,<5"
if ($LASTEXITCODE -ne 0) { throw "coqui-tts kurulamadı." }

Write-Host "[4/4] XTTS v2 modeli"
foreach ($file in 'config.json', 'vocab.json', 'speakers_xtts.pth', 'model.pth') {
    Get-File "https://huggingface.co/coqui/XTTS-v2/resolve/main/$file" (Join-Path $modelDir $file)
}

& $python -c "import torch; print('CUDA:', torch.cuda.is_available(), torch.cuda.get_device_name(0) if torch.cuda.is_available() else '')"
Write-Host ""
Write-Host "XTTS kuruldu. Friday'i yeniden başlatınca ses motoru 'xtts' olur."
