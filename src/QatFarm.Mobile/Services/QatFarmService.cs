using System.Text.Json;
using QatFarm.Mobile.Data;
using QatFarm.Mobile.Models;
using SQLite;

namespace QatFarm.Mobile.Services;

public sealed class QatFarmService
{
    private readonly MobileDb _db;
    private readonly AppSession _session;
    private readonly DebtSmsService _debtSms;
    private readonly ZakatNotificationService _zakatNotifications;

    public QatFarmService(MobileDb db, AppSession session, DebtSmsService debtSms, ZakatNotificationService zakatNotifications)
    {
        _db = db;
        _session = session;
        _debtSms = debtSms;
        _zakatNotifications = zakatNotifications;
    }

    private async Task<SQLiteAsyncConnection> DbAsync() => await _db.GetAsync();

    private void EnsureAdmin()
    {
        if (!_session.IsAdmin) throw new InvalidOperationException("هذه العملية متاحة للمدير فقط.");
    }

    private void EnsureCanEditInvoices()
    {
        if (!_session.CanEditInvoices) throw new InvalidOperationException("لا توجد صلاحية لتعديل الفواتير لهذا الحساب.");
    }

    private void EnsureCanDeleteInvoices()
    {
        if (!_session.CanDeleteInvoices) throw new InvalidOperationException("لا توجد صلاحية لحذف الفواتير لهذا الحساب.");
    }

    private async Task AuditAsync(string action, string entity, long id, object? oldValue = null, object? newValue = null)
    {
        var db = await DbAsync();
        await db.InsertAsync(new AuditLog
        {
            UserId = _session.CurrentUser?.Id,
            UserName = _session.CurrentUser?.FullName ?? "النظام",
            Action = action,
            EntityName = entity,
            EntityId = id.ToString(),
            OldValues = oldValue is null ? null : JsonSerializer.Serialize(oldValue),
            NewValues = newValue is null ? null : JsonSerializer.Serialize(newValue),
            ActionDate = DateTime.Now
        });
    }

    public async Task<List<Farm>> GetFarmsAsync(bool activeOnly = false)
    {
        var db = await DbAsync();
        var rows = await db.Table<Farm>().Where(x => !x.IsDeleted).OrderBy(x => x.Name).ToListAsync();
        return activeOnly ? rows.Where(x => x.IsActive).ToList() : rows;
    }

    public async Task SaveFarmAsync(Farm model)
    {
        if (string.IsNullOrWhiteSpace(model.Name)) throw new InvalidOperationException("اسم المزرعة مطلوب.");
        var db = await DbAsync();
        if (model.Id == 0)
        {
            model.Name = model.Name.Trim();
            await db.InsertAsync(model);
            await AuditAsync("إضافة", nameof(Farm), model.Id, null, model);
        }
        else
        {
            EnsureAdmin();
            var old = await db.FindAsync<Farm>(model.Id) ?? throw new InvalidOperationException("المزرعة غير موجودة.");
            model.CreatedAt = old.CreatedAt;
            model.UpdatedAt = DateTime.Now;
            await db.UpdateAsync(model);
            await AuditAsync("تعديل", nameof(Farm), model.Id, old, model);
        }
    }

    public async Task DeleteFarmAsync(long id)
    {
        EnsureAdmin();
        var db = await DbAsync();
        var item = await db.FindAsync<Farm>(id);
        if (item is null) return;
        item.IsDeleted = true;
        item.IsActive = false;
        item.UpdatedAt = DateTime.Now;
        await db.UpdateAsync(item);
        await AuditAsync("حذف منطقي", nameof(Farm), id, item, null);
    }

    public async Task<List<CustomerBalanceRow>> GetCustomersAsync()
    {
        var db = await DbAsync();
        var customers = await db.Table<Customer>().Where(x => !x.IsDeleted).OrderBy(x => x.Name).ToListAsync();
        var invoices = await db.Table<SalesInvoice>().Where(x => !x.IsDeleted && x.Status == InvoiceStatus.Posted).ToListAsync();
        var payments = await db.Table<CustomerPayment>().Where(x => !x.IsDeleted).ToListAsync();
        return customers.Select(c => new CustomerBalanceRow
        {
            Customer = c,
            Invoiced = invoices.Where(i => i.CustomerId == c.Id).Sum(i => i.GrossAmount),
            Paid = invoices.Where(i => i.CustomerId == c.Id).Sum(i => i.AmountPaid)
                   + payments.Where(p => p.CustomerId == c.Id && p.SalesInvoiceId == null).Sum(p => p.Amount)
        }).ToList();
    }

    private static string? NormalizePhoneForStorage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        var plus = text.StartsWith('+');
        var digits = new string(text.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? null : plus ? "+" + digits : digits;
    }

    private static async Task<(decimal Total, decimal Paid, decimal Balance)> CalculateCustomerAccountAsync(
        SQLiteAsyncConnection db, Customer customer, long? excludeInvoiceId = null)
    {
        var invoices = await db.Table<SalesInvoice>()
            .Where(x => !x.IsDeleted && x.Status == InvoiceStatus.Posted && x.CustomerId == customer.Id)
            .ToListAsync();
        if (excludeInvoiceId.HasValue) invoices = invoices.Where(x => x.Id != excludeInvoiceId.Value).ToList();

        var openingPayments = await db.Table<CustomerPayment>()
            .Where(x => !x.IsDeleted && x.CustomerId == customer.Id && x.SalesInvoiceId == null)
            .ToListAsync();
        var total = customer.OpeningBalance + invoices.Sum(x => x.GrossAmount);
        var paid = invoices.Sum(x => x.AmountPaid) + openingPayments.Sum(x => x.Amount);
        return (Math.Max(0, total), Math.Max(0, paid), Math.Max(0, total - paid));
    }

    private static async Task<decimal> CalculateCustomerBalanceAsync(SQLiteAsyncConnection db, Customer customer, long? excludeInvoiceId = null)
        => (await CalculateCustomerAccountAsync(db, customer, excludeInvoiceId)).Balance;

    public async Task<CustomerBalanceRow?> GetCustomerBalanceAsync(long customerId)
    {
        var rows = await GetCustomersAsync();
        return rows.FirstOrDefault(x => x.Customer.Id == customerId);
    }

    private static bool SameAlert(Customer customer, decimal balance, decimal limit)
    {
        if (!customer.LastDebtAlertAt.HasValue) return false;
        if (DateTime.Now - customer.LastDebtAlertAt.Value > TimeSpan.FromHours(12)) return false;
        return Math.Abs(customer.LastDebtAlertBalance - balance) < 0.01m &&
               Math.Abs(customer.LastDebtAlertLimit - limit) < 0.01m;
    }

    private async Task ResetCreditAlertIfBelowLimitAsync(SQLiteAsyncConnection db, Customer customer)
    {
        if (!customer.LastDebtAlertAt.HasValue) return;
        var balance = await CalculateCustomerBalanceAsync(db, customer);
        if (balance >= customer.CreditLimit) return;
        customer.LastDebtAlertAt = null;
        customer.LastDebtAlertBalance = 0;
        customer.LastDebtAlertLimit = 0;
        customer.UpdatedAt = DateTime.Now;
        await db.UpdateAsync(customer);
    }

    private async Task<DebtSmsResult> SendCreditAlertIfNeededAsync(SQLiteAsyncConnection db, Customer customer,
        decimal total, decimal paid, decimal balance, bool blocked)
    {
        if (!customer.DebtAlertEnabled) return new(false, "تنبيه SMS غير مفعل لهذا العميل.");
        if (SameAlert(customer, balance, customer.CreditLimit))
            return new(false, "سبق إرسال نفس التنبيه خلال آخر 12 ساعة.");

        var result = await _debtSms.SendCreditAlertAsync(customer, total, paid, balance, customer.CreditLimit, blocked);
        if (result.Sent)
        {
            customer.LastDebtAlertAt = DateTime.Now;
            customer.LastDebtAlertBalance = balance;
            customer.LastDebtAlertLimit = customer.CreditLimit;
            customer.UpdatedAt = DateTime.Now;
            await db.UpdateAsync(customer);
            await AuditAsync("إرسال تنبيه حد ائتماني", nameof(Customer), customer.Id, null, new
            {
                customer.Name, customer.Phone, customer.SellerPhone, Total = total, Paid = paid, Balance = balance, customer.CreditLimit, Blocked = blocked
            });
        }
        return result;
    }

