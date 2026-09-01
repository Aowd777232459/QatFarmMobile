using Microsoft.Maui.Storage;
using QatFarm.Mobile.Models;
using QatFarm.Mobile.Services;
using SQLite;

namespace QatFarm.Mobile.Data;

public sealed class MobileDb
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SQLiteAsyncConnection? _db;
    private bool _initialized;

    public string DatabasePath => Path.Combine(FileSystem.AppDataDirectory, "QatFarmMobile.db3");

    public async Task<SQLiteAsyncConnection> GetAsync()
    {
        _db ??= new SQLiteAsyncConnection(DatabasePath,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
        if (!_initialized) await InitializeAsync();
        return _db;
    }

    public async Task InitializeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_initialized) return;
            _db ??= new SQLiteAsyncConnection(DatabasePath,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
            await _db.EnableWriteAheadLoggingAsync();
            await UpgradeExistingTablesForSyncAsync(_db);
            await _db.CreateTablesAsync(CreateFlags.None,
                typeof(AppUser), typeof(Farm), typeof(Customer), typeof(Creditor),
                typeof(CultivationExpenseType), typeof(CultivationExpense),
                typeof(CultivationDebtPayment), typeof(QatType), typeof(DailyExpenseType),
                typeof(SalesInvoice), typeof(SalesInvoiceItem), typeof(InvoiceExpense),
                typeof(CustomerPayment), typeof(AuditLog), typeof(SystemSetting));
            await SeedAsync(_db);
            _initialized = true;
        }
        finally { _gate.Release(); }
    }

    // sqlite-net ينشئ الجداول الجديدة لكنه لا يضيف أعمدة إلى الجداول القديمة.
    // لذلك تتم هذه الترقية قبل CreateTablesAsync حتى تعمل النسخة الجديدة فوق بيانات المستخدم الحالية.
    private static async Task UpgradeExistingTablesForSyncAsync(SQLiteAsyncConnection db)
    {
        string[] tables =
        [
            "AppUsers", "Farms", "Customers", "Creditors", "CultivationExpenseTypes",
            "CultivationExpenses", "CultivationDebtPayments", "QatTypes", "DailyExpenseTypes",
            "SalesInvoices", "SalesInvoiceItems", "InvoiceExpenses", "CustomerPayments"
        ];

        foreach (var table in tables)
        {
            var exists = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = ?;", table);
            if (exists == 0) continue;

            var columns = await db.QueryAsync<SqliteColumnInfo>($"PRAGMA table_info(\"{table}\");");
            if (columns.All(x => !string.Equals(x.Name, "SyncKey", StringComparison.OrdinalIgnoreCase)))
                await db.ExecuteAsync($"ALTER TABLE \"{table}\" ADD COLUMN SyncKey TEXT;");

            await db.ExecuteAsync(
                $"UPDATE \"{table}\" SET SyncKey = lower(hex(randomblob(16))) WHERE SyncKey IS NULL OR length(SyncKey) <> 32;");
            await db.ExecuteAsync(
                $"CREATE UNIQUE INDEX IF NOT EXISTS \"UX_{table}_SyncKey\" ON \"{table}\"(SyncKey);");
        }

        // ترقية حسابات المستخدمين إلى رمز دخول من 6 أحرف وصلاحيات فواتير مستقلة.
        var usersExist = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'AppUsers';");
        if (usersExist > 0)
        {
            var userColumns = await db.QueryAsync<SqliteColumnInfo>("PRAGMA table_info(\"AppUsers\");");
            if (userColumns.All(x => !string.Equals(x.Name, "AccessCodeHash", StringComparison.OrdinalIgnoreCase)))
                await db.ExecuteAsync("ALTER TABLE \"AppUsers\" ADD COLUMN AccessCodeHash TEXT NOT NULL DEFAULT '';");
            if (userColumns.All(x => !string.Equals(x.Name, "AccessCodeSalt", StringComparison.OrdinalIgnoreCase)))
                await db.ExecuteAsync("ALTER TABLE \"AppUsers\" ADD COLUMN AccessCodeSalt TEXT NOT NULL DEFAULT '';");
            if (userColumns.All(x => !string.Equals(x.Name, "CanEditInvoices", StringComparison.OrdinalIgnoreCase)))
                await db.ExecuteAsync("ALTER TABLE \"AppUsers\" ADD COLUMN CanEditInvoices INTEGER NOT NULL DEFAULT 0;");
            if (userColumns.All(x => !string.Equals(x.Name, "CanDeleteInvoices", StringComparison.OrdinalIgnoreCase)))
                await db.ExecuteAsync("ALTER TABLE \"AppUsers\" ADD COLUMN CanDeleteInvoices INTEGER NOT NULL DEFAULT 0;");
            await db.ExecuteAsync("UPDATE \"AppUsers\" SET CanEditInvoices = 1, CanDeleteInvoices = 1 WHERE Role = 0;");
        }

        // بيانات تأكيد وصول الزكاة.
        var invoicesExist = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'SalesInvoices';");
        if (invoicesExist > 0)
        {
            var invoiceColumns = await db.QueryAsync<SqliteColumnInfo>("PRAGMA table_info(\"SalesInvoices\");");
            if (invoiceColumns.All(x => !string.Equals(x.Name, "ZakatRecipientName", StringComparison.OrdinalIgnoreCase)))
                await db.ExecuteAsync("ALTER TABLE \"SalesInvoices\" ADD COLUMN ZakatRecipientName TEXT;");
        }

        // ترقية بيانات العملاء الخاصة بالحد الائتماني والتنبيه النصي.
        // يتم ضبط 100,000 ريال كحد افتراضي مرة واحدة فقط للعملاء السابقين
        // عند إضافة أعمدة التنبيه لأول مرة، ثم يظل للمدير حرية تغييره حتى إلى صفر.
        var customersExist = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Customers';");
        if (customersExist > 0)
        {
            var customerColumns = await db.QueryAsync<SqliteColumnInfo>("PRAGMA table_info(\"Customers\");");
            var firstCreditAlertUpgrade = customerColumns.All(x =>
                !string.Equals(x.Name, "DebtAlertEnabled", StringComparison.OrdinalIgnoreCase));

            if (customerColumns.All(x => !string.Equals(x.Name, "SellerPhone", StringComparison.OrdinalIgnoreCase)))
                await db.ExecuteAsync("ALTER TABLE \"Customers\" ADD COLUMN SellerPhone TEXT;");
            if (firstCreditAlertUpgrade)
                await db.ExecuteAsync("ALTER TABLE \"Customers\" ADD COLUMN DebtAlertEnabled INTEGER NOT NULL DEFAULT 1;");
            if (customerColumns.All(x => !string.Equals(x.Name, "LastDebtAlertBalance", StringComparison.OrdinalIgnoreCase)))
                await db.ExecuteAsync("ALTER TABLE \"Customers\" ADD COLUMN LastDebtAlertBalance NUMERIC NOT NULL DEFAULT 0;");
            if (customerColumns.All(x => !string.Equals(x.Name, "LastDebtAlertLimit", StringComparison.OrdinalIgnoreCase)))
                await db.ExecuteAsync("ALTER TABLE \"Customers\" ADD COLUMN LastDebtAlertLimit NUMERIC NOT NULL DEFAULT 0;");
            if (customerColumns.All(x => !string.Equals(x.Name, "LastDebtAlertAt", StringComparison.OrdinalIgnoreCase)))
                await db.ExecuteAsync("ALTER TABLE \"Customers\" ADD COLUMN LastDebtAlertAt DATETIME NULL;");

            if (firstCreditAlertUpgrade)
                await db.ExecuteAsync("UPDATE \"Customers\" SET CreditLimit = 100000 WHERE CreditLimit IS NULL OR CreditLimit = 0;");
        }
    }

    private sealed class SqliteColumnInfo
    {
        [Column("name")]
        public string Name { get; set; } = string.Empty;
    }

    private static async Task SeedAsync(SQLiteAsyncConnection db)
    {
        if (await db.Table<CultivationExpenseType>().CountAsync() == 0)
        {
            foreach (var name in new[] {"السقي","أجور العمال","السم الحديدي","السماد","المبيدات","النقل","الصيانة","أخرى"})
                await db.InsertAsync(new CultivationExpenseType { Name = name });
        }

        if (await db.Table<QatType>().CountAsync() == 0)
        {
            foreach (var name in new[] {"أميال رقم واحد","أميال مخضر","بزغة مخضر","قات عادي","نوع آخر"})
                await db.InsertAsync(new QatType { Name = name });
        }

        if (await db.Table<DailyExpenseType>().CountAsync() == 0)
        {
            foreach (var name in new[] {"عمال","نقل","سقي","تغليف","عمولة","أخرى"})
                await db.InsertAsync(new DailyExpenseType { Name = name });
        }
    }

    public async Task CheckpointAsync()
    {
        var db = await GetAsync();
        await db.ExecuteAsync("PRAGMA wal_checkpoint(FULL);");
    }

    public async Task ReplaceDatabaseAsync(Stream source)
    {
        await _gate.WaitAsync();
        try
        {
            if (_db is not null) { await _db.CloseAsync(); _db = null; }
            var temp = DatabasePath + ".import";
            await using (var output = File.Create(temp)) await source.CopyToAsync(output);
            if (File.Exists(DatabasePath)) File.Copy(DatabasePath, DatabasePath + ".before-import", true);
            File.Copy(temp, DatabasePath, true);
            File.Delete(temp);
            _initialized = false;
        }
        finally { _gate.Release(); }
        await InitializeAsync();
    }
}
