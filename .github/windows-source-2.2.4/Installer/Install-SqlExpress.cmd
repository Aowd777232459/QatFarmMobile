@echo off
setlocal EnableExtensions
set "PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
net session >nul 2>&1
if errorlevel 1 (
  "%PS%" -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b 0
)
"%PS%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-SqlExpress.ps1"
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" pause
exit /b %RC%
