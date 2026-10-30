@echo off
rem Genere installer\Ilot-win-Setup.exe, qui installe aussi le runtime .NET 10 Desktop s'il manque.
rem Pour une mise a jour : augmenter <Version> dans Ilot.csproj, relancer ce script, puis executer le nouveau Setup.
setlocal
cd /d "%~dp0"
set "DOTNET=dotnet"
where dotnet >nul 2>nul || set "DOTNET=%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe"
if not defined DOTNET_ROOT if exist "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe" set "DOTNET_ROOT=%LOCALAPPDATA%\Microsoft\dotnet"
for /f "tokens=3 delims=<>" %%v in ('findstr /c:"<Version>" src\Ilot\Ilot.csproj') do set "VERSION=%%v"
call "%~dp0publish.cmd" || exit /b 1
"%DOTNET%" tool restore || exit /b 1
"%DOTNET%" tool run vpk -- pack --packId Ilot --packVersion %VERSION% --packDir dist --mainExe Ilot.exe --packTitle Ilot --icon src\Ilot\Assets\Ilot.ico --framework net10.0-x64-desktop --outputDir installer
