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
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'

dotnet restore (Join-Path $PSScriptRoot 'Friday.slnx') --nologo -m:1
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build (Join-Path $PSScriptRoot 'Friday.slnx') -c Release --no-restore --nologo -m:1
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet test (Join-Path $PSScriptRoot 'Friday.slnx') -c Release --no-restore --no-build --nologo -m:1
exit $LASTEXITCODE