    public async Task<List<Customer>> GetCustomerLookupsAsync()
    {
        var db = await DbAsync();
        return await db.Table<Customer>().Where(x => !x.IsDeleted && x.IsActive).OrderBy(x => x.Name).ToListAsync();
    }

    public async Task SaveCustomerAsync(Customer model)
    {
        if (string.IsNullOrWhiteSpace(model.Name)) throw new InvalidOperationException("اسم العميل مطلوب.");
        if (model.OpeningBalance < 0) throw new InvalidOperationException("الرصيد الافتتاحي لا يمكن أن يكون سالباً.");
        if (model.CreditLimit < 0) throw new InvalidOperationException("حد الائتمان لا يمكن أن يكون سالباً.");
        var db = await DbAsync();
        if (model.Id == 0)
        {
            model.Name = model.Name.Trim();
            model.Phone = NormalizePhoneForStorage(model.Phone);
            model.SellerPhone = NormalizePhoneForStorage(model.SellerPhone);
            await db.InsertAsync(model);
            await AuditAsync("إضافة", nameof(Customer), model.Id, null, model);
        }
        else
        {
            EnsureAdmin();
            var old = await db.FindAsync<Customer>(model.Id) ?? throw new InvalidOperationException("العميل غير موجود.");
            model.CreatedAt = old.CreatedAt;
            model.UpdatedAt = DateTime.Now;
            model.Phone = NormalizePhoneForStorage(model.Phone);
            model.SellerPhone = NormalizePhoneForStorage(model.SellerPhone);
            // لا نمسح حالة آخر تنبيه عند تعديل بيانات العميل العادية.
            model.LastDebtAlertAt = old.LastDebtAlertAt;
            model.LastDebtAlertBalance = old.LastDebtAlertBalance;
            model.LastDebtAlertLimit = old.LastDebtAlertLimit;
            await db.UpdateAsync(model);
            await AuditAsync("تعديل", nameof(Customer), model.Id, old, model);
        }
    }

    public async Task DeleteCustomerAsync(long id)
    {
        EnsureAdmin();
        var db = await DbAsync();
        var item = await db.FindAsync<Customer>(id);
        if (item is null) return;
        item.IsDeleted = true;
        item.IsActive = false;
        item.UpdatedAt = DateTime.Now;
        await db.UpdateAsync(item);
        await AuditAsync("حذف منطقي", nameof(Customer), id, item, null);
    }

    public async Task AddCustomerPaymentAsync(long customerId, long? invoiceId, decimal amount, PaymentMethod method, string? reference)
    {
        if (!_session.CanRecordPayments) throw new InvalidOperationException("تسجيل الدفعات متاح للمدير أو المحاسب.");
        if (amount <= 0) throw new InvalidOperationException("مبلغ الدفعة يجب أن يكون أكبر من صفر.");
        amount = Math.Round(amount, 2);
        var db = await DbAsync();
        var customer = await db.FindAsync<Customer>(customerId)
            ?? throw new InvalidOperationException("العميل غير موجود.");

        if (invoiceId.HasValue)
        {
            var invoice = await db.FindAsync<SalesInvoice>(invoiceId.Value)
                ?? throw new InvalidOperationException("الفاتورة غير موجودة.");
            if (invoice.Status != InvoiceStatus.Posted || invoice.IsDeleted)
                throw new InvalidOperationException("لا يمكن السداد على فاتورة ملغاة أو محذوفة.");
            if (invoice.CustomerId != customerId)
                throw new InvalidOperationException("الفاتورة لا تخص العميل المحدد.");
            if (amount > invoice.AmountDue)
                throw new InvalidOperationException($"الدفعة أكبر من المتبقي في الفاتورة ({invoice.AmountDue:N2}).");

            await ApplyCustomerPaymentAsync(db, customerId, invoice, amount, method, reference);
            await ResetCreditAlertIfBelowLimitAsync(db, customer);
            var account = await CalculateCustomerAccountAsync(db, customer);
            await _debtSms.SendAccountUpdateAsync(customer, amount, account.Total, account.Paid, account.Balance);
            return;
        }

        // توزع الدفعة العامة محاسبياً: الرصيد الافتتاحي أولاً ثم أقدم الفواتير المستحقة.
        // بذلك لا يبقى مبلغ محصل خارج الفواتير ولا تُعرض ديون أكبر من حقيقتها.
        var openingPayments = await db.Table<CustomerPayment>()
            .Where(x => !x.IsDeleted && x.CustomerId == customerId && x.SalesInvoiceId == null)
            .ToListAsync();
        var openingDue = Math.Max(0, customer.OpeningBalance - openingPayments.Sum(x => x.Amount));
        var invoices = await db.Table<SalesInvoice>()
            .Where(x => !x.IsDeleted && x.Status == InvoiceStatus.Posted &&
                        x.CustomerId == customerId && x.AmountDue > 0)
            .ToListAsync();
        invoices = invoices.OrderBy(x => x.PaymentDueDate ?? x.InvoiceDate)
            .ThenBy(x => x.InvoiceDate).ToList();
        var totalDue = openingDue + invoices.Sum(x => x.AmountDue);
        if (amount > totalDue)
            throw new InvalidOperationException($"الدفعة أكبر من إجمالي رصيد العميل ({totalDue:N2}).");

        var remaining = amount;
        if (openingDue > 0)
        {
            var openingAllocation = Math.Min(remaining, openingDue);
            var openingPayment = new CustomerPayment
            {
                CustomerId = customerId,
                SalesInvoiceId = null,
                Amount = openingAllocation,
                PaymentMethod = method,
                ReferenceNumber = reference,
                Notes = "سداد رصيد افتتاحي",
                PaymentDate = DateTime.Now
            };
            await db.InsertAsync(openingPayment);
            await AuditAsync("سداد رصيد افتتاحي", nameof(CustomerPayment), openingPayment.Id, null, openingPayment);
            remaining -= openingAllocation;
        }

        foreach (var invoice in invoices)
        {
            if (remaining <= 0) break;
            var allocation = Math.Min(remaining, invoice.AmountDue);
            await ApplyCustomerPaymentAsync(db, customerId, invoice, allocation, method, reference);
            remaining -= allocation;
        }
        await ResetCreditAlertIfBelowLimitAsync(db, customer);
        var accountAfterPayment = await CalculateCustomerAccountAsync(db, customer);
        await _debtSms.SendAccountUpdateAsync(customer, amount, accountAfterPayment.Total, accountAfterPayment.Paid, accountAfterPayment.Balance);
    }

    private async Task ApplyCustomerPaymentAsync(SQLiteAsyncConnection db, long customerId, SalesInvoice invoice,
        decimal amount, PaymentMethod method, string? reference)
    {
        var payment = new CustomerPayment
        {
            CustomerId = customerId,
            SalesInvoiceId = invoice.Id,
            Amount = amount,
            PaymentMethod = method,
            ReferenceNumber = reference,
            Notes = "تخصيص تلقائي للفاتورة",
            PaymentDate = DateTime.Now
        };
        await db.InsertAsync(payment);
        invoice.AmountPaid = Math.Min(invoice.GrossAmount, invoice.AmountPaid + amount);
        invoice.AmountDue = Math.Max(0, invoice.GrossAmount - invoice.AmountPaid);
        invoice.PaymentStatus = invoice.AmountDue == 0 ? PaymentStatus.Paid : PaymentStatus.Partial;
        invoice.UpdatedAt = DateTime.Now;
        await db.UpdateAsync(invoice);
        await AuditAsync("تسجيل دفعة عميل", nameof(CustomerPayment), payment.Id, null, payment);
    }

    public async Task<List<Creditor>> GetCreditorsAsync(bool activeOnly = false)
    {
        var db = await DbAsync();
        var rows = await db.Table<Creditor>().Where(x => !x.IsDeleted).OrderBy(x => x.Name).ToListAsync();
        return activeOnly ? rows.Where(x => x.IsActive).ToList() : rows;
    }

    public async Task SaveCreditorAsync(Creditor model)
    {
        if (string.IsNullOrWhiteSpace(model.Name)) throw new InvalidOperationException("اسم الدائن مطلوب.");
        var db = await DbAsync();
        if (model.Id == 0)
        {
            model.Name = model.Name.Trim();
            await db.InsertAsync(model);
            await AuditAsync("إضافة", nameof(Creditor), model.Id, null, model);
        }
        else
        {
            EnsureAdmin();
            var old = await db.FindAsync<Creditor>(model.Id) ?? throw new InvalidOperationException("الدائن غير موجود.");
            model.CreatedAt = old.CreatedAt;
            model.UpdatedAt = DateTime.Now;
            await db.UpdateAsync(model);
            await AuditAsync("تعديل", nameof(Creditor), model.Id, old, model);
        }
    }

