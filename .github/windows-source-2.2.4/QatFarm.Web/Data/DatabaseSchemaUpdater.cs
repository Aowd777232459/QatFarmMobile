using Microsoft.EntityFrameworkCore;

namespace QatFarm.Web.Data;

/// <summary>
/// Applies backward-compatible SQL Server schema upgrades required by the application.
/// Every phase is executed in a separate SQL batch. This is important because SQL Server
/// compiles a batch before running ALTER TABLE statements; referencing a newly added column
/// later in the same batch can otherwise produce "Invalid column name".
/// </summary>
public static class DatabaseSchemaUpdater
{
    public static async Task ApplyAsync(ApplicationDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        // Phase 0: harden Identity columns for databases created by older releases.
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.AspNetUsers', N'FullName') IS NULL
    ALTER TABLE dbo.AspNetUsers ADD FullName nvarchar(150) NOT NULL
        CONSTRAINT DF_AspNetUsers_FullName DEFAULT(N'مستخدم النظام');
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.AspNetUsers', N'IsActive') IS NULL
    ALTER TABLE dbo.AspNetUsers ADD IsActive bit NOT NULL
        CONSTRAINT DF_AspNetUsers_IsActive DEFAULT(1);
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.AspNetUsers', N'MustChangePassword') IS NULL
    ALTER TABLE dbo.AspNetUsers ADD MustChangePassword bit NOT NULL
        CONSTRAINT DF_AspNetUsers_MustChangePassword DEFAULT(1);
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.AspNetUsers', N'CreatedAt') IS NULL
    ALTER TABLE dbo.AspNetUsers ADD CreatedAt datetime2(7) NOT NULL
        CONSTRAINT DF_AspNetUsers_CreatedAt DEFAULT(SYSUTCDATETIME());
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.AspNetUsers', N'LastLoginAt') IS NULL
    ALTER TABLE dbo.AspNetUsers ADD LastLoginAt datetime2(7) NULL;
""");

        // Store the applied product schema level for support and diagnostics.
        await ExecuteAsync(db, """
IF OBJECT_ID(N'dbo.QatFarmSchemaVersions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.QatFarmSchemaVersions
    (
        Version nvarchar(30) NOT NULL CONSTRAINT PK_QatFarmSchemaVersions PRIMARY KEY,
        AppliedAt datetime2(7) NOT NULL CONSTRAINT DF_QatFarmSchemaVersions_AppliedAt DEFAULT(SYSUTCDATETIME()),
        Notes nvarchar(500) NULL
    );
END;
""");

        // Phase 1: create independent master tables.
        await ExecuteAsync(db, """
IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Customers
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Customers PRIMARY KEY,
        Name nvarchar(150) NOT NULL,
        Phone nvarchar(30) NULL,
        Region nvarchar(150) NULL,
        Address nvarchar(300) NULL,
        OpeningBalance decimal(18,2) NOT NULL CONSTRAINT DF_Customers_OpeningBalance DEFAULT(0),
        CreditLimit decimal(18,2) NOT NULL CONSTRAINT DF_Customers_CreditLimit DEFAULT(0),
        Notes nvarchar(1000) NULL,
        IsActive bit NOT NULL CONSTRAINT DF_Customers_IsActive DEFAULT(1),
        CreatedAt datetime2(7) NOT NULL CONSTRAINT DF_Customers_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CreatedByUserId nvarchar(450) NULL,
        UpdatedAt datetime2(7) NULL,
        UpdatedByUserId nvarchar(450) NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_Customers_IsDeleted DEFAULT(0),
        DeletedAt datetime2(7) NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT CK_Customers_OpeningBalance CHECK (OpeningBalance >= 0),
        CONSTRAINT CK_Customers_CreditLimit CHECK (CreditLimit >= 0)
    );
END;
""");

        await ExecuteAsync(db, """
IF OBJECT_ID(N'dbo.Creditors', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Creditors
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Creditors PRIMARY KEY,
        Name nvarchar(150) NOT NULL,
        Phone nvarchar(30) NULL,
        Address nvarchar(250) NULL,
        Notes nvarchar(1000) NULL,
        IsActive bit NOT NULL CONSTRAINT DF_Creditors_IsActive DEFAULT(1),
        CreatedAt datetime2(7) NOT NULL CONSTRAINT DF_Creditors_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CreatedByUserId nvarchar(450) NULL,
        UpdatedAt datetime2(7) NULL,
        UpdatedByUserId nvarchar(450) NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_Creditors_IsDeleted DEFAULT(0),
        DeletedAt datetime2(7) NULL,
        RowVersion rowversion NOT NULL
    );
END;
""");

        // Phase 2: add invoice/customer/zakat columns. Each ALTER is a separate batch so
        // subsequent indexes and updates can safely reference the new columns.
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.SalesInvoices', N'CustomerId') IS NULL
    ALTER TABLE dbo.SalesInvoices ADD CustomerId bigint NULL;
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.SalesInvoices', N'PaymentDueDate') IS NULL
    ALTER TABLE dbo.SalesInvoices ADD PaymentDueDate datetime2(7) NULL;
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.SalesInvoices', N'ZakatStatus') IS NULL
    ALTER TABLE dbo.SalesInvoices ADD ZakatStatus int NOT NULL
        CONSTRAINT DF_SalesInvoices_ZakatStatus DEFAULT(0);
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.SalesInvoices', N'ZakatPaidAt') IS NULL
    ALTER TABLE dbo.SalesInvoices ADD ZakatPaidAt datetime2(7) NULL;
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.SalesInvoices', N'ZakatPaidByUserId') IS NULL
    ALTER TABLE dbo.SalesInvoices ADD ZakatPaidByUserId nvarchar(450) NULL;
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.SalesInvoices', N'ZakatPaymentReference') IS NULL
    ALTER TABLE dbo.SalesInvoices ADD ZakatPaymentReference nvarchar(100) NULL;
""");

