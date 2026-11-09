<#
  Friday'in ihtiyaç duyduğu her şeyi D: üzerine indirir. C: sürücüsüne hiçbir şey kurulmaz:
  Ollama taşınabilir zip olarak açılır, kurulum sihirbazı çalıştırılmaz, ortam değişkenleri yalnızca bu
  betiğin süreci için ayarlanır (kalıcı değildir).

  D:\Friday\tools\ollama     Ollama (yerel dil modeli sunucusu)
  D:\Friday\tools\piper      Piper (yerel Türkçe ses sentezi)
  D:\Friday\models\ollama    Dil modeli (varsayılan gemma4:e4b)
  D:\Friday\models\piper     Türkçe ses modeli
  D:\Friday\models\whisper   Whisper (yalnızca D:\LinguaFlowModels'te yoksa iner)
  D:\Friday\models\wakeword  "Hey Jarvis" uyandırma kelimesi (openWakeWord, ~4 MB)
#>
param(
    [string]$FridayHome = $(if ($env:FRIDAY_HOME) { $env:FRIDAY_HOME } else { 'D:\Friday' }),
    [string]$Model = 'gemma4:e4b'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

if ($FridayHome.StartsWith('C:', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Friday C: sürücüsüne kurulmaz. FRIDAY_HOME için D: gibi başka bir sürücü seçin."
}

$OllamaVersion = 'v0.40.6'
$PiperVersion = '2023.11.14-2'
$Voice = 'tr_TR-dfki-medium'
$WhisperFile = 'ggml-large-v3-turbo-q5_0.bin'
$OllamaPort = 11435

$tools = Join-Path $FridayHome 'tools'
$models = Join-Path $FridayHome 'models'
$data = Join-Path $FridayHome 'data'
$downloads = Join-Path $FridayHome 'downloads'
foreach ($dir in $tools, $models, $data, $downloads) { New-Item -ItemType Directory -Force $dir | Out-Null }

function Get-File([string]$Url, [string]$Destination) {
    if (Test-Path $Destination) { Write-Host "  zaten var: $Destination"; return }
    $part = "$Destination.part"
    Write-Host "  indiriliyor: $Url"
    # -C - yarıda kalan indirmeyi kaldığı yerden sürdürür.
    curl.exe -L --fail --retry 5 --retry-delay 3 -C - -o $part $Url
    if ($LASTEXITCODE -ne 0) { throw "İndirme başarısız: $Url" }
    Move-Item $part $Destination
}

Write-Host "[1/6] Ollama $OllamaVersion"
$ollamaDir = Join-Path $tools 'ollama'
$ollamaExe = Join-Path $ollamaDir 'ollama.exe'
if (-not (Test-Path $ollamaExe)) {
    $zip = Join-Path $downloads "ollama-$OllamaVersion-windows-amd64.zip"
    Get-File "https://github.com/ollama/ollama/releases/download/$OllamaVersion/ollama-windows-amd64.zip" $zip
    New-Item -ItemType Directory -Force $ollamaDir | Out-Null
    tar.exe -xf $zip -C $ollamaDir
    if ($LASTEXITCODE -ne 0) { throw "Ollama arşivi açılamadı." }
    Remove-Item $zip
}

Write-Host "[2/6] Piper $PiperVersion"
$piperExe = Join-Path $tools 'piper\piper.exe'
if (-not (Test-Path $piperExe)) {
    $zip = Join-Path $downloads "piper-$PiperVersion-windows-amd64.zip"
    Get-File "https://github.com/rhasspy/piper/releases/download/$PiperVersion/piper_windows_amd64.zip" $zip
    # Arşivin içinde zaten bir piper\ klasörü var.
    tar.exe -xf $zip -C $tools
    if ($LASTEXITCODE -ne 0) { throw "Piper arşivi açılamadı." }
    Remove-Item $zip
}

Write-Host "[3/6] Türkçe sesler"
$voiceDir = Join-Path $models 'piper'
New-Item -ItemType Directory -Force $voiceDir | Out-Null
# Varsayılan: topluluğun Türkçe verilerle eğittiği daha doğal ses (99eren99/piper-turkish-tts).
Get-File "https://huggingface.co/99eren99/piper-turkish-tts/resolve/main/model.onnx" (Join-Path $voiceDir 'tr_TR-99eren99.onnx')
Get-File "https://huggingface.co/99eren99/piper-turkish-tts/resolve/main/config.json" (Join-Path $voiceDir 'tr_TR-99eren99.onnx.json')
# Yedek: Piper'ın resmî Türkçe sesi.
$voiceBase = "https://huggingface.co/rhasspy/piper-voices/resolve/main/tr/tr_TR/dfki/medium/$Voice"
Get-File "$voiceBase.onnx" (Join-Path $voiceDir "$Voice.onnx")
Get-File "$voiceBase.onnx.json" (Join-Path $voiceDir "$Voice.onnx.json")

Write-Host "[4/6] Whisper"
if (Test-Path (Join-Path 'D:\LinguaFlowModels' $WhisperFile)) {
    Write-Host "  LinguaFlow'un modeli kullanılacak: D:\LinguaFlowModels\$WhisperFile"
} else {
    $whisperDir = Join-Path $models 'whisper'
    New-Item -ItemType Directory -Force $whisperDir | Out-Null
    Get-File "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/$WhisperFile" (Join-Path $whisperDir $WhisperFile)
}

Write-Host "[5/6] Uyandırma kelimesi (Hey Jarvis)"
$wakeDir = Join-Path $models 'wakeword'
New-Item -ItemType Directory -Force $wakeDir | Out-Null
# openWakeWord'ün hazır modelleri; ses yalnızca bu bilgisayarda işlenir. Lisans: CC BY-NC-SA 4.0 (ticari olmayan).
$wakeBase = 'https://github.com/dscripka/openWakeWord/releases/download/v0.5.1'
foreach ($file in 'melspectrogram.onnx', 'embedding_model.onnx', 'hey_jarvis_v0.1.onnx') {
    Get-File "$wakeBase/$file" (Join-Path $wakeDir $file)
}

Write-Host "[6/6] Dil modeli ($Model)"
# Bu değişkenler yalnızca bu süreç ve alt süreçleri içindir; Ollama'nın C:\Users\...\.ollama'ya
# yazmasını engeller.
$ollamaHome = Join-Path $data 'ollama-home'
$ollamaTmp = Join-Path $data 'tmp'
New-Item -ItemType Directory -Force $ollamaHome, $ollamaTmp | Out-Null
$env:OLLAMA_MODELS = Join-Path $models 'ollama'
$env:OLLAMA_HOST = "127.0.0.1:$OllamaPort"
$env:USERPROFILE = $ollamaHome
$env:HOME = $ollamaHome
$env:TMP = $ollamaTmp
$env:TEMP = $ollamaTmp

$server = Start-Process -FilePath $ollamaExe -ArgumentList 'serve' -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $data 'ollama-setup.log') -RedirectStandardError (Join-Path $data 'ollama-setup.err.log')
try {
    $ready = $false
    for ($i = 0; $i -lt 60 -and -not $ready; $i++) {
        try { Invoke-RestMethod "http://127.0.0.1:$OllamaPort/api/version" | Out-Null; $ready = $true }
        catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $ready) { throw "Ollama sunucusu başlamadı. Ayrıntı: $data\ollama-setup.err.log" }

    & $ollamaExe pull $Model
    if ($LASTEXITCODE -ne 0) { throw "Model indirilemedi: $Model" }
} finally {
    Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Kurulum tamam. Friday'i başlatmak için: .\run.ps1"
