@echo off
setlocal EnableExtensions
cd /d "%~dp0.."

echo Publishing portable OptiForge into App\ ...
dotnet publish src\OptiForge.App\OptiForge.App.csproj -c Release -r win-x64 --self-contained false -o App
if errorlevel 1 (
  echo Publish failed.
  pause
  exit /b 1
)

echo Done. Run Launch.bat
endlocal