        // Phase 3: customer payment table.
        await ExecuteAsync(db, """
IF OBJECT_ID(N'dbo.CustomerPayments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerPayments
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerPayments PRIMARY KEY,
        CustomerId bigint NOT NULL,
        SalesInvoiceId bigint NULL,
        Amount decimal(18,2) NOT NULL,
        PaymentDate datetime2(7) NOT NULL,
        PaymentMethod int NOT NULL CONSTRAINT DF_CustomerPayments_PaymentMethod DEFAULT(0),
        ReferenceNumber nvarchar(100) NULL,
        Notes nvarchar(500) NULL,
        CreatedAt datetime2(7) NOT NULL CONSTRAINT DF_CustomerPayments_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CreatedByUserId nvarchar(450) NULL,
        UpdatedAt datetime2(7) NULL,
        UpdatedByUserId nvarchar(450) NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_CustomerPayments_IsDeleted DEFAULT(0),
        DeletedAt datetime2(7) NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT CK_CustomerPayments_Amount CHECK (Amount > 0),
        CONSTRAINT CK_CustomerPayments_PaymentMethod CHECK (PaymentMethod BETWEEN 0 AND 3)
    );
END;
""");

        // Phase 4: cultivation debt columns. These must be separate batches to avoid
        // SQL Server's compile-before-execute behavior.
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.CultivationExpenses', N'PaymentType') IS NULL
    ALTER TABLE dbo.CultivationExpenses ADD PaymentType int NOT NULL
        CONSTRAINT DF_CultivationExpenses_PaymentType DEFAULT(0);
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.CultivationExpenses', N'CreditorId') IS NULL
    ALTER TABLE dbo.CultivationExpenses ADD CreditorId bigint NULL;
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.CultivationExpenses', N'PaidAmount') IS NULL
    ALTER TABLE dbo.CultivationExpenses ADD PaidAmount decimal(18,2) NOT NULL
        CONSTRAINT DF_CultivationExpenses_PaidAmount DEFAULT(0);
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.CultivationExpenses', N'DueDate') IS NULL
    ALTER TABLE dbo.CultivationExpenses ADD DueDate datetime2(7) NULL;
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.CultivationExpenses', N'DebtStatus') IS NULL
    ALTER TABLE dbo.CultivationExpenses ADD DebtStatus int NOT NULL
        CONSTRAINT DF_CultivationExpenses_DebtStatus DEFAULT(0);
""");

        // Phase 5: debt payment table (depends on the master tables, not yet on FKs).
        await ExecuteAsync(db, """
IF OBJECT_ID(N'dbo.CultivationDebtPayments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CultivationDebtPayments
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CultivationDebtPayments PRIMARY KEY,
        CultivationExpenseId bigint NOT NULL,
        CreditorId bigint NOT NULL,
        Amount decimal(18,2) NOT NULL,
        PaymentDate datetime2(7) NOT NULL,
        PaymentMethod int NOT NULL CONSTRAINT DF_CultivationDebtPayments_PaymentMethod DEFAULT(0),
        ReferenceNumber nvarchar(100) NULL,
        Notes nvarchar(500) NULL,
        CreatedAt datetime2(7) NOT NULL CONSTRAINT DF_CultivationDebtPayments_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CreatedByUserId nvarchar(450) NULL,
        UpdatedAt datetime2(7) NULL,
        UpdatedByUserId nvarchar(450) NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_CultivationDebtPayments_IsDeleted DEFAULT(0),
        DeletedAt datetime2(7) NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT CK_CultivationDebtPayments_Amount CHECK (Amount > 0),
        CONSTRAINT CK_CultivationDebtPayments_PaymentMethod CHECK (PaymentMethod BETWEEN 0 AND 3)
    );
END;
""");

        // Phase 6: foreign keys. Execute every relationship independently so one legacy
        // data problem reports the exact constraint and cannot prevent unrelated relations.
        await ExecuteAsync(db, """
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_SalesInvoices_Customers_CustomerId')
    ALTER TABLE dbo.SalesInvoices WITH CHECK
        ADD CONSTRAINT FK_SalesInvoices_Customers_CustomerId
        FOREIGN KEY(CustomerId) REFERENCES dbo.Customers(Id);
""");
        await ExecuteAsync(db, """
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CustomerPayments_Customers_CustomerId')
    ALTER TABLE dbo.CustomerPayments WITH CHECK
        ADD CONSTRAINT FK_CustomerPayments_Customers_CustomerId
        FOREIGN KEY(CustomerId) REFERENCES dbo.Customers(Id);
""");
        await ExecuteAsync(db, """
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CustomerPayments_SalesInvoices_SalesInvoiceId')
    ALTER TABLE dbo.CustomerPayments WITH CHECK
        ADD CONSTRAINT FK_CustomerPayments_SalesInvoices_SalesInvoiceId
        FOREIGN KEY(SalesInvoiceId) REFERENCES dbo.SalesInvoices(Id);
""");
        await ExecuteAsync(db, """
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CultivationExpenses_Creditors_CreditorId')
    ALTER TABLE dbo.CultivationExpenses WITH CHECK
        ADD CONSTRAINT FK_CultivationExpenses_Creditors_CreditorId
        FOREIGN KEY(CreditorId) REFERENCES dbo.Creditors(Id);
""");
        await ExecuteAsync(db, """
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CultivationDebtPayments_CultivationExpenses_CultivationExpenseId')
    ALTER TABLE dbo.CultivationDebtPayments WITH CHECK
        ADD CONSTRAINT FK_CultivationDebtPayments_CultivationExpenses_CultivationExpenseId
        FOREIGN KEY(CultivationExpenseId) REFERENCES dbo.CultivationExpenses(Id);
""");
        await ExecuteAsync(db, """
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CultivationDebtPayments_Creditors_CreditorId')
    ALTER TABLE dbo.CultivationDebtPayments WITH CHECK
        ADD CONSTRAINT FK_CultivationDebtPayments_Creditors_CreditorId
        FOREIGN KEY(CreditorId) REFERENCES dbo.Creditors(Id);
""");

        // Phase 7: indexes. Execute separately for deterministic, restart-safe upgrades.
        await ExecuteAsync(db, """
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Customers_Name' AND object_id = OBJECT_ID(N'dbo.Customers'))
    CREATE INDEX IX_Customers_Name ON dbo.Customers(Name);
""");
        await ExecuteAsync(db, """
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Customers_Phone' AND object_id = OBJECT_ID(N'dbo.Customers'))
    CREATE INDEX IX_Customers_Phone ON dbo.Customers(Phone);
""");
        await ExecuteAsync(db, """
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SalesInvoices_CustomerId_PaymentDueDate' AND object_id = OBJECT_ID(N'dbo.SalesInvoices'))
    CREATE INDEX IX_SalesInvoices_CustomerId_PaymentDueDate
        ON dbo.SalesInvoices(CustomerId, PaymentDueDate)
        INCLUDE(AmountDue, PaymentStatus, Status, ZakatStatus);
""");
        await ExecuteAsync(db, """
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CustomerPayments_CustomerId_PaymentDate' AND object_id = OBJECT_ID(N'dbo.CustomerPayments'))
    CREATE INDEX IX_CustomerPayments_CustomerId_PaymentDate
        ON dbo.CustomerPayments(CustomerId, PaymentDate DESC);
""");
        await ExecuteAsync(db, """
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SalesInvoices_ZakatStatus_InvoiceDate' AND object_id = OBJECT_ID(N'dbo.SalesInvoices'))
    CREATE INDEX IX_SalesInvoices_ZakatStatus_InvoiceDate
        ON dbo.SalesInvoices(ZakatStatus, InvoiceDate)
        INCLUDE(ZakatAmount, Status, IsDeleted);
""");
        await ExecuteAsync(db, """
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Creditors_Name' AND object_id = OBJECT_ID(N'dbo.Creditors'))
    CREATE INDEX IX_Creditors_Name ON dbo.Creditors(Name);
""");
        await ExecuteAsync(db, """
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Creditors_Phone' AND object_id = OBJECT_ID(N'dbo.Creditors'))
    CREATE INDEX IX_Creditors_Phone ON dbo.Creditors(Phone);
""");
        await ExecuteAsync(db, """
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CultivationExpenses_CreditorId_DueDate_DebtStatus' AND object_id = OBJECT_ID(N'dbo.CultivationExpenses'))
    CREATE INDEX IX_CultivationExpenses_CreditorId_DueDate_DebtStatus
        ON dbo.CultivationExpenses(CreditorId, DueDate, DebtStatus)
        INCLUDE(FarmId, ExpenseTypeId, Amount, PaidAmount, ExpenseDate, IsDeleted);
""");
        await ExecuteAsync(db, """
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CultivationDebtPayments_ExpenseDate' AND object_id = OBJECT_ID(N'dbo.CultivationDebtPayments'))
    CREATE INDEX IX_CultivationDebtPayments_ExpenseDate
        ON dbo.CultivationDebtPayments(CultivationExpenseId, PaymentDate DESC)
        INCLUDE(CreditorId, Amount, IsDeleted);
""");
        await ExecuteAsync(db, """
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CultivationDebtPayments_CreditorDate' AND object_id = OBJECT_ID(N'dbo.CultivationDebtPayments'))
    CREATE INDEX IX_CultivationDebtPayments_CreditorDate
        ON dbo.CultivationDebtPayments(CreditorId, PaymentDate DESC)
        INCLUDE(CultivationExpenseId, Amount, IsDeleted);
""");

        // Phase 8: normalize legacy data after the schema is complete.
        await ExecuteAsync(db, """
UPDATE dbo.SalesInvoices
SET ZakatStatus = CASE WHEN ZakatAmount > 0 THEN 0 ELSE 2 END
WHERE ZakatStatus NOT IN (0, 1, 2)
   OR (ZakatAmount = 0 AND ZakatStatus = 0);
""");

        await ExecuteAsync(db, """
-- Legacy cultivation expenses had no debt information. Mark them as fully paid cash
-- to prevent old rows from appearing as false debts.
UPDATE dbo.CultivationExpenses
SET PaymentType = 0,
    PaidAmount = Amount,
    DebtStatus = 3,
    DueDate = NULL
WHERE CreditorId IS NULL
  AND PaidAmount = 0
  AND DebtStatus = 0
  AND IsDeleted = 0;
""");

        // Phase 9: professional double-entry accounting module.
        await ExecuteAsync(db, """
IF OBJECT_ID(N'dbo.ChartOfAccounts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ChartOfAccounts
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ChartOfAccounts PRIMARY KEY,
        Code nvarchar(20) NOT NULL,
        Name nvarchar(150) NOT NULL,
        Category int NOT NULL,
        ParentId bigint NULL,
        IsSystem bit NOT NULL CONSTRAINT DF_ChartOfAccounts_IsSystem DEFAULT(0),
        IsActive bit NOT NULL CONSTRAINT DF_ChartOfAccounts_IsActive DEFAULT(1),
        AllowPosting bit NOT NULL CONSTRAINT DF_ChartOfAccounts_AllowPosting DEFAULT(1),
        Notes nvarchar(500) NULL,
        CreatedAt datetime2(7) NOT NULL CONSTRAINT DF_ChartOfAccounts_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CreatedByUserId nvarchar(450) NULL,
        UpdatedAt datetime2(7) NULL,
        UpdatedByUserId nvarchar(450) NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_ChartOfAccounts_IsDeleted DEFAULT(0),
        DeletedAt datetime2(7) NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT CK_ChartOfAccounts_Category CHECK (Category BETWEEN 0 AND 4),
        CONSTRAINT FK_ChartOfAccounts_Parent FOREIGN KEY(ParentId) REFERENCES dbo.ChartOfAccounts(Id)
    );
END;
""");
        await ExecuteAsync(db, """
IF OBJECT_ID(N'dbo.JournalEntries', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.JournalEntries
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_JournalEntries PRIMARY KEY,
        EntryNumber nvarchar(50) NOT NULL,
        EntryDate datetime2(7) NOT NULL,
        Description nvarchar(500) NOT NULL,
        SourceType nvarchar(60) NULL,
        SourceId nvarchar(100) NULL,
        SourceHash nvarchar(64) NULL,
        Status int NOT NULL CONSTRAINT DF_JournalEntries_Status DEFAULT(0),
        IsAutomatic bit NOT NULL CONSTRAINT DF_JournalEntries_IsAutomatic DEFAULT(0),
        FarmId bigint NULL,
        ReversesEntryId bigint NULL,
        CreatedAt datetime2(7) NOT NULL CONSTRAINT DF_JournalEntries_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CreatedByUserId nvarchar(450) NULL,
        UpdatedAt datetime2(7) NULL,
        UpdatedByUserId nvarchar(450) NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_JournalEntries_IsDeleted DEFAULT(0),
        DeletedAt datetime2(7) NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT CK_JournalEntries_Status CHECK (Status BETWEEN 0 AND 1),
        CONSTRAINT FK_JournalEntries_Farms_FarmId FOREIGN KEY(FarmId) REFERENCES dbo.Farms(Id),
        CONSTRAINT FK_JournalEntries_Reverses FOREIGN KEY(ReversesEntryId) REFERENCES dbo.JournalEntries(Id)
    );
END;
""");
        // Some 2.x databases already contain JournalEntries but predate the Status
        // column. EnsureCreated does not modify an existing table, so repair that
        // legacy shape before any index or EF query references Status.
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.JournalEntries', N'Status') IS NULL
    ALTER TABLE dbo.JournalEntries ADD Status int NOT NULL
        CONSTRAINT DF_JournalEntries_Status DEFAULT(0) WITH VALUES;
""");
        // 2.2.3: databases upgraded from early 2.x builds can have JournalEntries
        // without columns later used by indexes and EF. Repair every safe legacy
        // column before creating indexes; every ALTER is its own batch so SQL Server
        // compiles against the updated shape.
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.JournalEntries', N'SourceHash') IS NULL
    ALTER TABLE dbo.JournalEntries ADD SourceHash nvarchar(64) NULL;
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.JournalEntries', N'IsAutomatic') IS NULL
    ALTER TABLE dbo.JournalEntries ADD IsAutomatic bit NOT NULL
        CONSTRAINT DF_JournalEntries_IsAutomatic DEFAULT(0) WITH VALUES;
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.JournalEntries', N'FarmId') IS NULL
    ALTER TABLE dbo.JournalEntries ADD FarmId bigint NULL;
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.JournalEntries', N'ReversesEntryId') IS NULL
    ALTER TABLE dbo.JournalEntries ADD ReversesEntryId bigint NULL;
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.JournalEntries', N'CreatedAt') IS NULL
    ALTER TABLE dbo.JournalEntries ADD CreatedAt datetime2(7) NOT NULL
        CONSTRAINT DF_JournalEntries_CreatedAt DEFAULT(SYSUTCDATETIME()) WITH VALUES;
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.JournalEntries', N'CreatedByUserId') IS NULL
    ALTER TABLE dbo.JournalEntries ADD CreatedByUserId nvarchar(450) NULL;
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.JournalEntries', N'UpdatedAt') IS NULL
    ALTER TABLE dbo.JournalEntries ADD UpdatedAt datetime2(7) NULL;
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.JournalEntries', N'UpdatedByUserId') IS NULL
    ALTER TABLE dbo.JournalEntries ADD UpdatedByUserId nvarchar(450) NULL;
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.JournalEntries', N'IsDeleted') IS NULL
    ALTER TABLE dbo.JournalEntries ADD IsDeleted bit NOT NULL
        CONSTRAINT DF_JournalEntries_IsDeleted DEFAULT(0) WITH VALUES;
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.JournalEntries', N'DeletedAt') IS NULL
    ALTER TABLE dbo.JournalEntries ADD DeletedAt datetime2(7) NULL;
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.JournalEntries', N'RowVersion') IS NULL
    ALTER TABLE dbo.JournalEntries ADD RowVersion rowversion NOT NULL;
