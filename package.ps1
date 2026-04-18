param(
    [string] $Configuration = "Release",
    [switch] $NoZip
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnetRoot = Join-Path $root ".dotnet"
$dotnet = Join-Path $dotnetRoot "dotnet.exe"
$solution = Join-Path $root "CodexBar.Win.sln"
$project = Join-Path $root "src\CodexBar.Win\CodexBar.Win.csproj"
$nugetConfig = Join-Path $root "NuGet.Config"
$buildOutput = Join-Path $root "src\CodexBar.Win\bin\$Configuration\net8.0-windows"

if (!(Test-Path $dotnet)) {
    throw "Local .NET SDK/runtime not found at $dotnet"
}

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments
    )

    & $dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed: $($Arguments -join ' ')"
    }
}

[xml] $versionProps = Get-Content -LiteralPath (Join-Path $root "Directory.Build.props")
$version = $versionProps.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Version not found in Directory.Build.props"
}

$packageName = "CodexBar-portable-win-x64-v$version"
$packageRoot = Join-Path $root "artifacts\package\$packageName"
$zipPath = Join-Path $root "artifacts\package\$packageName.zip"
$runtimeRoot = Join-Path $packageRoot ".dotnet"

$env:DOTNET_ROOT = $dotnetRoot
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

foreach ($path in @($packageRoot, $zipPath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

New-Item -ItemType Directory -Force -Path $packageRoot, $runtimeRoot | Out-Null

Invoke-DotNet @(
    "restore",
    $solution,
    "--configfile", $nugetConfig,
    "-p:RestoreUseStaticGraphEvaluation=true"
)
Invoke-DotNet @(
    "build",
    $project,
    "--no-restore",
    "--configuration", $Configuration,
    "-m:1",
    "-p:UseSharedCompilation=false"
)

$appDll = Join-Path $buildOutput "CodexBar.Win.dll"
if (!(Test-Path -LiteralPath $appDll)) {
    throw "Build output missing app DLL: $appDll"
}

Copy-Item -Path (Join-Path $buildOutput "*") -Destination $packageRoot -Recurse -Force
Get-ChildItem -LiteralPath $packageRoot -Filter "*.pdb" -File -ErrorAction SilentlyContinue | Remove-Item -Force
Copy-Item -LiteralPath (Join-Path $root "README.md") -Destination (Join-Path $packageRoot "README.md")
Copy-Item -LiteralPath (Join-Path $root "CHANGELOG.md") -Destination (Join-Path $packageRoot "CHANGELOG.md")

foreach ($file in @("dotnet.exe", "LICENSE.txt", "ThirdPartyNotices.txt")) {
    Copy-Item -LiteralPath (Join-Path $dotnetRoot $file) -Destination (Join-Path $runtimeRoot $file)
}

foreach ($directory in @("host", "shared")) {
    Copy-Item -LiteralPath (Join-Path $dotnetRoot $directory) -Destination (Join-Path $runtimeRoot $directory) -Recurse -Force
}

$startScript = @'
@echo off
setlocal
set "APP_ROOT=%~dp0"
set "DOTNET_ROOT=%APP_ROOT%.dotnet"
set "PATH=%DOTNET_ROOT%;%PATH%"
start "" /D "%APP_ROOT%" "%DOTNET_ROOT%\dotnet.exe" "%APP_ROOT%CodexBar.Win.dll" %*
'@
Set-Content -LiteralPath (Join-Path $packageRoot "start-codexbar.cmd") -Value $startScript -Encoding ASCII

$settingsScript = @'
@echo off
setlocal
set "APP_ROOT=%~dp0"
set "DOTNET_ROOT=%APP_ROOT%.dotnet"
set "PATH=%DOTNET_ROOT%;%PATH%"
start "" /D "%APP_ROOT%" "%DOTNET_ROOT%\dotnet.exe" "%APP_ROOT%CodexBar.Win.dll" --settings
'@
Set-Content -LiteralPath (Join-Path $packageRoot "open-settings.cmd") -Value $settingsScript -Encoding ASCII

$packageNotes = @"
CodexBar for Windows
Version: $version
Configuration: $Configuration
Package mode: portable bundle with local .NET runtime

Recommended launchers:
- start-codexbar.cmd
- open-settings.cmd

The package includes a local .dotnet runtime under .dotnet\ and does not require a global .NET install.
"@
Set-Content -LiteralPath (Join-Path $packageRoot "PACKAGE_INFO.txt") -Value $packageNotes -Encoding UTF8

if (-not $NoZip) {
    Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -CompressionLevel Optimal
}

Write-Host "version=$version"
Write-Host "build_output=$buildOutput"
Write-Host "package_root=$packageRoot"
if (-not $NoZip) {
    Write-Host "zip_path=$zipPath"
}
