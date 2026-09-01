[CmdletBinding()]
param(
    [switch]$SkipToolInstall,
    [switch]$NoExplorer,
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Solution = Join-Path $Root "QatFarmSystem.sln"
$Project = Join-Path $Root "QatFarm.Web\QatFarm.Web.csproj"
$PublishDir = Join-Path $Root "Release\publish"
$InstallerDir = Join-Path $Root "Release\installer"
$IssFile = Join-Path $PSScriptRoot "QatFarmSystem.iss"

function Write-Step([string]$Message) {
    Write-Host "`n============================================================" -ForegroundColor DarkGreen
    Write-Host $Message -ForegroundColor Green
    Write-Host "============================================================" -ForegroundColor DarkGreen
}

function Refresh-Path {
    $machine = [Environment]::GetEnvironmentVariable("Path", "Machine")
    $user = [Environment]::GetEnvironmentVariable("Path", "User")
    $env:Path = "$machine;$user"
}

function Ensure-WingetTool([string]$Command, [string]$PackageId, [string]$DisplayName) {
    if (Get-Command $Command -ErrorAction SilentlyContinue) { return }
    if ($SkipToolInstall) { throw "$DisplayName غير موجود." }
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw "winget غير موجود. ثبّت $DisplayName يدويًا ثم أعد المحاولة."
    }
    Write-Step "تثبيت $DisplayName تلقائيًا"
    winget install --id $PackageId --exact --accept-source-agreements --accept-package-agreements --silent
    if ($LASTEXITCODE -ne 0) { throw "فشل تثبيت $DisplayName بواسطة winget." }
    Refresh-Path
}

Write-Step "فحص أدوات البناء"
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Ensure-WingetTool -Command "dotnet" -PackageId "Microsoft.DotNet.SDK.10" -DisplayName ".NET 10 SDK"
}

$sdks = & dotnet --list-sdks
if (-not ($sdks -match '^10\.')) {
    if ($SkipToolInstall) { throw ".NET 10 SDK غير مثبت." }
    Write-Step "تثبيت .NET 10 SDK"
    winget install --id Microsoft.DotNet.SDK.10 --exact --accept-source-agreements --accept-package-agreements --silent
    if ($LASTEXITCODE -ne 0) { throw "فشل تثبيت .NET 10 SDK." }
    Refresh-Path
}


