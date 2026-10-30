@echo off
rem Genere dist\Dynamic-Island.exe. Le runtime .NET 10 Desktop n'est pas embarque : l'installateur l'installe s'il manque.
setlocal
set "DOTNET=dotnet"
where dotnet >nul 2>nul || set "DOTNET=%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe"
"%DOTNET%" publish "%~dp0src\Dynamic-Island\Dynamic-Island.csproj" -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o "%~dp0dist"
