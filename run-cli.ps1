param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $CliArgs
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnetRoot = Join-Path $root ".dotnet"
$dotnet = Join-Path $dotnetRoot "dotnet.exe"
$dll = Join-Path $root "src\CodexBar.Cli\bin\Debug\net8.0-windows\CodexBar.Cli.dll"

if (!(Test-Path $dotnet)) {
    throw "Local .NET SDK/runtime not found at $dotnet"
}

if (!(Test-Path $dll)) {
    & (Join-Path $root "build.ps1")
}

$env:DOTNET_ROOT = $dotnetRoot
$env:PATH = "$dotnetRoot;$env:PATH"

& $dotnet $dll @CliArgs
