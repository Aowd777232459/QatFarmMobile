[CmdletBinding()]
param(
    [switch]$Silent
)

$ErrorActionPreference = "Stop"
$Host.UI.RawUI.WindowTitle = "تجهيز SQL Server لنظام المزارع"

function Get-SqlService {
    Get-Service -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq 'MSSQL$SQLEXPRESS' -or $_.Name -eq 'MSSQLSERVER' } |
        Select-Object -First 1
}

Write-Host "فحص SQL Server..." -ForegroundColor Cyan
$service = Get-SqlService
if ($service) {
    if ($service.Status -ne 'Running') {
        Write-Host "تشغيل خدمة $($service.Name)..." -ForegroundColor Yellow
        Set-Service -Name $service.Name -StartupType Automatic
        Start-Service -Name $service.Name
    }
    Write-Host "SQL Server جاهز: $($service.Name)" -ForegroundColor Green
    if (-not $Silent) { Read-Host "اضغط Enter للإغلاق" }
    exit 0
}

Write-Host "لم يتم العثور على SQL Server Database Engine." -ForegroundColor Yellow
if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
    if ($Silent) {
        throw "winget غير موجود. ثبّت SQL Server 2022 Express ثم أعد تشغيل المثبّت."
    }
    Write-Host "winget غير موجود. سيتم فتح صفحة تنزيل SQL Server الرسمية." -ForegroundColor Yellow
    Start-Process "https://www.microsoft.com/sql-server/sql-server-downloads"
    Read-Host "بعد تثبيت SQL Server Express أعد تشغيل هذه الأداة. اضغط Enter للإغلاق"
    exit 1
}

if ($Silent) {
    Write-Host "تثبيت SQL Server 2022 Express تلقائيًا..." -ForegroundColor Cyan
    winget install --id Microsoft.SQLServer.2022.Express --exact --silent --disable-interactivity `
        --accept-source-agreements --accept-package-agreements
} else {
    Write-Host "سيتم فتح مثبت SQL Server 2022 Express. اختر التثبيت Basic أو Custom واترك اسم النسخة SQLEXPRESS." -ForegroundColor Cyan
    winget install --id Microsoft.SQLServer.2022.Express --exact --interactive `
        --accept-source-agreements --accept-package-agreements
}
if ($LASTEXITCODE -ne 0) {
    throw "لم يكتمل تثبيت SQL Server Express. راجع رسالة المثبت ثم أعد تشغيل الأداة."
}

$service = $null
for ($attempt = 0; $attempt -lt 60 -and -not $service; $attempt++) {
    Start-Sleep -Seconds 2
    $service = Get-SqlService
}
if ($service) {
    Set-Service -Name $service.Name -StartupType Automatic
    if ($service.Status -ne 'Running') { Start-Service -Name $service.Name }
    Write-Host "تم تجهيز SQL Server: $($service.Name)" -ForegroundColor Green
} else {
    throw "اكتمل مثبت SQL Server، لكن الخدمة لم تظهر. أعد تشغيل Windows ثم افتح النظام."
}

if (-not $Silent) { Read-Host "اضغط Enter للإغلاق" }
