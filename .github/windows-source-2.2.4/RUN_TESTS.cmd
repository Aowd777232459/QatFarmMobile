@echo off
setlocal EnableExtensions
cd /d "%~dp0"
set "PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if not exist "%PS%" (
  echo ERROR: Windows PowerShell was not found.
  pause
  exit /b 1
)
"%PS%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Test-System.ps1"
set "RC=%ERRORLEVEL%"
echo.
pause
exit /b %RC%
