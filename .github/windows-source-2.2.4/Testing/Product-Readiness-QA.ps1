[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Web = Join-Path $Root 'QatFarm.Web'
$script:Checks = New-Object System.Collections.Generic.List[object]

function Add-Check {
    param([string]$Name, [bool]$Passed, [string]$Detail = '')
    $script:Checks.Add([pscustomobject]@{ Name = $Name; Passed = $Passed; Detail = $Detail }) | Out-Null
}
function Read-Utf8Text([string]$Path) { return [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8) }
function Test-ContainsAll([string]$Text, [string[]]$Tokens, [string]$Prefix) {
    foreach ($token in $Tokens) { Add-Check "$Prefix $token" ($Text.Contains($token)) }
}

foreach ($relative in @(
    'BUILD_FINAL_INSTALLER.cmd', 'Installer/Build-Installer.ps1', 'Installer/QatFarmSystem.iss',
    'Installer/Install-SqlExpress.ps1', 'Installer/Install-SqlExpress.cmd',
    'QatFarm.Web/Properties/PublishProfiles/WindowsInstaller.pubxml',
    'QatFarm.Web/Infrastructure/RuntimePaths.cs', 'QatFarm.Web/Infrastructure/StartupState.cs',
    'QatFarm.Web/Infrastructure/RollingFileLogger.cs', 'QatFarm.Web/Services/DatabaseBackupService.cs',
    'QatFarm.Web/Services/UserManagementService.cs', 'QatFarm.Web/Components/Pages/Users.razor',
    'QatFarm.Web/Services/AuditLogService.cs', 'QatFarm.Web/Components/Pages/AuditLogs.razor',
    'QatFarm.Web/appsettings.Production.json', 'Testing/Static-QA.ps1', 'Testing/Product-Readiness-QA.ps1'
)) { Add-Check "ملف المنتج $relative" (Test-Path (Join-Path $Root $relative)) }

foreach ($relative in @('QatFarm.Web/appsettings.json','QatFarm.Web/appsettings.Production.json')) {
    try {
        $null = Get-Content (Join-Path $Root $relative) -Raw -Encoding UTF8 | ConvertFrom-Json
        Add-Check "JSON صالح $relative" $true
    } catch { Add-Check "JSON صالح $relative" $false $_.Exception.Message }
}
foreach ($relative in @('QatFarm.Web/QatFarm.Web.csproj','QatFarm.Web/Properties/PublishProfiles/WindowsInstaller.pubxml')) {
    try {
        $xmlDocument = [xml](Get-Content (Join-Path $Root $relative) -Raw -Encoding UTF8)
        Add-Check "XML صالح $relative" ($null -ne $xmlDocument)
    } catch { Add-Check "XML صالح $relative" $false $_.Exception.Message }
}

$appSettings = Get-Content (Join-Path $Web 'appsettings.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$production = Get-Content (Join-Path $Web 'appsettings.Production.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$connection = [string]$appSettings.ConnectionStrings.DefaultConnection
$developerMachineToken = ('A' + 'OED')
Add-Check 'عدم ربط المشروع باسم جهاز المطور' (-not $connection.ToUpperInvariant().Contains($developerMachineToken)) $connection
Add-Check 'اسم SQL Server محمول' ($connection.Contains('Server=.\SQLEXPRESS')) $connection
Add-Check 'وضع سطح المكتب في الإنتاج' ([bool]$production.DesktopMode.Enabled)
Add-Check 'فتح المتصفح في النسخة المثبتة' ([bool]$production.DesktopMode.AutoOpenBrowser)

$csproj = Read-Utf8Text (Join-Path $Web 'QatFarm.Web.csproj')
$pubxml = Read-Utf8Text (Join-Path $Web 'Properties/PublishProfiles/WindowsInstaller.pubxml')
Add-Check 'إصدار المنتج' ($csproj.Contains('<Version>2.2.3</Version>'))
Add-Check 'نشر مستقل ذاتيًا' ($pubxml.Contains('<SelfContained>true</SelfContained>'))
Add-Check 'نشر Windows x64' ($pubxml.Contains('<RuntimeIdentifier>win-x64</RuntimeIdentifier>'))
Add-Check 'عدم Trim لتفادي كسر Blazor وQuestPDF' ($pubxml.Contains('<PublishTrimmed>false</PublishTrimmed>'))
Add-Check 'تعطيل ReadyToRun لتجنب NETSDK1094' ($pubxml.Contains('<PublishReadyToRun>false</PublishReadyToRun>'))

$program = Read-Utf8Text (Join-Path $Web 'Program.cs')
Test-ContainsAll $program @('AddSingleton<StartupState>()','AddScoped<DatabaseBackupService>()','AddScoped<UserManagementService>()','AddScoped<AuditLogService>()','MapGet("/startup-error"','MapGet("/startup/retry"','MapGet("/health"','startupState.MarkReady()','startupState.MarkFailed(ex)','RuntimePaths.LogsDirectory') 'برنامج التشغيل'
$startIndex = $program.IndexOf('var startupState')
$endIndex = $program.IndexOf('if (desktopMode && autoOpenBrowser)')
$startupBlock = if ($startIndex -ge 0 -and $endIndex -gt $startIndex) { $program.Substring($startIndex, $endIndex - $startIndex) } else { '' }
Add-Check 'عدم إغلاق التطبيق عند فشل القاعدة' ($program.Contains('فشل تهيئة قاعدة البيانات') -and -not $startupBlock.Contains('throw;'))

$backup = Read-Utf8Text (Join-Path $Web 'Services/DatabaseBackupService.cs')
Test-ContainsAll $backup @('BACKUP DATABASE','RESTORE VERIFYONLY','COPY_ONLY','CHECKSUM','EnsureAdministratorAsync','IsCompressionUnsupported') 'النسخ الاحتياطي'

$updater = Read-Utf8Text (Join-Path $Web 'Data/DatabaseSchemaUpdater.cs')
Test-ContainsAll $updater @('QatFarmSchemaVersions','CreditorId','PaidAmount','DebtStatus','MustChangePassword','LastLoginAt') 'ترقية قاعدة البيانات'
$legacyJournalRepair = $updater.IndexOf("COL_LENGTH(N'dbo.JournalEntries', N'Status')")
$journalSourceIndex = $updater.IndexOf('CREATE INDEX IX_JournalEntries_Source')
Add-Check 'إصلاح عمود حالة القيود القديمة قبل إنشاء الفهرس' `
    ($legacyJournalRepair -ge 0 -and $journalSourceIndex -gt $legacyJournalRepair) `
    "Repair=$legacyJournalRepair Index=$journalSourceIndex"
$executeCount = ([regex]::Matches($updater, 'await ExecuteAsync')).Count
$fkCount = ([regex]::Matches($updater, 'FK_CultivationDebtPayments')).Count
Add-Check 'علاقات القاعدة في دفعات مستقلة' ($fkCount -ge 2 -and $executeCount -ge 25) "FK=$fkCount Execute=$executeCount"

$cultivation = Read-Utf8Text (Join-Path $Web 'Services/CultivationExpenseService.cs')
$pdf = Read-Utf8Text (Join-Path $Web 'Services/CultivationDebtPdfService.cs')
$dashboard = Read-Utf8Text (Join-Path $Web 'Services/DashboardService.cs')
foreach ($entry in @(@('الخدمة',$cultivation),@('PDF',$pdf),@('لوحة التحكم',$dashboard))) {
    $name = $entry[0]; $text = $entry[1]
    Add-Check "$name يجمع المبيعات المحصلة" ($text.Contains('CollectedSales') -or $text.Contains('collectedSales'))
    Add-Check "$name يحجز كامل خسائر التربية" ($text.Contains('- totalExpenses') -or $text.Contains('- cultivationLosses'))
    Add-Check "$name يمنع التوزيع السالب" ($text.Contains('Math.Max(0m'))
}
Add-Check 'إزالة معادلة خصم الدين مرتين' (-not (($cultivation + $pdf).ToLowerInvariant().Contains('accountingprofit - outstanding')))
Add-Check 'استخدام الأقل بين الربح والسيولة' ($cultivation.Contains('Math.Min(accountingProfit, cashAfterAllReserves)'))

$usersService = Read-Utf8Text (Join-Path $Web 'Services/UserManagementService.cs')
Test-ContainsAll $usersService @('EnsureAdministratorAsync','EnsureAnotherAdministratorExistsAsync','لا يمكنك إيقاف حسابك الحالي','MustChangePassword = true','UpdateUser','CreateUser') 'إدارة المستخدمين'
$usersPage = Read-Utf8Text (Join-Path $Web 'Components/Pages/Users.razor')
Add-Check 'مسار إدارة المستخدمين' ($usersPage.Contains('@page "/users"'))
Add-Check 'أحداث إدارة المستخدمين متوافقة' (-not $usersPage.Contains('@onclick="Reset"'))

$auditService = Read-Utf8Text (Join-Path $Web 'Services/AuditLogService.cs')
$auditPage = Read-Utf8Text (Join-Path $Web 'Components/Pages/AuditLogs.razor')
foreach ($token in @('EnsureAdministratorAsync','AuditLogs','OrderByDescending','OldValues','NewValues')) {
    Add-Check "سجل التدقيق $token" ($auditService.Contains($token) -or $auditPage.Contains($token))
}
Add-Check 'مسار سجل التدقيق' ($auditPage.Contains('@page "/audit-logs"'))
Add-Check 'سجل التدقيق للمدير فقط' ($auditPage.Contains('[Authorize(Roles = "Administrator")]'))

$routes = New-Object System.Collections.Generic.List[string]
Get-ChildItem $Web -Recurse -File -Filter '*.razor' | ForEach-Object {
    $text = Read-Utf8Text $_.FullName
    foreach ($match in [regex]::Matches($text, '(?m)^@page\s+"([^"]+)"')) { $routes.Add($match.Groups[1].Value) | Out-Null }
}
$uniqueRoutes = @($routes | Sort-Object -Unique)
Add-Check 'عدم تكرار مسارات Razor' ($routes.Count -eq $uniqueRoutes.Count) (($routes -join ', '))

$uiText = ''
Get-ChildItem (Join-Path $Web 'Components'),(Join-Path $Web 'wwwroot') -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {
    try { $uiText += "`n" + (Read-Utf8Text $_.FullName) } catch { }
}
$externalMatches = [regex]::Matches($uiText, '(?i)(?:src|href)\s*=\s*["'']https?://|url\(\s*["'']?https?://')
Add-Check 'الواجهة دون CDN' ($externalMatches.Count -eq 0) ([string]$externalMatches.Count)

$iss = Read-Utf8Text (Join-Path $Root 'Installer/QatFarmSystem.iss')
$builder = Read-Utf8Text (Join-Path $Root 'Installer/Build-Installer.ps1')
Test-ContainsAll $iss @('Release\publish\*','QatFarm.Web.exe','appsettings.Local.json','SQLEXPRESS','PrivilegesRequired=admin') 'المثبت'
Test-ContainsAll $builder @('Microsoft.DotNet.SDK.10','JRSoftware.InnoSetup','dotnet restore','dotnet build','dotnet publish','--self-contained true','Static-QA.ps1','Product-Readiness-QA.ps1') 'بناء المثبت'
Add-Check 'لا يعتمد على Python' (-not ($builder -match '(?i)Get-Command\s+(python|py)|Python\.Python|\.py\b'))
Add-Check 'لا يعتمد على ملف لغة Inno غير مضمون' (-not $iss.Contains('Arabic.isl'))
Add-Check 'إصلاح متغير ProgramFiles(x86)' (-not $builder.Contains('$env:ProgramFiles(x86)') -and $builder.Contains('${env:ProgramFiles(x86)}'))
Add-Check 'تعطيل ReadyToRun في أمر النشر' ($builder.Contains('-p:PublishReadyToRun=false'))

$sqlPrerequisite = Read-Utf8Text (Join-Path $Root 'Installer/Install-SqlExpress.ps1')
Test-ContainsAll $sqlPrerequisite @('MSSQL$SQLEXPRESS','Microsoft.SQLServer.2022.Express','Start-Service','Set-Service') 'أداة SQL Server'

$testSystem = Read-Utf8Text (Join-Path $Root 'Test-System.ps1')
Test-ContainsAll $testSystem @('Static-QA.ps1','Product-Readiness-QA.ps1','dotnet build','-warnaserror','$BaseUrl/health','DatabaseSmokeTests.sql') 'اختبار النظام'
Add-Check 'اختبارات النظام دون Python' (-not ($testSystem -match '(?i)Get-Command\s+(python|py)|Python\.Python|\.py\b'))
Add-Check 'تمرير مسار المشروع بصورة صحيحة' ($testSystem.Contains('"--project", $Project'))

$allText = ''
Get-ChildItem $Root -Recurse -File -ErrorAction SilentlyContinue | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj|Release|.vs)[\\/]' -and
    $_.Extension.ToLowerInvariant() -in @('.cs','.razor','.json','.xml','.csproj','.ps1','.cmd','.bat','.iss','.sql','.md','.txt')
} | ForEach-Object {
    try { $allText += "`n" + (Read-Utf8Text $_.FullName) } catch { }
}
Add-Check 'عدم بقاء اسم جهاز المطور' (-not $allText.ToUpperInvariant().Contains($developerMachineToken))
$upgradeSql = Read-Utf8Text (Join-Path $Web 'database/Upgrade_Product_2_0.sql')
$goCount = ([regex]::Matches($upgradeSql, '(?m)^GO\s*$')).Count
Add-Check 'سكريبت ترقية المنتج النهائي' ($goCount -ge 35 -and $upgradeSql.Contains('QatFarmSchemaVersions')) "GO=$goCount"


