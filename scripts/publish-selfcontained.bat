@echo off
setlocal EnableExtensions
cd /d "%~dp0.."

echo === OptiForge publish SELF-CONTAINED win-x64 ===
echo Larger output (~70-100MB+) but no .NET install required on target PC.
dotnet publish src\OptiForge.App\OptiForge.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o App
if errorlevel 1 exit /b 1
copy /Y "App\OptiForge.App.exe" "App\OptiForge.exe" >nul
echo Done: App\OptiForge.exe
endlocal
