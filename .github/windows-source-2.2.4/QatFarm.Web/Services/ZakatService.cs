using Microsoft.EntityFrameworkCore;
using QatFarm.Web.Data;
using QatFarm.Web.Models;

namespace QatFarm.Web.Services;

public sealed class ZakatService(
    IDbContextFactory<ApplicationDbContext> factory,
    CurrentUserService currentUser)
{
    public event Func<Task>? Changed;

    public async Task<ZakatNotificationSummary> GetSummaryAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var query = db.SalesInvoices.AsNoTracking().Where(x =>
            x.Status == InvoiceStatus.Posted &&
            x.ZakatAmount > 0 &&
            x.ZakatStatus == ZakatPaymentStatus.Pending);

        return new ZakatNotificationSummary(
            await query.CountAsync(),
            await query.SumAsync(x => (decimal?)x.ZakatAmount) ?? 0,
            await query.MinAsync(x => (DateTime?)x.InvoiceDate));
    }

    public async Task<List<ZakatPendingRow>> GetPendingAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var today = DateTime.Today;
        return await db.SalesInvoices.AsNoTracking()
            .Where(x => x.Status == InvoiceStatus.Posted &&
                        x.ZakatAmount > 0 &&
                        x.ZakatStatus == ZakatPaymentStatus.Pending)
            .OrderBy(x => x.InvoiceDate)
            .Select(x => new ZakatPendingRow(
                x.Id,
                x.InvoiceNumber,
                x.Farm.Name,
                x.Customer != null ? x.Customer.Name : (x.BuyerName ?? "—"),
                x.InvoiceDate,
                x.GrossAmount,
                x.ZakatAmount,
                EF.Functions.DateDiffDay(x.InvoiceDate, today)))
            .ToListAsync();
    }

    public async Task ConfirmPaidAsync(long invoiceId, string? reference)
    {
        await currentUser.EnsureFinancialRoleAsync();
        var actor = await currentUser.GetAsync();
        await using var db = await factory.CreateDbContextAsync();
        var invoice = await db.SalesInvoices.FirstOrDefaultAsync(x => x.Id == invoiceId)
            ?? throw new InvalidOperationException("الفاتورة غير موجودة.");
        if (invoice.Status != InvoiceStatus.Posted)
            throw new InvalidOperationException("لا يمكن اعتماد زكاة فاتورة غير مرحلة.");
        if (invoice.ZakatAmount <= 0)
            throw new InvalidOperationException("لا توجد زكاة مستحقة على هذه الفاتورة.");

        invoice.ZakatStatus = ZakatPaymentStatus.Paid;
        invoice.ZakatPaidAt = DateTime.UtcNow;
        invoice.ZakatPaidByUserId = actor.UserId;
        invoice.ZakatPaymentReference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        invoice.UpdatedAt = DateTime.UtcNow;
        invoice.UpdatedByUserId = actor.UserId;

        db.AuditLogs.Add(new AuditLog
        {
            UserId = actor.UserId,
            IpAddress = actor.IpAddress,
            Action = "ConfirmZakatPaid",
            EntityName = nameof(SalesInvoice),
            EntityId = invoice.Id.ToString(),
            NewValues = $"Zakat={invoice.ZakatAmount:0.00}|Reference={invoice.ZakatPaymentReference}"
        });
        await db.SaveChangesAsync();
        await RaiseChangedAsync();
    }

    public async Task RaiseChangedAsync()
    {
        if (Changed is null) return;
        foreach (var handler in Changed.GetInvocationList().Cast<Func<Task>>())
            await handler();
    }
}
