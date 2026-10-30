@echo off
rem Genere dist\Dynamic-Island.exe (autonome : aucun runtime .NET requis pour l'executer).
setlocal
set "DOTNET=dotnet"
where dotnet >nul 2>nul || set "DOTNET=%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe"
"%DOTNET%" publish "%~dp0src\Dynamic-Island\Dynamic-Island.csproj" -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o "%~dp0dist"
