@echo off
setlocal
set "ROOT=%~dp0"
set "DOTNET_ROOT=%ROOT%.dotnet"
set "PATH=%DOTNET_ROOT%;%PATH%"
start "" "%ROOT%src\CodexBar.Win\bin\Debug\net8.0-windows\CodexBar.Win.exe" %*
