$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnet = Join-Path $root ".dotnet\dotnet.exe"

if (!(Test-Path $dotnet)) {
    throw "Local .NET SDK not found at $dotnet"
}

$env:DOTNET_ROOT = Join-Path $root ".dotnet"
$env:DOTNET_CLI_HOME = $root
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = "0"
$env:NUGET_PACKAGES = Join-Path $root ".nuget\packages"
$env:APPDATA = Join-Path $root ".appdata"
$env:LOCALAPPDATA = Join-Path $root ".localappdata"
$env:MSBUILDDISABLENODEREUSE = "1"

New-Item -ItemType Directory -Force -Path $env:APPDATA, $env:LOCALAPPDATA, $env:NUGET_PACKAGES | Out-Null

& $dotnet restore (Join-Path $root "CodexBar.Win.sln") --configfile (Join-Path $root "NuGet.Config") -p:RestoreUseStaticGraphEvaluation=true
& $dotnet build (Join-Path $root "CodexBar.Win.sln") --no-restore -m:1 -p:UseSharedCompilation=false
