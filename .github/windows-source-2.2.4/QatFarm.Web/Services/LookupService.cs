using Microsoft.EntityFrameworkCore;
using QatFarm.Web.Data;
using QatFarm.Web.Models;

namespace QatFarm.Web.Services;

public sealed class LookupService(IDbContextFactory<ApplicationDbContext> factory)
{
    public async Task AddQatTypeAsync(string name)
    {
        var value = RequireName(name);
        await using var db = await factory.CreateDbContextAsync();
        if (await db.QatTypes.IgnoreQueryFilters().AnyAsync(x => x.Name == value))
            throw new InvalidOperationException("نوع القات موجود مسبقًا.");
        db.QatTypes.Add(new QatType { Name = value, IsActive = true });
        await db.SaveChangesAsync();
    }

    public async Task AddDailyExpenseTypeAsync(string name)
    {
        var value = RequireName(name);
        await using var db = await factory.CreateDbContextAsync();
        if (await db.DailyExpenseTypes.IgnoreQueryFilters().AnyAsync(x => x.Name == value))
            throw new InvalidOperationException("نوع مصروف الفاتورة موجود مسبقًا.");
        db.DailyExpenseTypes.Add(new DailyExpenseType { Name = value, IsActive = true });
        await db.SaveChangesAsync();
    }

    public async Task AddCultivationExpenseTypeAsync(string name)
    {
        var value = RequireName(name);
        await using var db = await factory.CreateDbContextAsync();
        if (await db.CultivationExpenseTypes.IgnoreQueryFilters().AnyAsync(x => x.Name == value))
            throw new InvalidOperationException("نوع خسارة التربية موجود مسبقًا.");
        db.CultivationExpenseTypes.Add(new CultivationExpenseType { Name = value, IsActive = true });
        await db.SaveChangesAsync();
    }

    private static string RequireName(string name)
    {
        var value = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("اكتب اسم النوع أولًا.");
        if (value.Length > 100) throw new InvalidOperationException("اسم النوع طويل جدًا.");
        return value;
    }

    public async Task<List<LookupItem>> FarmsAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Farms.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new LookupItem(x.Id, x.Name)).ToListAsync();
    }

    public async Task<List<LookupItem>> FarmsForReportsAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Farms
            .IgnoreQueryFilters()
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new LookupItem(x.Id, x.Name))
            .ToListAsync();
    }

    public async Task<List<int>> ReportingYearsAsync()
    {
        await using var db = await factory.CreateDbContextAsync();

        var invoiceYears = await db.SalesInvoices
            .AsNoTracking()
            .Select(x => x.InvoiceDate.Year)
            .Distinct()
            .ToListAsync();

        var cultivationYears = await db.CultivationExpenses
            .AsNoTracking()
            .Select(x => x.ExpenseDate.Year)
            .Distinct()
            .ToListAsync();

        return invoiceYears
            .Concat(cultivationYears)
            .Append(DateTime.Today.Year)
            .Where(x => x >= 2000 && x <= DateTime.Today.Year + 1)
            .Distinct()
            .OrderByDescending(x => x)
            .ToList();
    }

    public async Task<List<LookupItem>> CustomersAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Customers.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new LookupItem(x.Id, x.Name + (x.Phone == null ? "" : " — " + x.Phone))).ToListAsync();
    }

    public async Task<List<LookupItem>> CreditorsAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Creditors.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new LookupItem(x.Id, x.Name + (x.Phone == null ? "" : " — " + x.Phone)))
            .ToListAsync();
    }

    public async Task<List<LookupItem>> QatTypesAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.QatTypes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new LookupItem(x.Id, x.Name)).ToListAsync();
    }

    public async Task<List<LookupItem>> CultivationExpenseTypesAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.CultivationExpenseTypes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new LookupItem(x.Id, x.Name)).ToListAsync();
    }

    public async Task<List<LookupItem>> DailyExpenseTypesAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.DailyExpenseTypes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new LookupItem(x.Id, x.Name)).ToListAsync();
    }
}
