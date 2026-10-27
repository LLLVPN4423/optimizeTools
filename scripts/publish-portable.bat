@echo off
setlocal EnableExtensions
cd /d "%~dp0.."

echo === OptiForge publish framework-dependent ===
dotnet publish src\OptiForge.App\OptiForge.App.csproj -c Release -r win-x64 --self-contained false -o App
if errorlevel 1 exit /b 1
copy /Y "App\OptiForge.App.exe" "App\OptiForge.exe" >nul
echo Done: App\  ^(needs .NET 10 Desktop Runtime^)
endlocal
