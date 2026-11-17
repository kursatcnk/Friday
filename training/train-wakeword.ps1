<#
  "Hey Friday" uyandırma kelimesini yeniden eğitir. Her şey D:\Friday\training altında kalır; C: sürücüsüne hiçbir
  şey yazılmaz (Python'un, PyTorch'un ve CUDA'nın ev, geçici ve önbellek klasörleri bu süreç için D:'ye yönlendirilir).

  Gerekenler (bir kez, ~330 MB):
    D:\Friday\training\voices     en_US-libritts_r-medium, en_GB-vctk-medium (Piper, huggingface.co/rhasspy/piper-voices)
    D:\Friday\training\features   validation_set_features.npy (huggingface.co/datasets/davidscripka/openwakeword_features)
    D:\Friday\training\pylibs     onnxruntime, onnx (+ ml_dtypes, flatbuffers, coloredlogs, humanfriendly)
  PyTorch, setup-xtts.ps1'in kurduğu D:\Friday\tools\xtts-env'den kullanılır; o ortama paket eklenmez.

  Kullanım: .\training\train-wakeword.ps1 [generate|build|train|all]
#>
param([string]$Stage = 'all', [int]$Steps = 20000)

$ErrorActionPreference = 'Stop'
$root = 'D:\Friday\training'
$python = 'D:\Friday\tools\xtts-env\Scripts\python.exe'
$home2 = Join-Path $root 'home'
New-Item -ItemType Directory -Force $home2, (Join-Path $root 'tmp') | Out-Null

$env:PYTHONPATH = Join-Path $root 'pylibs'
$env:PYTHONNOUSERSITE = '1'
$env:PYTHONDONTWRITEBYTECODE = '1'
$env:PYTHONIOENCODING = 'utf-8'
$env:HOME = $home2
$env:USERPROFILE = $home2
$env:APPDATA = Join-Path $home2 'Roaming'
$env:LOCALAPPDATA = Join-Path $home2 'Local'
$env:TEMP = Join-Path $root 'tmp'
$env:TMP = Join-Path $root 'tmp'
$env:TORCH_HOME = Join-Path $home2 'torch'
$env:CUDA_CACHE_PATH = Join-Path $home2 'cuda-cache'
$env:FRIDAY_TRAINING = $root

& $python (Join-Path $PSScriptRoot 'wakeword\train_wakeword.py') $Stage --steps $Steps
exit $LASTEXITCODE
