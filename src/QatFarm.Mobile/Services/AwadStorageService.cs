using System.Text.Json;
using QatFarm.Mobile.Data;
using QatFarm.Mobile.Models;

namespace QatFarm.Mobile.Services;

/// <summary>
/// يدير مجلد «عواد سوفت» الموحد والنسخ الاحتياطية التلقائية.
/// في Windows يكون المجلد مرئياً داخل Documents، وفي Android يبقى داخل مساحة التطبيق الآمنة.
/// </summary>
public sealed class AwadStorageService
{
    private readonly SemaphoreSlim _backupGate = new(1, 1);
    private DateTime _lastBackedUpWriteUtc = DateTime.MinValue;

    public string RootPath { get; }
    public string DataPath { get; }
    public string BackupsPath { get; }
    public string InvoicesPath { get; }
    public string ReportsPath { get; }
    public string CustomersPath { get; }
    public string SyncPath { get; }
    public string DatabasePath { get; }

    public AwadStorageService()
    {
#if WINDOWS
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        RootPath = Path.Combine(documents, "عواد سوفت");
        DataPath = Path.Combine(RootPath, "البيانات");
        DatabasePath = Path.Combine(DataPath, "QatFarm.db3");
#else
        // نحافظ على مسار قاعدة Android السابق حتى لا تضيع بيانات المستخدم عند الترقية.
        RootPath = Path.Combine(FileSystem.AppDataDirectory, "عواد سوفت");
        DataPath = Path.Combine(RootPath, "البيانات");
        DatabasePath = Path.Combine(FileSystem.AppDataDirectory, "QatFarmMobile.db3");
#endif
        BackupsPath = Path.Combine(RootPath, "النسخ الاحتياطية");
        InvoicesPath = Path.Combine(RootPath, "فواتير البيع");
        ReportsPath = Path.Combine(RootPath, "التقارير");
        CustomersPath = Path.Combine(RootPath, "العملاء");
        SyncPath = Path.Combine(RootPath, "المزامنة");
        EnsureStructure();
    }

    public void EnsureStructure()
    {
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(DataPath);
        Directory.CreateDirectory(BackupsPath);
        Directory.CreateDirectory(InvoicesPath);
        Directory.CreateDirectory(ReportsPath);
        Directory.CreateDirectory(CustomersPath);
        Directory.CreateDirectory(SyncPath);

#if WINDOWS
        var readme = Path.Combine(RootPath, "اقرأني.txt");
        if (!File.Exists(readme))
        {
            File.WriteAllText(readme,
                "مجلد عواد سوفت\r\n" +
                "================\r\n" +
                "يتم إنشاء هذا المجلد تلقائياً بواسطة نظام زراعي عواد سوفت.\r\n" +
                "البيانات: قاعدة البيانات الرئيسية.\r\n" +
                "النسخ الاحتياطية: نسخ تلقائية مؤرخة.\r\n" +
                "فواتير البيع: ملفات الفواتير المصدرة.\r\n" +
                "التقارير: ملفات التقارير المصدرة.\r\n" +
                "العملاء: لقطة JSON محدثة من بيانات العملاء.\r\n" +
                "المزامنة: معلومات وحالة الربط بين الكمبيوتر والجوال.\r\n",
                System.Text.Encoding.UTF8);
        }
#endif
    }

    public string GetInvoiceExportPath(string fileName) => Path.Combine(InvoicesPath, SafeFileName(fileName));
    public string GetReportExportPath(string fileName) => Path.Combine(ReportsPath, SafeFileName(fileName));

    public async Task<string?> CreateAutomaticBackupAsync(MobileDb db, string reason = "auto")
    {
        await _backupGate.WaitAsync();
        try
        {
            EnsureStructure();
            await db.CheckpointAsync();
            if (!File.Exists(db.DatabasePath)) return null;

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var safeReason = SafeFileName(string.IsNullOrWhiteSpace(reason) ? "auto" : reason);
            var backupPath = Path.Combine(BackupsPath, $"QatFarm-{stamp}-{safeReason}.db3");
            File.Copy(db.DatabasePath, backupPath, overwrite: true);
            File.Copy(db.DatabasePath, Path.Combine(BackupsPath, "QatFarm-LATEST.db3"), overwrite: true);
            _lastBackedUpWriteUtc = File.GetLastWriteTimeUtc(db.DatabasePath);

            await WriteReadableSnapshotsAsync(db);
            WriteBackupManifest(backupPath, reason);
            PruneOldBackups(90);
            return backupPath;
        }
        finally
        {
            _backupGate.Release();
        }
    }

    public async Task RunAutomaticBackupLoopAsync(MobileDb db, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (File.Exists(db.DatabasePath))
                {
                    var writeUtc = File.GetLastWriteTimeUtc(db.DatabasePath);
                    if (writeUtc > _lastBackedUpWriteUtc)
                        await CreateAutomaticBackupAsync(db, "auto");
                }
                await Task.Delay(TimeSpan.FromMinutes(2), token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                try { await Task.Delay(TimeSpan.FromSeconds(30), token); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task WriteReadableSnapshotsAsync(MobileDb db)
    {
        var conn = await db.GetAsync();
        var json = new JsonSerializerOptions { WriteIndented = true };

        var customers = await conn.Table<Customer>().ToListAsync();
        var farms = await conn.Table<Farm>().ToListAsync();
        var invoices = await conn.Table<SalesInvoice>().ToListAsync();
        var invoiceItems = await conn.Table<SalesInvoiceItem>().ToListAsync();
        var payments = await conn.Table<CustomerPayment>().ToListAsync();
        var cultivation = await conn.Table<CultivationExpense>().ToListAsync();

        await File.WriteAllTextAsync(Path.Combine(CustomersPath, "customers-latest.json"), JsonSerializer.Serialize(customers, json));
        await File.WriteAllTextAsync(Path.Combine(DataPath, "farms-latest.json"), JsonSerializer.Serialize(farms, json));
        await File.WriteAllTextAsync(Path.Combine(DataPath, "sales-invoices-latest.json"), JsonSerializer.Serialize(invoices, json));
        await File.WriteAllTextAsync(Path.Combine(DataPath, "sales-invoice-items-latest.json"), JsonSerializer.Serialize(invoiceItems, json));
        await File.WriteAllTextAsync(Path.Combine(DataPath, "customer-payments-latest.json"), JsonSerializer.Serialize(payments, json));
        await File.WriteAllTextAsync(Path.Combine(DataPath, "cultivation-expenses-latest.json"), JsonSerializer.Serialize(cultivation, json));
    }

    private void WriteBackupManifest(string backupPath, string reason)
    {
        var manifest = new
        {
            Product = "AWAD SOFT QatFarm",
            Version = "2.3.0",
            CreatedAt = DateTimeOffset.Now,
            Reason = reason,
            Database = Path.GetFileName(backupPath),
            RootPath
        };
        File.WriteAllText(Path.Combine(BackupsPath, "latest-backup.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void PruneOldBackups(int keep)
    {
        var files = new DirectoryInfo(BackupsPath)
            .GetFiles("QatFarm-*.db3")
            .Where(x => !x.Name.Equals("QatFarm-LATEST.db3", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.CreationTimeUtc)
            .Skip(keep)
            .ToArray();
        foreach (var file in files)
        {
            try { file.Delete(); } catch { }
        }
    }

    private static string SafeFileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '-');
        return value.Trim();
    }
}