    public async Task DeleteCreditorAsync(long id)
    {
        EnsureAdmin();
        var db = await DbAsync();
        var item = await db.FindAsync<Creditor>(id);
        if (item is null) return;
        item.IsDeleted = true;
        item.IsActive = false;
        item.UpdatedAt = DateTime.Now;
        await db.UpdateAsync(item);
        await AuditAsync("حذف منطقي", nameof(Creditor), id, item, null);
    }

    public async Task<List<CultivationExpenseType>> GetCultivationTypesAsync(bool activeOnly = true)
    {
        var db = await DbAsync();
        var rows = await db.Table<CultivationExpenseType>().OrderBy(x => x.Name).ToListAsync();
        return activeOnly ? rows.Where(x => !x.IsDeleted && x.IsActive).ToList() : rows;
    }

    public async Task<List<CultivationExpenseRow>> GetCultivationExpensesAsync(long? farmId, int year)
    {
        var db = await DbAsync();
        var from = new DateTime(year, 1, 1);
        var to = from.AddYears(1);
        var expenses = await db.Table<CultivationExpense>()
            .Where(x => !x.IsDeleted && x.ExpenseDate >= from && x.ExpenseDate < to)
            .OrderByDescending(x => x.ExpenseDate).ToListAsync();

        if (farmId.HasValue && farmId.Value > 0)
            expenses = expenses.Where(x => x.FarmId == farmId.Value).ToList();

        var farms = (await GetFarmsAsync()).ToDictionary(x => x.Id, x => x.Name);
        var types = (await GetCultivationTypesAsync(false)).ToDictionary(x => x.Id, x => x.Name);
        var creditors = (await GetCreditorsAsync()).ToDictionary(x => x.Id, x => x.Name);

        return expenses.Select(x => new CultivationExpenseRow
        {
            Expense = x,
            FarmName = farms.GetValueOrDefault(x.FarmId, "غير معروف"),
            ExpenseTypeName = types.GetValueOrDefault(x.ExpenseTypeId, "غير معروف"),
            CreditorName = x.CreditorId.HasValue ? creditors.GetValueOrDefault(x.CreditorId.Value, "غير معروف") : string.Empty
        }).ToList();
    }

    public async Task SaveCultivationExpenseAsync(CultivationExpense model)
    {
        if (model.FarmId <= 0 || model.ExpenseTypeId <= 0 || model.Amount <= 0)
            throw new InvalidOperationException("اختر المزرعة والنوع وأدخل مبلغًا صحيحًا.");

        if (model.PaymentType == CultivationExpensePaymentType.Cash)
        {
            model.PaidAmount = model.Amount;
            model.CreditorId = null;
            model.DueDate = null;
            model.DebtStatus = CultivationDebtStatus.NoDebt;
        }
        else
        {
            if (!model.CreditorId.HasValue || model.CreditorId <= 0)
                throw new InvalidOperationException("اختر الدائن عند تسجيل خسارة آجلة.");
            if (model.PaidAmount < 0 || model.PaidAmount > model.Amount)
                throw new InvalidOperationException("المبلغ المدفوع غير صحيح.");
            model.DebtStatus = model.PaidAmount <= 0 ? CultivationDebtStatus.Unpaid :
                model.PaidAmount >= model.Amount ? CultivationDebtStatus.Paid : CultivationDebtStatus.Partial;
        }

        if (string.IsNullOrWhiteSpace(model.ReceiptNumber))
            model.ReceiptNumber = $"CE-{DateTime.Now:yyyyMMddHHmmss}";

        var db = await DbAsync();
        if (model.Id == 0)
        {
            await db.InsertAsync(model);
            await AuditAsync("إضافة خسارة تربية", nameof(CultivationExpense), model.Id, null, model);
        }
        else
        {
            EnsureAdmin();
            var old = await db.FindAsync<CultivationExpense>(model.Id)
                ?? throw new InvalidOperationException("الخسارة غير موجودة.");
            model.CreatedAt = old.CreatedAt;
            model.UpdatedAt = DateTime.Now;
            await db.UpdateAsync(model);
            await AuditAsync("تعديل خسارة تربية", nameof(CultivationExpense), model.Id, old, model);
        }
    }

    public async Task DeleteCultivationExpenseAsync(long id)
    {
        EnsureAdmin();
        var db = await DbAsync();
        var item = await db.FindAsync<CultivationExpense>(id);
        if (item is null) return;
        item.IsDeleted = true;
        item.UpdatedAt = DateTime.Now;
        await db.UpdateAsync(item);
        await AuditAsync("حذف خسارة تربية", nameof(CultivationExpense), id, item, null);
    }

    public async Task AddCultivationDebtPaymentAsync(long expenseId, decimal amount, PaymentMethod method, string? reference)
    {
        if (!_session.CanRecordPayments) throw new InvalidOperationException("تسجيل الدفعات متاح للمدير أو المحاسب.");
        if (amount <= 0) throw new InvalidOperationException("أدخل مبلغًا صحيحًا.");

        var db = await DbAsync();
        var expense = await db.FindAsync<CultivationExpense>(expenseId)
            ?? throw new InvalidOperationException("الخسارة غير موجودة.");
        var outstanding = Math.Max(0, expense.Amount - expense.PaidAmount);
        if (amount > outstanding) throw new InvalidOperationException($"الدفعة أكبر من المتبقي ({outstanding:N2}).");

        var payment = new CultivationDebtPayment
        {
            CultivationExpenseId = expense.Id,
            CreditorId = expense.CreditorId ?? 0,
            Amount = amount,
            PaymentMethod = method,
            ReferenceNumber = reference,
            PaymentDate = DateTime.Now
        };
        await db.InsertAsync(payment);
        expense.PaidAmount += amount;
        expense.DebtStatus = expense.PaidAmount >= expense.Amount ? CultivationDebtStatus.Paid : CultivationDebtStatus.Partial;
        expense.UpdatedAt = DateTime.Now;
        await db.UpdateAsync(expense);
        await AuditAsync("سداد دين تربية", nameof(CultivationDebtPayment), payment.Id, null, payment);
    }


