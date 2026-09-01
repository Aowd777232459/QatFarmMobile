@echo off
setlocal EnableExtensions
cd /d "%~dp0"
echo ============================================================
echo QatFarm System - Restore and build
 echo ============================================================
where dotnet >nul 2>nul
if errorlevel 1 (
  echo ERROR: .NET 10 SDK is not installed.
  echo Run BUILD_FINAL_INSTALLER.cmd as Administrator.
  pause
  exit /b 1
)
for /d /r %%D in (bin,obj) do if exist "%%D" rd /s /q "%%D"
dotnet restore "QatFarmSystem.sln" --configfile "%~dp0NuGet.config" --force
if errorlevel 1 goto :fail
dotnet build "QatFarmSystem.sln" -c Release --no-restore -warnaserror
if errorlevel 1 goto :fail
echo.
echo SUCCESS: Build completed without errors or warnings.
pause
exit /b 0
:fail
echo.
echo ERROR: Build failed. Review the compiler messages above.
pause
exit /b 1
