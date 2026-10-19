$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = 'D:\CodexCache\Friday\dotnet-home'
$env:NUGET_PACKAGES = 'D:\CodexCache\Friday\nuget-packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

$exe = Join-Path $PSScriptRoot 'src\Friday.Desktop\bin\Release\net10.0-windows10.0.19041.0\win-x64\Friday.exe'
if (-not (Test-Path $exe)) {
    dotnet build (Join-Path $PSScriptRoot 'src\Friday.Desktop\Friday.Desktop.csproj') -c Release --nologo -m:1
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Start-Process $exe