$pwshCommand = Get-Command pwsh.exe -ErrorAction SilentlyContinue
$PowerShellExe = if ($pwshCommand) { $pwshCommand.Source } else { Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe' }
if (-not (Test-Path $PowerShellExe)) { throw 'PowerShell غير موجود.' }

function Find-Iscc {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command -and (Test-Path $command.Source)) { return $command.Source }

    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Inno Setup 6\ISCC.exe",
        "$env:USERPROFILE\AppData\Local\Programs\Inno Setup 6\ISCC.exe"
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path $candidate)) { return $candidate }
    }

    $registryRoots = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )

    foreach ($rootKey in $registryRoots) {
        $apps = Get-ItemProperty $rootKey -ErrorAction SilentlyContinue |
            Where-Object { $_.DisplayName -like 'Inno Setup*' }
        foreach ($app in $apps) {
            if ($app.InstallLocation) {
                $candidate = Join-Path $app.InstallLocation 'ISCC.exe'
                if (Test-Path $candidate) { return $candidate }
            }
            if ($app.DisplayIcon) {
                $iconPath = ($app.DisplayIcon -replace ',\d+$','').Trim('"')
                $folder = Split-Path -Parent $iconPath -ErrorAction SilentlyContinue
                if ($folder) {
                    $candidate = Join-Path $folder 'ISCC.exe'
                    if (Test-Path $candidate) { return $candidate }
                }
            }
        }
    }

    $searchRoots = @(
        $env:LOCALAPPDATA,
        ${env:ProgramFiles(x86)},
        $env:ProgramFiles
    ) | Where-Object { $_ -and (Test-Path $_) }

    foreach ($searchRoot in $searchRoots) {
        $found = Get-ChildItem -Path $searchRoot -Filter ISCC.exe -File -Recurse `
            -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) { return $found.FullName }
    }

    return $null
}

$Iscc = Find-Iscc
if (-not $Iscc -and -not $SkipToolInstall) {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw "Inno Setup غير مثبت وwinget غير متوفر. ثبّت Inno Setup 6 ثم أعد تشغيل الملف."
    }

    Write-Step "تثبيت Inno Setup تلقائيًا"
    & winget install --id JRSoftware.InnoSetup --exact --source winget `
        --accept-source-agreements --accept-package-agreements --silent --disable-interactivity

    if ($LASTEXITCODE -ne 0) {
        Write-Host "محاولة إصلاح/ترقية Inno Setup..." -ForegroundColor Yellow
        & winget upgrade --id JRSoftware.InnoSetup --exact --source winget `
            --accept-source-agreements --accept-package-agreements --silent --disable-interactivity
    }

    Start-Sleep -Seconds 2
    Refresh-Path
    $Iscc = Find-Iscc
}

if (-not $Iscc) {
    throw @"
لم يتم العثور على ISCC.exe الخاص بـ Inno Setup.
ثبّت Inno Setup 6 ثم أعد تشغيل BUILD_FINAL_INSTALLER.cmd.
الأمر اليدوي:
winget install --id JRSoftware.InnoSetup -e --source winget
المسار المتوقع غالبًا:
$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe
"@
}

Write-Host "Inno Setup compiler: $Iscc" -ForegroundColor Cyan

Write-Step "تنظيف ملفات البناء القديمة"
Get-ChildItem $Root -Directory -Recurse -Force |
    Where-Object { $_.Name -in @('bin', 'obj', '.vs') } |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $PublishDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $InstallerDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item $PublishDir -ItemType Directory -Force | Out-Null
New-Item $InstallerDir -ItemType Directory -Force | Out-Null

Write-Step "استعادة حزم NuGet"
& dotnet restore $Solution --configfile (Join-Path $Root "NuGet.config") --force
if ($LASTEXITCODE -ne 0) { throw "فشل Restore." }

Write-Step "بناء المشروع وفحص المترجم"
& dotnet build $Solution -c Release --no-restore -warnaserror
if ($LASTEXITCODE -ne 0) { throw "فشل Build. راجع الأخطاء الظاهرة." }

Write-Step "تشغيل اختبارات الجاهزية دون Python"
& $PowerShellExe -NoLogo -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Root "Testing\Static-QA.ps1")
if ($LASTEXITCODE -ne 0) { throw "فشل الفحص الثابت الأساسي." }
& $PowerShellExe -NoLogo -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Root "Testing\Product-Readiness-QA.ps1")
if ($LASTEXITCODE -ne 0) { throw "فشل فحص جاهزية المنتج." }

Write-Step "استعادة حزم Runtime الخاصة بـ Windows"
& dotnet restore $Project --configfile (Join-Path $Root "NuGet.config") -r $Runtime --force `
    -p:PublishReadyToRun=false
if ($LASTEXITCODE -ne 0) { throw "فشل Restore الخاص بـ $Runtime." }

Write-Step "نشر نسخة Windows مستقلة دون الحاجة إلى .NET"
& dotnet publish $Project -c Release -r $Runtime --self-contained true --no-restore `
    -p:PublishReadyToRun=false -p:PublishSingleFile=false -p:PublishTrimmed=false `
    -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "فشل Publish." }

$exe = Join-Path $PublishDir "QatFarm.Web.exe"
if (-not (Test-Path $exe)) { throw "ملف التشغيل المنشور غير موجود: $exe" }

Write-Step "إنشاء ملف التثبيت"
& $Iscc $IssFile
if ($LASTEXITCODE -ne 0) { throw "فشل إنشاء Setup بواسطة Inno Setup." }

$setup = Get-ChildItem $InstallerDir -Filter "*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $setup) { throw "لم يتم العثور على ملف Setup النهائي." }

$hash = Get-FileHash $setup.FullName -Algorithm SHA256
$report = @"
QatFarm System Release
Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
Setup: $($setup.FullName)
Size: $($setup.Length)
SHA256: $($hash.Hash)
Runtime: $Runtime self-contained
"@
$report | Set-Content (Join-Path $Root "Release\RELEASE_INFO.txt") -Encoding UTF8

Write-Step "اكتمل إنشاء المثبت بنجاح"
Write-Host "Setup: $($setup.FullName)" -ForegroundColor Cyan
Write-Host "SHA256: $($hash.Hash)" -ForegroundColor Cyan
if (-not $NoExplorer) {
    Start-Process explorer.exe -ArgumentList "/select,`"$($setup.FullName)`""
}
