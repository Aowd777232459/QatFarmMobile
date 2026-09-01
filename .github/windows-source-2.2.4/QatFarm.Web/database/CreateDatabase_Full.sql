/*
    نظام إدارة مزارع وبيع القات
    SQL Server 2022 - قاعدة البيانات الكاملة
    متوافق مع مشروع QatFarm.Web (.NET 10 / EF Core 10 / ASP.NET Core Identity)

    بيانات المدير الافتراضية:
    البريد: abdulmalik.awad@qat.local
    كلمة المرور المؤقتة: Qat@2026#ChangeMe
    يجب تغيير كلمة المرور بعد أول دخول.

    هذا السكربت آمن لإعادة التنفيذ: لا يحذف الجداول أو البيانات الموجودة.
*/

USE [master];
GO

IF DB_ID(N'QatFarmDb') IS NULL
BEGIN
    PRINT N'إنشاء قاعدة البيانات QatFarmDb...';
    CREATE DATABASE [QatFarmDb] COLLATE Arabic_100_CI_AI;
END
ELSE
BEGIN
    PRINT N'قاعدة البيانات QatFarmDb موجودة مسبقًا.';
END
GO

ALTER DATABASE [QatFarmDb] SET RECOVERY FULL;
ALTER DATABASE [QatFarmDb] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
GO

USE [QatFarmDb];
GO

/* =========================================================
   1) جداول ASP.NET Core Identity
   ========================================================= */

