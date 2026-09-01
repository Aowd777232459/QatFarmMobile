/* ============================================================
   QatFarm System 2.0.0 - ترقية شاملة وآمنة لإعادة التنفيذ
   العملاء، الزكاة، ديون التربية، المستخدمون، وسجل إصدار المخطط
   SQL Server 2022+
   ============================================================ */
USE [QatFarmDb];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH(N'dbo.AspNetUsers', N'FullName') IS NULL
    ALTER TABLE dbo.AspNetUsers ADD FullName nvarchar(150) NOT NULL
        CONSTRAINT DF_AspNetUsers_FullName DEFAULT(N'مستخدم النظام');
GO

IF COL_LENGTH(N'dbo.AspNetUsers', N'IsActive') IS NULL
    ALTER TABLE dbo.AspNetUsers ADD IsActive bit NOT NULL
        CONSTRAINT DF_AspNetUsers_IsActive DEFAULT(1);
GO

IF COL_LENGTH(N'dbo.AspNetUsers', N'MustChangePassword') IS NULL
    ALTER TABLE dbo.AspNetUsers ADD MustChangePassword bit NOT NULL
        CONSTRAINT DF_AspNetUsers_MustChangePassword DEFAULT(1);
GO

IF COL_LENGTH(N'dbo.AspNetUsers', N'CreatedAt') IS NULL
    ALTER TABLE dbo.AspNetUsers ADD CreatedAt datetime2(7) NOT NULL
        CONSTRAINT DF_AspNetUsers_CreatedAt DEFAULT(SYSUTCDATETIME());
GO

IF COL_LENGTH(N'dbo.AspNetUsers', N'LastLoginAt') IS NULL
    ALTER TABLE dbo.AspNetUsers ADD LastLoginAt datetime2(7) NULL;
GO

