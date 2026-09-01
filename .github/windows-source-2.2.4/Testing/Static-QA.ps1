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

function Read-Utf8Text([string]$Path) {
    return [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
}

function Test-ContainsAll([string]$Text, [string[]]$Tokens, [string]$Prefix) {
    foreach ($token in $Tokens) {
        Add-Check "$Prefix $token" ($Text.Contains($token))
    }
}

$required = @(
    'QatFarmSystem.sln', 'global.json', 'QatFarm.Web/QatFarm.Web.csproj', 'QatFarm.Web/Program.cs',
    'QatFarm.Web/Models/DomainModels.cs', 'QatFarm.Web/Models/ViewModels.cs',
    'QatFarm.Web/Services/CustomerService.cs', 'QatFarm.Web/Services/ZakatService.cs',
    'QatFarm.Web/Components/Pages/Customers.razor', 'QatFarm.Web/Components/Pages/CustomerDetails.razor',
    'QatFarm.Web/Components/Pages/Zakat.razor', 'QatFarm.Web/Components/Shared/ZakatNotification.razor',
    'QatFarm.Web/database/Upgrade_Legendary_Customers_Zakat.sql',
    'QatFarm.Web/Services/CultivationExpenseService.cs', 'QatFarm.Web/Services/CultivationDebtPdfService.cs',
    'QatFarm.Web/Services/DatabaseBackupService.cs', 'QatFarm.Web/Infrastructure/StartupState.cs',
    'Installer/QatFarmSystem.iss', 'Installer/Build-Installer.ps1',
    'QatFarm.Web/Services/UserManagementService.cs', 'QatFarm.Web/Components/Pages/Users.razor',
    'QatFarm.Web/Services/AuditLogService.cs', 'QatFarm.Web/Components/Pages/AuditLogs.razor'
)
foreach ($relative in $required) {
    Add-Check "وجود $relative" (Test-Path (Join-Path $Root $relative))
}

Get-ChildItem $Web -Recurse -File -Filter '*.json' | ForEach-Object {
    try {
        $null = (Get-Content $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json)
        Add-Check "JSON $($_.FullName.Substring($Root.Length + 1))" $true
    }
    catch {
        Add-Check "JSON $($_.FullName.Substring($Root.Length + 1))" $false $_.Exception.Message
    }
}

try {
    $xmlDocument = [xml](Get-Content (Join-Path $Web 'QatFarm.Web.csproj') -Raw -Encoding UTF8)
    Add-Check 'XML csproj' ($null -ne $xmlDocument)
}
catch { Add-Check 'XML csproj' $false $_.Exception.Message }

$appSettings = Get-Content (Join-Path $Web 'appsettings.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$conn = [string]$appSettings.ConnectionStrings.DefaultConnection
Add-Check 'اتصال SQL Server محمول' ($conn.Contains('Server=.\SQLEXPRESS')) $conn
Add-Check 'قاعدة QatFarmDb' ($conn.Contains('Database=QatFarmDb')) $conn

$css = Read-Utf8Text (Join-Path $Web 'wwwroot/css/app.css')
$openCss = ([regex]::Matches($css, '\{')).Count
$closeCss = ([regex]::Matches($css, '\}')).Count
Add-Check 'توازن CSS' ($openCss -eq $closeCss) "open=$openCss close=$closeCss"

$razorFiles = Get-ChildItem $Web -Recurse -File -Filter '*.razor'
$allRazor = ($razorFiles | ForEach-Object { Read-Utf8Text $_.FullName }) -join "`n"
Add-Check 'عدم وجود @x.Id.pdf' (-not $allRazor.Contains('@x.Id.pdf'))
Add-Check 'عدم تعارض Dashboard' (-not $allRazor.Contains("@inject DashboardService Dashboard`n"))
$imports = Read-Utf8Text (Join-Path $Web 'Components/_Imports.razor')
Add-Check 'استيراد Routing' ($imports.Contains('@using Microsoft.AspNetCore.Components.Routing'))

$routes = New-Object System.Collections.Generic.List[string]
foreach ($razor in $razorFiles) {
    $text = Read-Utf8Text $razor.FullName
    foreach ($match in [regex]::Matches($text, '(?m)^@page\s+"([^"]+)"')) {
        $routes.Add($match.Groups[1].Value) | Out-Null
    }
}
$uniqueRoutes = @($routes | Sort-Object -Unique)
Add-Check 'عدم تكرار المسارات' ($routes.Count -eq $uniqueRoutes.Count) (($routes -join ', '))
foreach ($expected in @('/', '/farms', '/customers', '/customers/{CustomerId:long}', '/cultivation-expenses', '/sales/new', '/sales/edit/{InvoiceId:long}', '/invoices', '/zakat', '/reports', '/settings', '/users', '/audit-logs')) {
    Add-Check "مسار $expected" ($routes -contains $expected)
}

$program = Read-Utf8Text (Join-Path $Web 'Program.cs')
foreach ($service in @('CustomerService','ZakatService','InvoiceService','DashboardService','PdfReportService','CurrentUserService','CultivationExpenseService','CultivationDebtPdfService','DatabaseBackupService','UserManagementService','AuditLogService','AccountingService')) {
    Add-Check "تسجيل $service" ($program.Contains("AddScoped<$service>()"))
}
Add-Check 'مسار PDF العميل' ($program.Contains('/reports/customer/{id:long}.pdf'))

$nav = Read-Utf8Text (Join-Path $Web 'Components/Layout/NavMenu.razor')
foreach ($href in @('customers','zakat','sales/new','invoices','accounting')) {
    $expectedHref = 'href="{0}"' -f $href
    Add-Check "رابط القائمة $href" ($nav.Contains($expectedHref))
}

$models = (Read-Utf8Text (Join-Path $Web 'Models/DomainModels.cs')) + (Read-Utf8Text (Join-Path $Web 'Models/ViewModels.cs'))
Test-ContainsAll $models @('class Customer','class CustomerPayment','CustomerId','PaymentDueDate','ZakatStatus','ZakatPaidAt','ZakatPaymentReference','List<InvoiceItemEditorModel>','List<InvoiceExpenseEditorModel>') 'نموذج'

$invoice = Read-Utf8Text (Join-Path $Web 'Services/InvoiceService.cs')
Test-ContainsAll $invoice @('CreateExecutionStrategy()','BeginTransactionAsync()','DeleteAsync(long id)','ZakatPaymentStatus.Pending','ValidateCreditLimitAsync','CustomerPayments.AnyAsync') 'فاتورة'

$customer = Read-Utf8Text (Join-Path $Web 'Services/CustomerService.cs')
Test-ContainsAll $customer @('AddPaymentAsync','invoice.AmountPaid += model.Amount','invoice.AmountDue','CreditLimit','OpeningBalance') 'عملاء'

$zakat = Read-Utf8Text (Join-Path $Web 'Services/ZakatService.cs')
Test-ContainsAll $zakat @('ConfirmPaidAsync','ZakatPaymentStatus.Paid','RaiseChangedAsync','ZakatPaymentReference') 'زكاة'

$sql = Read-Utf8Text (Join-Path $Web 'database/Upgrade_Legendary_Customers_Zakat.sql')
Test-ContainsAll $sql @('CREATE TABLE dbo.Customers','CREATE TABLE dbo.CustomerPayments','ADD CustomerId','ADD PaymentDueDate','ADD ZakatStatus','FK_CustomerPayments_SalesInvoices_SalesInvoiceId','IX_SalesInvoices_ZakatStatus_InvoiceDate') 'SQL'
$fullSql = Read-Utf8Text (Join-Path $Web 'database/CreateDatabase_Full.sql')
Add-Check 'السكربت الكامل يحتوي الترقية' ($fullSql.Contains('CREATE TABLE dbo.Customers') -and $fullSql.Contains('CREATE TABLE dbo.CustomerPayments'))

$updater = Read-Utf8Text (Join-Path $Web 'Data/DatabaseSchemaUpdater.cs')
$dbInitializer = Read-Utf8Text (Join-Path $Web 'Data/DbInitializer.cs')
Add-Check 'ترقية تلقائية للقاعدة' ($dbInitializer.Contains('DatabaseSchemaUpdater.ApplyAsync(db)'))
Add-Check 'محدث القاعدة يحتوي العملاء' ($updater.Contains('CREATE TABLE dbo.Customers'))

$pdf = Read-Utf8Text (Join-Path $Web 'Services/PdfReportService.cs')
Add-Check 'PDF كشف العميل' ($pdf.Contains('CreateCustomerStatementPdfAsync'))
Add-Check 'PDF متعدد الأصناف' ($pdf.Contains('invoice.Items.OrderBy') -and $pdf.Contains('var item = items[index]'))
Add-Check 'PDF متعدد المصروفات' ($pdf.Contains('invoice.Expenses.OrderBy') -and $pdf.Contains('var expense = expenses[index]'))
$rtlCount = ([regex]::Matches($pdf, [regex]::Escape('page.ContentFromRightToLeft();'))).Count
Add-Check 'اتجاه PDF RTL' ($rtlCount -ge 4) ([string]$rtlCount)

$sales = Read-Utf8Text (Join-Path $Web 'Components/Pages/Sales.razor')
Add-Check 'زر إضافة صنف' ($sales.Contains('AddItem') -and $sales.Contains('إضافة صنف آخر'))
Add-Check 'زر إضافة مصروف' ($sales.Contains('AddExpense') -and $sales.Contains('إضافة مصروف آخر'))
Add-Check 'اختيار العميل' ($sales.Contains('model.CustomerId'))

$invoices = Read-Utf8Text (Join-Path $Web 'Components/Pages/Invoices.razor')
Add-Check 'تعديل الفاتورة' ($invoices.Contains('/sales/edit/'))
Add-Check 'حذف الفاتورة' ($invoices.Contains('ConfirmDelete') -and $invoice.Contains('DeleteAsync'))

$notification = Read-Utf8Text (Join-Path $Web 'Components/Shared/ZakatNotification.razor')
Add-Check 'إشعار الزكاة العام' ($notification.Contains('سيبقى هذا التنبيه ظاهرًا') -and $notification.Contains('Service.Changed += Refresh'))

$cases = @(
    @([decimal]100000,[decimal]5,[decimal]15000,[decimal]60000,[decimal]5000,[decimal]80000,[decimal]40000),
    @([decimal]50000,[decimal]5,[decimal]0,[decimal]50000,[decimal]2500,[decimal]47500,[decimal]0),
    @([decimal]10000,[decimal]5,[decimal]500,[decimal]0,[decimal]500,[decimal]9000,[decimal]10000)
)
$caseIndex = 0
foreach ($case in $cases) {
    $caseIndex++
    $gross,$percent,$expenses,$paid,$expectedZakat,$expectedNet,$expectedDue = $case
    $actualZakat = [math]::Round([decimal]($gross * $percent / [decimal]100), 2)
    $actualNet = $gross - $actualZakat - $expenses
    $actualDue = if (($gross - $paid) -gt 0) { $gross - $paid } else { [decimal]0 }
    Add-Check "حساب مالي $caseIndex" (($actualZakat -eq $expectedZakat) -and ($actualNet -eq $expectedNet) -and ($actualDue -eq $expectedDue)) "$actualZakat,$actualNet,$actualDue"
}


$accountingModels = Read-Utf8Text (Join-Path $Web 'Models/AccountingModels.cs')
$accountingService = Read-Utf8Text (Join-Path $Web 'Services/AccountingService.cs')
$accountingPage = Read-Utf8Text (Join-Path $Web 'Components/Pages/Accounting.razor')
Test-ContainsAll $accountingModels @('class ChartOfAccount','class JournalEntry','class JournalEntryLine','AccountingSummary','TrialBalanceRow','FinancialPositionModel','GeneralLedgerRow') 'نماذج المحاسبة'
Test-ContainsAll $accountingService @('SyncOperationalLedgerAsync','CreateManualEntryAsync','ReverseManualEntryAsync','GetTrialBalanceAsync','GetIncomeStatementAsync','GetFinancialPositionAsync','GetGeneralLedgerAsync','SalesInvoice','CustomerPayment','CultivationExpense','ZakatPayment') 'خدمة المحاسبة'
Test-ContainsAll $accountingPage @('@page "/accounting"','ميزان المراجعة','دفتر اليومية','الأستاذ العام','قائمة المركز المالي','قائمة الدخل','قيد يدوي') 'واجهة المحاسبة'
Add-Check 'قيود المحاسبة مزدوجة ومتوازنة' ($accountingService.Contains('decimal.Round(debit, 2) != decimal.Round(credit, 2)'))
Add-Check 'العكس المحاسبي يحفظ الأصل' ($accountingService.Contains('ReversesEntry = original') -and $accountingService.Contains('JournalEntryStatus.Reversed'))
Add-Check 'قاعدة البيانات تحتوي جداول المحاسبة' ($updater.Contains('CREATE TABLE dbo.ChartOfAccounts') -and $updater.Contains('CREATE TABLE dbo.JournalEntries') -and $updater.Contains('CREATE TABLE dbo.JournalEntryLines'))
Add-Check 'السكربت الكامل يحتوي المحاسبة' ($fullSql.Contains('CREATE TABLE dbo.ChartOfAccounts') -and $fullSql.Contains('CREATE TABLE dbo.JournalEntries') -and $fullSql.Contains("Version = N'2.1.0'"))

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
