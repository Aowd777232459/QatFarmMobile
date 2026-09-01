[CmdletBinding()]
param([string]$SqlInstance)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($SqlInstance)) {
    $SqlInstance = Read-Host "اكتب اسم SQL Server (الافتراضي .\SQLEXPRESS)"
    if ([string]::IsNullOrWhiteSpace($SqlInstance)) { $SqlInstance = ".\SQLEXPRESS" }
}

$DataDir = Join-Path $env:ProgramData "QatFarmSystem"
$ConfigPath = Join-Path $DataDir "appsettings.Local.json"
$BackupDir = Join-Path $DataDir "Backups"
New-Item $DataDir -ItemType Directory -Force | Out-Null
New-Item $BackupDir -ItemType Directory -Force | Out-Null

$configuration = [ordered]@{
    ConnectionStrings = [ordered]@{
        DefaultConnection = "Server=$SqlInstance;Database=QatFarmDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;"
    }
    DesktopMode = [ordered]@{
        Enabled = $true
        Url = "http://127.0.0.1:5275"
        AutoOpenBrowser = $true
    }
    DatabaseBackup = [ordered]@{
        Directory = "%ProgramData%\QatFarmSystem\Backups"
        KeepLatest = 30
    }
}

$configuration | ConvertTo-Json -Depth 5 | Set-Content $ConfigPath -Encoding UTF8
Write-Host "تم حفظ إعداد قاعدة البيانات:" -ForegroundColor Green
Write-Host $ConfigPath -ForegroundColor Cyan
Write-Host "الخادم: $SqlInstance" -ForegroundColor Cyan
Write-Host "أغلق النظام ثم شغله من جديد." -ForegroundColor Yellow
Read-Host "اضغط Enter للإغلاق"