""");
        await ExecuteAsync(db, """
IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE name = N'CK_JournalEntries_Status'
      AND parent_object_id = OBJECT_ID(N'dbo.JournalEntries')
)
    ALTER TABLE dbo.JournalEntries WITH CHECK
        ADD CONSTRAINT CK_JournalEntries_Status CHECK (Status BETWEEN 0 AND 1);
""");
        await ExecuteAsync(db, """
IF OBJECT_ID(N'dbo.JournalEntryLines', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.JournalEntryLines
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_JournalEntryLines PRIMARY KEY,
        JournalEntryId bigint NOT NULL,
        AccountId bigint NOT NULL,
        Debit decimal(18,2) NOT NULL CONSTRAINT DF_JournalEntryLines_Debit DEFAULT(0),
        Credit decimal(18,2) NOT NULL CONSTRAINT DF_JournalEntryLines_Credit DEFAULT(0),
        Description nvarchar(500) NULL,
        CustomerId bigint NULL,
        CreditorId bigint NULL,
        FarmId bigint NULL,
        CreatedAt datetime2(7) NOT NULL CONSTRAINT DF_JournalEntryLines_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CreatedByUserId nvarchar(450) NULL,
        UpdatedAt datetime2(7) NULL,
        UpdatedByUserId nvarchar(450) NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_JournalEntryLines_IsDeleted DEFAULT(0),
        DeletedAt datetime2(7) NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT CK_JournalEntryLines_DebitCredit CHECK (Debit >= 0 AND Credit >= 0 AND ((Debit > 0 AND Credit = 0) OR (Credit > 0 AND Debit = 0))),
        CONSTRAINT FK_JournalEntryLines_JournalEntries FOREIGN KEY(JournalEntryId) REFERENCES dbo.JournalEntries(Id) ON DELETE CASCADE,
        CONSTRAINT FK_JournalEntryLines_Accounts FOREIGN KEY(AccountId) REFERENCES dbo.ChartOfAccounts(Id),
        CONSTRAINT FK_JournalEntryLines_Customers FOREIGN KEY(CustomerId) REFERENCES dbo.Customers(Id),
        CONSTRAINT FK_JournalEntryLines_Creditors FOREIGN KEY(CreditorId) REFERENCES dbo.Creditors(Id),
        CONSTRAINT FK_JournalEntryLines_Farms FOREIGN KEY(FarmId) REFERENCES dbo.Farms(Id)
    );
