@echo off
rem Genere installer\Dynamic-Island-win-Setup.exe, qui installe aussi le runtime .NET 10 Desktop s'il manque.
rem Pour une mise a jour : augmenter <Version> dans Dynamic-Island.csproj, relancer ce script, puis executer le nouveau Setup.
setlocal
cd /d "%~dp0"
set "DOTNET=dotnet"
where dotnet >nul 2>nul || set "DOTNET=%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe"
if not defined DOTNET_ROOT if exist "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe" set "DOTNET_ROOT=%LOCALAPPDATA%\Microsoft\dotnet"
for /f "tokens=3 delims=<>" %%v in ('findstr /c:"<Version>" src\Dynamic-Island\Dynamic-Island.csproj') do set "VERSION=%%v"
call "%~dp0publish.cmd" || exit /b 1
"%DOTNET%" tool restore || exit /b 1
"%DOTNET%" tool run vpk -- pack --packId Dynamic-Island --packVersion %VERSION% --packDir dist --mainExe Dynamic-Island.exe --packTitle Dynamic-Island --icon src\Dynamic-Island\Assets\Dynamic-Island.ico --framework net10.0-x64-desktop --outputDir installer
