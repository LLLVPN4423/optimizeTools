@echo off
setlocal EnableExtensions
cd /d "%~dp0.."

echo === OptiForge publish (win-x64, framework-dependent) ===
dotnet publish src\OptiForge.App\OptiForge.App.csproj -c Release -r win-x64 --self-contained false -o App
if errorlevel 1 (
  echo Publish failed.
  exit /b 1
)

copy /Y "App\OptiForge.App.exe" "App\OptiForge.exe" >nul

echo.
echo Published to App\
echo Run: Launch.bat
echo.
echo Optional AI:
echo   1^) Put llama-server.exe in Runtime\llama\
echo   2^) Put a Q4_K_M .gguf in Models\fast\  ^(about 2GB^)
echo.
endlocal
