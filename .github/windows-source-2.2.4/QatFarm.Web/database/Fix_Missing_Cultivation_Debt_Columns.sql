USE QatFarmDb;
GO

SET XACT_ABORT ON;
BEGIN TRANSACTION;

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

IF COL_LENGTH(N'dbo.CultivationExpenses', N'PaymentType') IS NULL
    ALTER TABLE dbo.CultivationExpenses ADD PaymentType int NOT NULL CONSTRAINT DF_CultivationExpenses_PaymentType DEFAULT(0);
GO
IF COL_LENGTH(N'dbo.CultivationExpenses', N'CreditorId') IS NULL
    ALTER TABLE dbo.CultivationExpenses ADD CreditorId bigint NULL;
GO
IF COL_LENGTH(N'dbo.CultivationExpenses', N'PaidAmount') IS NULL
    ALTER TABLE dbo.CultivationExpenses ADD PaidAmount decimal(18,2) NOT NULL CONSTRAINT DF_CultivationExpenses_PaidAmount DEFAULT(0);
GO
IF COL_LENGTH(N'dbo.CultivationExpenses', N'DueDate') IS NULL
    ALTER TABLE dbo.CultivationExpenses ADD DueDate datetime2(7) NULL;
GO
IF COL_LENGTH(N'dbo.CultivationExpenses', N'DebtStatus') IS NULL
    ALTER TABLE dbo.CultivationExpenses ADD DebtStatus int NOT NULL CONSTRAINT DF_CultivationExpenses_DebtStatus DEFAULT(0);
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

COMMIT TRANSACTION;
GO

SELECT N'تم إصلاح أعمدة ديون خسائر التربية بنجاح.' AS Result;
GO
