param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $AppArgs
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnetRoot = Join-Path $root ".dotnet"
$exe = Join-Path $root "src\CodexBar.Win\bin\Debug\net8.0-windows\CodexBar.Win.exe"

if (!(Test-Path (Join-Path $dotnetRoot "host\fxr"))) {
    throw "Local .NET runtime not found under $dotnetRoot"
}

if (!(Test-Path $exe)) {
    & (Join-Path $root "build.ps1")
}

$env:DOTNET_ROOT = $dotnetRoot
$env:PATH = "$dotnetRoot;$env:PATH"

Start-Process -FilePath $exe -ArgumentList $AppArgs -WorkingDirectory (Split-Path -Parent $exe)
