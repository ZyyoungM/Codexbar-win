$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $root "build.ps1")
& (Join-Path $root ".dotnet\dotnet.exe") (Join-Path $root "tests\CodexBar.Tests\bin\Debug\net8.0-windows\CodexBar.Tests.dll")
