@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "OPTIFORGE_ROOT=%~dp0"
set "OPTIFORGE_DATA=%OPTIFORGE_ROOT%App\data"
set "OPTIFORGE_MODELS=%OPTIFORGE_ROOT%Models"
set "OPTIFORGE_LLAMA=%OPTIFORGE_ROOT%Runtime\llama"
set "OPTIFORGE_KNOWLEDGE=%OPTIFORGE_ROOT%Knowledge"

if not exist "%OPTIFORGE_DATA%" mkdir "%OPTIFORGE_DATA%"

set "EXE=%OPTIFORGE_ROOT%App\OptiForge.exe"
if not exist "%EXE%" set "EXE=%OPTIFORGE_ROOT%App\OptiForge.App.exe"
if not exist "%EXE%" set "EXE=%OPTIFORGE_ROOT%src\OptiForge.App\bin\Release\net10.0-windows\OptiForge.App.exe"
if not exist "%EXE%" set "EXE=%OPTIFORGE_ROOT%src\OptiForge.App\bin\Debug\net10.0-windows\OptiForge.App.exe"

if not exist "%EXE%" (
  echo [OptiForge] Executable not found.
  echo.
  echo Build + publish:
  echo   scripts\publish-portable.bat
  echo.
  pause
  exit /b 1
)

echo [OptiForge] Root=%OPTIFORGE_ROOT%
echo [OptiForge] Starting %EXE%
start "" "%EXE%"
endlocal