IF OBJECT_ID(N'dbo.QatFarmSchemaVersions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.QatFarmSchemaVersions
    (
        Version nvarchar(30) NOT NULL CONSTRAINT PK_QatFarmSchemaVersions PRIMARY KEY,
        AppliedAt datetime2(7) NOT NULL CONSTRAINT DF_QatFarmSchemaVersions_AppliedAt DEFAULT(SYSUTCDATETIME()),
        Notes nvarchar(500) NULL
    );
END;
GO

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
GO

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
GO

IF COL_LENGTH(N'dbo.SalesInvoices', N'CustomerId') IS NULL
    ALTER TABLE dbo.SalesInvoices ADD CustomerId bigint NULL;
GO

IF COL_LENGTH(N'dbo.SalesInvoices', N'PaymentDueDate') IS NULL
    ALTER TABLE dbo.SalesInvoices ADD PaymentDueDate datetime2(7) NULL;
GO

IF COL_LENGTH(N'dbo.SalesInvoices', N'ZakatStatus') IS NULL
    ALTER TABLE dbo.SalesInvoices ADD ZakatStatus int NOT NULL
        CONSTRAINT DF_SalesInvoices_ZakatStatus DEFAULT(0);
GO

IF COL_LENGTH(N'dbo.SalesInvoices', N'ZakatPaidAt') IS NULL
    ALTER TABLE dbo.SalesInvoices ADD ZakatPaidAt datetime2(7) NULL;
GO

IF COL_LENGTH(N'dbo.SalesInvoices', N'ZakatPaidByUserId') IS NULL
    ALTER TABLE dbo.SalesInvoices ADD ZakatPaidByUserId nvarchar(450) NULL;
GO

IF COL_LENGTH(N'dbo.SalesInvoices', N'ZakatPaymentReference') IS NULL
    ALTER TABLE dbo.SalesInvoices ADD ZakatPaymentReference nvarchar(100) NULL;
GO

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
GO

IF COL_LENGTH(N'dbo.CultivationExpenses', N'PaymentType') IS NULL
    ALTER TABLE dbo.CultivationExpenses ADD PaymentType int NOT NULL
        CONSTRAINT DF_CultivationExpenses_PaymentType DEFAULT(0);
GO

IF COL_LENGTH(N'dbo.CultivationExpenses', N'CreditorId') IS NULL
    ALTER TABLE dbo.CultivationExpenses ADD CreditorId bigint NULL;
GO

IF COL_LENGTH(N'dbo.CultivationExpenses', N'PaidAmount') IS NULL
    ALTER TABLE dbo.CultivationExpenses ADD PaidAmount decimal(18,2) NOT NULL
        CONSTRAINT DF_CultivationExpenses_PaidAmount DEFAULT(0);
GO

IF COL_LENGTH(N'dbo.CultivationExpenses', N'DueDate') IS NULL
    ALTER TABLE dbo.CultivationExpenses ADD DueDate datetime2(7) NULL;
GO

IF COL_LENGTH(N'dbo.CultivationExpenses', N'DebtStatus') IS NULL
    ALTER TABLE dbo.CultivationExpenses ADD DebtStatus int NOT NULL
        CONSTRAINT DF_CultivationExpenses_DebtStatus DEFAULT(0);
GO

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
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_SalesInvoices_Customers_CustomerId')
    ALTER TABLE dbo.SalesInvoices WITH CHECK
        ADD CONSTRAINT FK_SalesInvoices_Customers_CustomerId
        FOREIGN KEY(CustomerId) REFERENCES dbo.Customers(Id);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CustomerPayments_Customers_CustomerId')
    ALTER TABLE dbo.CustomerPayments WITH CHECK
        ADD CONSTRAINT FK_CustomerPayments_Customers_CustomerId
        FOREIGN KEY(CustomerId) REFERENCES dbo.Customers(Id);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CustomerPayments_SalesInvoices_SalesInvoiceId')
    ALTER TABLE dbo.CustomerPayments WITH CHECK
        ADD CONSTRAINT FK_CustomerPayments_SalesInvoices_SalesInvoiceId
        FOREIGN KEY(SalesInvoiceId) REFERENCES dbo.SalesInvoices(Id);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CultivationExpenses_Creditors_CreditorId')
    ALTER TABLE dbo.CultivationExpenses WITH CHECK
        ADD CONSTRAINT FK_CultivationExpenses_Creditors_CreditorId
        FOREIGN KEY(CreditorId) REFERENCES dbo.Creditors(Id);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CultivationDebtPayments_CultivationExpenses_CultivationExpenseId')
    ALTER TABLE dbo.CultivationDebtPayments WITH CHECK
        ADD CONSTRAINT FK_CultivationDebtPayments_CultivationExpenses_CultivationExpenseId
        FOREIGN KEY(CultivationExpenseId) REFERENCES dbo.CultivationExpenses(Id);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CultivationDebtPayments_Creditors_CreditorId')
    ALTER TABLE dbo.CultivationDebtPayments WITH CHECK
        ADD CONSTRAINT FK_CultivationDebtPayments_Creditors_CreditorId
        FOREIGN KEY(CreditorId) REFERENCES dbo.Creditors(Id);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Customers_Name' AND object_id = OBJECT_ID(N'dbo.Customers'))
    CREATE INDEX IX_Customers_Name ON dbo.Customers(Name);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Customers_Phone' AND object_id = OBJECT_ID(N'dbo.Customers'))
    CREATE INDEX IX_Customers_Phone ON dbo.Customers(Phone);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SalesInvoices_CustomerId_PaymentDueDate' AND object_id = OBJECT_ID(N'dbo.SalesInvoices'))
    CREATE INDEX IX_SalesInvoices_CustomerId_PaymentDueDate
        ON dbo.SalesInvoices(CustomerId, PaymentDueDate)
        INCLUDE(AmountDue, PaymentStatus, Status, ZakatStatus);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CustomerPayments_CustomerId_PaymentDate' AND object_id = OBJECT_ID(N'dbo.CustomerPayments'))
    CREATE INDEX IX_CustomerPayments_CustomerId_PaymentDate
        ON dbo.CustomerPayments(CustomerId, PaymentDate DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SalesInvoices_ZakatStatus_InvoiceDate' AND object_id = OBJECT_ID(N'dbo.SalesInvoices'))
    CREATE INDEX IX_SalesInvoices_ZakatStatus_InvoiceDate
        ON dbo.SalesInvoices(ZakatStatus, InvoiceDate)
        INCLUDE(ZakatAmount, Status, IsDeleted);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Creditors_Name' AND object_id = OBJECT_ID(N'dbo.Creditors'))
    CREATE INDEX IX_Creditors_Name ON dbo.Creditors(Name);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Creditors_Phone' AND object_id = OBJECT_ID(N'dbo.Creditors'))
    CREATE INDEX IX_Creditors_Phone ON dbo.Creditors(Phone);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CultivationExpenses_CreditorId_DueDate_DebtStatus' AND object_id = OBJECT_ID(N'dbo.CultivationExpenses'))
    CREATE INDEX IX_CultivationExpenses_CreditorId_DueDate_DebtStatus
        ON dbo.CultivationExpenses(CreditorId, DueDate, DebtStatus)
        INCLUDE(FarmId, ExpenseTypeId, Amount, PaidAmount, ExpenseDate, IsDeleted);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CultivationDebtPayments_ExpenseDate' AND object_id = OBJECT_ID(N'dbo.CultivationDebtPayments'))
    CREATE INDEX IX_CultivationDebtPayments_ExpenseDate
        ON dbo.CultivationDebtPayments(CultivationExpenseId, PaymentDate DESC)
        INCLUDE(CreditorId, Amount, IsDeleted);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CultivationDebtPayments_CreditorDate' AND object_id = OBJECT_ID(N'dbo.CultivationDebtPayments'))
    CREATE INDEX IX_CultivationDebtPayments_CreditorDate
        ON dbo.CultivationDebtPayments(CreditorId, PaymentDate DESC)
        INCLUDE(CultivationExpenseId, Amount, IsDeleted);
GO

UPDATE dbo.SalesInvoices
SET ZakatStatus = CASE WHEN ZakatAmount > 0 THEN 0 ELSE 2 END
WHERE ZakatStatus NOT IN (0, 1, 2)
   OR (ZakatAmount = 0 AND ZakatStatus = 0);
GO

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
GO

IF NOT EXISTS (SELECT 1 FROM dbo.QatFarmSchemaVersions WHERE Version = N'2.0.0')
    INSERT INTO dbo.QatFarmSchemaVersions(Version, Notes)
    VALUES(N'2.0.0', N'نسخة المنتج: العملاء والزكاة وديون التربية وإدارة المستخدمين والتقارير السنوية.');
GO

PRINT N'اكتملت ترقية QatFarm System 2.0.0 بنجاح.';
GO