    public async Task<List<CultivationDebtPayment>> GetCultivationDebtPaymentsAsync(IEnumerable<long> expenseIds)
    {
        var ids = expenseIds.Distinct().ToHashSet();
        if (ids.Count == 0) return [];
        var db = await DbAsync();
        var rows = await db.Table<CultivationDebtPayment>()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.PaymentDate).ToListAsync();
        return rows.Where(x => ids.Contains(x.CultivationExpenseId)).ToList();
    }


    public async Task<List<QatType>> GetQatTypesAsync(bool activeOnly = true)
    {
        var db = await DbAsync();
        var rows = await db.Table<QatType>().OrderBy(x => x.Name).ToListAsync();
        return activeOnly ? rows.Where(x => !x.IsDeleted && x.IsActive).ToList() : rows;
    }

    public async Task<List<DailyExpenseType>> GetDailyExpenseTypesAsync(bool activeOnly = true)
    {
        var db = await DbAsync();
        var rows = await db.Table<DailyExpenseType>().OrderBy(x => x.Name).ToListAsync();
        return activeOnly ? rows.Where(x => !x.IsDeleted && x.IsActive).ToList() : rows;
    }

    public async Task SaveQatTypeAsync(QatType model)
    {
        EnsureAdmin();
        if (string.IsNullOrWhiteSpace(model.Name)) throw new InvalidOperationException("اسم نوع القات مطلوب.");
        var db = await DbAsync();
        model.Name = model.Name.Trim();
        var duplicate = (await db.Table<QatType>().ToListAsync())
            .FirstOrDefault(x => x.Id != model.Id && x.Name.Equals(model.Name, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null) throw new InvalidOperationException("نوع القات موجود مسبقاً.");
        if (model.Id == 0)
        {
            await db.InsertAsync(model);
            await AuditAsync("إضافة نوع قات", nameof(QatType), model.Id, null, model);
            return;
        }
        var old = await db.FindAsync<QatType>(model.Id) ?? throw new InvalidOperationException("نوع القات غير موجود.");
        model.CreatedAt = old.CreatedAt;
        model.UpdatedAt = DateTime.Now;
        await db.UpdateAsync(model);
        await AuditAsync("تعديل نوع قات", nameof(QatType), model.Id, old, model);
    }

    public async Task SaveDailyExpenseTypeAsync(DailyExpenseType model)
    {
        EnsureAdmin();
        if (string.IsNullOrWhiteSpace(model.Name)) throw new InvalidOperationException("اسم نوع المصروف مطلوب.");
        var db = await DbAsync();
        model.Name = model.Name.Trim();
        var duplicate = (await db.Table<DailyExpenseType>().ToListAsync())
            .FirstOrDefault(x => x.Id != model.Id && x.Name.Equals(model.Name, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null) throw new InvalidOperationException("نوع المصروف موجود مسبقاً.");
        if (model.Id == 0)
        {
            await db.InsertAsync(model);
            await AuditAsync("إضافة نوع مصروف فاتورة", nameof(DailyExpenseType), model.Id, null, model);
            return;
        }
        var old = await db.FindAsync<DailyExpenseType>(model.Id) ?? throw new InvalidOperationException("نوع المصروف غير موجود.");
        model.CreatedAt = old.CreatedAt;
        model.UpdatedAt = DateTime.Now;
        await db.UpdateAsync(model);
        await AuditAsync("تعديل نوع مصروف فاتورة", nameof(DailyExpenseType), model.Id, old, model);
    }

    public async Task SaveCultivationExpenseTypeAsync(CultivationExpenseType model)
    {
        EnsureAdmin();
        if (string.IsNullOrWhiteSpace(model.Name)) throw new InvalidOperationException("اسم نوع خسارة التربية مطلوب.");
        var db = await DbAsync();
        model.Name = model.Name.Trim();
        var duplicate = (await db.Table<CultivationExpenseType>().ToListAsync())
            .FirstOrDefault(x => x.Id != model.Id && x.Name.Equals(model.Name, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null) throw new InvalidOperationException("نوع خسارة التربية موجود مسبقاً.");
        if (model.Id == 0)
        {
            await db.InsertAsync(model);
            await AuditAsync("إضافة نوع خسارة تربية", nameof(CultivationExpenseType), model.Id, null, model);
            return;
        }
        var old = await db.FindAsync<CultivationExpenseType>(model.Id)
            ?? throw new InvalidOperationException("نوع خسارة التربية غير موجود.");
        model.CreatedAt = old.CreatedAt;
        model.UpdatedAt = DateTime.Now;
        await db.UpdateAsync(model);
        await AuditAsync("تعديل نوع خسارة تربية", nameof(CultivationExpenseType), model.Id, old, model);
    }

    public async Task<InvoiceEditorModel> GetInvoiceEditorAsync(long id)
    {
        EnsureCanEditInvoices();
        var db = await DbAsync();
        var invoice = await db.FindAsync<SalesInvoice>(id)
            ?? throw new InvalidOperationException("الفاتورة غير موجودة.");
        var items = await db.Table<SalesInvoiceItem>().Where(x => x.InvoiceId == id && !x.IsDeleted).ToListAsync();
        var expenses = await db.Table<InvoiceExpense>().Where(x => x.InvoiceId == id && !x.IsDeleted).ToListAsync();

        return new InvoiceEditorModel
        {
            Id = invoice.Id, InvoiceNumber = invoice.InvoiceNumber, FarmId = invoice.FarmId, CustomerId = invoice.CustomerId,
            InvoiceDate = invoice.InvoiceDate, PaymentDueDate = invoice.PaymentDueDate,
            BuyerName = invoice.BuyerName, BuyerPhone = invoice.BuyerPhone,
            ZakatPercent = invoice.ZakatPercent, AmountPaid = invoice.AmountPaid,
            PaymentMethod = invoice.PaymentMethod, Notes = invoice.Notes,
            Items = items.Select(x => new InvoiceItemInput
            {
                QatTypeId = x.QatTypeId, Quantity = x.Quantity, UnitPrice = x.UnitPrice
            }).ToList(),
            Expenses = expenses.Select(x => new InvoiceExpenseInput
            {
                ExpenseTypeId = x.ExpenseTypeId, Amount = x.Amount, Notes = x.Notes
            }).ToList()
        };
    }

    public async Task<long> SaveInvoiceAsync(InvoiceEditorModel model)
    {
        if (!model.CustomerId.HasValue)
        {
            model.PaymentMethod = PaymentMethod.Cash;
            model.AmountPaid = model.GrossAmount;
            model.PaymentDueDate = null;
        }
        if (model.FarmId <= 0) throw new InvalidOperationException("اختر المزرعة.");
        if (model.Items.Count == 0 || model.Items.Any(x => x.QatTypeId <= 0 || x.Quantity <= 0 || x.UnitPrice <= 0))
            throw new InvalidOperationException("أدخل صنفًا واحدًا صحيحًا على الأقل.");
        if (model.Expenses.Any(x => x.ExpenseTypeId <= 0 || x.Amount <= 0))
            throw new InvalidOperationException("أكمل نوع ومبلغ كل مصروف أو احذف السطر الفارغ.");
        if (model.ZakatPercent < 0 || model.ZakatPercent > 100)
            throw new InvalidOperationException("نسبة الزكاة يجب أن تكون بين 0 و100.");
        if (model.AmountPaid < 0 || model.AmountPaid > model.GrossAmount)
            throw new InvalidOperationException("المبلغ المدفوع غير صحيح.");
        if (model.AmountDue > 0 && !model.CustomerId.HasValue)
            throw new InvalidOperationException("للفواتير الآجلة أو الجزئية اختر حساب العميل حتى تُسجل المديونية بدقة.");

        var db = await DbAsync();
        SalesInvoice invoice;
        decimal recordedPayments = 0;
        if (model.Id == 0)
            invoice = new SalesInvoice { InvoiceNumber = $"INV-{DateTime.Now:yyyyMMdd-HHmmssfff}" };
        else
        {
            EnsureCanEditInvoices();
            invoice = await db.FindAsync<SalesInvoice>(model.Id)
                ?? throw new InvalidOperationException("الفاتورة غير موجودة.");
            recordedPayments = (await db.Table<CustomerPayment>()
                .Where(x => !x.IsDeleted && x.SalesInvoiceId == invoice.Id).ToListAsync()).Sum(x => x.Amount);
            if (model.AmountPaid < recordedPayments)
                throw new InvalidOperationException($"لا يمكن جعل المدفوع أقل من الدفعات المسجلة ({recordedPayments:N2}).");
            if (recordedPayments > 0 && invoice.CustomerId != model.CustomerId)
                throw new InvalidOperationException("لا يمكن تغيير العميل بعد تسجيل دفعات على الفاتورة.");
        }

        var isNewInvoice = model.Id == 0;
        Customer? creditCustomer = null;
        decimal projectedBalance = 0;
        decimal projectedTotal = 0;
        decimal projectedPaid = 0;
        decimal currentBalance = 0;
        bool sendLimitReachedAlertAfterSave = false;
        if (model.CustomerId.HasValue)
        {
            creditCustomer = await db.FindAsync<Customer>(model.CustomerId.Value)
                ?? throw new InvalidOperationException("العميل المحدد غير موجود.");
            if (creditCustomer.IsDeleted || !creditCustomer.IsActive)
                throw new InvalidOperationException("حساب العميل غير نشط.");

            var current = await CalculateCustomerAccountAsync(db, creditCustomer);
            currentBalance = current.Balance;
            var withoutEdited = await CalculateCustomerAccountAsync(
                db, creditCustomer, model.Id > 0 && invoice.CustomerId == creditCustomer.Id ? model.Id : null);
            projectedTotal = Math.Round(withoutEdited.Total + model.GrossAmount, 2);
            projectedPaid = Math.Round(withoutEdited.Paid + model.AmountPaid, 2);
            projectedBalance = Math.Max(0, Math.Round(projectedTotal - projectedPaid, 2));
            var increasesDebt = projectedBalance > currentBalance + 0.009m;

            if (projectedBalance > creditCustomer.CreditLimit && increasesDebt)
            {
                var sms = await SendCreditAlertIfNeededAsync(db, creditCustomer, projectedTotal, projectedPaid, projectedBalance, blocked: true);
                throw new InvalidOperationException(
                    $"تم رفض البيع الآجل: سيصبح دين العميل {projectedBalance:N2} ر.ي بينما الحد المسموح {creditCustomer.CreditLimit:N2} ر.ي. {sms.Message}");
            }

            sendLimitReachedAlertAfterSave = projectedBalance >= creditCustomer.CreditLimit &&
                                             creditCustomer.CreditLimit >= 0 &&
                                             projectedBalance > 0;
        }

        var old = model.Id == 0 ? null : JsonSerializer.Serialize(invoice);
        var previousZakatAmount = invoice.ZakatAmount;
        invoice.FarmId = model.FarmId;
        invoice.CustomerId = model.CustomerId;
        invoice.InvoiceDate = model.InvoiceDate;
        invoice.PaymentDueDate = model.PaymentDueDate;
        invoice.BuyerName = model.BuyerName;
        invoice.BuyerPhone = model.BuyerPhone;
        invoice.GrossAmount = model.GrossAmount;
        invoice.ZakatPercent = model.ZakatPercent;
        invoice.ZakatAmount = model.ZakatAmount;
        invoice.TotalExpenses = model.TotalExpenses;
        invoice.NetAmount = model.NetAmount;
        invoice.AmountPaid = model.AmountPaid;
        invoice.AmountDue = model.AmountDue;
        invoice.PaymentMethod = model.PaymentMethod;
        invoice.PaymentStatus = invoice.AmountDue == 0 ? PaymentStatus.Paid :
            invoice.AmountPaid > 0 ? PaymentStatus.Partial : PaymentStatus.Unpaid;
        invoice.Notes = model.Notes;
        invoice.Status = InvoiceStatus.Posted;
        if (model.Id == 0 || (invoice.ZakatStatus == ZakatPaymentStatus.Paid && previousZakatAmount != model.ZakatAmount))
        {
            invoice.ZakatStatus = model.ZakatAmount > 0 ? ZakatPaymentStatus.Pending : ZakatPaymentStatus.NotApplicable;
            invoice.ZakatPaidAt = null;
            invoice.ZakatPaymentReference = null;
            invoice.ZakatRecipientName = null;
        }
        invoice.UpdatedAt = model.Id == 0 ? null : DateTime.Now;

        if (model.Id == 0) await db.InsertAsync(invoice); else await db.UpdateAsync(invoice);

        var oldItems = await db.Table<SalesInvoiceItem>().Where(x => x.InvoiceId == invoice.Id).ToListAsync();
        var oldExpenses = await db.Table<InvoiceExpense>().Where(x => x.InvoiceId == invoice.Id).ToListAsync();
        foreach (var x in oldItems)
        {
            x.IsDeleted = true;
            x.UpdatedAt = DateTime.Now;
            await db.UpdateAsync(x);
        }
        foreach (var x in oldExpenses)
        {
            x.IsDeleted = true;
            x.UpdatedAt = DateTime.Now;
            await db.UpdateAsync(x);
        }

        foreach (var item in model.Items)
            await db.InsertAsync(new SalesInvoiceItem
            {
                InvoiceId = invoice.Id, QatTypeId = item.QatTypeId,
                Quantity = item.Quantity, UnitPrice = item.UnitPrice, TotalPrice = item.Total
            });

        foreach (var expense in model.Expenses.Where(x => x.ExpenseTypeId > 0 && x.Amount > 0))
            await db.InsertAsync(new InvoiceExpense
            {
                InvoiceId = invoice.Id, ExpenseTypeId = expense.ExpenseTypeId,
                Amount = expense.Amount, Notes = expense.Notes
            });

        await AuditAsync(isNewInvoice ? "إنشاء فاتورة" : "تعديل فاتورة",
            nameof(SalesInvoice), invoice.Id, old, invoice);

        if (creditCustomer is not null && sendLimitReachedAlertAfterSave)
        {
            var actual = await CalculateCustomerAccountAsync(db, creditCustomer);
            await SendCreditAlertIfNeededAsync(db, creditCustomer, actual.Total, actual.Paid, actual.Balance, blocked: false);
        }

        if (isNewInvoice)
        {
            var buyerPhone = creditCustomer?.Phone ?? invoice.BuyerPhone;
            if (!string.IsNullOrWhiteSpace(buyerPhone))
            {
                var typeNames = (await db.Table<QatType>().Where(x => !x.IsDeleted).ToListAsync()).ToDictionary(x => x.Id, x => x.Name);
                var smsItems = model.Items.Select(x => new SaleSmsItem(
                    typeNames.GetValueOrDefault(x.QatTypeId, "صنف"), x.Quantity, x.UnitPrice, x.Total)).ToList();
                var smsResult = await _debtSms.SendSaleReceiptAsync(buyerPhone, creditCustomer?.Name ?? invoice.BuyerName ?? "العميل", invoice, smsItems);
                await AuditAsync(smsResult.Sent ? "إرسال تفاصيل بيع SMS" : "تعذر إرسال تفاصيل بيع SMS", nameof(SalesInvoice), invoice.Id, null, smsResult.Message);
            }
        }

        if (invoice.ZakatStatus == ZakatPaymentStatus.Pending)
            await _zakatNotifications.RefreshAsync(requestPermission: true);

        return invoice.Id;
    }

    public async Task<List<InvoiceListRow>> GetInvoicesAsync(long? farmId, int year, int? month = null)
    {
        var db = await DbAsync();
        var from = month.HasValue ? new DateTime(year, Math.Clamp(month.Value, 1, 12), 1) : new DateTime(year, 1, 1);
        var to = month.HasValue ? from.AddMonths(1) : from.AddYears(1);
        var rows = await db.Table<SalesInvoice>()
            .Where(x => !x.IsDeleted && x.InvoiceDate >= from && x.InvoiceDate < to)
            .OrderByDescending(x => x.InvoiceDate).ToListAsync();
        if (farmId.HasValue && farmId > 0) rows = rows.Where(x => x.FarmId == farmId).ToList();

        var farms = (await GetFarmsAsync()).ToDictionary(x => x.Id, x => x.Name);
        var customers = (await GetCustomerLookupsAsync()).ToDictionary(x => x.Id, x => x.Name);
        return rows.Select(x => new InvoiceListRow
        {
            Invoice = x,
            FarmName = farms.GetValueOrDefault(x.FarmId, "غير معروف"),
            CustomerName = x.CustomerId.HasValue
                ? customers.GetValueOrDefault(x.CustomerId.Value, x.BuyerName ?? "نقدي")
                : x.BuyerName ?? "نقدي"
        }).ToList();
    }

    public async Task CancelInvoiceAsync(long id)
    {
        EnsureAdmin();
        var db = await DbAsync();
        var invoice = await db.FindAsync<SalesInvoice>(id);
        if (invoice is null) return;
        var old = JsonSerializer.Serialize(invoice);
        invoice.Status = InvoiceStatus.Cancelled;
        invoice.ZakatStatus = ZakatPaymentStatus.NotApplicable;
        invoice.UpdatedAt = DateTime.Now;
        await db.UpdateAsync(invoice);
        await AuditAsync("إلغاء فاتورة", nameof(SalesInvoice), id, old, invoice);
    }

    public async Task DeleteInvoiceAsync(long id)
    {
        EnsureCanDeleteInvoices();
        var db = await DbAsync();
        var invoice = await db.FindAsync<SalesInvoice>(id) ?? throw new InvalidOperationException("الفاتورة غير موجودة.");
        var linkedPayments = await db.Table<CustomerPayment>()
            .Where(x => !x.IsDeleted && x.SalesInvoiceId == id).CountAsync();
        if (linkedPayments > 0)
            throw new InvalidOperationException("لا يمكن حذف فاتورة مرتبطة بدفعات مسجلة. راجع دفعات العميل أولاً.");
        var old = JsonSerializer.Serialize(invoice);
        invoice.IsDeleted = true;
        invoice.Status = InvoiceStatus.Cancelled;
        invoice.ZakatStatus = ZakatPaymentStatus.NotApplicable;
        invoice.UpdatedAt = DateTime.Now;
        await db.UpdateAsync(invoice);
        foreach (var item in await db.Table<SalesInvoiceItem>().Where(x => x.InvoiceId == id && !x.IsDeleted).ToListAsync())
        { item.IsDeleted = true; item.UpdatedAt = DateTime.Now; await db.UpdateAsync(item); }
        foreach (var expense in await db.Table<InvoiceExpense>().Where(x => x.InvoiceId == id && !x.IsDeleted).ToListAsync())
        { expense.IsDeleted = true; expense.UpdatedAt = DateTime.Now; await db.UpdateAsync(expense); }
        await AuditAsync("حذف فاتورة", nameof(SalesInvoice), id, old, null);
        if (invoice.CustomerId.HasValue)
        {
            var customer = await db.FindAsync<Customer>(invoice.CustomerId.Value);
            if (customer is not null) await ResetCreditAlertIfBelowLimitAsync(db, customer);
        }
        await _zakatNotifications.RefreshAsync(requestPermission: false);
    }

    public async Task ConfirmZakatPaidAsync(long id, string? recipientName, string? reference)
    {
        if (!_session.CanRecordPayments) throw new InvalidOperationException("تأكيد الزكاة متاح للمدير أو المحاسب.");
        if (string.IsNullOrWhiteSpace(recipientName)) throw new InvalidOperationException("اسم مستلم الزكاة مطلوب.");
        if (string.IsNullOrWhiteSpace(reference)) throw new InvalidOperationException("رقم السند أو المرجع مطلوب.");
        var db = await DbAsync();
        var invoice = await db.FindAsync<SalesInvoice>(id)
            ?? throw new InvalidOperationException("الفاتورة غير موجودة.");
        invoice.ZakatStatus = ZakatPaymentStatus.Paid;
        invoice.ZakatPaidAt = DateTime.Now;
        invoice.ZakatRecipientName = recipientName.Trim();
        invoice.ZakatPaymentReference = reference.Trim();
        invoice.UpdatedAt = DateTime.Now;
        await db.UpdateAsync(invoice);
        await AuditAsync("تأكيد وصول الزكاة", nameof(SalesInvoice), id, null, invoice);
        await _zakatNotifications.RefreshAsync(requestPermission: false);
    }

    public async Task<List<InvoiceListRow>> GetPendingZakatAsync()
    {
        var db = await DbAsync();
        var invoices = await db.Table<SalesInvoice>()
            .Where(x => !x.IsDeleted && x.Status == InvoiceStatus.Posted &&
                        x.ZakatStatus == ZakatPaymentStatus.Pending && x.ZakatAmount > 0)
            .OrderBy(x => x.InvoiceDate).ToListAsync();
        var farms = (await GetFarmsAsync()).ToDictionary(x => x.Id, x => x.Name);
        var customers = (await db.Table<Customer>().Where(x => !x.IsDeleted).ToListAsync())
            .ToDictionary(x => x.Id, x => x.Name);
        return invoices.Select(x => new InvoiceListRow
        {
            Invoice = x,
            FarmName = farms.GetValueOrDefault(x.FarmId, "غير معروف"),
            CustomerName = x.CustomerId.HasValue
                ? customers.GetValueOrDefault(x.CustomerId.Value, x.BuyerName ?? "نقدي")
                : x.BuyerName ?? "نقدي"
        }).ToList();
    }

    public async Task<List<InvoiceListRow>> GetPaidZakatAsync(int take = 100)
    {
        var db = await DbAsync();
        var invoices = await db.Table<SalesInvoice>()
            .Where(x => !x.IsDeleted && x.Status == InvoiceStatus.Posted &&
                        x.ZakatStatus == ZakatPaymentStatus.Paid && x.ZakatAmount > 0)
            .OrderByDescending(x => x.ZakatPaidAt).Take(take).ToListAsync();
        var farms = (await GetFarmsAsync()).ToDictionary(x => x.Id, x => x.Name);
        var customers = (await db.Table<Customer>().Where(x => !x.IsDeleted).ToListAsync()).ToDictionary(x => x.Id, x => x.Name);
        return invoices.Select(x => new InvoiceListRow
        {
            Invoice = x, FarmName = farms.GetValueOrDefault(x.FarmId, "غير معروف"),
            CustomerName = x.CustomerId.HasValue ? customers.GetValueOrDefault(x.CustomerId.Value, x.BuyerName ?? "نقدي") : x.BuyerName ?? "نقدي"
        }).ToList();
    }

    public async Task<DashboardSummary> GetDashboardAsync()
    {
        var db = await DbAsync();
        var today = DateTime.Today;
        var from = new DateTime(today.Year, 1, 1);
        var to = from.AddYears(1);
        // Load the small local tables first, then filter in memory. This avoids
        // provider-specific expression translation failures on some Android devices.
        var invoiceRows = await db.Table<SalesInvoice>().ToListAsync();
        var cultivationRows = await db.Table<CultivationExpense>().ToListAsync();
        var customerBalances = await GetCustomersAsync();

        var invoices = invoiceRows.Where(x => !x.IsDeleted &&
            x.Status == InvoiceStatus.Posted &&
            x.InvoiceDate >= from && x.InvoiceDate < to).ToList();
        var cultivation = cultivationRows.Where(x => !x.IsDeleted &&
            x.ExpenseDate >= from && x.ExpenseDate < to).ToList();

        return new DashboardSummary
        {
            SalesToday = invoices.Where(x => x.InvoiceDate.Date == today).Sum(x => x.GrossAmount),
            SalesYear = invoices.Sum(x => x.GrossAmount),
            NetYear = invoices.Sum(x => x.NetAmount) - cultivation.Sum(x => x.Amount),
            CustomerDebts = customerBalances.Sum(x => Math.Max(0, x.Balance)),
            CultivationDebts = cultivation.Sum(x => Math.Max(0, x.Amount - x.PaidAmount)),
            PendingZakat = invoices.Where(x => x.ZakatStatus == ZakatPaymentStatus.Pending).Sum(x => x.ZakatAmount),
            InvoiceCountYear = invoices.Count,
            OverdueDebtCount = cultivation.Count(x => x.Amount > x.PaidAmount &&
                x.DueDate.HasValue && x.DueDate.Value.Date < today)
        };
    }

    public async Task<AnnualFinanceSummary> GetAnnualFinanceSummaryAsync(long farmId, int year)
    {
        var db = await DbAsync();
        var farm = await db.FindAsync<Farm>(farmId) ?? throw new InvalidOperationException("المزرعة غير موجودة.");
        var from = new DateTime(year, 1, 1);
        var to = from.AddYears(1);
        var invoices = await db.Table<SalesInvoice>()
            .Where(x => !x.IsDeleted && x.Status == InvoiceStatus.Posted && x.FarmId == farmId &&
                        x.InvoiceDate >= from && x.InvoiceDate < to).ToListAsync();
        var cultivation = await db.Table<CultivationExpense>()
            .Where(x => !x.IsDeleted && x.FarmId == farmId &&
                        x.ExpenseDate >= from && x.ExpenseDate < to).ToListAsync();

        var gross = invoices.Sum(x => x.GrossAmount);
        var collected = invoices.Sum(x => x.AmountPaid);
        var invoiceExpenses = invoices.Sum(x => x.TotalExpenses);
        var zakat = invoices.Sum(x => x.ZakatAmount);
        var cultivationTotal = cultivation.Sum(x => x.Amount);
        var cultivationDebt = cultivation.Sum(x => Math.Max(0, x.Amount - x.PaidAmount));
        var accounting = gross - invoiceExpenses - zakat - cultivationTotal;
        var cashAvailable = collected - invoices.Sum(x => x.ZakatStatus == ZakatPaymentStatus.Paid ? x.ZakatAmount : 0)
            - cultivation.Sum(x => x.PaidAmount) - invoiceExpenses;
        var safe = Math.Max(0, Math.Min(accounting, cashAvailable - cultivationDebt));

        return new AnnualFinanceSummary
        {
            FarmName = farm.Name, Year = year, GrossSales = gross, CollectedSales = collected,
            InvoiceExpenses = invoiceExpenses, Zakat = zakat, CultivationExpenses = cultivationTotal,
            CultivationDebtOutstanding = cultivationDebt, AccountingProfit = accounting,
            SafeDistributableProfit = safe
        };
    }

    public async Task<AccountingCenterSummary> GetAccountingCenterAsync(int year, long? farmId = null)
    {
        if (year < 2000 || year > DateTime.Today.Year + 1)
            throw new InvalidOperationException("السنة المحددة غير صحيحة.");

        var db = await DbAsync();
        var from = new DateTime(year, 1, 1);
        var to = from.AddYears(1);
        var farms = await db.Table<Farm>().Where(x => !x.IsDeleted).OrderBy(x => x.Name).ToListAsync();
        var farmNames = farms.ToDictionary(x => x.Id, x => x.Name);
        if (farmId.HasValue && farmId.Value > 0 && !farmNames.ContainsKey(farmId.Value))
            throw new InvalidOperationException("المزرعة المحددة غير موجودة.");

        var allInvoices = await db.Table<SalesInvoice>()
            .Where(x => !x.IsDeleted && x.Status == InvoiceStatus.Posted).ToListAsync();
        var allCultivation = await db.Table<CultivationExpense>()
            .Where(x => !x.IsDeleted).ToListAsync();
        var customerPayments = await db.Table<CustomerPayment>()
            .Where(x => !x.IsDeleted).ToListAsync();
        var debtPayments = await db.Table<CultivationDebtPayment>()
            .Where(x => !x.IsDeleted).ToListAsync();
        var customers = await db.Table<Customer>().Where(x => !x.IsDeleted).ToListAsync();
        var customerNames = customers.ToDictionary(x => x.Id, x => x.Name);

        bool SelectedFarm(long id) => !farmId.HasValue || farmId.Value <= 0 || farmId.Value == id;
        var invoices = allInvoices.Where(x => SelectedFarm(x.FarmId) && x.InvoiceDate >= from && x.InvoiceDate < to).ToList();
        var cultivation = allCultivation.Where(x => SelectedFarm(x.FarmId) && x.ExpenseDate >= from && x.ExpenseDate < to).ToList();

        var grossSales = invoices.Sum(x => x.GrossAmount);
        var collectedSales = invoices.Sum(x => x.AmountPaid);
        var invoiceExpenses = invoices.Sum(x => x.TotalExpenses);
        var cultivationExpenses = cultivation.Sum(x => x.Amount);
        var zakatAccrued = invoices.Sum(x => x.ZakatAmount);
        var costs = invoiceExpenses + cultivationExpenses + zakatAccrued;

        var movements = new List<CashMovementRow>();
        var paymentSumsByInvoice = customerPayments.Where(x => x.SalesInvoiceId.HasValue)
            .GroupBy(x => x.SalesInvoiceId!.Value).ToDictionary(x => x.Key, x => x.Sum(p => p.Amount));
        var debtPaymentSumsByExpense = debtPayments.GroupBy(x => x.CultivationExpenseId)
            .ToDictionary(x => x.Key, x => x.Sum(p => p.Amount));
        var invoiceMap = allInvoices.ToDictionary(x => x.Id);
        var cultivationMap = allCultivation.ToDictionary(x => x.Id);

        foreach (var invoice in invoices)
        {
            var laterPayments = paymentSumsByInvoice.GetValueOrDefault(invoice.Id);
            var paidAtSale = Math.Max(0, invoice.AmountPaid - laterPayments);
            if (paidAtSale > 0)
                movements.Add(new CashMovementRow
                {
                    Date = invoice.InvoiceDate,
                    Kind = "تحصيل بيع",
                    DocumentNumber = invoice.InvoiceNumber,
                    FarmName = farmNames.GetValueOrDefault(invoice.FarmId, "غير معروف"),
                    Description = invoice.BuyerName ?? (invoice.CustomerId.HasValue
                        ? customerNames.GetValueOrDefault(invoice.CustomerId.Value, "عميل") : "بيع نقدي"),
                    Inflow = paidAtSale
                });

            if (invoice.TotalExpenses > 0)
                movements.Add(new CashMovementRow
                {
                    Date = invoice.InvoiceDate,
                    Kind = "مصروف فاتورة",
                    DocumentNumber = invoice.InvoiceNumber,
                    FarmName = farmNames.GetValueOrDefault(invoice.FarmId, "غير معروف"),
                    Description = "مصروفات البيع المرتبطة بالفاتورة",
                    Outflow = invoice.TotalExpenses
                });
        }

        foreach (var payment in customerPayments.Where(x => x.PaymentDate >= from && x.PaymentDate < to))
        {
            SalesInvoice? invoice = null;
            if (payment.SalesInvoiceId.HasValue)
                invoiceMap.TryGetValue(payment.SalesInvoiceId.Value, out invoice);
            if (farmId.HasValue && farmId.Value > 0 && (invoice is null || invoice.FarmId != farmId.Value)) continue;
            movements.Add(new CashMovementRow
            {
                Date = payment.PaymentDate,
                Kind = payment.SalesInvoiceId.HasValue ? "دفعة عميل" : "رصيد افتتاحي",
                DocumentNumber = invoice?.InvoiceNumber ?? payment.ReferenceNumber ?? "دفعة عامة",
                FarmName = invoice is null ? "عام" : farmNames.GetValueOrDefault(invoice.FarmId, "غير معروف"),
                Description = customerNames.GetValueOrDefault(payment.CustomerId, "عميل"),
                Inflow = payment.Amount
            });
        }

        foreach (var expense in cultivation)
        {
            var laterPayments = debtPaymentSumsByExpense.GetValueOrDefault(expense.Id);
            var paidAtRegistration = Math.Max(0, expense.PaidAmount - laterPayments);
            if (paidAtRegistration <= 0) continue;
            movements.Add(new CashMovementRow
            {
                Date = expense.ExpenseDate,
                Kind = "خسارة تربية",
                DocumentNumber = expense.ReceiptNumber,
                FarmName = farmNames.GetValueOrDefault(expense.FarmId, "غير معروف"),
                Description = expense.Notes ?? "مصروف تربية مدفوع",
                Outflow = paidAtRegistration
            });
        }

        foreach (var payment in debtPayments.Where(x => x.PaymentDate >= from && x.PaymentDate < to))
        {
            if (!cultivationMap.TryGetValue(payment.CultivationExpenseId, out var expense)) continue;
            if (!SelectedFarm(expense.FarmId)) continue;
            movements.Add(new CashMovementRow
            {
                Date = payment.PaymentDate,
                Kind = "سداد دائن",
                DocumentNumber = payment.ReferenceNumber ?? expense.ReceiptNumber,
                FarmName = farmNames.GetValueOrDefault(expense.FarmId, "غير معروف"),
                Description = payment.Notes ?? "سداد دين تربية",
                Outflow = payment.Amount
            });
        }

        var paidZakatInvoices = allInvoices.Where(x => SelectedFarm(x.FarmId) &&
            x.ZakatStatus == ZakatPaymentStatus.Paid && x.ZakatPaidAt.HasValue &&
            x.ZakatPaidAt.Value >= from && x.ZakatPaidAt.Value < to).ToList();
        foreach (var invoice in paidZakatInvoices)
            movements.Add(new CashMovementRow
            {
                Date = invoice.ZakatPaidAt!.Value,
                Kind = "دفع زكاة",
                DocumentNumber = invoice.ZakatPaymentReference ?? invoice.InvoiceNumber,
                FarmName = farmNames.GetValueOrDefault(invoice.FarmId, "غير معروف"),
                Description = $"زكاة الفاتورة {invoice.InvoiceNumber}",
                Outflow = invoice.ZakatAmount
            });

        decimal running = 0;
        foreach (var movement in movements.OrderBy(x => x.Date).ThenBy(x => x.Kind))
        {
            running += movement.Inflow - movement.Outflow;
            movement.RunningBalance = running;
        }

        var arabicMonths = new[] { "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو",
            "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر" };
        var monthly = Enumerable.Range(1, 12).Select(month =>
        {
            var monthInvoices = invoices.Where(x => x.InvoiceDate.Month == month).ToList();
            var monthCultivation = cultivation.Where(x => x.ExpenseDate.Month == month).ToList();
            var sales = monthInvoices.Sum(x => x.GrossAmount);
            var monthCosts = monthInvoices.Sum(x => x.TotalExpenses + x.ZakatAmount) + monthCultivation.Sum(x => x.Amount);
            return new MonthlyFinanceRow
            {
                Month = month,
                MonthName = arabicMonths[month - 1],
                Sales = sales,
                Collected = monthInvoices.Sum(x => x.AmountPaid),
                Costs = monthCosts,
                NetProfit = sales - monthCosts
            };
        }).ToList();

        var farmPerformance = farms.Where(f => !f.IsDeleted).Select(farm =>
        {
            var farmInvoices = allInvoices.Where(x => x.FarmId == farm.Id && x.InvoiceDate >= from && x.InvoiceDate < to).ToList();
            var farmCultivation = allCultivation.Where(x => x.FarmId == farm.Id && x.ExpenseDate >= from && x.ExpenseDate < to).ToList();
            var sales = farmInvoices.Sum(x => x.GrossAmount);
            var farmCosts = farmInvoices.Sum(x => x.TotalExpenses + x.ZakatAmount) + farmCultivation.Sum(x => x.Amount);
            return new FarmPerformanceRow
            {
                FarmId = farm.Id,
                FarmName = farm.Name,
                Sales = sales,
                Collected = farmInvoices.Sum(x => x.AmountPaid),
                Costs = farmCosts,
                NetProfit = sales - farmCosts,
                Receivables = farmInvoices.Sum(x => x.AmountDue),
                Payables = farmCultivation.Sum(x => Math.Max(0, x.Amount - x.PaidAmount))
            };
        }).Where(x => x.Sales != 0 || x.Costs != 0 || x.Receivables != 0 || x.Payables != 0)
          .OrderByDescending(x => x.NetProfit).ToList();

        return new AccountingCenterSummary
        {
            Year = year,
            FarmName = farmId.HasValue && farmId.Value > 0
                ? farmNames.GetValueOrDefault(farmId.Value, "غير معروف") : "كل المزارع",
            GrossSales = grossSales,
            CollectedSales = collectedSales,
            CustomerReceivables = invoices.Sum(x => x.AmountDue),
            InvoiceExpenses = invoiceExpenses,
            CultivationExpenses = cultivationExpenses,
            CultivationPayables = cultivation.Sum(x => Math.Max(0, x.Amount - x.PaidAmount)),
            ZakatAccrued = zakatAccrued,
            ZakatPaid = paidZakatInvoices.Sum(x => x.ZakatAmount),
            AccountingProfit = grossSales - costs,
            CashInflow = movements.Sum(x => x.Inflow),
            CashOutflow = movements.Sum(x => x.Outflow),
            PostedInvoiceCount = invoices.Count,
            OverdueCustomerInvoiceCount = invoices.Count(x => x.AmountDue > 0 && x.PaymentDueDate.HasValue &&
                x.PaymentDueDate.Value.Date < DateTime.Today),
            OverdueCultivationDebtCount = cultivation.Count(x => x.Amount > x.PaidAmount && x.DueDate.HasValue &&
                x.DueDate.Value.Date < DateTime.Today),
            Months = monthly,
            Farms = farmId.HasValue && farmId.Value > 0
                ? farmPerformance.Where(x => x.FarmId == farmId.Value).ToList() : farmPerformance,
            RecentCashMovements = movements.OrderByDescending(x => x.Date).Take(60).ToList()
        };
    }

    public async Task<List<AppUser>> GetUsersAsync()
    {
        EnsureAdmin();
        var db = await DbAsync();
        return await db.Table<AppUser>().Where(x => !x.IsDeleted).OrderBy(x => x.FullName).ToListAsync();
    }

    public async Task SaveUserAsync(UserEditModel model)
    {
        EnsureAdmin();
        if (string.IsNullOrWhiteSpace(model.FullName)) throw new InvalidOperationException("اسم الحساب مطلوب.");
        var db = await DbAsync();
        var code = string.IsNullOrWhiteSpace(model.AccessCode) ? null : AppSession.NormalizeAccessCode(model.AccessCode);
        if (model.Id == 0 && code is null) throw new InvalidOperationException("رمز الدخول يجب أن يتكون من 6 أحرف بالضبط.");
        if (!string.IsNullOrWhiteSpace(model.AccessCode) && code is null) throw new InvalidOperationException("رمز الدخول يجب أن يتكون من 6 أحرف بالضبط.");

        if (code is not null)
        {
            var others = await db.Table<AppUser>().Where(x => !x.IsDeleted && x.Id != model.Id).ToListAsync();
            if (others.Any(x => !string.IsNullOrWhiteSpace(x.AccessCodeHash) && PasswordHasher.Verify(code, x.AccessCodeHash, x.AccessCodeSalt)))
                throw new InvalidOperationException("رمز الدخول مستخدم في حساب آخر. اختر رمزًا مختلفًا.");
        }

        if (model.Id == 0)
        {
            var (hash, salt) = PasswordHasher.HashPassword(code!);
            var user = new AppUser
            {
                FullName = model.FullName.Trim(),
                Email = $"user-{Guid.NewGuid():N}@local.awad",
                PasswordHash = hash, PasswordSalt = salt, AccessCodeHash = hash, AccessCodeSalt = salt,
                Role = model.Role,
                CanEditInvoices = model.Role == UserRole.Administrator || model.CanEditInvoices,
                CanDeleteInvoices = model.Role == UserRole.Administrator || model.CanDeleteInvoices,
                IsActive = model.IsActive
            };
            await db.InsertAsync(user);
            await AuditAsync("إنشاء مستخدم", nameof(AppUser), user.Id, null, new { user.FullName, user.Role, user.CanEditInvoices, user.CanDeleteInvoices, user.IsActive });
        }
        else
        {
            var user = await db.FindAsync<AppUser>(model.Id) ?? throw new InvalidOperationException("المستخدم غير موجود.");
            if (_session.CurrentUser?.Id == user.Id && !model.IsActive)
                throw new InvalidOperationException("لا يمكنك إيقاف حسابك الحالي.");
            var removesAdministrator = user.Role == UserRole.Administrator &&
                (model.Role != UserRole.Administrator || !model.IsActive);
            if (removesAdministrator)
            {
                var otherAdmins = await db.Table<AppUser>()
                    .Where(x => x.Id != user.Id && !x.IsDeleted && x.IsActive && x.Role == UserRole.Administrator)
                    .CountAsync();
                if (otherAdmins == 0) throw new InvalidOperationException("لا يمكن إيقاف أو تغيير دور آخر مدير نشط.");
            }
            var oldUser = JsonSerializer.Serialize(new { user.FullName, user.Role, user.CanEditInvoices, user.CanDeleteInvoices, user.IsActive });
            user.FullName = model.FullName.Trim();
            user.Role = model.Role;
            user.CanEditInvoices = model.Role == UserRole.Administrator || model.CanEditInvoices;
            user.CanDeleteInvoices = model.Role == UserRole.Administrator || model.CanDeleteInvoices;
            user.IsActive = model.IsActive;
            user.UpdatedAt = DateTime.Now;
            if (code is not null)
            {
                var (hash, salt) = PasswordHasher.HashPassword(code);
                user.AccessCodeHash = hash; user.AccessCodeSalt = salt;
                user.PasswordHash = hash; user.PasswordSalt = salt;
            }
            await db.UpdateAsync(user);
            await AuditAsync("تعديل مستخدم", nameof(AppUser), user.Id, oldUser, new { user.FullName, user.Role, user.CanEditInvoices, user.CanDeleteInvoices, user.IsActive });
        }
    }

    public async Task<List<AuditLog>> GetAuditLogsAsync(int take = 300)
    {
        EnsureAdmin();
        var db = await DbAsync();
        return await db.Table<AuditLog>().OrderByDescending(x => x.ActionDate).Take(take).ToListAsync();
    }

    public async Task<(SalesInvoice Invoice, Farm Farm, Customer? Customer, List<SalesInvoiceItem> Items,
        List<InvoiceExpense> Expenses, Dictionary<long,string> QatTypes, Dictionary<long,string> ExpenseTypes)>
        GetInvoiceDetailsAsync(long id)
    {
        var db = await DbAsync();
        var invoice = await db.FindAsync<SalesInvoice>(id) ?? throw new InvalidOperationException("الفاتورة غير موجودة.");
        var farm = await db.FindAsync<Farm>(invoice.FarmId) ?? new Farm { Name = "غير معروف" };
        var customer = invoice.CustomerId.HasValue ? await db.FindAsync<Customer>(invoice.CustomerId.Value) : null;
        var items = await db.Table<SalesInvoiceItem>().Where(x => x.InvoiceId == id && !x.IsDeleted).ToListAsync();
        var expenses = await db.Table<InvoiceExpense>().Where(x => x.InvoiceId == id && !x.IsDeleted).ToListAsync();
        var qatTypes = (await GetQatTypesAsync(false)).ToDictionary(x => x.Id, x => x.Name);
        var expenseTypes = (await GetDailyExpenseTypesAsync(false)).ToDictionary(x => x.Id, x => x.Name);
        return (invoice, farm, customer, items, expenses, qatTypes, expenseTypes);
    }
}
