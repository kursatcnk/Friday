$ErrorActionPreference = 'Stop'
# .NET ve NuGet'in bütün önbellekleri ve geçici dosyaları C: yerine D: üzerinde tutulur.
$cache = 'D:\CodexCache\Friday'
$env:DOTNET_CLI_HOME = "$cache\dotnet-home"
$env:NUGET_PACKAGES = "$cache\nuget-packages"
$env:NUGET_HTTP_CACHE_PATH = "$cache\nuget-http"
$env:NUGET_PLUGINS_CACHE_PATH = "$cache\nuget-plugins"
$env:NUGET_SCRATCH = "$cache\nuget-scratch"
$env:TEMP = "$cache\tmp"
$env:TMP = "$cache\tmp"
New-Item -ItemType Directory -Force $env:TEMP | Out-Null
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

$exe = Join-Path $PSScriptRoot 'src\Friday.Desktop\bin\Release\net10.0-windows10.0.19041.0\win-x64\Friday.exe'
if (-not (Test-Path $exe)) {
    dotnet build (Join-Path $PSScriptRoot 'src\Friday.Desktop\Friday.Desktop.csproj') -c Release --nologo -m:1
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Start-Process $exe