END;
""");
        await ExecuteAsync(db, """
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ChartOfAccounts_Code' AND object_id = OBJECT_ID(N'dbo.ChartOfAccounts'))
    CREATE UNIQUE INDEX IX_ChartOfAccounts_Code ON dbo.ChartOfAccounts(Code);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ChartOfAccounts_Name' AND object_id = OBJECT_ID(N'dbo.ChartOfAccounts'))
    CREATE INDEX IX_ChartOfAccounts_Name ON dbo.ChartOfAccounts(Name);
""");
        await ExecuteAsync(db, """
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JournalEntries_EntryNumber' AND object_id = OBJECT_ID(N'dbo.JournalEntries'))
    CREATE UNIQUE INDEX IX_JournalEntries_EntryNumber ON dbo.JournalEntries(EntryNumber);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JournalEntries_Source' AND object_id = OBJECT_ID(N'dbo.JournalEntries'))
    CREATE INDEX IX_JournalEntries_Source ON dbo.JournalEntries(SourceType, SourceId, Status) INCLUDE(EntryDate, IsAutomatic, SourceHash);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JournalEntries_EntryDate' AND object_id = OBJECT_ID(N'dbo.JournalEntries'))
    CREATE INDEX IX_JournalEntries_EntryDate ON dbo.JournalEntries(EntryDate DESC);
