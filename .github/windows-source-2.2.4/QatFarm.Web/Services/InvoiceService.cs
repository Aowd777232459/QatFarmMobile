using Microsoft.EntityFrameworkCore;
using QatFarm.Web.Data;
using QatFarm.Web.Models;

namespace QatFarm.Web.Services;

public sealed class InvoiceService(
    IDbContextFactory<ApplicationDbContext> factory,
    CurrentUserService currentUser,
    ZakatService zakatService)
{
    public async Task<List<SalesInvoice>> GetRecentAsync(
        long? farmId = null,
        DateTime? from = null,
        DateTime? to = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var query = db.SalesInvoices
            .AsNoTracking()
            .Include(x => x.Farm)
            .Include(x => x.Customer)
            .AsQueryable();

        if (farmId.HasValue && farmId > 0)
            query = query.Where(x => x.FarmId == farmId.Value);
        if (from.HasValue)
            query = query.Where(x => x.InvoiceDate >= from.Value.Date);
        if (to.HasValue)
            query = query.Where(x => x.InvoiceDate < to.Value.Date.AddDays(1));

        return await query.OrderByDescending(x => x.InvoiceDate).Take(500).ToListAsync();
    }

    public async Task<List<SalesInvoice>> GetByFarmAndYearAsync(long? farmId, int year)
    {
        if (year < 2000 || year > DateTime.Today.Year + 1)
            throw new ArgumentOutOfRangeException(nameof(year), "السنة المحددة غير صحيحة.");

        var from = new DateTime(year, 1, 1);
        var toExclusive = from.AddYears(1);

        await using var db = await factory.CreateDbContextAsync();
        var query = db.SalesInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Farm)
            .Include(x => x.Customer)
            .Where(x => !x.IsDeleted && x.InvoiceDate >= from && x.InvoiceDate < toExclusive)
            .AsQueryable();

        if (farmId.HasValue && farmId.Value > 0)
            query = query.Where(x => x.FarmId == farmId.Value);

        return await query
            .OrderByDescending(x => x.InvoiceDate)
            .ThenByDescending(x => x.Id)
            .Take(2000)
            .ToListAsync();
    }

    public async Task<SalesInvoice?> GetDetailsAsync(long id)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.SalesInvoices
            .AsNoTracking()
            .Include(x => x.Farm)
            .Include(x => x.Customer)
            .Include(x => x.Items).ThenInclude(x => x.QatType)
            .Include(x => x.Expenses).ThenInclude(x => x.ExpenseType)
            .FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task<InvoiceEditorModel?> GetEditorAsync(long id)
    {
        var invoice = await GetDetailsAsync(id);
        if (invoice is null) return null;

        return new InvoiceEditorModel
        {
            Id = invoice.Id,
            RowVersion = invoice.RowVersion,
            FarmId = invoice.FarmId,
            CustomerId = invoice.CustomerId,
            InvoiceDate = invoice.InvoiceDate,
            PaymentDueDate = invoice.PaymentDueDate,
            BuyerName = invoice.BuyerName,
            BuyerPhone = invoice.BuyerPhone,
            ZakatPercent = invoice.ZakatPercent,
            AmountPaid = invoice.AmountPaid,
            PaymentMethod = invoice.PaymentMethod,
            Notes = invoice.Notes,
            Items = invoice.Items.Select(x => new InvoiceItemEditorModel
            {
                QatTypeId = x.QatTypeId,
                Quantity = x.Quantity,
                UnitPrice = x.UnitPrice
            }).ToList(),
            Expenses = invoice.Expenses.Select(x => new InvoiceExpenseEditorModel
            {
                ExpenseTypeId = x.ExpenseTypeId,
                Amount = x.Amount,
                Notes = x.Notes
            }).ToList()
        };
    }

    public async Task<long> SaveAsync(InvoiceEditorModel model)
    {
        NormalizeDirectCashSale(model);
        ValidateModel(model);
        var actor = await currentUser.GetAsync();
        var newInvoiceNumber = model.Id.HasValue
            ? null
            : $"INV-{DateTime.Now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}";

        await using var strategyContext = await factory.CreateDbContextAsync();
        var strategy = strategyContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync();
            if (!model.Id.HasValue && !string.IsNullOrWhiteSpace(newInvoiceNumber))
            {
                var existingId = await db.SalesInvoices.AsNoTracking()
                    .Where(x => x.InvoiceNumber == newInvoiceNumber)
                    .Select(x => (long?)x.Id)
                    .FirstOrDefaultAsync();
                if (existingId.HasValue) return existingId.Value;
            }

            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                var customer = await ValidateReferencesAsync(db, model);
                SalesInvoice invoice;
                decimal? previousZakatAmount = null;
                ZakatPaymentStatus previousZakatStatus = ZakatPaymentStatus.Pending;
                DateTime? previousZakatPaidAt = null;
                string? previousZakatPaidBy = null;
                string? previousZakatReference = null;

                if (model.Id.HasValue)
                {
                    invoice = await db.SalesInvoices
                        .Include(x => x.Items)
                        .Include(x => x.Expenses)
                        .FirstOrDefaultAsync(x => x.Id == model.Id.Value)
                        ?? throw new InvalidOperationException("الفاتورة غير موجودة.");

                    if (model.RowVersion.Length > 0)
                        db.Entry(invoice).Property(x => x.RowVersion).OriginalValue = model.RowVersion;
                    if (invoice.Status == InvoiceStatus.Cancelled)
                        throw new InvalidOperationException("لا يمكن تعديل فاتورة ملغاة.");

                    var recordedPayments = await db.CustomerPayments
                        .Where(x => x.SalesInvoiceId == invoice.Id)
                        .SumAsync(x => (decimal?)x.Amount) ?? 0;
                    if (model.AmountPaid < recordedPayments)
                        throw new InvalidOperationException("لا يمكن جعل المدفوع أقل من سندات القبض المسجلة على الفاتورة.");
                    if (recordedPayments > 0 && model.CustomerId != invoice.CustomerId)
                        throw new InvalidOperationException("لا يمكن تغيير العميل بعد تسجيل دفعات على الفاتورة.");

                    previousZakatAmount = invoice.ZakatAmount;
                    previousZakatStatus = invoice.ZakatStatus;
                    previousZakatPaidAt = invoice.ZakatPaidAt;
                    previousZakatPaidBy = invoice.ZakatPaidByUserId;
                    previousZakatReference = invoice.ZakatPaymentReference;

                    foreach (var item in invoice.Items)
                    {
                        item.IsDeleted = true;
                        item.DeletedAt = DateTime.UtcNow;
                        item.UpdatedAt = DateTime.UtcNow;
                    }
                    foreach (var expense in invoice.Expenses)
                    {
                        expense.IsDeleted = true;
                        expense.DeletedAt = DateTime.UtcNow;
                        expense.UpdatedAt = DateTime.UtcNow;
                    }
                    invoice.UpdatedAt = DateTime.UtcNow;
                    invoice.UpdatedByUserId = actor.UserId;
                }
                else
                {
                    invoice = new SalesInvoice
                    {
                        InvoiceNumber = newInvoiceNumber!,
                        CreatedByUserId = actor.UserId
                    };
                    db.SalesInvoices.Add(invoice);
                }

                ApplyInvoiceValues(invoice, model, actor.UserId, customer);
                CalculateInvoiceTotals(invoice);
                if (invoice.AmountDue > 0 && customer is null)
                    throw new InvalidOperationException("أي فاتورة تحتوي مبلغًا متبقيًا يجب ربطها بعميل مسجل حتى يمكن متابعة الذمة والتحصيل.");
                await ValidateCreditLimitAsync(db, invoice, customer);

                if (invoice.ZakatAmount <= 0)
                {
                    invoice.ZakatStatus = ZakatPaymentStatus.NotApplicable;
                    invoice.ZakatPaidAt = null;
                    invoice.ZakatPaidByUserId = null;
                    invoice.ZakatPaymentReference = null;
                }
                else if (previousZakatAmount.HasValue &&
                         previousZakatStatus == ZakatPaymentStatus.Paid &&
                         previousZakatAmount.Value == invoice.ZakatAmount)
                {
                    invoice.ZakatStatus = ZakatPaymentStatus.Paid;
                    invoice.ZakatPaidAt = previousZakatPaidAt;
                    invoice.ZakatPaidByUserId = previousZakatPaidBy;
                    invoice.ZakatPaymentReference = previousZakatReference;
                }
                else
                {
                    invoice.ZakatStatus = ZakatPaymentStatus.Pending;
                    invoice.ZakatPaidAt = null;
                    invoice.ZakatPaidByUserId = null;
                    invoice.ZakatPaymentReference = null;
                }

                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw new InvalidOperationException("تم تعديل الفاتورة من مستخدم آخر. أعد فتحها ثم حاول مرة أخرى.");
                }

                db.AuditLogs.Add(new AuditLog
                {
                    UserId = actor.UserId,
                    IpAddress = actor.IpAddress,
                    Action = model.Id.HasValue ? "Update" : "Create",
                    EntityName = nameof(SalesInvoice),
                    EntityId = invoice.Id.ToString(),
                    NewValues = $"{invoice.InvoiceNumber}|Customer={invoice.CustomerId}|Gross={invoice.GrossAmount:0.00}|Net={invoice.NetAmount:0.00}|Zakat={invoice.ZakatAmount:0.00}"
                });
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
                await zakatService.RaiseChangedAsync();
                return invoice.Id;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    public async Task CancelAsync(long id)
    {
        await currentUser.EnsureAdministratorAsync();
        var actor = await currentUser.GetAsync();
        await using var db = await factory.CreateDbContextAsync();
        var invoice = await db.SalesInvoices.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("الفاتورة غير موجودة.");
        if (invoice.ZakatStatus == ZakatPaymentStatus.Paid)
            throw new InvalidOperationException("لا يمكن إلغاء فاتورة تم اعتماد دفع زكاتها.");
        if (await db.CustomerPayments.AnyAsync(x => x.SalesInvoiceId == id))
            throw new InvalidOperationException("لا يمكن إلغاء فاتورة مرتبطة بسندات قبض.");

        invoice.Status = InvoiceStatus.Cancelled;
        invoice.UpdatedAt = DateTime.UtcNow;
        invoice.UpdatedByUserId = actor.UserId;
        AddAudit(db, actor, "Cancel", invoice, invoice.InvoiceNumber);
        await db.SaveChangesAsync();
        await zakatService.RaiseChangedAsync();
    }

    public async Task DeleteAsync(long id)
    {
        await currentUser.EnsureAdministratorAsync();
        var actor = await currentUser.GetAsync();
        await using var db = await factory.CreateDbContextAsync();
        var invoice = await db.SalesInvoices
            .Include(x => x.Items)
            .Include(x => x.Expenses)
            .FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("الفاتورة غير موجودة.");

        if (invoice.ZakatStatus == ZakatPaymentStatus.Paid)
            throw new InvalidOperationException("لا يمكن حذف فاتورة تم اعتماد دفع زكاتها.");
        if (await db.CustomerPayments.AnyAsync(x => x.SalesInvoiceId == id))
            throw new InvalidOperationException("لا يمكن حذف فاتورة مرتبطة بسندات قبض. ألغِ السندات أولًا.");

        invoice.IsDeleted = true;
        invoice.DeletedAt = DateTime.UtcNow;
        invoice.UpdatedByUserId = actor.UserId;
        foreach (var item in invoice.Items)
        {
            item.IsDeleted = true;
            item.DeletedAt = DateTime.UtcNow;
            item.UpdatedByUserId = actor.UserId;
        }
        foreach (var expense in invoice.Expenses)
        {
            expense.IsDeleted = true;
            expense.DeletedAt = DateTime.UtcNow;
            expense.UpdatedByUserId = actor.UserId;
        }

        AddAudit(db, actor, "SoftDelete", invoice, invoice.InvoiceNumber);
        await db.SaveChangesAsync();
        await zakatService.RaiseChangedAsync();
    }

    private static void ValidateModel(InvoiceEditorModel model)
    {
        if (model.FarmId <= 0) throw new InvalidOperationException("يجب اختيار المزرعة.");
        if (model.ZakatPercent is < 0 or > 100) throw new InvalidOperationException("نسبة الزكاة يجب أن تكون بين 0 و100.");
        if (model.AmountPaid < 0) throw new InvalidOperationException("المبلغ المدفوع لا يمكن أن يكون سالبًا.");
        if (model.PaymentDueDate.HasValue && model.PaymentDueDate.Value.Date < model.InvoiceDate.Date)
            throw new InvalidOperationException("تاريخ استحقاق الدين لا يمكن أن يسبق تاريخ الفاتورة.");
        if (model.Items.Count == 0 || model.Items.Any(x => x.QatTypeId <= 0 || x.Quantity <= 0 || x.UnitPrice <= 0))
            throw new InvalidOperationException("أدخل صنفًا واحدًا على الأقل بكمية وسعر صحيحين.");
        if (model.Expenses.Any(x => x.ExpenseTypeId <= 0 || x.Amount <= 0))
            throw new InvalidOperationException("بيانات المصروفات غير مكتملة.");
        if (model.PaymentMethod == PaymentMethod.Credit && !model.CustomerId.HasValue)
            throw new InvalidOperationException("البيع الآجل يتطلب اختيار عميل مسجل.");
        if (model.PaymentMethod == PaymentMethod.Credit && model.AmountPaid > 0)
            throw new InvalidOperationException("عند اختيار البيع الآجل يجب أن يكون المدفوع عند إنشاء الفاتورة صفرًا. استخدم مختلط عند وجود دفعة مقدمة.");
        if (!model.CustomerId.HasValue && model.PaymentMethod != PaymentMethod.Cash)
            throw new InvalidOperationException("البيع لغير عميل مسجل يجب أن يكون نقديًا فقط.");
    }

    private static void NormalizeDirectCashSale(InvoiceEditorModel model)
    {
        if (model.CustomerId.HasValue) return;
        var gross = model.Items.Sum(x => decimal.Round(x.Quantity * x.UnitPrice, 2, MidpointRounding.AwayFromZero));
        model.PaymentMethod = PaymentMethod.Cash;
        model.AmountPaid = gross;
        model.PaymentDueDate = null;
    }

    private static async Task<Customer?> ValidateReferencesAsync(ApplicationDbContext db, InvoiceEditorModel model)
    {
        if (!await db.Farms.AnyAsync(x => x.Id == model.FarmId && x.IsActive))
            throw new InvalidOperationException("المزرعة غير موجودة أو غير نشطة.");

        Customer? customer = null;
        if (model.CustomerId.HasValue)
        {
            customer = await db.Customers.FirstOrDefaultAsync(x => x.Id == model.CustomerId.Value && x.IsActive)
                ?? throw new InvalidOperationException("العميل غير موجود أو غير نشط.");
        }

        var qatIds = model.Items.Select(x => x.QatTypeId).Distinct().ToList();
        if (await db.QatTypes.CountAsync(x => qatIds.Contains(x.Id) && x.IsActive) != qatIds.Count)
            throw new InvalidOperationException("أحد أنواع القات غير موجود أو غير نشط.");

        var expenseIds = model.Expenses.Select(x => x.ExpenseTypeId).Distinct().ToList();
        if (expenseIds.Count > 0 &&
            await db.DailyExpenseTypes.CountAsync(x => expenseIds.Contains(x.Id) && x.IsActive) != expenseIds.Count)
            throw new InvalidOperationException("أحد أنواع المصروفات غير موجود أو غير نشط.");

        return customer;
    }

    private static void ApplyInvoiceValues(
        SalesInvoice invoice,
        InvoiceEditorModel model,
        string? userId,
        Customer? customer)
    {
        invoice.FarmId = model.FarmId;
        invoice.CustomerId = customer?.Id;
        invoice.InvoiceDate = model.InvoiceDate;
        invoice.PaymentDueDate = model.PaymentDueDate;
        invoice.BuyerName = customer?.Name ?? model.BuyerName;
        invoice.BuyerPhone = customer?.Phone ?? model.BuyerPhone;
        invoice.ZakatPercent = model.ZakatPercent;
        invoice.AmountPaid = model.AmountPaid;
        invoice.PaymentMethod = model.PaymentMethod;
        invoice.Notes = model.Notes;
        invoice.Status = InvoiceStatus.Posted;
        invoice.Items = model.Items.Select(x => new SalesInvoiceItem
        {
            QatTypeId = x.QatTypeId,
            Quantity = x.Quantity,
            UnitPrice = x.UnitPrice,
            TotalPrice = decimal.Round(x.Quantity * x.UnitPrice, 2, MidpointRounding.AwayFromZero),
            CreatedByUserId = userId
        }).ToList();
        invoice.Expenses = model.Expenses.Select(x => new InvoiceExpense
        {
            ExpenseTypeId = x.ExpenseTypeId,
            Amount = decimal.Round(x.Amount, 2, MidpointRounding.AwayFromZero),
            Notes = x.Notes,
            CreatedByUserId = userId
        }).ToList();
    }

    private static void CalculateInvoiceTotals(SalesInvoice invoice)
    {
        invoice.GrossAmount = invoice.Items.Sum(x => x.TotalPrice);
        invoice.ZakatAmount = decimal.Round(invoice.GrossAmount * invoice.ZakatPercent / 100m, 2, MidpointRounding.AwayFromZero);
        invoice.TotalExpenses = invoice.Expenses.Sum(x => x.Amount);
        invoice.NetAmount = invoice.GrossAmount - invoice.ZakatAmount - invoice.TotalExpenses;
        if (invoice.AmountPaid > invoice.GrossAmount)
            throw new InvalidOperationException("المبلغ المدفوع لا يمكن أن يتجاوز إجمالي البيع.");
        invoice.AmountDue = invoice.GrossAmount - invoice.AmountPaid;
        invoice.PaymentStatus = invoice.AmountDue <= 0
            ? PaymentStatus.Paid
            : invoice.AmountPaid > 0 ? PaymentStatus.Partial : PaymentStatus.Unpaid;
    }

    private static async Task ValidateCreditLimitAsync(ApplicationDbContext db, SalesInvoice invoice, Customer? customer)
    {
        if (customer is null || customer.CreditLimit <= 0 || invoice.AmountDue <= 0) return;
        var previousDue = await db.SalesInvoices
            .Where(x => x.CustomerId == customer.Id && x.Status == InvoiceStatus.Posted && x.Id != invoice.Id)
            .SumAsync(x => (decimal?)x.AmountDue) ?? 0;
        var openingPayments = await db.CustomerPayments
            .Where(x => x.CustomerId == customer.Id && x.SalesInvoiceId == null)
            .SumAsync(x => (decimal?)x.Amount) ?? 0;
        var totalDebt = Math.Max(0, customer.OpeningBalance - openingPayments) + previousDue + invoice.AmountDue;
        if (totalDebt > customer.CreditLimit)
            throw new InvalidOperationException($"سيصبح دين العميل {totalDebt:N0} وهو أعلى من حد الائتمان {customer.CreditLimit:N0}.");
    }

    private static void AddAudit(
        ApplicationDbContext db,
        CurrentUserInfo actor,
        string action,
        SalesInvoice invoice,
        string values)
    {
        db.AuditLogs.Add(new AuditLog
        {
            UserId = actor.UserId,
            IpAddress = actor.IpAddress,
            Action = action,
            EntityName = nameof(SalesInvoice),
            EntityId = invoice.Id.ToString(),
            OldValues = values
        });
    }
}
