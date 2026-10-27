@echo off
setlocal
rem Przebudowa ze zrodel (wymaga .NET SDK: winget install Microsoft.DotNet.SDK.8)
set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
if not exist "%DOTNET%" winget install -e --id Microsoft.DotNet.SDK.8 --accept-source-agreements --accept-package-agreements
"%DOTNET%" build "%~dp0SebLauncher.csproj" -c Release -o "%~dp0publish"
if errorlevel 1 (pause & exit /b 1)
set "TARGET=E:\Programs\SebLauncher"
if not exist "%TARGET%" mkdir "%TARGET%"
taskkill /im SebLauncher.exe /f >nul 2>nul
copy /y "%~dp0publish\SebLauncher.exe" "%TARGET%\" >nul
start "" "%TARGET%\SebLauncher.exe"