""");
        await ExecuteAsync(db, """
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JournalEntryLines_AccountEntry' AND object_id = OBJECT_ID(N'dbo.JournalEntryLines'))
    CREATE INDEX IX_JournalEntryLines_AccountEntry ON dbo.JournalEntryLines(AccountId, JournalEntryId) INCLUDE(Debit, Credit, FarmId, CustomerId, CreditorId);
""");

        // Phase 10: stable cross-device identifiers for local Wi-Fi synchronization.
        await ExecuteAsync(db, """
DECLARE @SyncTables TABLE(Name sysname);
INSERT INTO @SyncTables(Name) VALUES
(N'Farms'),(N'Customers'),(N'Creditors'),(N'CultivationExpenseTypes'),
(N'CultivationExpenses'),(N'CultivationDebtPayments'),(N'QatTypes'),
(N'DailyExpenseTypes'),(N'SalesInvoices'),(N'SalesInvoiceItems'),
(N'InvoiceExpenses'),(N'CustomerPayments');

DECLARE @TableName sysname, @Sql nvarchar(max), @IndexName sysname;
DECLARE SyncCursor CURSOR LOCAL FAST_FORWARD FOR SELECT Name FROM @SyncTables;
OPEN SyncCursor;
FETCH NEXT FROM SyncCursor INTO @TableName;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF COL_LENGTH(N'dbo.' + @TableName, N'SyncKey') IS NULL
    BEGIN
        SET @Sql = N'ALTER TABLE dbo.' + QUOTENAME(@TableName) + N' ADD SyncKey nvarchar(32) NULL;';
        EXEC sp_executesql @Sql;
    END;

    SET @Sql = N'UPDATE dbo.' + QUOTENAME(@TableName) +
               N' SET SyncKey = LOWER(REPLACE(CONVERT(nvarchar(36), NEWID()), N''-'', N'''')) WHERE SyncKey IS NULL OR SyncKey = N'''';';
    EXEC sp_executesql @Sql;

    SET @Sql = N';WITH DuplicateKeys AS (' +
               N' SELECT Id, SyncKey, ROW_NUMBER() OVER (PARTITION BY SyncKey ORDER BY Id) AS rn' +
               N' FROM dbo.' + QUOTENAME(@TableName) +
               N' WHERE SyncKey IS NOT NULL AND SyncKey <> N'''' )' +
               N' UPDATE target SET SyncKey = LOWER(REPLACE(CONVERT(nvarchar(36), NEWID()), N''-'', N''''))' +
               N' FROM dbo.' + QUOTENAME(@TableName) + N' AS target' +
               N' INNER JOIN DuplicateKeys d ON d.Id = target.Id WHERE d.rn > 1;';
    EXEC sp_executesql @Sql;

    SET @IndexName = N'UX_' + @TableName + N'_SyncKey';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = @IndexName AND object_id = OBJECT_ID(N'dbo.' + @TableName))
    BEGIN
        SET @Sql = N'CREATE UNIQUE INDEX ' + QUOTENAME(@IndexName) + N' ON dbo.' + QUOTENAME(@TableName) + N'(SyncKey);';
        EXEC sp_executesql @Sql;
    END;

    FETCH NEXT FROM SyncCursor INTO @TableName;
