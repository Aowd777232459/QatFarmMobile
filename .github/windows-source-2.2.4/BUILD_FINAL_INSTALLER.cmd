@echo off
setlocal EnableExtensions
cd /d "%~dp0"
set "PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if not exist "%PS%" (
  echo ERROR: Windows PowerShell was not found.
  pause
  exit /b 1
)
echo ============================================================
echo QatFarm System - Build, test, publish and create installer
echo ============================================================
"%PS%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Installer\Build-Installer.ps1"
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" (
  echo.
  echo ERROR: Installer build failed. Review the messages above.
  pause
  exit /b %RC%
)
echo.
echo SUCCESS: Setup file was created under Release\installer
pause
exit /b 0
