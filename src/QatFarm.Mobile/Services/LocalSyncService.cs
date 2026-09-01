using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
using QatFarm.Mobile.Data;
using QatFarm.Mobile.Models;
using SQLite;

namespace QatFarm.Mobile.Services;

public sealed class LocalSyncService : IDisposable
{
    private readonly MobileDb _mobileDb;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(45) };
    private Task? _loop;

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

    public LocalSyncService(MobileDb mobileDb)
    {
        _mobileDb = mobileDb;
        Connectivity.Current.ConnectivityChanged += ConnectivityChanged;
        LoadPreferences();
    }

    public event Action? Changed;
    public string ServerUrl { get; private set; } = string.Empty;
    public string PairingKey { get; private set; } = string.Empty;
    public bool AutoSync { get; private set; } = true;
    public bool IsSyncing { get; private set; }
    public DateTimeOffset? LastSuccessAt { get; private set; }
    public string LastMessage { get; private set; } = "لم تتم المزامنة بعد.";

    public void Start()
    {
        if (_loop is not null) return;
        _loop = RunLoopAsync(_stop.Token);
    }

    public async Task SavePreferencesAsync(string serverUrl, string pairingKey, bool autoSync)
    {
        ServerUrl = NormalizeServerUrl(serverUrl);
        PairingKey = pairingKey.Trim();
        AutoSync = autoSync;
        Preferences.Default.Set("LocalSync.ServerUrl", ServerUrl);
        Preferences.Default.Set("LocalSync.PairingKey", PairingKey);
        Preferences.Default.Set("LocalSync.AutoSync", AutoSync);
        LastMessage = "تم حفظ إعدادات الربط. جارٍ اختبار الكمبيوتر…";
        Changed?.Invoke();
        await SyncNowAsync();
    }

    public async Task<bool> SyncNowAsync()
    {
        if (!await _gate.WaitAsync(0)) return false;
        try
        {
            if (string.IsNullOrWhiteSpace(ServerUrl) || string.IsNullOrWhiteSpace(PairingKey))
            {
                LastMessage = "أدخل عنوان الكمبيوتر ورمز الربط أولًا.";
                Changed?.Invoke();
                return false;
            }
            if (Connectivity.Current.NetworkAccess == NetworkAccess.None)
            {
                LastMessage = "بانتظار اتصال Wi-Fi.";
                Changed?.Invoke();
                return false;
            }

            IsSyncing = true;
            LastMessage = "جارٍ مزامنة البيانات…";
            Changed?.Invoke();

            var batch = await BuildBatchAsync();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ServerUrl}/api/local-sync/sync")
            {
                Content = JsonContent.Create(batch, options: JsonOptions)
            };
            request.Headers.TryAddWithoutValidation("X-AWAD-SYNC-KEY", PairingKey);
            using var response = await _http.SendAsync(request, _stop.Token);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                throw new InvalidOperationException("رمز الربط غير صحيح.");
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"رفض الكمبيوتر المزامنة ({(int)response.StatusCode}): {detail}");
            }

            var result = await response.Content.ReadFromJsonAsync<LocalSyncResult>(JsonOptions)
                         ?? throw new InvalidOperationException("استجابة الكمبيوتر غير صالحة.");
            await ApplyServerRecordsAsync(result.Records);
            LastSuccessAt = DateTimeOffset.Now;
            Preferences.Default.Set("LocalSync.LastSuccessAt", LastSuccessAt.Value.ToString("O"));
            LastMessage = $"تمت المزامنة بنجاح — {LastSuccessAt:yyyy/MM/dd HH:mm}";
            Preferences.Default.Set("LocalSync.LastMessage", LastMessage);
            return true;
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            LastMessage = $"تعذرت المزامنة: {ex.Message}";
            Preferences.Default.Set("LocalSync.LastMessage", LastMessage);
            return false;
        }
        finally
        {
            IsSyncing = false;
            Changed?.Invoke();
            _gate.Release();
        }
    }

    private async Task<LocalSyncBatch> BuildBatchAsync()
    {
        var db = await _mobileDb.GetAsync();
        await EnsureAllKeysAsync(db);

        var farms = await db.Table<Farm>().ToListAsync();
        var customers = await db.Table<Customer>().ToListAsync();
        var creditors = await db.Table<Creditor>().ToListAsync();
        var cultivationTypes = await db.Table<CultivationExpenseType>().ToListAsync();
        var qatTypes = await db.Table<QatType>().ToListAsync();
        var dailyTypes = await db.Table<DailyExpenseType>().ToListAsync();
        var cultivation = await db.Table<CultivationExpense>().ToListAsync();
        var invoices = await db.Table<SalesInvoice>().ToListAsync();
        var debtPayments = await db.Table<CultivationDebtPayment>().ToListAsync();
        var invoiceItems = await db.Table<SalesInvoiceItem>().ToListAsync();
        var invoiceExpenses = await db.Table<InvoiceExpense>().ToListAsync();
        var customerPayments = await db.Table<CustomerPayment>().ToListAsync();

        var farmKeys = farms.ToDictionary(x => x.Id, x => x.SyncKey);
        var customerKeys = customers.ToDictionary(x => x.Id, x => x.SyncKey);
        var creditorKeys = creditors.ToDictionary(x => x.Id, x => x.SyncKey);
        var cultivationTypeKeys = cultivationTypes.ToDictionary(x => x.Id, x => x.SyncKey);
        var qatTypeKeys = qatTypes.ToDictionary(x => x.Id, x => x.SyncKey);
        var dailyTypeKeys = dailyTypes.ToDictionary(x => x.Id, x => x.SyncKey);
        var cultivationKeys = cultivation.ToDictionary(x => x.Id, x => x.SyncKey);
        var invoiceKeys = invoices.ToDictionary(x => x.Id, x => x.SyncKey);
        var records = new List<LocalSyncRecord>();

        records.AddRange(farms.Select(x => Record("Farm", x, new { x.Name, x.OwnerName, x.Location, x.Phone, x.Notes, x.IsActive })));
        records.AddRange(customers.Select(x => Record("Customer", x, new { x.Name, x.Phone, x.SellerPhone, x.Region, x.Address, x.OpeningBalance, x.CreditLimit, x.DebtAlertEnabled, x.Notes, x.IsActive })));
        records.AddRange(creditors.Select(x => Record("Creditor", x, new { x.Name, x.Phone, x.Address, x.Notes, x.IsActive })));
        records.AddRange(cultivationTypes.Select(x => Record("CultivationExpenseType", x, new { x.Name, x.IsActive })));
        records.AddRange(qatTypes.Select(x => Record("QatType", x, new { x.Name, x.IsActive })));
        records.AddRange(dailyTypes.Select(x => Record("DailyExpenseType", x, new { x.Name, x.IsActive })));
        records.AddRange(cultivation.Select(x => Record("CultivationExpense", x, new
        {
            FarmKey = farmKeys.GetValueOrDefault(x.FarmId),
            ExpenseTypeKey = cultivationTypeKeys.GetValueOrDefault(x.ExpenseTypeId),
            x.Amount, x.ExpenseDate, x.PaymentType,
            CreditorKey = x.CreditorId.HasValue ? creditorKeys.GetValueOrDefault(x.CreditorId.Value) : null,
            x.PaidAmount, x.DueDate, x.DebtStatus, x.Notes, x.ReceiptNumber
        })));
        records.AddRange(invoices.Select(x => Record("SalesInvoice", x, new
        {
            x.InvoiceNumber,
            FarmKey = farmKeys.GetValueOrDefault(x.FarmId),
            CustomerKey = x.CustomerId.HasValue ? customerKeys.GetValueOrDefault(x.CustomerId.Value) : null,
            x.InvoiceDate, x.PaymentDueDate, x.BuyerName, x.BuyerPhone, x.GrossAmount,
            x.ZakatPercent, x.ZakatAmount, x.ZakatStatus, x.ZakatPaidAt, x.ZakatPaymentReference, x.ZakatRecipientName,
            x.TotalExpenses, x.NetAmount, x.AmountPaid, x.AmountDue, x.PaymentMethod, x.PaymentStatus,
            x.Status, x.Notes
        })));
        records.AddRange(debtPayments.Select(x => Record("CultivationDebtPayment", x, new
        {
            CultivationExpenseKey = cultivationKeys.GetValueOrDefault(x.CultivationExpenseId),
            CreditorKey = creditorKeys.GetValueOrDefault(x.CreditorId),
            x.Amount, x.PaymentDate, x.PaymentMethod, x.ReferenceNumber, x.Notes
        })));
        records.AddRange(invoiceItems.Select(x => Record("SalesInvoiceItem", x, new
        {
            InvoiceKey = invoiceKeys.GetValueOrDefault(x.InvoiceId),
            QatTypeKey = qatTypeKeys.GetValueOrDefault(x.QatTypeId),
            x.Quantity, x.UnitPrice, x.TotalPrice
        })));
        records.AddRange(invoiceExpenses.Select(x => Record("InvoiceExpense", x, new
        {
            InvoiceKey = invoiceKeys.GetValueOrDefault(x.InvoiceId),
            ExpenseTypeKey = dailyTypeKeys.GetValueOrDefault(x.ExpenseTypeId),
            x.Amount, x.Notes
        })));
        records.AddRange(customerPayments.Select(x => Record("CustomerPayment", x, new
        {
            CustomerKey = customerKeys.GetValueOrDefault(x.CustomerId),
            InvoiceKey = x.SalesInvoiceId.HasValue ? invoiceKeys.GetValueOrDefault(x.SalesInvoiceId.Value) : null,
            x.Amount, x.PaymentDate, x.PaymentMethod, x.ReferenceNumber, x.Notes
        })));

        var deviceId = Preferences.Default.Get("LocalSync.DeviceId", string.Empty);
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            deviceId = $"android-{Guid.NewGuid():N}";
            Preferences.Default.Set("LocalSync.DeviceId", deviceId);
        }
        return new LocalSyncBatch { DeviceId = deviceId, Records = records };
    }

    private async Task ApplyServerRecordsAsync(List<LocalSyncRecord> records)
    {
        var db = await _mobileDb.GetAsync();
        foreach (var entity in ImportOrder)
        {
            foreach (var record in records.Where(x => x.Entity == entity && IsValidKey(x.Key)))
                await ApplyAsync(db, record);
        }
        await db.ExecuteAsync("PRAGMA wal_checkpoint(PASSIVE);");
    }

    private static async Task ApplyAsync(SQLiteAsyncConnection db, LocalSyncRecord r)
    {
        switch (r.Entity)
        {
            case "Farm":
            {
                var d = Read<FarmData>(r); var row = await FindAsync<Farm>(db, r.Key) ?? new Farm();
                Stamp(row, r); row.Name = d.Name; row.OwnerName = d.OwnerName; row.Location = d.Location; row.Phone = d.Phone; row.Notes = d.Notes; row.IsActive = d.IsActive;
                await UpsertAsync(db, row); break;
            }
            case "Customer":
            {
                var d = Read<CustomerData>(r); var row = await FindAsync<Customer>(db, r.Key) ?? new Customer();
                Stamp(row, r); row.Name = d.Name; row.Phone = d.Phone; if (d.SellerPhone is not null) row.SellerPhone = d.SellerPhone; row.Region = d.Region; row.Address = d.Address; row.OpeningBalance = d.OpeningBalance; row.CreditLimit = d.CreditLimit; if (d.DebtAlertEnabled.HasValue) row.DebtAlertEnabled = d.DebtAlertEnabled.Value; row.Notes = d.Notes; row.IsActive = d.IsActive;
                await UpsertAsync(db, row); break;
            }
            case "Creditor":
            {
                var d = Read<CreditorData>(r); var row = await FindAsync<Creditor>(db, r.Key) ?? new Creditor();
                Stamp(row, r); row.Name = d.Name; row.Phone = d.Phone; row.Address = d.Address; row.Notes = d.Notes; row.IsActive = d.IsActive;
                await UpsertAsync(db, row); break;
            }
            case "CultivationExpenseType": await ApplyNamedAsync<CultivationExpenseType>(db, r); break;
            case "QatType": await ApplyNamedAsync<QatType>(db, r); break;
            case "DailyExpenseType": await ApplyNamedAsync<DailyExpenseType>(db, r); break;
            case "CultivationExpense": await ApplyCultivationAsync(db, r); break;
            case "SalesInvoice": await ApplyInvoiceAsync(db, r); break;
            case "CultivationDebtPayment": await ApplyDebtPaymentAsync(db, r); break;
            case "SalesInvoiceItem": await ApplyInvoiceItemAsync(db, r); break;
            case "InvoiceExpense": await ApplyInvoiceExpenseAsync(db, r); break;
            case "CustomerPayment": await ApplyCustomerPaymentAsync(db, r); break;
        }
    }

    private static async Task ApplyNamedAsync<T>(SQLiteAsyncConnection db, LocalSyncRecord r) where T : LocalEntity, new()
    {
        var d = Read<NamedData>(r); var row = await FindAsync<T>(db, r.Key) ?? new T();
        Stamp(row, r); typeof(T).GetProperty("Name")!.SetValue(row, d.Name); typeof(T).GetProperty("IsActive")!.SetValue(row, d.IsActive);
        await UpsertAsync(db, row);
    }

    private static async Task ApplyCultivationAsync(SQLiteAsyncConnection db, LocalSyncRecord r)
    {
        var d = Read<CultivationData>(r); var farm = await FindAsync<Farm>(db, d.FarmKey); var type = await FindAsync<CultivationExpenseType>(db, d.ExpenseTypeKey);
        if (farm is null || type is null) return; var creditor = string.IsNullOrWhiteSpace(d.CreditorKey) ? null : await FindAsync<Creditor>(db, d.CreditorKey);
        var row = await FindAsync<CultivationExpense>(db, r.Key) ?? new CultivationExpense(); Stamp(row, r);
        row.FarmId = farm.Id; row.ExpenseTypeId = type.Id; row.Amount = d.Amount; row.ExpenseDate = d.ExpenseDate; row.PaymentType = d.PaymentType; row.CreditorId = creditor?.Id; row.PaidAmount = d.PaidAmount; row.DueDate = d.DueDate; row.DebtStatus = d.DebtStatus; row.Notes = d.Notes; row.ReceiptNumber = d.ReceiptNumber;
        await UpsertAsync(db, row);
    }

    private static async Task ApplyInvoiceAsync(SQLiteAsyncConnection db, LocalSyncRecord r)
    {
        var d = Read<InvoiceData>(r); var farm = await FindAsync<Farm>(db, d.FarmKey); if (farm is null) return;
        var customer = string.IsNullOrWhiteSpace(d.CustomerKey) ? null : await FindAsync<Customer>(db, d.CustomerKey);
        var row = await FindAsync<SalesInvoice>(db, r.Key) ?? new SalesInvoice(); Stamp(row, r);
        row.InvoiceNumber = d.InvoiceNumber; row.FarmId = farm.Id; row.CustomerId = customer?.Id; row.InvoiceDate = d.InvoiceDate; row.PaymentDueDate = customer is null ? null : d.PaymentDueDate; row.BuyerName = customer?.Name ?? d.BuyerName; row.BuyerPhone = customer?.Phone ?? d.BuyerPhone; row.GrossAmount = d.GrossAmount; row.ZakatPercent = d.ZakatPercent; row.ZakatAmount = d.ZakatAmount; row.ZakatStatus = d.ZakatStatus; row.ZakatPaidAt = d.ZakatPaidAt; row.ZakatPaymentReference = d.ZakatPaymentReference; if (d.ZakatRecipientName is not null) row.ZakatRecipientName = d.ZakatRecipientName; row.TotalExpenses = d.TotalExpenses; row.NetAmount = d.NetAmount; row.AmountPaid = customer is null ? d.GrossAmount : d.AmountPaid; row.AmountDue = customer is null ? 0 : d.AmountDue; row.PaymentMethod = customer is null ? PaymentMethod.Cash : d.PaymentMethod; row.PaymentStatus = customer is null ? PaymentStatus.Paid : d.PaymentStatus; row.Status = d.Status; row.Notes = d.Notes;
        await UpsertAsync(db, row);
    }

    private static async Task ApplyDebtPaymentAsync(SQLiteAsyncConnection db, LocalSyncRecord r)
    {
        var d = Read<DebtPaymentData>(r); var expense = await FindAsync<CultivationExpense>(db, d.CultivationExpenseKey); var creditor = await FindAsync<Creditor>(db, d.CreditorKey); if (expense is null || creditor is null) return;
        var row = await FindAsync<CultivationDebtPayment>(db, r.Key) ?? new CultivationDebtPayment(); Stamp(row, r); row.CultivationExpenseId = expense.Id; row.CreditorId = creditor.Id; row.Amount = d.Amount; row.PaymentDate = d.PaymentDate; row.PaymentMethod = d.PaymentMethod; row.ReferenceNumber = d.ReferenceNumber; row.Notes = d.Notes; await UpsertAsync(db, row);
    }

    private static async Task ApplyInvoiceItemAsync(SQLiteAsyncConnection db, LocalSyncRecord r)
    {
        var d = Read<InvoiceItemData>(r); var invoice = await FindAsync<SalesInvoice>(db, d.InvoiceKey); var type = await FindAsync<QatType>(db, d.QatTypeKey); if (invoice is null || type is null) return;
        var row = await FindAsync<SalesInvoiceItem>(db, r.Key) ?? new SalesInvoiceItem(); Stamp(row, r); row.InvoiceId = invoice.Id; row.QatTypeId = type.Id; row.Quantity = d.Quantity; row.UnitPrice = d.UnitPrice; row.TotalPrice = d.TotalPrice; await UpsertAsync(db, row);
    }

    private static async Task ApplyInvoiceExpenseAsync(SQLiteAsyncConnection db, LocalSyncRecord r)
    {
        var d = Read<InvoiceExpenseData>(r); var invoice = await FindAsync<SalesInvoice>(db, d.InvoiceKey); var type = await FindAsync<DailyExpenseType>(db, d.ExpenseTypeKey); if (invoice is null || type is null) return;
        var row = await FindAsync<InvoiceExpense>(db, r.Key) ?? new InvoiceExpense(); Stamp(row, r); row.InvoiceId = invoice.Id; row.ExpenseTypeId = type.Id; row.Amount = d.Amount; row.Notes = d.Notes; await UpsertAsync(db, row);
    }

    private static async Task ApplyCustomerPaymentAsync(SQLiteAsyncConnection db, LocalSyncRecord r)
    {
        var d = Read<CustomerPaymentData>(r); var customer = await FindAsync<Customer>(db, d.CustomerKey); if (customer is null) return; var invoice = string.IsNullOrWhiteSpace(d.InvoiceKey) ? null : await FindAsync<SalesInvoice>(db, d.InvoiceKey);
        var row = await FindAsync<CustomerPayment>(db, r.Key) ?? new CustomerPayment(); Stamp(row, r); row.CustomerId = customer.Id; row.SalesInvoiceId = invoice?.Id; row.Amount = d.Amount; row.PaymentDate = d.PaymentDate; row.PaymentMethod = d.PaymentMethod; row.ReferenceNumber = d.ReferenceNumber; row.Notes = d.Notes; await UpsertAsync(db, row);
    }

    private static async Task EnsureAllKeysAsync(SQLiteAsyncConnection db)
    {
        await EnsureKeysAsync<Farm>(db); await EnsureKeysAsync<Customer>(db); await EnsureKeysAsync<Creditor>(db);
        await EnsureKeysAsync<CultivationExpenseType>(db); await EnsureKeysAsync<QatType>(db); await EnsureKeysAsync<DailyExpenseType>(db);
        await EnsureKeysAsync<CultivationExpense>(db); await EnsureKeysAsync<SalesInvoice>(db); await EnsureKeysAsync<CultivationDebtPayment>(db);
        await EnsureKeysAsync<SalesInvoiceItem>(db); await EnsureKeysAsync<InvoiceExpense>(db); await EnsureKeysAsync<CustomerPayment>(db);
    }

    private static async Task EnsureKeysAsync<T>(SQLiteAsyncConnection db) where T : LocalEntity, new()
    {
        var rows = await db.Table<T>().ToListAsync();
        foreach (var row in rows.Where(x => !IsValidKey(x.SyncKey)))
        {
            row.SyncKey = Guid.NewGuid().ToString("N");
            await db.UpdateAsync(row);
        }
    }

    private static LocalSyncRecord Record(string entity, LocalEntity row, object data) => new()
    {
        Entity = entity, Key = row.SyncKey, UpdatedAtUtc = ToUtc(row.UpdatedAt ?? row.CreatedAt), IsDeleted = row.IsDeleted,
        Data = JsonSerializer.SerializeToElement(data, JsonOptions)
    };
    private static T Read<T>(LocalSyncRecord record) => record.Data.Deserialize<T>(JsonOptions) ?? throw new InvalidOperationException($"بيانات {record.Entity} غير صالحة.");
    private static async Task<T?> FindAsync<T>(SQLiteAsyncConnection db, string? key) where T : LocalEntity, new() => string.IsNullOrWhiteSpace(key) ? null : await db.Table<T>().Where(x => x.SyncKey == key).FirstOrDefaultAsync();
    private static Task UpsertAsync<T>(SQLiteAsyncConnection db, T row) where T : LocalEntity => row.Id == 0 ? db.InsertAsync(row) : db.UpdateAsync(row);
    private static void Stamp(LocalEntity row, LocalSyncRecord r) { row.SyncKey = r.Key; if (row.Id == 0) row.CreatedAt = r.UpdatedAtUtc.LocalDateTime; row.UpdatedAt = r.UpdatedAtUtc.LocalDateTime; row.IsDeleted = r.IsDeleted; }
    private static bool IsValidKey(string? value) => value?.Length == 32 && value.All(Uri.IsHexDigit);
    private static DateTimeOffset ToUtc(DateTime value) => value.Kind switch { DateTimeKind.Utc => new(value), DateTimeKind.Local => new DateTimeOffset(value).ToUniversalTime(), _ => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Local)).ToUniversalTime() };
    private static string NormalizeServerUrl(string value)
    {
        var text = value.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        if (!text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) text = "http://" + text;
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.IsDefaultPort) text = $"{uri.Scheme}://{uri.Host}:5276";
        return text;
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (AutoSync) await SyncNowAsync();
            try { await Task.Delay(TimeSpan.FromSeconds(45), token); } catch (OperationCanceledException) { break; }
        }
    }
    private void ConnectivityChanged(object? sender, ConnectivityChangedEventArgs e) { if (AutoSync && e.NetworkAccess != NetworkAccess.None) _ = SyncNowAsync(); }
    private void LoadPreferences()
    {
        ServerUrl = Preferences.Default.Get("LocalSync.ServerUrl", string.Empty); PairingKey = Preferences.Default.Get("LocalSync.PairingKey", string.Empty); AutoSync = Preferences.Default.Get("LocalSync.AutoSync", true); LastMessage = Preferences.Default.Get("LocalSync.LastMessage", "لم تتم المزامنة بعد.");
        var last = Preferences.Default.Get("LocalSync.LastSuccessAt", string.Empty); if (DateTimeOffset.TryParse(last, out var parsed)) LastSuccessAt = parsed;
    }
    public void Dispose() { Connectivity.Current.ConnectivityChanged -= ConnectivityChanged; _stop.Cancel(); _http.Dispose(); _stop.Dispose(); _gate.Dispose(); }

    private sealed record FarmData(string Name, string? OwnerName, string? Location, string? Phone, string? Notes, bool IsActive);
    private sealed record CustomerData(string Name, string? Phone, string? SellerPhone, string? Region, string? Address, decimal OpeningBalance, decimal CreditLimit, bool? DebtAlertEnabled, string? Notes, bool IsActive);
    private sealed record CreditorData(string Name, string? Phone, string? Address, string? Notes, bool IsActive);
    private sealed record NamedData(string Name, bool IsActive);
    private sealed record CultivationData(string FarmKey, string ExpenseTypeKey, decimal Amount, DateTime ExpenseDate, CultivationExpensePaymentType PaymentType, string? CreditorKey, decimal PaidAmount, DateTime? DueDate, CultivationDebtStatus DebtStatus, string? Notes, string ReceiptNumber);
    private sealed record InvoiceData(string InvoiceNumber, string FarmKey, string? CustomerKey, DateTime InvoiceDate, DateTime? PaymentDueDate, string? BuyerName, string? BuyerPhone, decimal GrossAmount, decimal ZakatPercent, decimal ZakatAmount, ZakatPaymentStatus ZakatStatus, DateTime? ZakatPaidAt, string? ZakatPaymentReference, string? ZakatRecipientName, decimal TotalExpenses, decimal NetAmount, decimal AmountPaid, decimal AmountDue, PaymentMethod PaymentMethod, PaymentStatus PaymentStatus, InvoiceStatus Status, string? Notes);
    private sealed record DebtPaymentData(string CultivationExpenseKey, string CreditorKey, decimal Amount, DateTime PaymentDate, PaymentMethod PaymentMethod, string? ReferenceNumber, string? Notes);
    private sealed record InvoiceItemData(string InvoiceKey, string QatTypeKey, int Quantity, decimal UnitPrice, decimal TotalPrice);
    private sealed record InvoiceExpenseData(string InvoiceKey, string ExpenseTypeKey, decimal Amount, string? Notes);
    private sealed record CustomerPaymentData(string CustomerKey, string? InvoiceKey, decimal Amount, DateTime PaymentDate, PaymentMethod PaymentMethod, string? ReferenceNumber, string? Notes);
}
