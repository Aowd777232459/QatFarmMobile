using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using QatFarm.Web.Data;
using QatFarm.Web.Models;

namespace QatFarm.Web.Services;

public sealed class AccountingService(
    IDbContextFactory<ApplicationDbContext> factory,
    CurrentUserService currentUser)
{
    public const string CashCode = "1101";
    public const string BankCode = "1102";
    public const string ReceivablesCode = "1201";
    public const string PayablesCode = "2101";
    public const string ZakatPayableCode = "2201";
    public const string OpeningEquityCode = "3101";
    public const string SalesRevenueCode = "4101";
    public const string CultivationExpenseCode = "5101";
    public const string SalesExpenseCode = "5201";
    public const string ZakatExpenseCode = "5301";
    public const string OtherExpenseCode = "5901";

    private sealed record PostingLine(
        string AccountCode,
        decimal Debit,
        decimal Credit,
        string? Description = null,
        long? CustomerId = null,
        long? CreditorId = null,
        long? FarmId = null);

    private sealed record DesiredPosting(
        string SourceType,
        string SourceId,
        DateTime EntryDate,
        string Description,
        long? FarmId,
        IReadOnlyList<PostingLine> Lines)
    {
        public string Hash => ComputeHash(this);
    }

    public async Task<int> SyncOperationalLedgerAsync()
    {
        await currentUser.EnsureFinancialRoleAsync();
        var actor = await currentUser.GetAsync();

        await using var strategyContext = await factory.CreateDbContextAsync();
        var strategy = strategyContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync();
            await using var tx = await db.Database.BeginTransactionAsync();

            var accounts = await db.ChartOfAccounts
                .Where(x => x.IsActive && x.AllowPosting)
                .ToDictionaryAsync(x => x.Code, x => x);
            EnsureSystemAccounts(accounts);

            var desired = await BuildDesiredPostingsAsync(db);
            var desiredKeys = desired.Select(x => (x.SourceType, x.SourceId)).ToHashSet();
            var changed = 0;

            var activeAutomatic = await db.JournalEntries
                .Include(x => x.Lines)
                .Where(x => x.IsAutomatic && x.Status == JournalEntryStatus.Posted && x.ReversesEntryId == null)
                .OrderBy(x => x.Id)
                .ToListAsync();

            foreach (var old in activeAutomatic)
            {
                var key = (old.SourceType ?? string.Empty, old.SourceId ?? string.Empty);
                if (!desiredKeys.Contains(key))
                {
                    ReverseEntry(db, old, actor.UserId, "عكس تلقائي بسبب إلغاء/حذف العملية المصدرية", old.EntryDate);
                    changed++;
                }
            }

            foreach (var posting in desired)
            {
                var existing = activeAutomatic.LastOrDefault(x =>
                    x.SourceType == posting.SourceType &&
                    x.SourceId == posting.SourceId &&
                    x.Status == JournalEntryStatus.Posted);

                if (existing is not null && existing.SourceHash == posting.Hash)
                    continue;

                if (existing is not null && existing.Status == JournalEntryStatus.Posted)
                    ReverseEntry(db, existing, actor.UserId, "عكس تلقائي قبل إعادة ترحيل العملية المعدلة", existing.EntryDate);

                AddPosting(db, posting, accounts, actor.UserId, true);
                changed++;
            }

            db.AuditLogs.Add(new AuditLog
            {
                UserId = actor.UserId,
                IpAddress = actor.IpAddress,
                Action = "AccountingSync",
                EntityName = nameof(JournalEntry),
                EntityId = "OperationalLedger",
                NewValues = $"Desired={desired.Count}|Changed={changed}"
            });

            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return changed;
        });
    }

    public async Task<long> CreateManualEntryAsync(ManualJournalEditorModel model)
    {
        await currentUser.EnsureFinancialRoleAsync();
        ValidateManualEntry(model);
        var actor = await currentUser.GetAsync();

        await using var db = await factory.CreateDbContextAsync();
        var accountIds = model.Lines.Select(x => x.AccountId).Distinct().ToList();
        var accounts = await db.ChartOfAccounts
            .Where(x => accountIds.Contains(x.Id) && x.IsActive && x.AllowPosting)
            .ToDictionaryAsync(x => x.Id);
        if (accounts.Count != accountIds.Count)
            throw new InvalidOperationException("يوجد حساب غير صالح أو موقوف في القيد.");

        var entry = new JournalEntry
        {
            EntryNumber = NewEntryNumber("MAN"),
            EntryDate = model.EntryDate,
            Description = model.Description.Trim(),
            FarmId = model.FarmId,
            Status = JournalEntryStatus.Posted,
            IsAutomatic = false,
            CreatedByUserId = actor.UserId,
            SourceType = "Manual"
        };

        foreach (var line in model.Lines.Where(x => x.Debit > 0 || x.Credit > 0))
        {
            entry.Lines.Add(new JournalEntryLine
            {
                AccountId = line.AccountId,
                Debit = line.Debit,
                Credit = line.Credit,
                Description = string.IsNullOrWhiteSpace(line.Description) ? model.Description.Trim() : line.Description.Trim(),
                FarmId = model.FarmId,
                CreatedByUserId = actor.UserId
            });
        }

        db.JournalEntries.Add(entry);
        await db.SaveChangesAsync();
        db.AuditLogs.Add(new AuditLog
        {
            UserId = actor.UserId,
            IpAddress = actor.IpAddress,
            Action = "CreateManualJournal",
            EntityName = nameof(JournalEntry),
            EntityId = entry.Id.ToString(),
            NewValues = $"{entry.EntryNumber}|Debit={entry.Lines.Sum(x => x.Debit):0.00}|Credit={entry.Lines.Sum(x => x.Credit):0.00}"
        });
        await db.SaveChangesAsync();
        return entry.Id;
    }

    public async Task ReverseManualEntryAsync(long id, string? reason = null)
    {
        await currentUser.EnsureAdministratorAsync();
        var actor = await currentUser.GetAsync();
        await using var db = await factory.CreateDbContextAsync();
        var entry = await db.JournalEntries.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("القيد غير موجود.");
        if (entry.IsAutomatic)
            throw new InvalidOperationException("القيود التلقائية تُعكس من خلال مزامنة العمليات المصدرية.");
        if (entry.ReversesEntryId.HasValue || (entry.SourceType?.EndsWith(":REV", StringComparison.Ordinal) ?? false))
            throw new InvalidOperationException("لا يمكن عكس قيد عكسي من هذه الشاشة. اعكس القيد الأصلي فقط.");
        if (entry.Status == JournalEntryStatus.Reversed)
            throw new InvalidOperationException("تم عكس هذا القيد مسبقًا.");

        ReverseEntry(db, entry, actor.UserId, string.IsNullOrWhiteSpace(reason) ? "عكس قيد يدوي" : reason.Trim());
        db.AuditLogs.Add(new AuditLog
        {
            UserId = actor.UserId,
            IpAddress = actor.IpAddress,
            Action = "ReverseManualJournal",
            EntityName = nameof(JournalEntry),
            EntityId = entry.Id.ToString(),
            OldValues = entry.EntryNumber,
            NewValues = reason
        });
        await db.SaveChangesAsync();
    }

    public async Task<List<ChartOfAccount>> GetPostingAccountsAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.ChartOfAccounts.AsNoTracking()
            .Where(x => x.IsActive && x.AllowPosting)
            .OrderBy(x => x.Code)
            .ToListAsync();
    }

    public async Task<AccountingSummary> GetSummaryAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var yearStart = new DateTime(DateTime.Today.Year, 1, 1);
        var nextYear = yearStart.AddYears(1);

        var all = await db.JournalEntryLines.AsNoTracking()
            .Where(x => x.JournalEntry.EntryDate < nextYear)
            .GroupBy(x => x.Account.Code)
            .Select(g => new { Code = g.Key, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) })
            .ToListAsync();

        decimal DebitBalance(string code) => all.Where(x => x.Code == code).Sum(x => x.Debit - x.Credit);
        decimal CreditBalance(string code) => all.Where(x => x.Code == code).Sum(x => x.Credit - x.Debit);

        var year = await db.JournalEntryLines.AsNoTracking()
            .Where(x => x.JournalEntry.EntryDate >= yearStart && x.JournalEntry.EntryDate < nextYear)
            .GroupBy(x => x.Account.Category)
            .Select(g => new { Category = g.Key, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) })
            .ToListAsync();
        var revenue = year.Where(x => x.Category == AccountCategory.Revenue).Sum(x => x.Credit - x.Debit);
        var expenses = year.Where(x => x.Category == AccountCategory.Expense).Sum(x => x.Debit - x.Credit);

        var entryTotals = await db.JournalEntries.AsNoTracking()
            .Select(x => new { Debit = x.Lines.Sum(l => l.Debit), Credit = x.Lines.Sum(l => l.Credit) })
            .ToListAsync();
        var unbalanced = entryTotals.Sum(x => Math.Abs(x.Debit - x.Credit));

        return new AccountingSummary(
            DebitBalance(CashCode),
            DebitBalance(BankCode),
            DebitBalance(ReceivablesCode),
            CreditBalance(PayablesCode),
            CreditBalance(ZakatPayableCode),
            revenue,
            expenses,
            revenue - expenses,
            await db.JournalEntries.CountAsync(x => x.Status == JournalEntryStatus.Posted),
            unbalanced);
    }

    public async Task<List<TrialBalanceRow>> GetTrialBalanceAsync(DateTime? asOf = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var end = (asOf ?? DateTime.Today).Date.AddDays(1);
        var rows = await db.ChartOfAccounts.AsNoTracking()
            .Where(a => a.IsActive)
            .Select(a => new TrialBalanceRow(
                a.Id,
                a.Code,
                a.Name,
                a.Category,
                a.JournalLines.Where(l => l.JournalEntry.EntryDate < end).Sum(l => (decimal?)l.Debit) ?? 0m,
                a.JournalLines.Where(l => l.JournalEntry.EntryDate < end).Sum(l => (decimal?)l.Credit) ?? 0m,
                (a.JournalLines.Where(l => l.JournalEntry.EntryDate < end).Sum(l => (decimal?)l.Debit) ?? 0m) -
                (a.JournalLines.Where(l => l.JournalEntry.EntryDate < end).Sum(l => (decimal?)l.Credit) ?? 0m)))
            .OrderBy(x => x.Code)
            .ToListAsync();
        return rows.Where(x => x.Debit != 0 || x.Credit != 0).ToList();
    }

    public async Task<IncomeStatementModel> GetIncomeStatementAsync(int year, long? farmId = null)
    {
        var from = new DateTime(year, 1, 1);
        var to = from.AddYears(1);
        await using var db = await factory.CreateDbContextAsync();
        var query = db.JournalEntryLines.AsNoTracking().Where(x =>
            x.JournalEntry.EntryDate >= from && x.JournalEntry.EntryDate < to);
        if (farmId.HasValue && farmId > 0)
            query = query.Where(x => x.FarmId == farmId || x.JournalEntry.FarmId == farmId);

        var rows = await query.GroupBy(x => x.Account.Code)
            .Select(g => new { Code = g.Key, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) })
            .ToListAsync();
        decimal Expense(string code) => rows.Where(x => x.Code == code).Sum(x => x.Debit - x.Credit);
        var revenue = rows.Where(x => x.Code == SalesRevenueCode).Sum(x => x.Credit - x.Debit);
        var cultivation = Expense(CultivationExpenseCode);
        var sales = Expense(SalesExpenseCode);
        var zakat = Expense(ZakatExpenseCode);
        var other = rows.Where(x => x.Code == OtherExpenseCode).Sum(x => x.Debit - x.Credit);
        var total = cultivation + sales + zakat + other;
        return new IncomeStatementModel(revenue, cultivation, sales, zakat, other, total, revenue - total);
    }

    public async Task<FinancialPositionModel> GetFinancialPositionAsync(DateTime? asOf = null, long? farmId = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var end = (asOf ?? DateTime.Today).Date.AddDays(1);
        var query = db.JournalEntryLines.AsNoTracking()
            .Where(x => x.JournalEntry.EntryDate < end);
        if (farmId.HasValue && farmId > 0)
            query = query.Where(x => x.FarmId == farmId || x.JournalEntry.FarmId == farmId);

        var rows = await query
            .GroupBy(x => new { x.Account.Category, x.Account.Code })
            .Select(g => new
            {
                g.Key.Category,
                g.Key.Code,
                Debit = g.Sum(x => x.Debit),
                Credit = g.Sum(x => x.Credit)
            })
            .ToListAsync();

        decimal DebitBalance(string code) => rows.Where(x => x.Code == code).Sum(x => x.Debit - x.Credit);
        decimal CreditBalance(string code) => rows.Where(x => x.Code == code).Sum(x => x.Credit - x.Debit);
        var totalAssets = rows.Where(x => x.Category == AccountCategory.Asset).Sum(x => x.Debit - x.Credit);
        var totalLiabilities = rows.Where(x => x.Category == AccountCategory.Liability).Sum(x => x.Credit - x.Debit);
        var postedEquity = rows.Where(x => x.Category == AccountCategory.Equity).Sum(x => x.Credit - x.Debit);
        var revenue = rows.Where(x => x.Category == AccountCategory.Revenue).Sum(x => x.Credit - x.Debit);
        var expenses = rows.Where(x => x.Category == AccountCategory.Expense).Sum(x => x.Debit - x.Credit);
        var accumulatedResult = revenue - expenses;
        var totalEquity = postedEquity + accumulatedResult;
        var cash = DebitBalance(CashCode);
        var bank = DebitBalance(BankCode);
        var receivables = DebitBalance(ReceivablesCode);
        var payables = CreditBalance(PayablesCode);
        var zakatPayable = CreditBalance(ZakatPayableCode);
        var otherAssets = totalAssets - cash - bank - receivables;
        var otherLiabilities = totalLiabilities - payables - zakatPayable;
        var liabilitiesAndEquity = totalLiabilities + totalEquity;

        return new FinancialPositionModel(
            cash,
            bank,
            receivables,
            otherAssets,
            totalAssets,
            payables,
            zakatPayable,
            otherLiabilities,
            totalLiabilities,
            postedEquity,
            accumulatedResult,
            totalEquity,
            liabilitiesAndEquity,
            totalAssets - liabilitiesAndEquity);
    }

    public async Task<List<GeneralLedgerRow>> GetGeneralLedgerAsync(
        long accountId,
        DateTime? from = null,
        DateTime? to = null,
        long? farmId = null)
    {
        if (accountId <= 0) return [];
        await using var db = await factory.CreateDbContextAsync();
        var accountExists = await db.ChartOfAccounts.AsNoTracking().AnyAsync(x => x.Id == accountId && x.IsActive);
        if (!accountExists) return [];

        var start = from?.Date;
        var endExclusive = to?.Date.AddDays(1);
        var allQuery = db.JournalEntryLines.AsNoTracking().Where(x => x.AccountId == accountId);
        if (farmId.HasValue && farmId > 0)
            allQuery = allQuery.Where(x => x.FarmId == farmId || x.JournalEntry.FarmId == farmId);

        var opening = start.HasValue
            ? (await allQuery.Where(x => x.JournalEntry.EntryDate < start.Value).SumAsync(x => (decimal?)(x.Debit - x.Credit)) ?? 0m)
            : 0m;
        if (start.HasValue)
            allQuery = allQuery.Where(x => x.JournalEntry.EntryDate >= start.Value);
        if (endExclusive.HasValue)
            allQuery = allQuery.Where(x => x.JournalEntry.EntryDate < endExclusive.Value);

        var data = await allQuery
            .OrderBy(x => x.JournalEntry.EntryDate)
            .ThenBy(x => x.JournalEntryId)
            .ThenBy(x => x.Id)
            .Select(x => new
            {
                x.JournalEntry.EntryDate,
                x.JournalEntry.EntryNumber,
                Description = x.Description ?? x.JournalEntry.Description,
                x.Debit,
                x.Credit,
                SourceType = x.JournalEntry.SourceType ?? "Manual",
                FarmName = x.Farm != null ? x.Farm.Name : (x.JournalEntry.Farm != null ? x.JournalEntry.Farm.Name : "عام")
            })
            .ToListAsync();

        var balance = opening;
        var result = new List<GeneralLedgerRow>(data.Count);
        foreach (var row in data)
        {
            balance += row.Debit - row.Credit;
            result.Add(new GeneralLedgerRow(
                row.EntryDate,
                row.EntryNumber,
                row.Description,
                row.Debit,
                row.Credit,
                balance,
                row.SourceType,
                row.FarmName));
        }
        return result;
    }

    public async Task<List<JournalEntryRow>> GetRecentEntriesAsync(int take = 100)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.JournalEntries.AsNoTracking()
            .OrderByDescending(x => x.EntryDate).ThenByDescending(x => x.Id)
            .Take(Math.Clamp(take, 1, 500))
            .Select(x => new JournalEntryRow(
                x.Id,
                x.EntryNumber,
                x.EntryDate,
                x.Description,
                x.SourceType ?? "يدوي",
                x.Farm != null ? x.Farm.Name : "عام",
                x.Lines.Sum(l => l.Debit),
                x.Lines.Sum(l => l.Credit),
                x.Status,
                x.IsAutomatic))
            .ToListAsync();
    }

    private static async Task<List<DesiredPosting>> BuildDesiredPostingsAsync(ApplicationDbContext db)
    {
        var result = new List<DesiredPosting>();

        var customers = await db.Customers.AsNoTracking().Where(x => x.OpeningBalance > 0).ToListAsync();
        foreach (var c in customers)
        {
            result.Add(new DesiredPosting("CustomerOpeningBalance", c.Id.ToString(), c.CreatedAt,
                $"رصيد افتتاحي للعميل: {c.Name}", null,
                [
                    new(ReceivablesCode, c.OpeningBalance, 0, "رصيد افتتاحي عميل", CustomerId: c.Id),
                    new(OpeningEquityCode, 0, c.OpeningBalance, "مقابل الرصيد الافتتاحي")
                ]));
        }

        var invoices = await db.SalesInvoices.AsNoTracking()
            .Include(x => x.CustomerPayments)
            .Where(x => x.Status == InvoiceStatus.Posted)
            .ToListAsync();
        foreach (var invoice in invoices)
        {
            var laterPayments = invoice.CustomerPayments.Where(x => !x.IsDeleted).Sum(x => x.Amount);
            var paidAtSale = Math.Max(0m, invoice.AmountPaid - laterPayments);
            paidAtSale = Math.Min(paidAtSale, invoice.GrossAmount);
            var creditAtSale = Math.Max(0m, invoice.GrossAmount - paidAtSale);
            var lines = new List<PostingLine>();
            if (paidAtSale > 0)
                lines.Add(new(CashAccountFor(invoice.PaymentMethod), paidAtSale, 0, "المقبوض عند البيع", CustomerId: invoice.CustomerId, FarmId: invoice.FarmId));
            if (creditAtSale > 0)
                lines.Add(new(ReceivablesCode, creditAtSale, 0, "مبيعات آجلة", CustomerId: invoice.CustomerId, FarmId: invoice.FarmId));
            lines.Add(new(SalesRevenueCode, 0, invoice.GrossAmount, "إيراد بيع القات", CustomerId: invoice.CustomerId, FarmId: invoice.FarmId));
            if (invoice.TotalExpenses > 0)
            {
                lines.Add(new(SalesExpenseCode, invoice.TotalExpenses, 0, "مصروفات مرتبطة بالفاتورة", FarmId: invoice.FarmId));
                lines.Add(new(CashCode, 0, invoice.TotalExpenses, "سداد مصروفات الفاتورة", FarmId: invoice.FarmId));
            }
            if (invoice.ZakatAmount > 0)
            {
                lines.Add(new(ZakatExpenseCode, invoice.ZakatAmount, 0, "إثبات مصروف الزكاة", FarmId: invoice.FarmId));
                lines.Add(new(ZakatPayableCode, 0, invoice.ZakatAmount, "زكاة مستحقة", FarmId: invoice.FarmId));
            }
            result.Add(new DesiredPosting("SalesInvoice", invoice.Id.ToString(), invoice.InvoiceDate,
                $"فاتورة بيع {invoice.InvoiceNumber}", invoice.FarmId, lines));

            if (invoice.ZakatStatus == ZakatPaymentStatus.Paid && invoice.ZakatAmount > 0)
            {
                result.Add(new DesiredPosting("ZakatPayment", invoice.Id.ToString(), invoice.ZakatPaidAt ?? invoice.InvoiceDate,
                    $"سداد زكاة الفاتورة {invoice.InvoiceNumber}", invoice.FarmId,
                    [
                        new(ZakatPayableCode, invoice.ZakatAmount, 0, "تسوية الزكاة المستحقة", FarmId: invoice.FarmId),
                        new(CashCode, 0, invoice.ZakatAmount, "دفع الزكاة", FarmId: invoice.FarmId)
                    ]));
            }
        }

        var customerPayments = await db.CustomerPayments.AsNoTracking()
            .Include(x => x.SalesInvoice)
            .ToListAsync();
        foreach (var p in customerPayments)
        {
            var paymentFarmId = p.SalesInvoice?.FarmId;
            result.Add(new DesiredPosting("CustomerPayment", p.Id.ToString(), p.PaymentDate,
                p.SalesInvoiceId.HasValue ? $"سند قبض من عميل للفاتورة #{p.SalesInvoiceId}" : "سداد رصيد افتتاحي من عميل",
                paymentFarmId,
                [
                    new(CashAccountFor(p.PaymentMethod), p.Amount, 0, "قبض من العميل", CustomerId: p.CustomerId, FarmId: paymentFarmId),
                    new(ReceivablesCode, 0, p.Amount, "تخفيض حساب العميل", CustomerId: p.CustomerId, FarmId: paymentFarmId)
                ]));
        }

        var cultivation = await db.CultivationExpenses.AsNoTracking().ToListAsync();
        foreach (var e in cultivation)
        {
            var lines = new List<PostingLine>
            {
                new(CultivationExpenseCode, e.Amount, 0, "خسارة/تكلفة تربية", FarmId: e.FarmId)
            };
            if (e.CreditorId.HasValue)
                lines.Add(new(PayablesCode, 0, e.Amount, "إثبات دين دائن", CreditorId: e.CreditorId, FarmId: e.FarmId));
            else
                lines.Add(new(CashCode, 0, e.Amount, "سداد خسارة التربية نقدًا", FarmId: e.FarmId));

            result.Add(new DesiredPosting("CultivationExpense", e.Id.ToString(), e.ExpenseDate,
                $"خسارة تربية {e.ReceiptNumber}", e.FarmId, lines));
        }

        var cultivationPayments = await db.CultivationDebtPayments.AsNoTracking()
            .Include(x => x.CultivationExpense)
            .ToListAsync();
        foreach (var p in cultivationPayments)
        {
            result.Add(new DesiredPosting("CultivationDebtPayment", p.Id.ToString(), p.PaymentDate,
                $"سداد لدائن خسارة التربية {p.CultivationExpense.ReceiptNumber}", p.CultivationExpense.FarmId,
                [
                    new(PayablesCode, p.Amount, 0, "تخفيض حساب الدائن", CreditorId: p.CreditorId, FarmId: p.CultivationExpense.FarmId),
                    new(CashAccountFor(p.PaymentMethod), 0, p.Amount, "سداد للدائن", CreditorId: p.CreditorId, FarmId: p.CultivationExpense.FarmId)
                ]));
        }

        return result.Where(x => x.Lines.Sum(l => l.Debit) > 0).ToList();
    }

    private static void AddPosting(
        ApplicationDbContext db,
        DesiredPosting posting,
        IReadOnlyDictionary<string, ChartOfAccount> accounts,
        string? actorUserId,
        bool automatic)
    {
        var debit = posting.Lines.Sum(x => x.Debit);
        var credit = posting.Lines.Sum(x => x.Credit);
        if (decimal.Round(debit, 2) != decimal.Round(credit, 2))
            throw new InvalidOperationException($"القيد الناتج من {posting.SourceType}/{posting.SourceId} غير متوازن: مدين {debit:0.00} ودائن {credit:0.00}.");

        var entry = new JournalEntry
        {
            EntryNumber = NewEntryNumber(automatic ? "AUTO" : "JV"),
            EntryDate = posting.EntryDate,
            Description = posting.Description,
            SourceType = posting.SourceType,
            SourceId = posting.SourceId,
            SourceHash = posting.Hash,
            IsAutomatic = automatic,
            Status = JournalEntryStatus.Posted,
            FarmId = posting.FarmId,
            CreatedByUserId = actorUserId
        };
        foreach (var line in posting.Lines.Where(x => x.Debit != 0 || x.Credit != 0))
        {
            if (!accounts.TryGetValue(line.AccountCode, out var account))
                throw new InvalidOperationException($"الحساب النظامي {line.AccountCode} غير موجود.");
            if (line.Debit < 0 || line.Credit < 0 || (line.Debit > 0 && line.Credit > 0))
                throw new InvalidOperationException("سطر القيد يجب أن يكون مدينًا أو دائنًا فقط وبقيمة موجبة.");
            entry.Lines.Add(new JournalEntryLine
            {
                AccountId = account.Id,
                Debit = line.Debit,
                Credit = line.Credit,
                Description = line.Description,
                CustomerId = line.CustomerId,
                CreditorId = line.CreditorId,
                FarmId = line.FarmId ?? posting.FarmId,
                CreatedByUserId = actorUserId
            });
        }
        db.JournalEntries.Add(entry);
    }

    private static void ReverseEntry(ApplicationDbContext db, JournalEntry original, string? actorUserId, string reason, DateTime? reversalDate = null)
    {
        if (original.Status == JournalEntryStatus.Reversed) return;
        original.Status = JournalEntryStatus.Reversed;
        original.UpdatedAt = DateTime.UtcNow;
        original.UpdatedByUserId = actorUserId;

        var reversal = new JournalEntry
        {
            EntryNumber = NewEntryNumber("REV"),
            EntryDate = reversalDate ?? DateTime.Now,
            Description = $"{reason}: {original.Description}",
            SourceType = $"{original.SourceType}:REV",
            SourceId = original.Id.ToString(),
            SourceHash = original.SourceHash,
            Status = JournalEntryStatus.Posted,
            IsAutomatic = original.IsAutomatic,
            FarmId = original.FarmId,
            ReversesEntry = original,
            CreatedByUserId = actorUserId
        };
        foreach (var line in original.Lines.Where(x => !x.IsDeleted))
        {
            reversal.Lines.Add(new JournalEntryLine
            {
                AccountId = line.AccountId,
                Debit = line.Credit,
                Credit = line.Debit,
                Description = $"عكس: {line.Description}",
                CustomerId = line.CustomerId,
                CreditorId = line.CreditorId,
                FarmId = line.FarmId,
                CreatedByUserId = actorUserId
            });
        }
        db.JournalEntries.Add(reversal);
    }

    private static void ValidateManualEntry(ManualJournalEditorModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Description))
            throw new InvalidOperationException("وصف القيد مطلوب.");
        var lines = model.Lines.Where(x => x.Debit > 0 || x.Credit > 0).ToList();
        if (lines.Count < 2)
            throw new InvalidOperationException("القيد يجب أن يحتوي على سطرين محاسبيين على الأقل.");
        if (lines.Any(x => x.AccountId <= 0 || x.Debit < 0 || x.Credit < 0 || (x.Debit > 0 && x.Credit > 0)))
            throw new InvalidOperationException("كل سطر يجب أن يحتوي حسابًا، وأن يكون مدينًا أو دائنًا فقط.");
        var debit = lines.Sum(x => x.Debit);
        var credit = lines.Sum(x => x.Credit);
        if (debit <= 0 || decimal.Round(debit, 2) != decimal.Round(credit, 2))
            throw new InvalidOperationException($"القيد غير متوازن. المدين {debit:N2}، الدائن {credit:N2}.");
    }

    private static string CashAccountFor(PaymentMethod method) => method == PaymentMethod.Transfer ? BankCode : CashCode;

    private static void EnsureSystemAccounts(IReadOnlyDictionary<string, ChartOfAccount> accounts)
    {
        var required = new[]
        {
            CashCode, BankCode, ReceivablesCode, PayablesCode, ZakatPayableCode,
            OpeningEquityCode, SalesRevenueCode, CultivationExpenseCode,
            SalesExpenseCode, ZakatExpenseCode, OtherExpenseCode
        };
        var missing = required.Where(x => !accounts.ContainsKey(x)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"دليل الحسابات غير مكتمل. الحسابات المفقودة: {string.Join(", ", missing)}");
    }

    private static string NewEntryNumber(string prefix) => $"{prefix}-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

    private static string ComputeHash(DesiredPosting posting)
    {
        var text = new StringBuilder()
            .Append(posting.SourceType).Append('|').Append(posting.SourceId).Append('|')
            .Append(posting.EntryDate.Ticks).Append('|').Append(posting.Description).Append('|')
            .Append(posting.FarmId).Append('|');
        foreach (var l in posting.Lines)
            text.Append(l.AccountCode).Append(':').Append(l.Debit).Append(':').Append(l.Credit).Append(':')
                .Append(l.CustomerId).Append(':').Append(l.CreditorId).Append(':').Append(l.FarmId).Append(':')
                .Append(l.Description).Append(';');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
