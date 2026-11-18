<#
  İndirilebilir sürümü hazırlar: Friday'i .NET kurulumu gerektirmeyecek şekilde (self-contained, win-x64) yayınlar,
  setup.ps1 ve "Hey Friday" modeliyle birlikte tek bir zip yapar. Ara dosyalar ve zip D:\CodexCache\Friday altında
  kalır; C: sürücüsüne hiçbir şey yazılmaz.

  Kullanım: .\release.ps1 -Version 0.1.0
#>
param([Parameter(Mandatory)][string]$Version)

$ErrorActionPreference = 'Stop'
$cache = 'D:\CodexCache\Friday'
$env:DOTNET_CLI_HOME = "$cache\dotnet-home"
$env:NUGET_PACKAGES = "$cache\nuget-packages"
$env:NUGET_HTTP_CACHE_PATH = "$cache\nuget-http"
$env:NUGET_PLUGINS_CACHE_PATH = "$cache\nuget-plugins"
$env:NUGET_SCRATCH = "$cache\nuget-scratch"
$env:TEMP = "$cache\tmp"
$env:TMP = "$cache\tmp"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
New-Item -ItemType Directory -Force $env:TEMP | Out-Null

$stage = Join-Path $cache "release\Friday-$Version"
$zip = Join-Path $cache "release\Friday-$Version-win-x64.zip"
Remove-Item $stage, $zip -Recurse -Force -ErrorAction SilentlyContinue

dotnet publish (Join-Path $PSScriptRoot 'src\Friday.Desktop\Friday.Desktop.csproj') -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:DebugType=none -o (Join-Path $stage 'app') --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Copy-Item (Join-Path $PSScriptRoot 'setup.ps1'), (Join-Path $PSScriptRoot 'setup-xtts.ps1'), (Join-Path $PSScriptRoot 'uninstall.ps1'), (Join-Path $PSScriptRoot 'LICENSE'), (Join-Path $PSScriptRoot 'README.md') $stage
New-Item -ItemType Directory -Force (Join-Path $stage 'models\wakeword') | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'models\wakeword\*.onnx') (Join-Path $stage 'models\wakeword')
Set-Content -Path (Join-Path $stage 'BAŞLA.txt') -Encoding utf8 -Value @"
Friday $Version

1. PowerShell'de bu klasörde: .\setup.ps1   (Ollama, Piper, Türkçe ses, Whisper ve Gemma 4'ü D:\Friday altına indirir; ~7 GB, bir kez)
2. app\Friday.exe'yi çalıştır.
3. "Hey Friday" de ya da Ctrl+Alt+F'ye bas.

C: sürücüsüne hiçbir şey kurulmaz. Ayrıntılar: README.md
"@

Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Host ("Hazır: {0} ({1:N0} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
