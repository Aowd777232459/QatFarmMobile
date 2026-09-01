using System.Security.Cryptography;
using System.Text.Json;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using QatFarm.Web.Data;
using QatFarm.Web.Models;

namespace QatFarm.Web.Services;

public sealed class LocalSyncService(IDbContextFactory<ApplicationDbContext> factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] ImportOrder =
    [
        "Farm", "Customer", "Creditor", "CultivationExpenseType", "QatType", "DailyExpenseType",
        "CultivationExpense", "SalesInvoice", "CultivationDebtPayment", "SalesInvoiceItem",
        "InvoiceExpense", "CustomerPayment"
    ];

    public async Task<string> GetOrCreatePairingKeyAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var setting = await db.SystemSettings.FirstOrDefaultAsync(x => x.Key == "LocalSyncPairingKey");
        if (setting is not null && !string.IsNullOrWhiteSpace(setting.Value)) return setting.Value;

        var bytes = RandomNumberGenerator.GetBytes(6);
        var key = $"AWAD-{Convert.ToHexString(bytes)}";
        db.SystemSettings.Add(new SystemSetting
        {
            Key = "LocalSyncPairingKey",
            Value = key,
            Description = "رمز ربط مزامنة Wi-Fi المحلية"
        });
        await db.SaveChangesAsync();
        return key;
    }

    public static IReadOnlyList<string> GetLocalSyncAddresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(x => x.GetIPProperties().UnicastAddresses)
            .Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x.Address))
            .Select(x => $"http://{x.Address}:5276")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<bool> IsPairingKeyValidAsync(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        var expected = await GetOrCreatePairingKeyAsync();
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(expected),
            System.Text.Encoding.UTF8.GetBytes(candidate.Trim()));
    }

    public async Task<LocalSyncResult> SynchronizeAsync(LocalSyncBatch batch)
    {
        if (string.IsNullOrWhiteSpace(batch.DeviceId))
            throw new InvalidOperationException("معرّف جهاز الجوال مفقود.");
        if (batch.Records.Count > 100_000)
            throw new InvalidOperationException("حزمة المزامنة أكبر من الحد المسموح.");

        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var received = 0;

        foreach (var entity in ImportOrder)
        {
            foreach (var record in batch.Records.Where(x => x.Entity == entity && IsValidKey(x.Key)))
            {
                await ApplyAsync(db, record);
                received++;
            }
            await db.SaveChangesAsync();
        }

        db.AuditLogs.Add(new AuditLog
        {
            Action = "LocalWiFiSync",
            EntityName = "SyncBatch",
            EntityId = batch.DeviceId,
            NewValues = $"Received={received}",
            ActionDate = DateTime.UtcNow,
            IpAddress = "LocalWiFi"
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        var records = await ExportAllAsync();
        return new LocalSyncResult
        {
            Success = true,
            Message = "تمت مزامنة بيانات الجوال والكمبيوتر داخل شبكة Wi-Fi.",
            ServerTime = DateTimeOffset.UtcNow,
            Received = received,
            Records = records
        };
    }

    public async Task<List<LocalSyncRecord>> ExportAllAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var result = new List<LocalSyncRecord>();

        var farms = await db.Farms.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        var customers = await db.Customers.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        var creditors = await db.Creditors.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        var cultivationTypes = await db.CultivationExpenseTypes.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        var qatTypes = await db.QatTypes.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        var dailyTypes = await db.DailyExpenseTypes.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        var cultivation = await db.CultivationExpenses.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        var invoices = await db.SalesInvoices.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        var debtPayments = await db.CultivationDebtPayments.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        var invoiceItems = await db.SalesInvoiceItems.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        var invoiceExpenses = await db.InvoiceExpenses.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        var customerPayments = await db.CustomerPayments.IgnoreQueryFilters().AsNoTracking().ToListAsync();

        var farmKeys = farms.ToDictionary(x => x.Id, x => x.SyncKey);
        var customerKeys = customers.ToDictionary(x => x.Id, x => x.SyncKey);
        var creditorKeys = creditors.ToDictionary(x => x.Id, x => x.SyncKey);
        var cultivationTypeKeys = cultivationTypes.ToDictionary(x => x.Id, x => x.SyncKey);
        var qatTypeKeys = qatTypes.ToDictionary(x => x.Id, x => x.SyncKey);
        var dailyTypeKeys = dailyTypes.ToDictionary(x => x.Id, x => x.SyncKey);
        var cultivationKeys = cultivation.ToDictionary(x => x.Id, x => x.SyncKey);
        var invoiceKeys = invoices.ToDictionary(x => x.Id, x => x.SyncKey);

        result.AddRange(farms.Select(x => Record("Farm", x, new { x.Name, x.OwnerName, x.Location, x.Phone, x.Notes, x.IsActive })));
        result.AddRange(customers.Select(x => Record("Customer", x, new { x.Name, x.Phone, x.Region, x.Address, x.OpeningBalance, x.CreditLimit, x.Notes, x.IsActive })));
        result.AddRange(creditors.Select(x => Record("Creditor", x, new { x.Name, x.Phone, x.Address, x.Notes, x.IsActive })));
        result.AddRange(cultivationTypes.Select(x => Record("CultivationExpenseType", x, new { x.Name, x.IsActive })));
        result.AddRange(qatTypes.Select(x => Record("QatType", x, new { x.Name, x.IsActive })));
        result.AddRange(dailyTypes.Select(x => Record("DailyExpenseType", x, new { x.Name, x.IsActive })));
        result.AddRange(cultivation.Select(x => Record("CultivationExpense", x, new
        {
            FarmKey = farmKeys.GetValueOrDefault(x.FarmId),
            ExpenseTypeKey = cultivationTypeKeys.GetValueOrDefault(x.ExpenseTypeId),
            x.Amount, x.ExpenseDate, x.PaymentType,
            CreditorKey = x.CreditorId.HasValue ? creditorKeys.GetValueOrDefault(x.CreditorId.Value) : null,
            x.PaidAmount, x.DueDate, x.DebtStatus, x.Notes, x.ReceiptNumber
        })));
        result.AddRange(invoices.Select(x => Record("SalesInvoice", x, new
        {
            x.InvoiceNumber,
            FarmKey = farmKeys.GetValueOrDefault(x.FarmId),
            CustomerKey = x.CustomerId.HasValue ? customerKeys.GetValueOrDefault(x.CustomerId.Value) : null,
            x.InvoiceDate, x.PaymentDueDate, x.BuyerName, x.BuyerPhone, x.GrossAmount,
            x.ZakatPercent, x.ZakatAmount, x.ZakatStatus, x.ZakatPaidAt, x.ZakatPaymentReference,
            x.TotalExpenses, x.NetAmount, x.AmountPaid, x.AmountDue, x.PaymentMethod, x.PaymentStatus,
            x.Status, x.Notes
        })));
        result.AddRange(debtPayments.Select(x => Record("CultivationDebtPayment", x, new
        {
            CultivationExpenseKey = cultivationKeys.GetValueOrDefault(x.CultivationExpenseId),
            CreditorKey = creditorKeys.GetValueOrDefault(x.CreditorId),
            x.Amount, x.PaymentDate, x.PaymentMethod, x.ReferenceNumber, x.Notes
        })));
        result.AddRange(invoiceItems.Select(x => Record("SalesInvoiceItem", x, new
        {
            InvoiceKey = invoiceKeys.GetValueOrDefault(x.InvoiceId),
            QatTypeKey = qatTypeKeys.GetValueOrDefault(x.QatTypeId),
            x.Quantity, x.UnitPrice, x.TotalPrice
        })));
        result.AddRange(invoiceExpenses.Select(x => Record("InvoiceExpense", x, new
        {
            InvoiceKey = invoiceKeys.GetValueOrDefault(x.InvoiceId),
            ExpenseTypeKey = dailyTypeKeys.GetValueOrDefault(x.ExpenseTypeId),
            x.Amount, x.Notes
        })));
        result.AddRange(customerPayments.Select(x => Record("CustomerPayment", x, new
        {
            CustomerKey = customerKeys.GetValueOrDefault(x.CustomerId),
            InvoiceKey = x.SalesInvoiceId.HasValue ? invoiceKeys.GetValueOrDefault(x.SalesInvoiceId.Value) : null,
            x.Amount, x.PaymentDate, x.PaymentMethod, x.ReferenceNumber, x.Notes
        })));
        return result;
    }

    private static LocalSyncRecord Record(string entity, SyncableEntity row, object data) => new()
    {
        Entity = entity,
        Key = row.SyncKey,
        UpdatedAtUtc = ToUtc(row.UpdatedAt ?? row.CreatedAt),
        IsDeleted = row.IsDeleted,
        Data = JsonSerializer.SerializeToElement(data, JsonOptions)
    };

    private static async Task ApplyAsync(ApplicationDbContext db, LocalSyncRecord record)
    {
        switch (record.Entity)
        {
            case "Farm": await ApplyFarmAsync(db, record); break;
            case "Customer": await ApplyCustomerAsync(db, record); break;
            case "Creditor": await ApplyCreditorAsync(db, record); break;
            case "CultivationExpenseType": await ApplyCultivationTypeAsync(db, record); break;
            case "QatType": await ApplyQatTypeAsync(db, record); break;
            case "DailyExpenseType": await ApplyDailyTypeAsync(db, record); break;
            case "CultivationExpense": await ApplyCultivationAsync(db, record); break;
            case "SalesInvoice": await ApplyInvoiceAsync(db, record); break;
            case "CultivationDebtPayment": await ApplyDebtPaymentAsync(db, record); break;
            case "SalesInvoiceItem": await ApplyInvoiceItemAsync(db, record); break;
            case "InvoiceExpense": await ApplyInvoiceExpenseAsync(db, record); break;
            case "CustomerPayment": await ApplyCustomerPaymentAsync(db, record); break;
        }
    }

    private static async Task ApplyFarmAsync(ApplicationDbContext db, LocalSyncRecord r)
    {
        var d = Read<FarmData>(r);
        var row = await db.Farms.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.SyncKey == r.Key)
                  ?? await db.Farms.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Name == d.Name);
        if (row is not null && !IncomingWins(row, r)) return;
        if (row is null) { row = new Farm(); db.Farms.Add(row); }
        Stamp(row, r); row.Name = d.Name.Trim(); row.OwnerName = d.OwnerName; row.Location = d.Location;
        row.Phone = d.Phone; row.Notes = d.Notes; row.IsActive = d.IsActive;
    }

    private static async Task ApplyCustomerAsync(ApplicationDbContext db, LocalSyncRecord r)
    {
        var d = Read<CustomerData>(r);
        var row = await db.Customers.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.SyncKey == r.Key);
        if (row is null && !string.IsNullOrWhiteSpace(d.Phone))
            row = await db.Customers.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Phone == d.Phone);
        if (row is not null && !IncomingWins(row, r)) return;
        if (row is null) { row = new Customer(); db.Customers.Add(row); }
        Stamp(row, r); row.Name = d.Name.Trim(); row.Phone = d.Phone; row.Region = d.Region; row.Address = d.Address;
        row.OpeningBalance = d.OpeningBalance; row.CreditLimit = d.CreditLimit; row.Notes = d.Notes; row.IsActive = d.IsActive;
    }

    private static async Task ApplyCreditorAsync(ApplicationDbContext db, LocalSyncRecord r)
    {
        var d = Read<CreditorData>(r);
        var row = await db.Creditors.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.SyncKey == r.Key);
        if (row is null && !string.IsNullOrWhiteSpace(d.Phone))
            row = await db.Creditors.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Phone == d.Phone);
        if (row is not null && !IncomingWins(row, r)) return;
        if (row is null) { row = new Creditor(); db.Creditors.Add(row); }
        Stamp(row, r); row.Name = d.Name.Trim(); row.Phone = d.Phone; row.Address = d.Address;
        row.Notes = d.Notes; row.IsActive = d.IsActive;
    }

    private static Task ApplyCultivationTypeAsync(ApplicationDbContext db, LocalSyncRecord r) =>
        ApplyNamedAsync(db, r, db.CultivationExpenseTypes.IgnoreQueryFilters(), () => new CultivationExpenseType(), x => db.CultivationExpenseTypes.Add(x));
    private static Task ApplyQatTypeAsync(ApplicationDbContext db, LocalSyncRecord r) =>
        ApplyNamedAsync(db, r, db.QatTypes.IgnoreQueryFilters(), () => new QatType(), x => db.QatTypes.Add(x));
    private static Task ApplyDailyTypeAsync(ApplicationDbContext db, LocalSyncRecord r) =>
        ApplyNamedAsync(db, r, db.DailyExpenseTypes.IgnoreQueryFilters(), () => new DailyExpenseType(), x => db.DailyExpenseTypes.Add(x));

    private static async Task ApplyNamedAsync<T>(ApplicationDbContext db, LocalSyncRecord r, IQueryable<T> query, Func<T> create, Action<T> add)
        where T : SyncableEntity
    {
        var d = Read<NamedData>(r);
        var row = await query.FirstOrDefaultAsync(x => x.SyncKey == r.Key);
        if (row is null)
        {
            row = await query.FirstOrDefaultAsync(x => EF.Property<string>(x, "Name") == d.Name);
        }
        if (row is not null && !IncomingWins(row, r)) return;
        if (row is null) { row = create(); add(row); }
        Stamp(row, r);
        typeof(T).GetProperty("Name")!.SetValue(row, d.Name.Trim());
        typeof(T).GetProperty("IsActive")!.SetValue(row, d.IsActive);
    }

    private static async Task ApplyCultivationAsync(ApplicationDbContext db, LocalSyncRecord r)
    {
        var d = Read<CultivationData>(r);
        var farm = await FindByKeyAsync(db.Farms.IgnoreQueryFilters(), d.FarmKey);
        var type = await FindByKeyAsync(db.CultivationExpenseTypes.IgnoreQueryFilters(), d.ExpenseTypeKey);
        if (farm is null || type is null) return;
        var creditor = string.IsNullOrWhiteSpace(d.CreditorKey) ? null : await FindByKeyAsync(db.Creditors.IgnoreQueryFilters(), d.CreditorKey);
        var row = await db.CultivationExpenses.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.SyncKey == r.Key)
                  ?? await db.CultivationExpenses.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.ReceiptNumber == d.ReceiptNumber);
        if (row is not null && !IncomingWins(row, r)) return;
        if (row is null) { row = new CultivationExpense(); db.CultivationExpenses.Add(row); }
        Stamp(row, r); row.FarmId = farm.Id; row.ExpenseTypeId = type.Id; row.Amount = d.Amount;
        row.ExpenseDate = d.ExpenseDate; row.PaymentType = d.PaymentType; row.CreditorId = creditor?.Id;
        row.PaidAmount = d.PaidAmount; row.DueDate = d.DueDate; row.DebtStatus = d.DebtStatus;
        row.Notes = d.Notes; row.ReceiptNumber = d.ReceiptNumber;
    }

    private static async Task ApplyInvoiceAsync(ApplicationDbContext db, LocalSyncRecord r)
    {
        var d = Read<InvoiceData>(r);
        var farm = await FindByKeyAsync(db.Farms.IgnoreQueryFilters(), d.FarmKey);
        if (farm is null) return;
        var customer = string.IsNullOrWhiteSpace(d.CustomerKey) ? null : await FindByKeyAsync(db.Customers.IgnoreQueryFilters(), d.CustomerKey);
        var row = await db.SalesInvoices.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.SyncKey == r.Key)
                  ?? await db.SalesInvoices.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.InvoiceNumber == d.InvoiceNumber);
        if (row is not null && !IncomingWins(row, r)) return;
        if (row is null) { row = new SalesInvoice(); db.SalesInvoices.Add(row); }
        Stamp(row, r); row.InvoiceNumber = d.InvoiceNumber; row.FarmId = farm.Id; row.CustomerId = customer?.Id;
        row.InvoiceDate = d.InvoiceDate; row.PaymentDueDate = customer is null ? null : d.PaymentDueDate;
        row.BuyerName = customer?.Name ?? d.BuyerName; row.BuyerPhone = customer?.Phone ?? d.BuyerPhone;
        row.GrossAmount = d.GrossAmount; row.ZakatPercent = d.ZakatPercent; row.ZakatAmount = d.ZakatAmount;
        row.ZakatStatus = d.ZakatStatus; row.ZakatPaidAt = d.ZakatPaidAt; row.ZakatPaymentReference = d.ZakatPaymentReference;
        row.TotalExpenses = d.TotalExpenses; row.NetAmount = d.NetAmount;
        row.AmountPaid = customer is null ? d.GrossAmount : d.AmountPaid;
        row.AmountDue = customer is null ? 0 : d.AmountDue;
        row.PaymentMethod = customer is null ? PaymentMethod.Cash : d.PaymentMethod;
        row.PaymentStatus = customer is null ? PaymentStatus.Paid : d.PaymentStatus;
        row.Status = d.Status; row.Notes = d.Notes;
    }

    private static async Task ApplyDebtPaymentAsync(ApplicationDbContext db, LocalSyncRecord r)
    {
        var d = Read<DebtPaymentData>(r);
        var expense = await FindByKeyAsync(db.CultivationExpenses.IgnoreQueryFilters(), d.CultivationExpenseKey);
        var creditor = await FindByKeyAsync(db.Creditors.IgnoreQueryFilters(), d.CreditorKey);
        if (expense is null || creditor is null) return;
        var row = await db.CultivationDebtPayments.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.SyncKey == r.Key);
        if (row is not null && !IncomingWins(row, r)) return;
        if (row is null) { row = new CultivationDebtPayment(); db.CultivationDebtPayments.Add(row); }
        Stamp(row, r); row.CultivationExpenseId = expense.Id; row.CreditorId = creditor.Id; row.Amount = d.Amount;
        row.PaymentDate = d.PaymentDate; row.PaymentMethod = d.PaymentMethod; row.ReferenceNumber = d.ReferenceNumber; row.Notes = d.Notes;
    }

    private static async Task ApplyInvoiceItemAsync(ApplicationDbContext db, LocalSyncRecord r)
    {
        var d = Read<InvoiceItemData>(r);
        var invoice = await FindByKeyAsync(db.SalesInvoices.IgnoreQueryFilters(), d.InvoiceKey);
        var type = await FindByKeyAsync(db.QatTypes.IgnoreQueryFilters(), d.QatTypeKey);
        if (invoice is null || type is null) return;
        var row = await db.SalesInvoiceItems.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.SyncKey == r.Key);
        if (row is not null && !IncomingWins(row, r)) return;
        if (row is null) { row = new SalesInvoiceItem(); db.SalesInvoiceItems.Add(row); }
        Stamp(row, r); row.InvoiceId = invoice.Id; row.QatTypeId = type.Id; row.Quantity = d.Quantity;
        row.UnitPrice = d.UnitPrice; row.TotalPrice = d.TotalPrice;
    }

    private static async Task ApplyInvoiceExpenseAsync(ApplicationDbContext db, LocalSyncRecord r)
    {
        var d = Read<InvoiceExpenseData>(r);
        var invoice = await FindByKeyAsync(db.SalesInvoices.IgnoreQueryFilters(), d.InvoiceKey);
        var type = await FindByKeyAsync(db.DailyExpenseTypes.IgnoreQueryFilters(), d.ExpenseTypeKey);
        if (invoice is null || type is null) return;
        var row = await db.InvoiceExpenses.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.SyncKey == r.Key);
        if (row is not null && !IncomingWins(row, r)) return;
        if (row is null) { row = new InvoiceExpense(); db.InvoiceExpenses.Add(row); }
        Stamp(row, r); row.InvoiceId = invoice.Id; row.ExpenseTypeId = type.Id; row.Amount = d.Amount; row.Notes = d.Notes;
    }

    private static async Task ApplyCustomerPaymentAsync(ApplicationDbContext db, LocalSyncRecord r)
    {
        var d = Read<CustomerPaymentData>(r);
        var customer = await FindByKeyAsync(db.Customers.IgnoreQueryFilters(), d.CustomerKey);
        if (customer is null) return;
        var invoice = string.IsNullOrWhiteSpace(d.InvoiceKey) ? null : await FindByKeyAsync(db.SalesInvoices.IgnoreQueryFilters(), d.InvoiceKey);
        var row = await db.CustomerPayments.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.SyncKey == r.Key);
        if (row is not null && !IncomingWins(row, r)) return;
        if (row is null) { row = new CustomerPayment(); db.CustomerPayments.Add(row); }
        Stamp(row, r); row.CustomerId = customer.Id; row.SalesInvoiceId = invoice?.Id; row.Amount = d.Amount;
        row.PaymentDate = d.PaymentDate; row.PaymentMethod = d.PaymentMethod; row.ReferenceNumber = d.ReferenceNumber; row.Notes = d.Notes;
    }

    private static T Read<T>(LocalSyncRecord record) =>
        record.Data.Deserialize<T>(JsonOptions) ?? throw new InvalidOperationException($"بيانات {record.Entity} غير صالحة.");

    private static async Task<T?> FindByKeyAsync<T>(IQueryable<T> query, string? key) where T : SyncableEntity =>
        string.IsNullOrWhiteSpace(key) ? null : await query.FirstOrDefaultAsync(x => x.SyncKey == key);

    private static bool IncomingWins(SyncableEntity row, LocalSyncRecord record) =>
        record.UpdatedAtUtc >= ToUtc(row.UpdatedAt ?? row.CreatedAt);

    private static void Stamp(SyncableEntity row, LocalSyncRecord record)
    {
        row.SyncKey = record.Key;
        if (row.Id == 0) row.CreatedAt = record.UpdatedAtUtc.UtcDateTime;
        row.UpdatedAt = record.UpdatedAtUtc.UtcDateTime;
        row.IsDeleted = record.IsDeleted;
        row.DeletedAt = record.IsDeleted ? record.UpdatedAtUtc.UtcDateTime : null;
    }

    private static bool IsValidKey(string? value) => value?.Length == 32 && value.All(Uri.IsHexDigit);
    private static DateTimeOffset ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => new DateTimeOffset(value),
        DateTimeKind.Local => new DateTimeOffset(value).ToUniversalTime(),
        _ => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
    };

    private sealed record FarmData(string Name, string? OwnerName, string? Location, string? Phone, string? Notes, bool IsActive);
    private sealed record CustomerData(string Name, string? Phone, string? Region, string? Address, decimal OpeningBalance, decimal CreditLimit, string? Notes, bool IsActive);
    private sealed record CreditorData(string Name, string? Phone, string? Address, string? Notes, bool IsActive);
    private sealed record NamedData(string Name, bool IsActive);
    private sealed record CultivationData(string FarmKey, string ExpenseTypeKey, decimal Amount, DateTime ExpenseDate, CultivationExpensePaymentType PaymentType, string? CreditorKey, decimal PaidAmount, DateTime? DueDate, CultivationDebtStatus DebtStatus, string? Notes, string ReceiptNumber);
    private sealed record InvoiceData(string InvoiceNumber, string FarmKey, string? CustomerKey, DateTime InvoiceDate, DateTime? PaymentDueDate, string? BuyerName, string? BuyerPhone, decimal GrossAmount, decimal ZakatPercent, decimal ZakatAmount, ZakatPaymentStatus ZakatStatus, DateTime? ZakatPaidAt, string? ZakatPaymentReference, decimal TotalExpenses, decimal NetAmount, decimal AmountPaid, decimal AmountDue, PaymentMethod PaymentMethod, PaymentStatus PaymentStatus, InvoiceStatus Status, string? Notes);
    private sealed record DebtPaymentData(string CultivationExpenseKey, string CreditorKey, decimal Amount, DateTime PaymentDate, PaymentMethod PaymentMethod, string? ReferenceNumber, string? Notes);
    private sealed record InvoiceItemData(string InvoiceKey, string QatTypeKey, int Quantity, decimal UnitPrice, decimal TotalPrice);
    private sealed record InvoiceExpenseData(string InvoiceKey, string ExpenseTypeKey, decimal Amount, string? Notes);
    private sealed record CustomerPaymentData(string CustomerKey, string? InvoiceKey, decimal Amount, DateTime PaymentDate, PaymentMethod PaymentMethod, string? ReferenceNumber, string? Notes);
}
