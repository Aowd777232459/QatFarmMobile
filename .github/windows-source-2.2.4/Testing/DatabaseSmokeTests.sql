/*
  اختبار قاعدة بيانات الإصدار الأسطوري
  لا يحتفظ بأي بيانات تجريبية: كل الإدخالات داخل معاملة ثم ROLLBACK.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
USE [QatFarmDb];
GO

BEGIN TRY
    DECLARE @RequiredTables TABLE (TableName sysname NOT NULL);
    INSERT INTO @RequiredTables(TableName) VALUES
    (N'AspNetRoles'), (N'AspNetUsers'), (N'AspNetUserRoles'),
    (N'Farms'), (N'Customers'), (N'CustomerPayments'),
    (N'CultivationExpenseTypes'), (N'CultivationExpenses'),
    (N'QatTypes'), (N'DailyExpenseTypes'), (N'SalesInvoices'),
    (N'SalesInvoiceItems'), (N'InvoiceExpenses'), (N'AuditLogs'), (N'SystemSettings'),
    (N'ChartOfAccounts'), (N'JournalEntries'), (N'JournalEntryLines');

    IF EXISTS (SELECT 1 FROM @RequiredTables R WHERE OBJECT_ID(N'dbo.' + R.TableName, N'U') IS NULL)
        THROW 51001, N'فشل الاختبار: يوجد جدول أساسي مفقود.', 1;

    IF COL_LENGTH(N'dbo.SalesInvoices', N'CustomerId') IS NULL OR
       COL_LENGTH(N'dbo.SalesInvoices', N'ZakatStatus') IS NULL OR
       COL_LENGTH(N'dbo.SalesInvoices', N'PaymentDueDate') IS NULL
        THROW 51002, N'فشل الاختبار: أعمدة العملاء أو الزكاة غير مكتملة.', 1;

    IF OBJECT_ID(N'dbo.vw_FarmFinancialSummary', N'V') IS NULL OR
       OBJECT_ID(N'dbo.vw_DailySalesSummary', N'V') IS NULL
        THROW 51003, N'فشل الاختبار: عروض التقارير الأساسية غير موجودة.', 1;


    IF NOT EXISTS (SELECT 1 FROM dbo.ChartOfAccounts WHERE Code=N'1101' AND Category=0 AND IsActive=1) OR
       NOT EXISTS (SELECT 1 FROM dbo.ChartOfAccounts WHERE Code=N'4101' AND Category=3 AND IsActive=1) OR
       NOT EXISTS (SELECT 1 FROM dbo.ChartOfAccounts WHERE Code=N'5101' AND Category=4 AND IsActive=1)
        THROW 51012, N'فشل الاختبار: دليل الحسابات الافتراضي غير مكتمل.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.QatFarmSchemaVersions WHERE Version=N'2.1.0')
        THROW 51013, N'فشل الاختبار: ترقية قاعدة البيانات المحاسبية 2.1.0 غير مسجلة.', 1;

    IF NOT EXISTS
    (
        SELECT 1 FROM dbo.AspNetUsers U
        INNER JOIN dbo.AspNetUserRoles UR ON UR.UserId = U.Id
        INNER JOIN dbo.AspNetRoles R ON R.Id = UR.RoleId
        WHERE U.NormalizedEmail = N'ABDULMALIK.AWAD@QAT.LOCAL'
          AND R.NormalizedName = N'ADMINISTRATOR' AND U.IsActive = 1
    )
        THROW 51004, N'فشل الاختبار: حساب المدير غير موجود.', 1;

    BEGIN TRANSACTION;

    DECLARE @Token nvarchar(32) = REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N'');
    DECLARE @FarmId bigint, @CustomerId bigint, @InvoiceId bigint;
    DECLARE @CultivationTypeId bigint, @QatTypeId1 bigint, @QatTypeId2 bigint;
    DECLARE @DailyTypeId1 bigint, @DailyTypeId2 bigint;
    DECLARE @InvoiceNumber nvarchar(40) = N'TEST-INV-' + LEFT(@Token, 20);
    DECLARE @ReceiptNumber nvarchar(40) = N'TEST-EXP-' + LEFT(@Token, 20);

    SELECT TOP (1) @CultivationTypeId = Id FROM dbo.CultivationExpenseTypes WHERE IsDeleted = 0 AND IsActive = 1 ORDER BY Id;
    SELECT TOP (1) @QatTypeId1 = Id FROM dbo.QatTypes WHERE IsDeleted = 0 AND IsActive = 1 ORDER BY Id;
    SELECT TOP (1) @QatTypeId2 = Id FROM dbo.QatTypes WHERE IsDeleted = 0 AND IsActive = 1 AND Id <> @QatTypeId1 ORDER BY Id;
    SELECT TOP (1) @DailyTypeId1 = Id FROM dbo.DailyExpenseTypes WHERE IsDeleted = 0 AND IsActive = 1 ORDER BY Id;
    SELECT TOP (1) @DailyTypeId2 = Id FROM dbo.DailyExpenseTypes WHERE IsDeleted = 0 AND IsActive = 1 AND Id <> @DailyTypeId1 ORDER BY Id;

    INSERT dbo.Farms(Name, OwnerName, Location, IsActive, CreatedAt, IsDeleted)
    VALUES(N'مزرعة اختبار ' + LEFT(@Token,8), N'مالك اختباري', N'موقع اختباري', 1, SYSUTCDATETIME(), 0);
    SET @FarmId = SCOPE_IDENTITY();

    INSERT dbo.Customers(Name, Phone, Region, OpeningBalance, CreditLimit, IsActive, CreatedAt, IsDeleted)
    VALUES(N'عميل اختبار ' + LEFT(@Token,8), N'777000000', N'صنعاء', 5000, 200000, 1, SYSUTCDATETIME(), 0);
    SET @CustomerId = SCOPE_IDENTITY();

    INSERT dbo.CultivationExpenses(FarmId, ExpenseTypeId, Amount, ExpenseDate, Notes, ReceiptNumber, CreatedAt, IsDeleted)
    VALUES(@FarmId, @CultivationTypeId, 20000, GETDATE(), N'اختبار', @ReceiptNumber, SYSUTCDATETIME(), 0);

    INSERT dbo.SalesInvoices
    (InvoiceNumber, FarmId, CustomerId, InvoiceDate, PaymentDueDate, BuyerName,
     GrossAmount, ZakatPercent, ZakatAmount, ZakatStatus, TotalExpenses, NetAmount,
     AmountPaid, AmountDue, PaymentMethod, PaymentStatus, Status, CreatedAt, IsDeleted)
    VALUES
    (@InvoiceNumber, @FarmId, @CustomerId, GETDATE(), DATEADD(DAY,7,GETDATE()), N'عميل اختبار',
     100000, 5, 5000, 0, 15000, 80000,
     60000, 40000, 3, 1, 1, SYSUTCDATETIME(), 0);
    SET @InvoiceId = SCOPE_IDENTITY();

    INSERT dbo.SalesInvoiceItems(InvoiceId,QatTypeId,Quantity,UnitPrice,TotalPrice,CreatedAt,IsDeleted)
    VALUES
    (@InvoiceId,@QatTypeId1,60,1000,60000,SYSUTCDATETIME(),0),
    (@InvoiceId,@QatTypeId2,20,2000,40000,SYSUTCDATETIME(),0);

    INSERT dbo.InvoiceExpenses(InvoiceId,ExpenseTypeId,Amount,Notes,CreatedAt,IsDeleted)
    VALUES
    (@InvoiceId,@DailyTypeId1,8000,N'عمال',SYSUTCDATETIME(),0),
    (@InvoiceId,@DailyTypeId2,7000,N'سقي',SYSUTCDATETIME(),0);

    IF (SELECT COUNT(*) FROM dbo.SalesInvoiceItems WHERE InvoiceId=@InvoiceId) <> 2
        THROW 51005, N'فشل الاختبار: تعدد الأصناف لا يعمل.', 1;
    IF (SELECT COUNT(*) FROM dbo.InvoiceExpenses WHERE InvoiceId=@InvoiceId) <> 2
        THROW 51006, N'فشل الاختبار: تعدد المصروفات لا يعمل.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.SalesInvoices WHERE Id=@InvoiceId AND GrossAmount=100000 AND ZakatAmount=5000 AND TotalExpenses=15000 AND NetAmount=80000 AND ZakatStatus=0)
        THROW 51007, N'فشل الاختبار: الحسابات أو إشعار الزكاة غير صحيح.', 1;

    INSERT dbo.CustomerPayments(CustomerId,SalesInvoiceId,Amount,PaymentDate,PaymentMethod,ReferenceNumber,CreatedAt,IsDeleted)
    VALUES(@CustomerId,@InvoiceId,10000,GETDATE(),0,N'TEST-PAY',SYSUTCDATETIME(),0);
    UPDATE dbo.SalesInvoices SET AmountPaid=70000,AmountDue=30000,PaymentStatus=1 WHERE Id=@InvoiceId;

    IF NOT EXISTS (SELECT 1 FROM dbo.CustomerPayments WHERE CustomerId=@CustomerId AND SalesInvoiceId=@InvoiceId AND Amount=10000)
        THROW 51008, N'فشل الاختبار: سند قبض العميل غير موجود.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.SalesInvoices WHERE Id=@InvoiceId AND AmountPaid=70000 AND AmountDue=30000)
        THROW 51009, N'فشل الاختبار: تحديث الدين بعد السداد غير صحيح.', 1;

    UPDATE dbo.SalesInvoices SET ZakatStatus=1,ZakatPaidAt=SYSUTCDATETIME(),ZakatPaymentReference=N'TEST-ZAKAT' WHERE Id=@InvoiceId;
    IF NOT EXISTS (SELECT 1 FROM dbo.SalesInvoices WHERE Id=@InvoiceId AND ZakatStatus=1 AND ZakatPaidAt IS NOT NULL)
        THROW 51010, N'فشل الاختبار: تأكيد دفع الزكاة غير صحيح.', 1;

    DECLARE @FinalProfit decimal(18,2);
    SELECT @FinalProfit=FinalNetProfit FROM dbo.vw_FarmFinancialSummary WHERE FarmId=@FarmId;
    IF @FinalProfit<>60000 THROW 51011, N'فشل الاختبار: الربح النهائي المتوقع 60000.', 1;


    -- اختبار البنية المحاسبية: قيد يدوي متوازن داخل المعاملة التجريبية.
    DECLARE @CashAccountId bigint, @EquityAccountId bigint, @JournalId bigint;
    SELECT @CashAccountId=Id FROM dbo.ChartOfAccounts WHERE Code=N'1101';
    SELECT @EquityAccountId=Id FROM dbo.ChartOfAccounts WHERE Code=N'3101';

    INSERT dbo.JournalEntries
    (EntryNumber,EntryDate,Description,SourceType,SourceId,Status,IsAutomatic,FarmId,CreatedAt,IsDeleted)
    VALUES(N'TEST-JV-' + LEFT(@Token,20),GETDATE(),N'اختبار قيد محاسبي متوازن',N'Manual',N'DB-SMOKE',0,0,@FarmId,SYSUTCDATETIME(),0);
    SET @JournalId=SCOPE_IDENTITY();

    INSERT dbo.JournalEntryLines
    (JournalEntryId,AccountId,Debit,Credit,Description,FarmId,CreatedAt,IsDeleted)
    VALUES
    (@JournalId,@CashAccountId,2500,0,N'اختبار مدين',@FarmId,SYSUTCDATETIME(),0),
    (@JournalId,@EquityAccountId,0,2500,N'اختبار دائن',@FarmId,SYSUTCDATETIME(),0);

    IF (SELECT ISNULL(SUM(Debit-Credit),0) FROM dbo.JournalEntryLines WHERE JournalEntryId=@JournalId)<>0
        THROW 51014, N'فشل الاختبار: القيد المحاسبي التجريبي غير متوازن.', 1;
    IF (SELECT COUNT(*) FROM dbo.JournalEntryLines WHERE JournalEntryId=@JournalId)<>2
        THROW 51015, N'فشل الاختبار: سطور القيد المحاسبي لم تحفظ كما يجب.', 1;

    ROLLBACK TRANSACTION;
    PRINT N'نجح اختبار قاعدة البيانات: العملاء والديون وتعدد الأصناف والمصروفات والزكاة والمحاسبة المزدوجة.';
    PRINT N'تم إلغاء جميع بيانات الاختبار.';
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    PRINT N'فشل اختبار قاعدة البيانات: ' + ERROR_MESSAGE();
    THROW;
END CATCH;
GO
