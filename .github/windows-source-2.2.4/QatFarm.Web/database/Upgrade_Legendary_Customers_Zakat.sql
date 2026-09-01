/* ============================================================
   ترقية نظام إدارة مزارع وبيع القات
   العملاء والديون + إشعارات الزكاة المستمرة
   SQL Server 2022 - آمن لإعادة التنفيذ
   ============================================================ */
USE [QatFarmDb];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
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

IF COL_LENGTH(N'dbo.SalesInvoices', N'CustomerId') IS NULL
    ALTER TABLE dbo.SalesInvoices ADD CustomerId bigint NULL;
IF COL_LENGTH(N'dbo.SalesInvoices', N'PaymentDueDate') IS NULL
    ALTER TABLE dbo.SalesInvoices ADD PaymentDueDate datetime2(7) NULL;
IF COL_LENGTH(N'dbo.SalesInvoices', N'ZakatStatus') IS NULL
    ALTER TABLE dbo.SalesInvoices ADD ZakatStatus int NOT NULL CONSTRAINT DF_SalesInvoices_ZakatStatus DEFAULT(0);
IF COL_LENGTH(N'dbo.SalesInvoices', N'ZakatPaidAt') IS NULL
    ALTER TABLE dbo.SalesInvoices ADD ZakatPaidAt datetime2(7) NULL;
IF COL_LENGTH(N'dbo.SalesInvoices', N'ZakatPaidByUserId') IS NULL
    ALTER TABLE dbo.SalesInvoices ADD ZakatPaidByUserId nvarchar(450) NULL;
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

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_SalesInvoices_Customers_CustomerId')
    ALTER TABLE dbo.SalesInvoices WITH CHECK ADD CONSTRAINT FK_SalesInvoices_Customers_CustomerId FOREIGN KEY(CustomerId) REFERENCES dbo.Customers(Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CustomerPayments_Customers_CustomerId')
    ALTER TABLE dbo.CustomerPayments WITH CHECK ADD CONSTRAINT FK_CustomerPayments_Customers_CustomerId FOREIGN KEY(CustomerId) REFERENCES dbo.Customers(Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CustomerPayments_SalesInvoices_SalesInvoiceId')
    ALTER TABLE dbo.CustomerPayments WITH CHECK ADD CONSTRAINT FK_CustomerPayments_SalesInvoices_SalesInvoiceId FOREIGN KEY(SalesInvoiceId) REFERENCES dbo.SalesInvoices(Id);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Customers_Name' AND object_id = OBJECT_ID(N'dbo.Customers'))
    CREATE INDEX IX_Customers_Name ON dbo.Customers(Name);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Customers_Phone' AND object_id = OBJECT_ID(N'dbo.Customers'))
    CREATE INDEX IX_Customers_Phone ON dbo.Customers(Phone);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SalesInvoices_CustomerId_PaymentDueDate' AND object_id = OBJECT_ID(N'dbo.SalesInvoices'))
    CREATE INDEX IX_SalesInvoices_CustomerId_PaymentDueDate ON dbo.SalesInvoices(CustomerId, PaymentDueDate) INCLUDE(AmountDue, PaymentStatus, Status, ZakatStatus);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CustomerPayments_CustomerId_PaymentDate' AND object_id = OBJECT_ID(N'dbo.CustomerPayments'))
    CREATE INDEX IX_CustomerPayments_CustomerId_PaymentDate ON dbo.CustomerPayments(CustomerId, PaymentDate DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SalesInvoices_ZakatStatus_InvoiceDate' AND object_id = OBJECT_ID(N'dbo.SalesInvoices'))
    CREATE INDEX IX_SalesInvoices_ZakatStatus_InvoiceDate ON dbo.SalesInvoices(ZakatStatus, InvoiceDate) INCLUDE(ZakatAmount, Status, IsDeleted);
GO

UPDATE dbo.SalesInvoices
SET ZakatStatus = CASE WHEN ZakatAmount > 0 THEN 0 ELSE 2 END
WHERE ZakatStatus NOT IN (0,1,2) OR (ZakatAmount = 0 AND ZakatStatus = 0);
GO

PRINT N'تمت ترقية العملاء والديون وإشعارات الزكاة بنجاح.';
GO
