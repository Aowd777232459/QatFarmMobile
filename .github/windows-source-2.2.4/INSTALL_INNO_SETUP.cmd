@echo off
setlocal
chcp 65001 >nul

echo Installing Inno Setup 6...
where winget >nul 2>nul
if errorlevel 1 (
  echo ERROR: winget was not found.
  echo Install Inno Setup 6 manually from the official JRSoftware website.
  pause
  exit /b 1
)

winget install --id JRSoftware.InnoSetup -e --source winget --accept-source-agreements --accept-package-agreements --silent --disable-interactivity
if errorlevel 1 (
  winget upgrade --id JRSoftware.InnoSetup -e --source winget --accept-source-agreements --accept-package-agreements --silent --disable-interactivity
)

echo.
echo Checking ISCC.exe...
for %%P in (
  "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
  "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
  "%ProgramFiles%\Inno Setup 6\ISCC.exe"
) do (
  if exist "%%~P" (
    echo FOUND: %%~P
    echo Inno Setup is ready.
    pause
    exit /b 0
  )
)

echo Inno Setup may be installed, but ISCC.exe was not found in the common paths.
echo Restart Windows, then run BUILD_FINAL_INSTALLER.cmd again.
pause
exit /b 1