END;
CLOSE SyncCursor;
DEALLOCATE SyncCursor;
""");

        await ExecuteAsync(db, """
IF NOT EXISTS (SELECT 1 FROM dbo.QatFarmSchemaVersions WHERE Version = N'2.0.0')
    INSERT INTO dbo.QatFarmSchemaVersions(Version, Notes)
    VALUES(N'2.0.0', N'نسخة المنتج: العملاء والزكاة وديون التربية وإدارة المستخدمين والتقارير السنوية.');
IF NOT EXISTS (SELECT 1 FROM dbo.QatFarmSchemaVersions WHERE Version = N'2.1.0')
    INSERT INTO dbo.QatFarmSchemaVersions(Version, Notes)
    VALUES(N'2.1.0', N'محاسبة مزدوجة: دليل حسابات وقيود يومية وترحيل آلي وعكس وتقرير ميزان مراجعة وقائمة دخل.');
IF NOT EXISTS (SELECT 1 FROM dbo.QatFarmSchemaVersions WHERE Version = N'2.2.1')
    INSERT INTO dbo.QatFarmSchemaVersions(Version, Notes)
    VALUES(N'2.2.1', N'مزامنة محلية آمنة عبر Wi-Fi ومعرّفات ثابتة بين الجوال والكمبيوتر.');
IF NOT EXISTS (SELECT 1 FROM dbo.QatFarmSchemaVersions WHERE Version = N'2.2.2')
    INSERT INTO dbo.QatFarmSchemaVersions(Version, Notes)
    VALUES(N'2.2.2', N'إصلاح ترقية قواعد البيانات القديمة وإضافة حالة القيود المحاسبية قبل إنشاء الفهارس.');
IF NOT EXISTS (SELECT 1 FROM dbo.QatFarmSchemaVersions WHERE Version = N'2.2.3')
    INSERT INTO dbo.QatFarmSchemaVersions(Version, Notes)
    VALUES(N'2.2.3', N'إصدار الاستقرار النهائي: إصلاح كامل لجدول القيود القديمة ومعالجة مفاتيح المزامنة المكررة.');
""");
    }

    private static Task ExecuteAsync(ApplicationDbContext db, string sql)
    {
        return db.Database.ExecuteSqlRawAsync(sql);
    }
}
