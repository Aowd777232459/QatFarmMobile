[CmdletBinding()]
param(
    [string]$SqlServer = ".\SQLEXPRESS",
    [string]$Database = "QatFarmDb",
    [string]$BaseUrl = "http://127.0.0.1:5288"
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$Solution = Join-Path $Root "QatFarmSystem.sln"
$Project = Join-Path $Root "QatFarm.Web\QatFarm.Web.csproj"
$SqlTest = Join-Path $Root "Testing\DatabaseSmokeTests.sql"
$LogFile = Join-Path $Root "Testing\app-smoke-test.log"
$ErrorLogFile = Join-Path $Root "Testing\app-smoke-test-error.log"
$Process = $null

function Pass([string]$Message) { Write-Host "[PASS] $Message" -ForegroundColor Green }
function Info([string]$Message) { Write-Host "[INFO] $Message" -ForegroundColor Cyan }
function Fail([string]$Message) { throw "[FAIL] $Message" }
function Read-AppLog {
    $Text = ""
    if (Test-Path $LogFile) { $Text += Get-Content $LogFile -Raw }
    if (Test-Path $ErrorLogFile) { $Text += "`n" + (Get-Content $ErrorLogFile -Raw) }
    return $Text
}

try {
    Info "التحقق من .NET SDK"
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Fail ".NET 10 SDK غير مثبت." }
    $DotnetVersion = (& dotnet --version).Trim()
    if (-not $DotnetVersion.StartsWith("10.")) { Fail "المطلوب .NET SDK 10، والموجود هو $DotnetVersion" }
    Pass ".NET SDK $DotnetVersion"

    Info "استعادة الحزم وبناء Release"
    & dotnet restore $Solution --configfile (Join-Path $Root "NuGet.config") --force
    if ($LASTEXITCODE -ne 0) { Fail "فشل Restore" }
    & dotnet build $Solution -c Release --no-restore -warnaserror
    if ($LASTEXITCODE -ne 0) { Fail "فشل Build" }
    Pass "المترجم أنهى البناء دون أخطاء أو تحذيرات"

    Info "تشغيل فحوصات المصدر دون Python"
    $PowerShellExe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    if (-not (Test-Path $PowerShellExe)) { Fail "Windows PowerShell غير موجود." }
    & $PowerShellExe -NoLogo -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Root "Testing\Static-QA.ps1")
    if ($LASTEXITCODE -ne 0) { Fail "فشل الفحص الثابت الأساسي" }
    & $PowerShellExe -NoLogo -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Root "Testing\Product-Readiness-QA.ps1")
    if ($LASTEXITCODE -ne 0) { Fail "فشل فحص جاهزية المنتج" }
    Pass "فحوصات الجاهزية دون Python"

    $SqlCmd = Get-Command sqlcmd -ErrorAction SilentlyContinue
    if ($null -eq $SqlCmd) {
        Write-Host "[WARN] sqlcmd غير مثبت؛ سيتم فحص الاتصال من داخل التطبيق." -ForegroundColor Yellow
    } else {
        Info "تشغيل اختبارات SQL داخل معاملة يتم التراجع عنها"
        & sqlcmd -S $SqlServer -E -d $Database -b -i $SqlTest
        if ($LASTEXITCODE -ne 0) { Fail "فشل اختبار SQL" }
        Pass "اختبارات قاعدة البيانات"
    }

    $env:QATFARM_CONNECTION_STRING = "Server=$SqlServer;Database=$Database;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;"
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $env:ASPNETCORE_URLS = $BaseUrl

    Remove-Item $LogFile, $ErrorLogFile -Force -ErrorAction SilentlyContinue
    Info "تشغيل التطبيق واختبار Health وواجهة الدخول والملفات المحلية"
    $Arguments = @(
        "run", "--no-build", "--no-restore", "-c", "Release",
        "--project", $Project, "--no-launch-profile"
    )
    $Process = Start-Process dotnet -ArgumentList $Arguments -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $LogFile -RedirectStandardError $ErrorLogFile

    $Ready = $false
    for ($Attempt = 1; $Attempt -le 45; $Attempt++) {
        Start-Sleep -Seconds 1
        if ($Process.HasExited) { Fail "توقف التطبيق أثناء البدء.`n$(Read-AppLog)" }
        try {
            $Health = Invoke-WebRequest -Uri "$BaseUrl/health" -UseBasicParsing -TimeoutSec 3
            if ($Health.StatusCode -eq 200) { $Ready = $true; break }
        } catch { }
    }
    if (-not $Ready) {
        try {
            $Diagnostic = Invoke-WebRequest -Uri "$BaseUrl/startup-error" -UseBasicParsing -TimeoutSec 5
            Fail "بدأ التطبيق في وضع التشخيص بسبب قاعدة البيانات.`n$($Diagnostic.Content)`n$(Read-AppLog)"
        } catch {
            Fail "لم يصبح التطبيق جاهزًا.`n$(Read-AppLog)"
        }
    }
    Pass "Health وقاعدة البيانات"

    $Login = Invoke-WebRequest -Uri "$BaseUrl/account/login" -UseBasicParsing -TimeoutSec 5
    if ($Login.StatusCode -ne 200 -or $Login.Content -notmatch "نظام إدارة مزارع وبيع القات") {
        Fail "صفحة تسجيل الدخول غير صحيحة"
    }
    Pass "صفحة تسجيل الدخول"

    foreach ($asset in @("css/app.css", "css/offline.css", "js/app.js", "favicon.svg")) {
        $Response = Invoke-WebRequest -Uri "$BaseUrl/$asset" -UseBasicParsing -TimeoutSec 5
        if ($Response.StatusCode -ne 200) { Fail "فشل تحميل الملف المحلي $asset" }
    }
    Pass "CSS وJavaScript والأيقونات المحلية"

    Write-Host "`nاكتملت اختبارات البناء والتشغيل الأساسية بنجاح." -ForegroundColor Green
}
finally {
    Remove-Item Env:QATFARM_CONNECTION_STRING -ErrorAction SilentlyContinue
    Remove-Item Env:ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue
    Remove-Item Env:ASPNETCORE_URLS -ErrorAction SilentlyContinue
    if ($null -ne $Process -and -not $Process.HasExited) {
        Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
    }
}