IF OBJECT_ID(N'dbo.AspNetRoles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetRoles
    (
        Id               nvarchar(450) NOT NULL,
        Name             nvarchar(256) NULL,
        NormalizedName   nvarchar(256) NULL,
        ConcurrencyStamp nvarchar(max) NULL,
        CONSTRAINT PK_AspNetRoles PRIMARY KEY (Id)
    );
END
GO

IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetUsers
    (
        Id                   nvarchar(450) NOT NULL,
        FullName             nvarchar(150) NOT NULL,
        IsActive             bit NOT NULL CONSTRAINT DF_AspNetUsers_IsActive DEFAULT (1),
        MustChangePassword   bit NOT NULL CONSTRAINT DF_AspNetUsers_MustChangePassword DEFAULT (1),
        CreatedAt            datetime2(7) NOT NULL CONSTRAINT DF_AspNetUsers_CreatedAt DEFAULT (SYSUTCDATETIME()),
        LastLoginAt          datetime2(7) NULL,
        UserName             nvarchar(256) NULL,
        NormalizedUserName   nvarchar(256) NULL,
        Email                nvarchar(256) NULL,
        NormalizedEmail      nvarchar(256) NULL,
        EmailConfirmed       bit NOT NULL CONSTRAINT DF_AspNetUsers_EmailConfirmed DEFAULT (0),
        PasswordHash         nvarchar(max) NULL,
        SecurityStamp        nvarchar(max) NULL,
        ConcurrencyStamp     nvarchar(max) NULL,
        PhoneNumber          nvarchar(max) NULL,
        PhoneNumberConfirmed bit NOT NULL CONSTRAINT DF_AspNetUsers_PhoneConfirmed DEFAULT (0),
        TwoFactorEnabled     bit NOT NULL CONSTRAINT DF_AspNetUsers_TwoFactor DEFAULT (0),
        LockoutEnd           datetimeoffset(7) NULL,
        LockoutEnabled       bit NOT NULL CONSTRAINT DF_AspNetUsers_LockoutEnabled DEFAULT (1),
        AccessFailedCount    int NOT NULL CONSTRAINT DF_AspNetUsers_AccessFailedCount DEFAULT (0),
        CONSTRAINT PK_AspNetUsers PRIMARY KEY (Id)
    );
END
GO

IF OBJECT_ID(N'dbo.AspNetRoleClaims', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetRoleClaims
    (
        Id         int IDENTITY(1,1) NOT NULL,
        RoleId     nvarchar(450) NOT NULL,
        ClaimType  nvarchar(max) NULL,
        ClaimValue nvarchar(max) NULL,
        CONSTRAINT PK_AspNetRoleClaims PRIMARY KEY (Id),
        CONSTRAINT FK_AspNetRoleClaims_AspNetRoles_RoleId
            FOREIGN KEY (RoleId) REFERENCES dbo.AspNetRoles(Id) ON DELETE CASCADE
    );
END
GO

IF OBJECT_ID(N'dbo.AspNetUserClaims', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetUserClaims
    (
        Id         int IDENTITY(1,1) NOT NULL,
        UserId     nvarchar(450) NOT NULL,
        ClaimType  nvarchar(max) NULL,
        ClaimValue nvarchar(max) NULL,
        CONSTRAINT PK_AspNetUserClaims PRIMARY KEY (Id),
        CONSTRAINT FK_AspNetUserClaims_AspNetUsers_UserId
            FOREIGN KEY (UserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE CASCADE
    );
END
GO

IF OBJECT_ID(N'dbo.AspNetUserLogins', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetUserLogins
    (
        LoginProvider       nvarchar(128) NOT NULL,
        ProviderKey         nvarchar(128) NOT NULL,
        ProviderDisplayName nvarchar(max) NULL,
        UserId              nvarchar(450) NOT NULL,
        CONSTRAINT PK_AspNetUserLogins PRIMARY KEY (LoginProvider, ProviderKey),
        CONSTRAINT FK_AspNetUserLogins_AspNetUsers_UserId
            FOREIGN KEY (UserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE CASCADE
    );
END
GO

IF OBJECT_ID(N'dbo.AspNetUserRoles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetUserRoles
    (
        UserId nvarchar(450) NOT NULL,
        RoleId nvarchar(450) NOT NULL,
        CONSTRAINT PK_AspNetUserRoles PRIMARY KEY (UserId, RoleId),
        CONSTRAINT FK_AspNetUserRoles_AspNetUsers_UserId
            FOREIGN KEY (UserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE CASCADE,
        CONSTRAINT FK_AspNetUserRoles_AspNetRoles_RoleId
            FOREIGN KEY (RoleId) REFERENCES dbo.AspNetRoles(Id) ON DELETE CASCADE
    );
END
GO

IF OBJECT_ID(N'dbo.AspNetUserTokens', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetUserTokens
    (
        UserId       nvarchar(450) NOT NULL,
        LoginProvider nvarchar(128) NOT NULL,
        Name          nvarchar(128) NOT NULL,
        Value         nvarchar(max) NULL,
        CONSTRAINT PK_AspNetUserTokens PRIMARY KEY (UserId, LoginProvider, Name),
        CONSTRAINT FK_AspNetUserTokens_AspNetUsers_UserId
            FOREIGN KEY (UserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE CASCADE
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'RoleNameIndex' AND object_id = OBJECT_ID(N'dbo.AspNetRoles'))
    CREATE UNIQUE INDEX RoleNameIndex ON dbo.AspNetRoles(NormalizedName) WHERE NormalizedName IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'EmailIndex' AND object_id = OBJECT_ID(N'dbo.AspNetUsers'))
    CREATE INDEX EmailIndex ON dbo.AspNetUsers(NormalizedEmail);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UserNameIndex' AND object_id = OBJECT_ID(N'dbo.AspNetUsers'))
    CREATE UNIQUE INDEX UserNameIndex ON dbo.AspNetUsers(NormalizedUserName) WHERE NormalizedUserName IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AspNetRoleClaims_RoleId' AND object_id = OBJECT_ID(N'dbo.AspNetRoleClaims'))
    CREATE INDEX IX_AspNetRoleClaims_RoleId ON dbo.AspNetRoleClaims(RoleId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AspNetUserClaims_UserId' AND object_id = OBJECT_ID(N'dbo.AspNetUserClaims'))
    CREATE INDEX IX_AspNetUserClaims_UserId ON dbo.AspNetUserClaims(UserId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AspNetUserLogins_UserId' AND object_id = OBJECT_ID(N'dbo.AspNetUserLogins'))
    CREATE INDEX IX_AspNetUserLogins_UserId ON dbo.AspNetUserLogins(UserId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AspNetUserRoles_RoleId' AND object_id = OBJECT_ID(N'dbo.AspNetUserRoles'))
    CREATE INDEX IX_AspNetUserRoles_RoleId ON dbo.AspNetUserRoles(RoleId);
GO

/* =========================================================
   2) الجداول الأساسية للنظام
   ========================================================= */

IF OBJECT_ID(N'dbo.Farms', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Farms
    (
        Id              bigint IDENTITY(1,1) NOT NULL,
        Name            nvarchar(150) NOT NULL,
        OwnerName       nvarchar(150) NULL,
        Location        nvarchar(250) NULL,
        Phone           nvarchar(30) NULL,
        Notes           nvarchar(1000) NULL,
        IsActive        bit NOT NULL CONSTRAINT DF_Farms_IsActive DEFAULT (1),
        CreatedAt       datetime2(7) NOT NULL CONSTRAINT DF_Farms_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedByUserId nvarchar(450) NULL,
        UpdatedAt       datetime2(7) NULL,
        UpdatedByUserId nvarchar(450) NULL,
        IsDeleted       bit NOT NULL CONSTRAINT DF_Farms_IsDeleted DEFAULT (0),
        DeletedAt       datetime2(7) NULL,
        RowVersion      rowversion NOT NULL,
        CONSTRAINT PK_Farms PRIMARY KEY (Id)
    );
END
GO

IF OBJECT_ID(N'dbo.CultivationExpenseTypes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CultivationExpenseTypes
    (
        Id              bigint IDENTITY(1,1) NOT NULL,
        Name            nvarchar(100) NOT NULL,
        IsActive        bit NOT NULL CONSTRAINT DF_CultivationExpenseTypes_IsActive DEFAULT (1),
        CreatedAt       datetime2(7) NOT NULL CONSTRAINT DF_CultivationExpenseTypes_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedByUserId nvarchar(450) NULL,
        UpdatedAt       datetime2(7) NULL,
        UpdatedByUserId nvarchar(450) NULL,
        IsDeleted       bit NOT NULL CONSTRAINT DF_CultivationExpenseTypes_IsDeleted DEFAULT (0),
        DeletedAt       datetime2(7) NULL,
        RowVersion      rowversion NOT NULL,
        CONSTRAINT PK_CultivationExpenseTypes PRIMARY KEY (Id)
    );
END
GO

IF OBJECT_ID(N'dbo.QatTypes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.QatTypes
    (
        Id              bigint IDENTITY(1,1) NOT NULL,
        Name            nvarchar(100) NOT NULL,
        IsActive        bit NOT NULL CONSTRAINT DF_QatTypes_IsActive DEFAULT (1),
        CreatedAt       datetime2(7) NOT NULL CONSTRAINT DF_QatTypes_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedByUserId nvarchar(450) NULL,
        UpdatedAt       datetime2(7) NULL,
        UpdatedByUserId nvarchar(450) NULL,
        IsDeleted       bit NOT NULL CONSTRAINT DF_QatTypes_IsDeleted DEFAULT (0),
        DeletedAt       datetime2(7) NULL,
        RowVersion      rowversion NOT NULL,
        CONSTRAINT PK_QatTypes PRIMARY KEY (Id)
    );
END
GO

IF OBJECT_ID(N'dbo.DailyExpenseTypes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DailyExpenseTypes
    (
        Id              bigint IDENTITY(1,1) NOT NULL,
        Name            nvarchar(100) NOT NULL,
        IsActive        bit NOT NULL CONSTRAINT DF_DailyExpenseTypes_IsActive DEFAULT (1),
        CreatedAt       datetime2(7) NOT NULL CONSTRAINT DF_DailyExpenseTypes_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedByUserId nvarchar(450) NULL,
        UpdatedAt       datetime2(7) NULL,
        UpdatedByUserId nvarchar(450) NULL,
        IsDeleted       bit NOT NULL CONSTRAINT DF_DailyExpenseTypes_IsDeleted DEFAULT (0),
        DeletedAt       datetime2(7) NULL,
        RowVersion      rowversion NOT NULL,
        CONSTRAINT PK_DailyExpenseTypes PRIMARY KEY (Id)
    );
END
GO

IF OBJECT_ID(N'dbo.CultivationExpenses', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CultivationExpenses
    (
        Id              bigint IDENTITY(1,1) NOT NULL,
        FarmId          bigint NOT NULL,
        ExpenseTypeId   bigint NOT NULL,
        Amount          decimal(18,2) NOT NULL,
        ExpenseDate     datetime2(7) NOT NULL,
        Notes           nvarchar(1000) NULL,
        ReceiptNumber   nvarchar(40) NOT NULL,
        CreatedAt       datetime2(7) NOT NULL CONSTRAINT DF_CultivationExpenses_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedByUserId nvarchar(450) NULL,
        UpdatedAt       datetime2(7) NULL,
        UpdatedByUserId nvarchar(450) NULL,
        IsDeleted       bit NOT NULL CONSTRAINT DF_CultivationExpenses_IsDeleted DEFAULT (0),
        DeletedAt       datetime2(7) NULL,
        RowVersion      rowversion NOT NULL,
        CONSTRAINT PK_CultivationExpenses PRIMARY KEY (Id),
        CONSTRAINT CK_CultivationExpenses_Amount CHECK (Amount > 0),
        CONSTRAINT FK_CultivationExpenses_Farms_FarmId
            FOREIGN KEY (FarmId) REFERENCES dbo.Farms(Id),
        CONSTRAINT FK_CultivationExpenses_CultivationExpenseTypes_ExpenseTypeId
            FOREIGN KEY (ExpenseTypeId) REFERENCES dbo.CultivationExpenseTypes(Id)
    );
END
GO

IF OBJECT_ID(N'dbo.SalesInvoices', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SalesInvoices
    (
        Id              bigint IDENTITY(1,1) NOT NULL,
        InvoiceNumber   nvarchar(40) NOT NULL,
        FarmId          bigint NOT NULL,
        InvoiceDate     datetime2(7) NOT NULL,
        BuyerName       nvarchar(150) NULL,
        BuyerPhone      nvarchar(30) NULL,
        GrossAmount     decimal(18,2) NOT NULL CONSTRAINT DF_SalesInvoices_GrossAmount DEFAULT (0),
        ZakatPercent    decimal(9,4) NOT NULL CONSTRAINT DF_SalesInvoices_ZakatPercent DEFAULT (5),
        ZakatAmount     decimal(18,2) NOT NULL CONSTRAINT DF_SalesInvoices_ZakatAmount DEFAULT (0),
        TotalExpenses   decimal(18,2) NOT NULL CONSTRAINT DF_SalesInvoices_TotalExpenses DEFAULT (0),
        NetAmount       decimal(18,2) NOT NULL CONSTRAINT DF_SalesInvoices_NetAmount DEFAULT (0),
        AmountPaid      decimal(18,2) NOT NULL CONSTRAINT DF_SalesInvoices_AmountPaid DEFAULT (0),
        AmountDue       decimal(18,2) NOT NULL CONSTRAINT DF_SalesInvoices_AmountDue DEFAULT (0),
        PaymentMethod   int NOT NULL CONSTRAINT DF_SalesInvoices_PaymentMethod DEFAULT (0),
        PaymentStatus   int NOT NULL CONSTRAINT DF_SalesInvoices_PaymentStatus DEFAULT (0),
        Status          int NOT NULL CONSTRAINT DF_SalesInvoices_Status DEFAULT (1),
        Notes           nvarchar(1000) NULL,
        CreatedAt       datetime2(7) NOT NULL CONSTRAINT DF_SalesInvoices_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedByUserId nvarchar(450) NULL,
        UpdatedAt       datetime2(7) NULL,
        UpdatedByUserId nvarchar(450) NULL,
        IsDeleted       bit NOT NULL CONSTRAINT DF_SalesInvoices_IsDeleted DEFAULT (0),
        DeletedAt       datetime2(7) NULL,
        RowVersion      rowversion NOT NULL,
        CONSTRAINT PK_SalesInvoices PRIMARY KEY (Id),
        CONSTRAINT CK_SalesInvoices_GrossAmount CHECK (GrossAmount >= 0),
        CONSTRAINT CK_SalesInvoices_ZakatPercent CHECK (ZakatPercent >= 0 AND ZakatPercent <= 100),
        CONSTRAINT CK_SalesInvoices_ZakatAmount CHECK (ZakatAmount >= 0),
        CONSTRAINT CK_SalesInvoices_TotalExpenses CHECK (TotalExpenses >= 0),
        CONSTRAINT CK_SalesInvoices_AmountPaid CHECK (AmountPaid >= 0),
        CONSTRAINT CK_SalesInvoices_AmountDue CHECK (AmountDue >= 0),
        CONSTRAINT CK_SalesInvoices_PaymentMethod CHECK (PaymentMethod BETWEEN 0 AND 3),
        CONSTRAINT CK_SalesInvoices_PaymentStatus CHECK (PaymentStatus BETWEEN 0 AND 2),
        CONSTRAINT CK_SalesInvoices_Status CHECK (Status BETWEEN 0 AND 2),
        CONSTRAINT FK_SalesInvoices_Farms_FarmId
            FOREIGN KEY (FarmId) REFERENCES dbo.Farms(Id)
    );
END
GO

IF OBJECT_ID(N'dbo.SalesInvoiceItems', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SalesInvoiceItems
    (
        Id              bigint IDENTITY(1,1) NOT NULL,
        InvoiceId       bigint NOT NULL,
        QatTypeId       bigint NOT NULL,
        Quantity        int NOT NULL,
        UnitPrice       decimal(18,2) NOT NULL,
        TotalPrice      decimal(18,2) NOT NULL,
        CreatedAt       datetime2(7) NOT NULL CONSTRAINT DF_SalesInvoiceItems_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedByUserId nvarchar(450) NULL,
        UpdatedAt       datetime2(7) NULL,
        UpdatedByUserId nvarchar(450) NULL,
        IsDeleted       bit NOT NULL CONSTRAINT DF_SalesInvoiceItems_IsDeleted DEFAULT (0),
        DeletedAt       datetime2(7) NULL,
        RowVersion      rowversion NOT NULL,
        CONSTRAINT PK_SalesInvoiceItems PRIMARY KEY (Id),
        CONSTRAINT CK_SalesInvoiceItems_Quantity CHECK (Quantity > 0),
        CONSTRAINT CK_SalesInvoiceItems_UnitPrice CHECK (UnitPrice > 0),
        CONSTRAINT CK_SalesInvoiceItems_TotalPrice CHECK (TotalPrice > 0),
        CONSTRAINT FK_SalesInvoiceItems_SalesInvoices_InvoiceId
            FOREIGN KEY (InvoiceId) REFERENCES dbo.SalesInvoices(Id) ON DELETE CASCADE,
        CONSTRAINT FK_SalesInvoiceItems_QatTypes_QatTypeId
            FOREIGN KEY (QatTypeId) REFERENCES dbo.QatTypes(Id)
    );
END
GO

IF OBJECT_ID(N'dbo.InvoiceExpenses', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.InvoiceExpenses
    (
        Id              bigint IDENTITY(1,1) NOT NULL,
        InvoiceId       bigint NOT NULL,
        ExpenseTypeId   bigint NOT NULL,
        Amount          decimal(18,2) NOT NULL,
        Notes           nvarchar(500) NULL,
        CreatedAt       datetime2(7) NOT NULL CONSTRAINT DF_InvoiceExpenses_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedByUserId nvarchar(450) NULL,
        UpdatedAt       datetime2(7) NULL,
        UpdatedByUserId nvarchar(450) NULL,
        IsDeleted       bit NOT NULL CONSTRAINT DF_InvoiceExpenses_IsDeleted DEFAULT (0),
        DeletedAt       datetime2(7) NULL,
        RowVersion      rowversion NOT NULL,
        CONSTRAINT PK_InvoiceExpenses PRIMARY KEY (Id),
        CONSTRAINT CK_InvoiceExpenses_Amount CHECK (Amount > 0),
        CONSTRAINT FK_InvoiceExpenses_SalesInvoices_InvoiceId
            FOREIGN KEY (InvoiceId) REFERENCES dbo.SalesInvoices(Id) ON DELETE CASCADE,
        CONSTRAINT FK_InvoiceExpenses_DailyExpenseTypes_ExpenseTypeId
            FOREIGN KEY (ExpenseTypeId) REFERENCES dbo.DailyExpenseTypes(Id)
    );
END
GO

IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditLogs
    (
        Id         bigint IDENTITY(1,1) NOT NULL,
        UserId     nvarchar(450) NULL,
        Action     nvarchar(150) NOT NULL,
        EntityName nvarchar(150) NOT NULL,
        EntityId   nvarchar(100) NOT NULL,
        OldValues  nvarchar(max) NULL,
        NewValues  nvarchar(max) NULL,
        ActionDate datetime2(7) NOT NULL CONSTRAINT DF_AuditLogs_ActionDate DEFAULT (SYSUTCDATETIME()),
        IpAddress  nvarchar(64) NULL,
        CONSTRAINT PK_AuditLogs PRIMARY KEY (Id)
    );
END
GO

IF OBJECT_ID(N'dbo.SystemSettings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SystemSettings
    (
        [Key]       nvarchar(100) NOT NULL,
        [Value]     nvarchar(1000) NOT NULL,
        [Description] nvarchar(500) NULL,
        CONSTRAINT PK_SystemSettings PRIMARY KEY ([Key])
    );
END
GO

/* =========================================================
   3) الفهارس والقيود الفريدة
   ========================================================= */

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Farms_Name' AND object_id = OBJECT_ID(N'dbo.Farms'))
    CREATE UNIQUE INDEX IX_Farms_Name ON dbo.Farms(Name);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CultivationExpenseTypes_Name' AND object_id = OBJECT_ID(N'dbo.CultivationExpenseTypes'))
    CREATE UNIQUE INDEX IX_CultivationExpenseTypes_Name ON dbo.CultivationExpenseTypes(Name);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_QatTypes_Name' AND object_id = OBJECT_ID(N'dbo.QatTypes'))
    CREATE UNIQUE INDEX IX_QatTypes_Name ON dbo.QatTypes(Name);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DailyExpenseTypes_Name' AND object_id = OBJECT_ID(N'dbo.DailyExpenseTypes'))
    CREATE UNIQUE INDEX IX_DailyExpenseTypes_Name ON dbo.DailyExpenseTypes(Name);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SalesInvoices_InvoiceNumber' AND object_id = OBJECT_ID(N'dbo.SalesInvoices'))
    CREATE UNIQUE INDEX IX_SalesInvoices_InvoiceNumber ON dbo.SalesInvoices(InvoiceNumber);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CultivationExpenses_ReceiptNumber' AND object_id = OBJECT_ID(N'dbo.CultivationExpenses'))
    CREATE UNIQUE INDEX IX_CultivationExpenses_ReceiptNumber ON dbo.CultivationExpenses(ReceiptNumber);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CultivationExpenses_FarmId_ExpenseDate' AND object_id = OBJECT_ID(N'dbo.CultivationExpenses'))
    CREATE INDEX IX_CultivationExpenses_FarmId_ExpenseDate ON dbo.CultivationExpenses(FarmId, ExpenseDate DESC) INCLUDE (Amount, ExpenseTypeId, IsDeleted);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CultivationExpenses_ExpenseTypeId' AND object_id = OBJECT_ID(N'dbo.CultivationExpenses'))
    CREATE INDEX IX_CultivationExpenses_ExpenseTypeId ON dbo.CultivationExpenses(ExpenseTypeId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SalesInvoices_FarmId_InvoiceDate' AND object_id = OBJECT_ID(N'dbo.SalesInvoices'))
    CREATE INDEX IX_SalesInvoices_FarmId_InvoiceDate ON dbo.SalesInvoices(FarmId, InvoiceDate DESC) INCLUDE (GrossAmount, ZakatAmount, TotalExpenses, NetAmount, Status, IsDeleted);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SalesInvoiceItems_InvoiceId' AND object_id = OBJECT_ID(N'dbo.SalesInvoiceItems'))
    CREATE INDEX IX_SalesInvoiceItems_InvoiceId ON dbo.SalesInvoiceItems(InvoiceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SalesInvoiceItems_QatTypeId' AND object_id = OBJECT_ID(N'dbo.SalesInvoiceItems'))
    CREATE INDEX IX_SalesInvoiceItems_QatTypeId ON dbo.SalesInvoiceItems(QatTypeId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_InvoiceExpenses_InvoiceId' AND object_id = OBJECT_ID(N'dbo.InvoiceExpenses'))
    CREATE INDEX IX_InvoiceExpenses_InvoiceId ON dbo.InvoiceExpenses(InvoiceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_InvoiceExpenses_ExpenseTypeId' AND object_id = OBJECT_ID(N'dbo.InvoiceExpenses'))
    CREATE INDEX IX_InvoiceExpenses_ExpenseTypeId ON dbo.InvoiceExpenses(ExpenseTypeId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AuditLogs_ActionDate' AND object_id = OBJECT_ID(N'dbo.AuditLogs'))
    CREATE INDEX IX_AuditLogs_ActionDate ON dbo.AuditLogs(ActionDate DESC);
GO

/* =========================================================
   4) البيانات الأساسية
   ========================================================= */

-- أنواع خسائر التربية
DECLARE @CultivationTypes TABLE (Name nvarchar(100));
INSERT INTO @CultivationTypes(Name) VALUES
(N'السقي'), (N'حساب العمال'), (N'سم حديدي'), (N'سماد'), (N'مبيدات'),
(N'تراب'), (N'نقل'), (N'أدوات زراعية'), (N'صيانة'), (N'مصروف آخر');

INSERT INTO dbo.CultivationExpenseTypes(Name, IsActive, CreatedAt, IsDeleted)
SELECT T.Name, 1, SYSUTCDATETIME(), 0
FROM @CultivationTypes T
WHERE NOT EXISTS (SELECT 1 FROM dbo.CultivationExpenseTypes X WHERE X.Name = T.Name);

-- أنواع القات
DECLARE @QatTypes TABLE (Name nvarchar(100));
INSERT INTO @QatTypes(Name) VALUES
(N'أميال رقم واحد'), (N'أميال مخضر'), (N'بزغة رقم واحد'), (N'بزغة مخضر');

INSERT INTO dbo.QatTypes(Name, IsActive, CreatedAt, IsDeleted)
SELECT T.Name, 1, SYSUTCDATETIME(), 0
FROM @QatTypes T
WHERE NOT EXISTS (SELECT 1 FROM dbo.QatTypes X WHERE X.Name = T.Name);

-- أنواع المصروفات اليومية
DECLARE @DailyTypes TABLE (Name nvarchar(100));
INSERT INTO @DailyTypes(Name) VALUES
(N'صرفة عمال'), (N'حساب عمال'), (N'مبيدات'), (N'تراب'), (N'السقي'),
(N'نقل'), (N'تعبئة'), (N'تحميل'), (N'عمولة'), (N'مصروف آخر');

INSERT INTO dbo.DailyExpenseTypes(Name, IsActive, CreatedAt, IsDeleted)
SELECT T.Name, 1, SYSUTCDATETIME(), 0
FROM @DailyTypes T
WHERE NOT EXISTS (SELECT 1 FROM dbo.DailyExpenseTypes X WHERE X.Name = T.Name);

-- إعدادات النظام
IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE [Key] = N'DefaultZakatPercent')
    INSERT INTO dbo.SystemSettings([Key], [Value], [Description]) VALUES (N'DefaultZakatPercent', N'5', N'نسبة الزكاة الافتراضية');
IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE [Key] = N'Currency')
    INSERT INTO dbo.SystemSettings([Key], [Value], [Description]) VALUES (N'Currency', N'ريال يمني', N'عملة النظام');
IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE [Key] = N'InvoicePrefix')
    INSERT INTO dbo.SystemSettings([Key], [Value], [Description]) VALUES (N'InvoicePrefix', N'INV', N'بادئة رقم الفاتورة');
GO

/* =========================================================
   5) الأدوار وحساب مدير النظام
   ========================================================= */

DECLARE @AdministratorRoleId nvarchar(450) =
    (SELECT TOP (1) Id FROM dbo.AspNetRoles WHERE NormalizedName = N'ADMINISTRATOR');
IF @AdministratorRoleId IS NULL
BEGIN
    SET @AdministratorRoleId = CONVERT(nvarchar(450), NEWID());
    INSERT INTO dbo.AspNetRoles(Id, Name, NormalizedName, ConcurrencyStamp)
    VALUES (@AdministratorRoleId, N'Administrator', N'ADMINISTRATOR', CONVERT(nvarchar(36), NEWID()));
END

DECLARE @AccountantRoleId nvarchar(450) =
    (SELECT TOP (1) Id FROM dbo.AspNetRoles WHERE NormalizedName = N'ACCOUNTANT');
IF @AccountantRoleId IS NULL
BEGIN
    SET @AccountantRoleId = CONVERT(nvarchar(450), NEWID());
    INSERT INTO dbo.AspNetRoles(Id, Name, NormalizedName, ConcurrencyStamp)
    VALUES (@AccountantRoleId, N'Accountant', N'ACCOUNTANT', CONVERT(nvarchar(36), NEWID()));
END

DECLARE @EmployeeRoleId nvarchar(450) =
    (SELECT TOP (1) Id FROM dbo.AspNetRoles WHERE NormalizedName = N'EMPLOYEE');
IF @EmployeeRoleId IS NULL
BEGIN
    SET @EmployeeRoleId = CONVERT(nvarchar(450), NEWID());
    INSERT INTO dbo.AspNetRoles(Id, Name, NormalizedName, ConcurrencyStamp)
    VALUES (@EmployeeRoleId, N'Employee', N'EMPLOYEE', CONVERT(nvarchar(36), NEWID()));
END

DECLARE @AdminEmail nvarchar(256) = N'abdulmalik.awad@qat.local';
DECLARE @AdminNormalizedEmail nvarchar(256) = N'ABDULMALIK.AWAD@QAT.LOCAL';
DECLARE @AdminUserId nvarchar(450) =
    (SELECT TOP (1) Id FROM dbo.AspNetUsers WHERE NormalizedEmail = @AdminNormalizedEmail);

IF @AdminUserId IS NULL
BEGIN
    SET @AdminUserId = CONVERT(nvarchar(450), NEWID());

    INSERT INTO dbo.AspNetUsers
    (
        Id, FullName, IsActive, MustChangePassword, CreatedAt, LastLoginAt,
        UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed,
        PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumber,
        PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount
    )
    VALUES
    (
        @AdminUserId,
        N'عبد الملك عواد',
        1,
        1,
        SYSUTCDATETIME(),
        NULL,
        @AdminEmail,
        @AdminNormalizedEmail,
        @AdminEmail,
        @AdminNormalizedEmail,
        1,
        N'AQAAAAIAAYagAAAAEIdM3cppY9QjmwlBJSQKqExLHIYBK++wJWH9ULkcap71WTpPeRU6FXIls+yjINKbqg==',
        CONVERT(nvarchar(36), NEWID()),
        CONVERT(nvarchar(36), NEWID()),
        NULL,
        0,
        0,
        NULL,
        1,
        0
    );
END

IF NOT EXISTS
(
    SELECT 1 FROM dbo.AspNetUserRoles
    WHERE UserId = @AdminUserId AND RoleId = @AdministratorRoleId
)
BEGIN
    INSERT INTO dbo.AspNetUserRoles(UserId, RoleId)
    VALUES (@AdminUserId, @AdministratorRoleId);
END
GO

/* =========================================================
   6) عروض جاهزة للتقارير
   ========================================================= */

CREATE OR ALTER VIEW dbo.vw_FarmFinancialSummary
AS
SELECT
    F.Id AS FarmId,
    F.Name AS FarmName,
    ISNULL(S.GrossSales, 0) AS GrossSales,
    ISNULL(S.ZakatAmount, 0) AS ZakatAmount,
    ISNULL(S.InvoiceExpenses, 0) AS InvoiceExpenses,
    ISNULL(C.CultivationExpenses, 0) AS CultivationExpenses,
    ISNULL(S.NetSales, 0) AS NetSalesBeforeCultivation,
    ISNULL(S.NetSales, 0) - ISNULL(C.CultivationExpenses, 0) AS FinalNetProfit
FROM dbo.Farms F
OUTER APPLY
(
    SELECT
        SUM(I.GrossAmount) AS GrossSales,
        SUM(I.ZakatAmount) AS ZakatAmount,
        SUM(I.TotalExpenses) AS InvoiceExpenses,
        SUM(I.NetAmount) AS NetSales
    FROM dbo.SalesInvoices I
    WHERE I.FarmId = F.Id
      AND I.IsDeleted = 0
      AND I.Status = 1
) S
OUTER APPLY
(
    SELECT SUM(E.Amount) AS CultivationExpenses
    FROM dbo.CultivationExpenses E
    WHERE E.FarmId = F.Id
      AND E.IsDeleted = 0
) C
WHERE F.IsDeleted = 0;
GO

CREATE OR ALTER VIEW dbo.vw_DailySalesSummary
AS
SELECT
    CAST(I.InvoiceDate AS date) AS SaleDate,
    I.FarmId,
    F.Name AS FarmName,
    COUNT_BIG(*) AS InvoiceCount,
    SUM(I.GrossAmount) AS GrossSales,
    SUM(I.ZakatAmount) AS TotalZakat,
    SUM(I.TotalExpenses) AS TotalExpenses,
    SUM(I.NetAmount) AS NetSales,
    SUM(I.AmountPaid) AS AmountPaid,
    SUM(I.AmountDue) AS AmountDue
FROM dbo.SalesInvoices I
INNER JOIN dbo.Farms F ON F.Id = I.FarmId
WHERE I.IsDeleted = 0
  AND I.Status = 1
  AND F.IsDeleted = 0
GROUP BY CAST(I.InvoiceDate AS date), I.FarmId, F.Name;
GO

PRINT N'تم إنشاء قاعدة البيانات QatFarmDb والجداول والبيانات الأساسية بنجاح.';
PRINT N'البريد: abdulmalik.awad@qat.local';
PRINT N'كلمة المرور المؤقتة: Qat@2026#ChangeMe';
PRINT N'غيّر كلمة المرور بعد أول دخول.';
GO


/* =========================================================
   الإضافات الأسطورية: العملاء والديون وإشعارات الزكاة
   ========================================================= */
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



/* =========================================================
   ترقية المنتج النهائية 2.0.0
   ========================================================= */
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

-- ============================================================
-- QatFarm System 2.1.0 - Professional Double-Entry Accounting
-- ============================================================
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

DECLARE @AccountingAccounts TABLE(Code nvarchar(20), Name nvarchar(150), Category int, Notes nvarchar(500));
INSERT INTO @AccountingAccounts(Code, Name, Category, Notes) VALUES
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
FROM @AccountingAccounts a
WHERE NOT EXISTS (SELECT 1 FROM dbo.ChartOfAccounts c WHERE c.Code = a.Code);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.QatFarmSchemaVersions WHERE Version = N'2.1.0')
    INSERT INTO dbo.QatFarmSchemaVersions(Version, Notes)
    VALUES(N'2.1.0', N'محاسبة مزدوجة: دليل حسابات وقيود يومية وترحيل آلي وعكس وميزان مراجعة وقائمة دخل.');
GO

PRINT N'اكتملت ترقية وحدة المحاسبة QatFarm System 2.1.0 بنجاح.';
GO
