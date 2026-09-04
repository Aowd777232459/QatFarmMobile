/* ============================================================
   QatFarm System 2.1.0 - Professional Double-Entry Accounting
   Idempotent SQL Server upgrade; safe to run more than once.
   Requires the core QatFarm 2.0 schema (Farms, Customers, Creditors).
   ============================================================ */
USE [QatFarmDb];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
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
GO

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
GO

IF COL_LENGTH(N'dbo.JournalEntries', N'Status') IS NULL
    ALTER TABLE dbo.JournalEntries ADD Status int NOT NULL
        CONSTRAINT DF_JournalEntries_Status DEFAULT(0) WITH VALUES;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_JournalEntries_Status'
      AND parent_object_id = OBJECT_ID(N'dbo.JournalEntries')
)
    ALTER TABLE dbo.JournalEntries WITH CHECK
        ADD CONSTRAINT CK_JournalEntries_Status CHECK (Status BETWEEN 0 AND 1);
GO

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
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ChartOfAccounts_Code' AND object_id = OBJECT_ID(N'dbo.ChartOfAccounts'))
    CREATE UNIQUE INDEX IX_ChartOfAccounts_Code ON dbo.ChartOfAccounts(Code);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ChartOfAccounts_Name' AND object_id = OBJECT_ID(N'dbo.ChartOfAccounts'))
    CREATE INDEX IX_ChartOfAccounts_Name ON dbo.ChartOfAccounts(Name);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JournalEntries_EntryNumber' AND object_id = OBJECT_ID(N'dbo.JournalEntries'))
    CREATE UNIQUE INDEX IX_JournalEntries_EntryNumber ON dbo.JournalEntries(EntryNumber);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JournalEntries_Source' AND object_id = OBJECT_ID(N'dbo.JournalEntries'))
    CREATE INDEX IX_JournalEntries_Source ON dbo.JournalEntries(SourceType, SourceId, Status) INCLUDE(EntryDate, IsAutomatic, SourceHash);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JournalEntries_EntryDate' AND object_id = OBJECT_ID(N'dbo.JournalEntries'))
    CREATE INDEX IX_JournalEntries_EntryDate ON dbo.JournalEntries(EntryDate DESC);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JournalEntryLines_AccountEntry' AND object_id = OBJECT_ID(N'dbo.JournalEntryLines'))
    CREATE INDEX IX_JournalEntryLines_AccountEntry ON dbo.JournalEntryLines(AccountId, JournalEntryId) INCLUDE(Debit, Credit, FarmId, CustomerId, CreditorId);
GO

DECLARE @Accounts TABLE(Code nvarchar(20), Name nvarchar(150), Category int, Notes nvarchar(500));
INSERT INTO @Accounts(Code, Name, Category, Notes) VALUES
(N'1101',N'الصندوق',0,N'النقدية المتاحة بالصندوق'),
(N'1102',N'البنك والتحويلات',0,N'الأرصدة البنكية والتحويلات'),
(N'1201',N'حسابات العملاء المدينة',0,N'المبالغ المستحقة على العملاء'),
(N'2101',N'حسابات الدائنين',1,N'المبالغ المستحقة للدائنين'),
(N'2201',N'الزكاة المستحقة',1,N'الزكاة المثبتة ولم يتم سدادها بعد'),
(N'3101',N'حقوق الملكية والأرصدة الافتتاحية',2,NULL),
(N'4101',N'إيرادات بيع القات',3,NULL),
(N'5101',N'مصروفات وخسائر التربية',4,NULL),
(N'5201',N'مصروفات البيع والتشغيل',4,NULL),
(N'5301',N'مصروف الزكاة',4,NULL),
(N'5901',N'مصروفات أخرى',4,NULL);

INSERT INTO dbo.ChartOfAccounts(Code, Name, Category, IsSystem, IsActive, AllowPosting, Notes)
SELECT a.Code, a.Name, a.Category, 1, 1, 1, a.Notes
FROM @Accounts a
WHERE NOT EXISTS (SELECT 1 FROM dbo.ChartOfAccounts c WHERE c.Code = a.Code);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.QatFarmSchemaVersions WHERE Version = N'2.1.0')
    INSERT INTO dbo.QatFarmSchemaVersions(Version, Notes)
    VALUES(N'2.1.0', N'محاسبة مزدوجة: دليل حسابات وقيود يومية وترحيل آلي وعكس وميزان مراجعة وقائمة دخل ومركز مالي وأستاذ عام.');
GO

PRINT N'اكتملت ترقية QatFarm System 2.1.0 المحاسبية بنجاح.';
GO
