$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
Write-Host "سيتم تثبيت أدوات البناء الناقصة، فحص النظام، ونشر نسخة مستقلة ثم إنشاء Setup." -ForegroundColor Cyan
& PowerShell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Root "Installer\Build-Installer.ps1")
exit $LASTEXITCODE