$accountingService = Read-Utf8Text (Join-Path $Web 'Services/AccountingService.cs')
$accountingPage = Read-Utf8Text (Join-Path $Web 'Components/Pages/Accounting.razor')
$accountingModels = Read-Utf8Text (Join-Path $Web 'Models/AccountingModels.cs')
Add-Check 'وحدة المحاسبة مسجلة' ($program.Contains('AddScoped<AccountingService>()'))
Add-Check 'مسار المحاسبة محمي ماليًا' ($accountingPage.Contains('[Authorize(Roles = "Administrator,Accountant")]'))
Test-ContainsAll $accountingService @('SyncOperationalLedgerAsync','GetTrialBalanceAsync','GetIncomeStatementAsync','GetFinancialPositionAsync','GetGeneralLedgerAsync','ReverseEntry') 'المحاسبة الاحترافية'
Test-ContainsAll $accountingModels @('AccountCategory','JournalEntryStatus','ChartOfAccount','JournalEntryLine','FinancialPositionModel','GeneralLedgerRow') 'كيانات المحاسبة'
Add-Check 'ترقية 2.1.0 المحاسبية' ($updater.Contains("Version = N'2.1.0'") -and $updater.Contains('CK_JournalEntryLines_DebitCredit'))

$failures = @($script:Checks | Where-Object { -not $_.Passed })
foreach ($check in $script:Checks) {
    $prefix = if ($check.Passed) { 'PASS' } else { 'FAIL' }
    $color = if ($check.Passed) { 'Green' } else { 'Red' }
    $suffix = if ([string]::IsNullOrWhiteSpace($check.Detail)) { '' } else { " - $($check.Detail)" }
    Write-Host "$prefix $($check.Name)$suffix" -ForegroundColor $color
}
Write-Host "`nTOTAL=$($script:Checks.Count) PASS=$($script:Checks.Count - $failures.Count) FAIL=$($failures.Count)"
if ($failures.Count -gt 0) { exit 1 }
exit 0
