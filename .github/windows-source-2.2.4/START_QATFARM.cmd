@echo off
setlocal EnableExtensions
cd /d "%~dp0"
if exist "Release\publish\QatFarm.Web.exe" (
  start "" "Release\publish\QatFarm.Web.exe"
  exit /b 0
)
where dotnet >nul 2>nul
if errorlevel 1 (
  echo ERROR: No published build and .NET SDK was not found.
  echo Run BUILD_FINAL_INSTALLER.cmd first.
  pause
  exit /b 1
)
if not exist "QatFarm.Web\bin\Release\net10.0\QatFarm.Web.dll" call "%~dp0RESTORE_AND_BUILD.cmd"
if errorlevel 1 exit /b 1
set "ASPNETCORE_ENVIRONMENT=Development"
dotnet run --project "QatFarm.Web\QatFarm.Web.csproj" -c Release --no-build --no-restore
exit /b %ERRORLEVEL%
