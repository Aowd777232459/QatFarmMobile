using Microsoft.EntityFrameworkCore;
using QatFarm.Web.Data;
using QatFarm.Web.Models;

namespace QatFarm.Web.Services;

public sealed class CultivationExpenseService(
    IDbContextFactory<ApplicationDbContext> factory,
    CurrentUserService currentUser)
{
    public async Task<CultivationExpenseOverview> GetOverviewAsync(long? farmId, int year)
    {
        ValidateYear(year);
        var from = new DateTime(year, 1, 1);
        var toExclusive = from.AddYears(1);

        await using var db = await factory.CreateDbContextAsync();

        var expensesQuery = db.CultivationExpenses
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Farm)
            .Include(x => x.ExpenseType)
            .Include(x => x.Creditor)
            .Where(x => !x.IsDeleted && x.ExpenseDate >= from && x.ExpenseDate < toExclusive);

        if (farmId.HasValue && farmId.Value > 0)
            expensesQuery = expensesQuery.Where(x => x.FarmId == farmId.Value);

        var entities = await expensesQuery
            .OrderByDescending(x => x.ExpenseDate)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        var today = DateTime.Today;
        var rows = entities.Select(x =>
        {
            var outstanding = Math.Max(0m, x.Amount - x.PaidAmount);
            return new CultivationExpenseRow(
                x.Id,
                x.ReceiptNumber,
                x.FarmId,
                x.Farm?.Name ?? "—",
                x.ExpenseTypeId,
                x.ExpenseType?.Name ?? "—",
                x.Amount,
                x.PaidAmount,
                outstanding,
                x.ExpenseDate,
                x.PaymentType,
                x.CreditorId,
                x.Creditor?.Name ?? "—",
                x.DueDate,
                x.DebtStatus,
                outstanding > 0 && x.DueDate.HasValue && x.DueDate.Value.Date < today,
                x.Notes,
                x.RowVersion);
        }).ToList();

        var creditorRows = rows
            .Where(x => x.CreditorId.HasValue)
            .GroupBy(x => new { Id = x.CreditorId!.Value, x.CreditorName })
            .Select(group => new CreditorDebtRow(
                group.Key.Id,
                group.Key.CreditorName,
                entities.FirstOrDefault(x => x.CreditorId == group.Key.Id)?.Creditor?.Phone,
                group.Sum(x => x.Amount),
                group.Sum(x => x.PaidAmount),
                group.Sum(x => x.OutstandingAmount),
                group.Count(x => x.OutstandingAmount > 0),
                group.Count(x => x.IsOverdue)))
            .OrderByDescending(x => x.Outstanding)
            .ThenBy(x => x.CreditorName)
            .ToList();

        var invoiceQuery = db.SalesInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => !x.IsDeleted &&
                        x.Status == InvoiceStatus.Posted &&
                        x.InvoiceDate >= from &&
                        x.InvoiceDate < toExclusive);

        if (farmId.HasValue && farmId.Value > 0)
            invoiceQuery = invoiceQuery.Where(x => x.FarmId == farmId.Value);

        var salesTotals = await invoiceQuery
            .GroupBy(_ => 1)
            .Select(group => new
            {
                GrossSales = group.Sum(x => x.GrossAmount),
                CollectedSales = group.Sum(x => x.AmountPaid),
                CustomerReceivables = group.Sum(x => x.AmountDue),
                InvoiceExpenses = group.Sum(x => x.TotalExpenses),
                Zakat = group.Sum(x => x.ZakatAmount),
                NetSalesBeforeCultivation = group.Sum(x => x.NetAmount)
            })
            .FirstOrDefaultAsync();

        var grossSales = salesTotals?.GrossSales ?? 0m;
        var collectedSales = salesTotals?.CollectedSales ?? 0m;
        var customerReceivables = salesTotals?.CustomerReceivables ?? 0m;
        var invoiceExpenses = salesTotals?.InvoiceExpenses ?? 0m;
        var zakat = salesTotals?.Zakat ?? 0m;
        var netSalesBeforeCultivation = salesTotals?.NetSalesBeforeCultivation ?? 0m;

        var totalExpenses = rows.Sum(x => x.Amount);
        var totalPaid = rows.Sum(x => x.PaidAmount);
        var outstandingDebt = rows.Sum(x => x.OutstandingAmount);
        var accountingProfit = netSalesBeforeCultivation - totalExpenses;

        // الربح المحاسبي يخصم كامل مصروف التربية مرة واحدة سواء سُدد أو بقي دينًا.
        // أما الربح الآمن فيعتمد على النقد المحصل فعلًا بعد حجز جميع الالتزامات،
        // ولذلك لا نتعامل مع ذمم العملاء أو ديون التربية كسيولة قابلة للتوزيع.
        var cashAfterAllReserves = collectedSales - invoiceExpenses - zakat - totalExpenses;
        var safeDistributableProfit = Math.Max(0m, Math.Min(accountingProfit, cashAfterAllReserves));

        var summary = new CultivationAnnualSummary(
            totalExpenses,
            totalPaid,
            outstandingDebt,
            grossSales,
            collectedSales,
            customerReceivables,
            netSalesBeforeCultivation,
            accountingProfit,
            cashAfterAllReserves,
            safeDistributableProfit,
            rows.Count(x => x.OutstandingAmount > 0),
            rows.Count(x => x.IsOverdue));

        return new CultivationExpenseOverview(rows, creditorRows, summary);
    }

    public async Task<List<CultivationDebtPaymentRow>> GetPaymentsAsync(long cultivationExpenseId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.CultivationDebtPayments
            .AsNoTracking()
            .Include(x => x.Creditor)
            .Where(x => x.CultivationExpenseId == cultivationExpenseId)
            .OrderByDescending(x => x.PaymentDate)
            .ThenByDescending(x => x.Id)
            .Select(x => new CultivationDebtPaymentRow(
                x.Id,
                x.CultivationExpenseId,
                x.PaymentDate,
                x.Amount,
                x.PaymentMethod,
                x.ReferenceNumber,
                x.Notes,
                x.Creditor.Name))
            .ToListAsync();
    }

    public async Task<List<Creditor>> GetCreditorsAsync(bool includeInactive = false)
    {
        await using var db = await factory.CreateDbContextAsync();
        var query = db.Creditors.AsNoTracking().AsQueryable();
        if (!includeInactive)
            query = query.Where(x => x.IsActive);
        return await query.OrderBy(x => x.Name).ToListAsync();
    }

    public async Task SaveCreditorAsync(CreditorEditorModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
            throw new InvalidOperationException("اسم الدائن مطلوب.");

        var actor = await currentUser.GetAsync();
        await using var db = await factory.CreateDbContextAsync();

        if (model.Id == 0)
        {
            if (await db.Creditors.AnyAsync(x => x.Name == model.Name.Trim() && x.Phone == model.Phone))
                throw new InvalidOperationException("يوجد دائن بنفس الاسم ورقم الهاتف.");

            var entity = new Creditor
            {
                Name = model.Name.Trim(),
                Phone = model.Phone?.Trim(),
                Address = model.Address?.Trim(),
                Notes = model.Notes?.Trim(),
                IsActive = model.IsActive,
                CreatedByUserId = actor.UserId
            };
            db.Creditors.Add(entity);
            await db.SaveChangesAsync();
            db.AuditLogs.Add(CreateAudit(actor, "Create", nameof(Creditor), entity.Id, null, entity.Name));
        }
        else
        {
            await currentUser.EnsureAdministratorAsync();
            var entity = await db.Creditors.FirstOrDefaultAsync(x => x.Id == model.Id)
                ?? throw new InvalidOperationException("الدائن غير موجود.");

            if (model.RowVersion.Length > 0)
                db.Entry(entity).Property(x => x.RowVersion).OriginalValue = model.RowVersion;

            var old = $"{entity.Name}|{entity.Phone}|{entity.IsActive}";
            entity.Name = model.Name.Trim();
            entity.Phone = model.Phone?.Trim();
            entity.Address = model.Address?.Trim();
            entity.Notes = model.Notes?.Trim();
            entity.IsActive = model.IsActive;
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedByUserId = actor.UserId;
            db.AuditLogs.Add(CreateAudit(actor, "Update", nameof(Creditor), entity.Id, old,
                $"{entity.Name}|{entity.Phone}|{entity.IsActive}"));
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("تم تعديل بيانات الدائن من مستخدم آخر. حدّث الصفحة ثم أعد المحاولة.");
        }
    }

    public async Task DeleteCreditorAsync(long id)
    {
        await currentUser.EnsureAdministratorAsync();
        var actor = await currentUser.GetAsync();
        await using var db = await factory.CreateDbContextAsync();

        var entity = await db.Creditors.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("الدائن غير موجود.");

        var hasOpenDebt = await db.CultivationExpenses.AnyAsync(x =>
            x.CreditorId == id && x.Amount > x.PaidAmount);
        if (hasOpenDebt)
            throw new InvalidOperationException("لا يمكن حذف الدائن قبل سداد جميع ديونه.");

        entity.IsDeleted = true;
        entity.IsActive = false;
        entity.DeletedAt = DateTime.UtcNow;
        entity.UpdatedByUserId = actor.UserId;
        db.AuditLogs.Add(CreateAudit(actor, "SoftDelete", nameof(Creditor), id, entity.Name, null));
        await db.SaveChangesAsync();
    }

    public async Task<long> SaveAsync(CultivationExpenseEditorModel model)
    {
        ValidateExpenseEditor(model);
        if (model.Id > 0)
            await currentUser.EnsureAdministratorAsync();

        var actor = await currentUser.GetAsync();
        await using var strategyContext = await factory.CreateDbContextAsync();
        var strategy = strategyContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync();
            await using var transaction = await db.Database.BeginTransactionAsync();

            if (!await db.Farms.AnyAsync(x => x.Id == model.FarmId && x.IsActive))
                throw new InvalidOperationException("المزرعة غير موجودة أو غير نشطة.");
            if (!await db.CultivationExpenseTypes.AnyAsync(x => x.Id == model.ExpenseTypeId && x.IsActive))
                throw new InvalidOperationException("نوع الخسارة غير موجود أو غير نشط.");

            if (RequiresCreditor(model.PaymentType) && !model.CreditorId.HasValue)
                throw new InvalidOperationException("يجب اختيار الدائن عند تسجيل خسارة آجلة أو مدفوعة جزئيًا.");
            if (model.CreditorId.HasValue &&
                !await db.Creditors.AnyAsync(x => x.Id == model.CreditorId.Value && x.IsActive))
                throw new InvalidOperationException("الدائن غير موجود أو غير نشط.");

            if (model.Id == 0)
            {
                var initialPaid = NormalizeInitialPaid(model);
                var entity = new CultivationExpense
                {
                    FarmId = model.FarmId,
                    ExpenseTypeId = model.ExpenseTypeId,
                    Amount = model.Amount,
                    ExpenseDate = model.ExpenseDate,
                    PaymentType = model.PaymentType,
                    CreditorId = model.CreditorId,
                    PaidAmount = initialPaid,
                    DueDate = initialPaid < model.Amount ? model.DueDate : null,
                    DebtStatus = CalculateDebtStatus(model.Amount, initialPaid),
                    Notes = model.Notes?.Trim(),
                    ReceiptNumber = $"EXP-{DateTime.Now:yyyyMMdd}-{Guid.NewGuid():N}"[..21].ToUpperInvariant(),
                    CreatedByUserId = actor.UserId
                };

                db.CultivationExpenses.Add(entity);
                await db.SaveChangesAsync();

                if (initialPaid > 0 && entity.CreditorId.HasValue)
                {
                    db.CultivationDebtPayments.Add(new CultivationDebtPayment
                    {
                        CultivationExpenseId = entity.Id,
                        CreditorId = entity.CreditorId.Value,
                        Amount = initialPaid,
                        PaymentDate = model.ExpenseDate,
                        PaymentMethod = model.InitialPaymentMethod,
                        ReferenceNumber = model.InitialPaymentReference?.Trim(),
                        Notes = "دفعة مسجلة عند إنشاء خسارة التربية",
                        CreatedByUserId = actor.UserId
                    });
                }

                db.AuditLogs.Add(CreateAudit(actor, "Create", nameof(CultivationExpense), entity.Id, null,
                    $"Amount={entity.Amount};Paid={entity.PaidAmount};Creditor={entity.CreditorId}"));

                await db.SaveChangesAsync();
                await transaction.CommitAsync();
                return entity.Id;
            }

            var existing = await db.CultivationExpenses
                .Include(x => x.DebtPayments)
                .FirstOrDefaultAsync(x => x.Id == model.Id)
                ?? throw new InvalidOperationException("عملية خسارة التربية غير موجودة.");

            if (model.RowVersion.Length > 0)
                db.Entry(existing).Property(x => x.RowVersion).OriginalValue = model.RowVersion;

            if (model.Amount < existing.PaidAmount)
                throw new InvalidOperationException("لا يمكن جعل مبلغ الخسارة أقل من المبلغ الذي تم سداده.");

            if (existing.DebtPayments.Any(x => !x.IsDeleted) && existing.CreditorId != model.CreditorId)
                throw new InvalidOperationException("لا يمكن تغيير الدائن بعد تسجيل دفعات. احذف الدفعات أولًا بصلاحية المدير.");

            var oldValues =
                $"Farm={existing.FarmId};Type={existing.ExpenseTypeId};Amount={existing.Amount};Paid={existing.PaidAmount};Creditor={existing.CreditorId}";

            existing.FarmId = model.FarmId;
            existing.ExpenseTypeId = model.ExpenseTypeId;
            existing.Amount = model.Amount;
            existing.ExpenseDate = model.ExpenseDate;
            existing.CreditorId = model.CreditorId;
            existing.DueDate = existing.PaidAmount < model.Amount ? model.DueDate : null;
            existing.PaymentType = CalculatePaymentType(existing.Amount, existing.PaidAmount);
            existing.DebtStatus = CalculateDebtStatus(existing.Amount, existing.PaidAmount);
            existing.Notes = model.Notes?.Trim();
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedByUserId = actor.UserId;

            if (existing.PaidAmount < existing.Amount && !existing.CreditorId.HasValue)
                throw new InvalidOperationException("الخسارة ما زالت تحتوي مبلغًا غير مسدد؛ يجب تحديد الدائن.");

            db.AuditLogs.Add(CreateAudit(actor, "Update", nameof(CultivationExpense), existing.Id, oldValues,
                $"Farm={existing.FarmId};Type={existing.ExpenseTypeId};Amount={existing.Amount};Paid={existing.PaidAmount};Creditor={existing.CreditorId}"));

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new InvalidOperationException("تم تعديل عملية الخسارة من مستخدم آخر. حدّث الصفحة ثم أعد المحاولة.");
            }

            await transaction.CommitAsync();
            return existing.Id;
        });
    }

    public async Task AddPaymentAsync(CultivationDebtPaymentEditorModel model)
    {
        await currentUser.EnsureFinancialRoleAsync();
        if (model.CultivationExpenseId <= 0 || model.Amount <= 0)
            throw new InvalidOperationException("بيانات الدفعة غير صحيحة.");
        if (model.PaymentMethod is PaymentMethod.Credit or PaymentMethod.Mixed)
            throw new InvalidOperationException("دفعة الدين يجب أن تكون نقدًا أو تحويلًا.");

        var actor = await currentUser.GetAsync();
        await using var strategyContext = await factory.CreateDbContextAsync();
        var strategy = strategyContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync();
            await using var transaction = await db.Database.BeginTransactionAsync();

            var expense = await db.CultivationExpenses
                .FirstOrDefaultAsync(x => x.Id == model.CultivationExpenseId)
                ?? throw new InvalidOperationException("عملية خسارة التربية غير موجودة.");

            if (!expense.CreditorId.HasValue)
                throw new InvalidOperationException("لا يوجد دائن مرتبط بهذه العملية.");

            var outstanding = Math.Max(0m, expense.Amount - expense.PaidAmount);
            if (outstanding <= 0)
                throw new InvalidOperationException("تم سداد هذا الدين بالكامل.");
            if (model.Amount > outstanding)
                throw new InvalidOperationException($"مبلغ الدفعة أكبر من المتبقي ({outstanding:N0} ر.ي).");

            var payment = new CultivationDebtPayment
            {
                CultivationExpenseId = expense.Id,
                CreditorId = expense.CreditorId.Value,
                Amount = model.Amount,
                PaymentDate = model.PaymentDate,
                PaymentMethod = model.PaymentMethod,
                ReferenceNumber = model.ReferenceNumber?.Trim(),
                Notes = model.Notes?.Trim(),
                CreatedByUserId = actor.UserId
            };
            db.CultivationDebtPayments.Add(payment);

            var oldPaid = expense.PaidAmount;
            expense.PaidAmount += model.Amount;
            expense.DebtStatus = CalculateDebtStatus(expense.Amount, expense.PaidAmount);
            expense.PaymentType = CalculatePaymentType(expense.Amount, expense.PaidAmount);
            expense.DueDate = expense.PaidAmount >= expense.Amount ? null : expense.DueDate;
            expense.UpdatedAt = DateTime.UtcNow;
            expense.UpdatedByUserId = actor.UserId;

            db.AuditLogs.Add(CreateAudit(actor, "DebtPayment", nameof(CultivationExpense), expense.Id,
                $"Paid={oldPaid}", $"Paid={expense.PaidAmount};Payment={model.Amount}"));

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        });
    }

    public async Task DeletePaymentAsync(long paymentId)
    {
        await currentUser.EnsureAdministratorAsync();
        var actor = await currentUser.GetAsync();
        await using var strategyContext = await factory.CreateDbContextAsync();
        var strategy = strategyContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync();
            await using var transaction = await db.Database.BeginTransactionAsync();

            var payment = await db.CultivationDebtPayments
                .Include(x => x.CultivationExpense)
                .FirstOrDefaultAsync(x => x.Id == paymentId)
                ?? throw new InvalidOperationException("الدفعة غير موجودة.");

            var expense = payment.CultivationExpense;
            expense.PaidAmount = Math.Max(0m, expense.PaidAmount - payment.Amount);
            expense.DebtStatus = CalculateDebtStatus(expense.Amount, expense.PaidAmount);
            expense.PaymentType = CalculatePaymentType(expense.Amount, expense.PaidAmount);
            expense.UpdatedAt = DateTime.UtcNow;
            expense.UpdatedByUserId = actor.UserId;

            payment.IsDeleted = true;
            payment.DeletedAt = DateTime.UtcNow;
            payment.UpdatedByUserId = actor.UserId;

            db.AuditLogs.Add(CreateAudit(actor, "SoftDeletePayment", nameof(CultivationDebtPayment), payment.Id,
                payment.Amount.ToString("0.00"), null));

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        });
    }

    public async Task DeleteAsync(long id)
    {
        await currentUser.EnsureAdministratorAsync();
        var actor = await currentUser.GetAsync();
        await using var strategyContext = await factory.CreateDbContextAsync();
        var strategy = strategyContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync();
            await using var transaction = await db.Database.BeginTransactionAsync();

            var entity = await db.CultivationExpenses
                .Include(x => x.DebtPayments)
                .FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new InvalidOperationException("عملية الخسارة غير موجودة.");

            entity.IsDeleted = true;
            entity.DeletedAt = DateTime.UtcNow;
            entity.UpdatedByUserId = actor.UserId;

            foreach (var payment in entity.DebtPayments.Where(x => !x.IsDeleted))
            {
                payment.IsDeleted = true;
                payment.DeletedAt = DateTime.UtcNow;
                payment.UpdatedByUserId = actor.UserId;
            }

            db.AuditLogs.Add(CreateAudit(actor, "SoftDelete", nameof(CultivationExpense), id,
                $"Amount={entity.Amount};Paid={entity.PaidAmount};Creditor={entity.CreditorId}", null));

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        });
    }

    private static void ValidateExpenseEditor(CultivationExpenseEditorModel model)
    {
        if (model.FarmId <= 0)
            throw new InvalidOperationException("يجب اختيار المزرعة.");
        if (model.ExpenseTypeId <= 0)
            throw new InvalidOperationException("يجب اختيار نوع الخسارة.");
        if (model.Amount <= 0)
            throw new InvalidOperationException("المبلغ يجب أن يكون أكبر من صفر.");
        if (model.InitialPaidAmount < 0 || model.InitialPaidAmount > model.Amount)
            throw new InvalidOperationException("المبلغ المدفوع يجب أن يكون بين صفر وإجمالي الخسارة.");
        if (model.PaymentType != CultivationExpensePaymentType.Credit &&
            model.InitialPaymentMethod is PaymentMethod.Credit or PaymentMethod.Mixed)
            throw new InvalidOperationException("الدفعة الفعلية يجب أن تكون نقدًا أو تحويلًا.");
    }

    private static decimal NormalizeInitialPaid(CultivationExpenseEditorModel model)
    {
        return model.PaymentType switch
        {
            CultivationExpensePaymentType.Cash => model.Amount,
            CultivationExpensePaymentType.Credit when model.InitialPaidAmount == 0 => 0m,
            CultivationExpensePaymentType.Credit => throw new InvalidOperationException("عند اختيار دين كامل يجب أن يكون المدفوع صفرًا."),
            CultivationExpensePaymentType.Partial when model.InitialPaidAmount > 0 && model.InitialPaidAmount < model.Amount
                => model.InitialPaidAmount,
            CultivationExpensePaymentType.Partial => throw new InvalidOperationException("الدفع الجزئي يجب أن يكون أكبر من صفر وأقل من إجمالي الخسارة."),
            _ => throw new InvalidOperationException("طريقة تسجيل الخسارة غير صحيحة.")
        };
    }

    private static bool RequiresCreditor(CultivationExpensePaymentType paymentType) =>
        paymentType is CultivationExpensePaymentType.Credit or CultivationExpensePaymentType.Partial;

    private static CultivationDebtStatus CalculateDebtStatus(decimal amount, decimal paid)
    {
        if (paid <= 0) return CultivationDebtStatus.Unpaid;
        if (paid < amount) return CultivationDebtStatus.Partial;
        return CultivationDebtStatus.Paid;
    }

    private static CultivationExpensePaymentType CalculatePaymentType(decimal amount, decimal paid)
    {
        if (paid <= 0) return CultivationExpensePaymentType.Credit;
        if (paid < amount) return CultivationExpensePaymentType.Partial;
        return CultivationExpensePaymentType.Cash;
    }

    private static void ValidateYear(int year)
    {
        if (year is < 2000 or > 2100)
            throw new ArgumentOutOfRangeException(nameof(year), "السنة المحددة غير صحيحة.");
    }

    private static AuditLog CreateAudit(
        CurrentUserInfo actor,
        string action,
        string entityName,
        long entityId,
        string? oldValues,
        string? newValues) => new()
    {
        UserId = actor.UserId,
        IpAddress = actor.IpAddress,
        Action = action,
        EntityName = entityName,
        EntityId = entityId.ToString(),
        OldValues = oldValues,
        NewValues = newValues
    };
}
