/*
  ترقيات سلامة النسخة المختبرة.
  نفّذ هذا الملف مرة واحدة إذا كنت استخدمت نسخة أقدم من المشروع.
*/
USE [QatFarmDb];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- استرجاع المزارع التي أخفتها النسخ السابقة، ثم إيقافها بدل حذف تاريخها.
UPDATE dbo.Farms
SET IsDeleted = 0,
    DeletedAt = NULL,
    IsActive = 0,
    UpdatedAt = SYSUTCDATETIME()
WHERE IsDeleted = 1;

-- إصلاح أرقام السندات الفارغة أو المكررة قبل إنشاء الفهرس الفريد.
UPDATE dbo.CultivationExpenses
SET ReceiptNumber = N'EXP-RECOVER-' + CONVERT(nvarchar(20), Id)
WHERE NULLIF(LTRIM(RTRIM(ReceiptNumber)), N'') IS NULL;

;WITH DuplicateReceipts AS
(
    SELECT Id, ReceiptNumber,
           ROW_NUMBER() OVER (PARTITION BY ReceiptNumber ORDER BY Id) AS RowNo
    FROM dbo.CultivationExpenses
)
UPDATE E
SET ReceiptNumber = LEFT(E.ReceiptNumber, 25) + N'-' + CONVERT(nvarchar(14), E.Id)
FROM dbo.CultivationExpenses E
INNER JOIN DuplicateReceipts D ON D.Id = E.Id
WHERE D.RowNo > 1;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_CultivationExpenses_ReceiptNumber'
      AND object_id = OBJECT_ID(N'dbo.CultivationExpenses')
)
    CREATE UNIQUE INDEX IX_CultivationExpenses_ReceiptNumber
    ON dbo.CultivationExpenses(ReceiptNumber);

COMMIT TRANSACTION;
PRINT N'تم تطبيق ترقيات السلامة بنجاح.';
GO
