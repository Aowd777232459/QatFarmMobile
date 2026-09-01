using Microsoft.EntityFrameworkCore;
using QatFarm.Web.Data;
using QatFarm.Web.Models;

namespace QatFarm.Web.Services;

public sealed class DashboardService(IDbContextFactory<ApplicationDbContext> factory)
{
    public async Task<DashboardSummary> GetSummaryAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var todayStart = DateTime.Today;
        var todayEnd = todayStart.AddDays(1);
        var monthStart = new DateTime(todayStart.Year, todayStart.Month, 1);
        var nextMonth = monthStart.AddMonths(1);
        var previousMonth = monthStart.AddMonths(-1);
        var yearStart = new DateTime(todayStart.Year, 1, 1);
        var nextYear = yearStart.AddYears(1);

        var posted = db.SalesInvoices
            .AsNoTracking()
            .Where(x => x.Status == InvoiceStatus.Posted);

        var today = posted.Where(x => x.InvoiceDate >= todayStart && x.InvoiceDate < todayEnd);
        var month = posted.Where(x => x.InvoiceDate >= monthStart && x.InvoiceDate < nextMonth);
        var previous = posted.Where(x => x.InvoiceDate >= previousMonth && x.InvoiceDate < monthStart);
        var year = posted.Where(x => x.InvoiceDate >= yearStart && x.InvoiceDate < nextYear);

        var monthSales = await month.SumAsync(x => (decimal?)x.GrossAmount) ?? 0m;
        var previousSales = await previous.SumAsync(x => (decimal?)x.GrossAmount) ?? 0m;
        var growth = previousSales == 0m
            ? (monthSales > 0m ? 100m : 0m)
            : decimal.Round((monthSales - previousSales) / previousSales * 100m, 1);

        var topFarm = await month
            .GroupBy(x => x.Farm.Name)
            .Select(group => new { Name = group.Key, Value = group.Sum(x => x.GrossAmount) })
            .OrderByDescending(x => x.Value)
            .Select(x => x.Name)
            .FirstOrDefaultAsync() ?? "لا توجد مبيعات";

        var topQatType = await db.SalesInvoiceItems.AsNoTracking()
            .Where(x => x.Invoice.Status == InvoiceStatus.Posted &&
                        x.Invoice.InvoiceDate >= monthStart &&
                        x.Invoice.InvoiceDate < nextMonth)
            .GroupBy(x => x.QatType.Name)
            .Select(group => new { Name = group.Key, Quantity = group.Sum(x => x.Quantity) })
            .OrderByDescending(x => x.Quantity)
            .Select(x => x.Name)
            .FirstOrDefaultAsync() ?? "لا توجد أصناف";

        var yearTotals = await year
            .GroupBy(_ => 1)
            .Select(group => new
            {
                GrossSales = group.Sum(x => x.GrossAmount),
                CollectedSales = group.Sum(x => x.AmountPaid),
                InvoiceExpenses = group.Sum(x => x.TotalExpenses),
                Zakat = group.Sum(x => x.ZakatAmount),
                Net = group.Sum(x => x.NetAmount)
            })
            .FirstOrDefaultAsync();

        var cultivationQuery = db.CultivationExpenses
            .AsNoTracking()
            .Where(x => x.ExpenseDate >= yearStart && x.ExpenseDate < nextYear);

        var cultivationTotals = await cultivationQuery
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Total = group.Sum(x => x.Amount),
                Paid = group.Sum(x => x.PaidAmount),
                Debt = group.Sum(x => x.Amount > x.PaidAmount ? x.Amount - x.PaidAmount : 0m),
                Overdue = group.Sum(x =>
                    x.Amount > x.PaidAmount && x.DueDate.HasValue && x.DueDate.Value < todayStart
                        ? x.Amount - x.PaidAmount
                        : 0m)
            })
            .FirstOrDefaultAsync();

        var yearSales = yearTotals?.GrossSales ?? 0m;
        var yearCollectedSales = yearTotals?.CollectedSales ?? 0m;
        var yearInvoiceExpenses = yearTotals?.InvoiceExpenses ?? 0m;
        var yearZakat = yearTotals?.Zakat ?? 0m;
        var yearNetBeforeCultivation = yearTotals?.Net ?? 0m;
        var cultivationLosses = cultivationTotals?.Total ?? 0m;
        var cultivationPaid = cultivationTotals?.Paid ?? 0m;
        var cultivationDebt = cultivationTotals?.Debt ?? 0m;
        var cultivationOverdueDebt = cultivationTotals?.Overdue ?? 0m;

        var accountingProfitYear = yearNetBeforeCultivation - cultivationLosses;
        var cashAfterAllReserves = yearCollectedSales - yearInvoiceExpenses - yearZakat - cultivationLosses;
        var safeDistributableProfitYear = Math.Max(0m, Math.Min(accountingProfitYear, cashAfterAllReserves));

        return new DashboardSummary(
            await db.Farms.CountAsync(x => x.IsActive),
            await db.Customers.CountAsync(x => x.IsActive),
            await today.SumAsync(x => (decimal?)x.GrossAmount) ?? 0m,
            await today.SumAsync(x => (decimal?)x.TotalExpenses) ?? 0m,
            await today.SumAsync(x => (decimal?)x.ZakatAmount) ?? 0m,
            await today.SumAsync(x => (decimal?)x.NetAmount) ?? 0m,
            monthSales,
            await month.SumAsync(x => (decimal?)x.NetAmount) ?? 0m,
            yearSales,
            yearCollectedSales,
            cultivationLosses,
            cultivationPaid,
            cultivationDebt,
            cultivationOverdueDebt,
            accountingProfitYear,
            safeDistributableProfitYear,
            await today.CountAsync(),
            await posted.SumAsync(x => (decimal?)x.AmountDue) ?? 0m,
            await posted
                .Where(x => x.AmountDue > 0m && x.PaymentDueDate != null && x.PaymentDueDate < todayStart)
                .SumAsync(x => (decimal?)x.AmountDue) ?? 0m,
            await posted
                .Where(x => x.ZakatStatus == ZakatPaymentStatus.Pending)
                .SumAsync(x => (decimal?)x.ZakatAmount) ?? 0m,
            await posted.CountAsync(x =>
                x.ZakatStatus == ZakatPaymentStatus.Pending && x.ZakatAmount > 0m),
            growth,
            topFarm,
            topQatType);
    }

    public async Task<List<MonthlyPoint>> GetLastSixMonthsAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        List<MonthlyPoint> result = [];
        var first = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-5);

        for (var i = 0; i < 6; i++)
        {
            var from = first.AddMonths(i);
            var to = from.AddMonths(1);
            var query = db.SalesInvoices.AsNoTracking().Where(x =>
                x.InvoiceDate >= from &&
                x.InvoiceDate < to &&
                x.Status == InvoiceStatus.Posted);

            var sales = await query.SumAsync(x => (decimal?)x.GrossAmount) ?? 0m;
            var expenses = await query.SumAsync(x => (decimal?)x.TotalExpenses) ?? 0m;
            var net = await query.SumAsync(x => (decimal?)x.NetAmount) ?? 0m;
            result.Add(new MonthlyPoint(from.ToString("yyyy/MM"), sales, expenses, net));
        }

        return result;
    }
}
