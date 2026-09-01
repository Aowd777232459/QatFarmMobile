using Microsoft.EntityFrameworkCore;
using QatFarm.Web.Data;
using QatFarm.Web.Models;

namespace QatFarm.Web.Services;

public sealed class CustomerService(
    IDbContextFactory<ApplicationDbContext> factory,
    CurrentUserService currentUser)
{
    public async Task<List<CustomerListRow>> GetAllAsync(string? search = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var customers = db.Customers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim();
            customers = customers.Where(x =>
                x.Name.Contains(value) ||
                (x.Phone != null && x.Phone.Contains(value)) ||
                (x.Region != null && x.Region.Contains(value)));
        }

        var rows = await customers
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Phone,
                x.Region,
                x.OpeningBalance,
                x.CreditLimit,
                x.IsActive,
                x.RowVersion,
                TotalPurchases = x.Invoices
                    .Where(i => i.Status == InvoiceStatus.Posted)
                    .Sum(i => (decimal?)i.GrossAmount) ?? 0,
                InvoicePaid = x.Invoices
                    .Where(i => i.Status == InvoiceStatus.Posted)
                    .Sum(i => (decimal?)i.AmountPaid) ?? 0,
                InvoiceDue = x.Invoices
                    .Where(i => i.Status == InvoiceStatus.Posted)
                    .Sum(i => (decimal?)i.AmountDue) ?? 0,
                OpeningPayments = x.Payments
                    .Where(p => p.SalesInvoiceId == null)
                    .Sum(p => (decimal?)p.Amount) ?? 0,
                LastPaymentDate = x.Payments.Max(p => (DateTime?)p.PaymentDate)
            })
            .ToListAsync();

        return rows.Select(x => new CustomerListRow(
            x.Id,
            x.Name,
            x.Phone,
            x.Region,
            x.TotalPurchases,
            x.InvoicePaid + x.OpeningPayments,
            Math.Max(0, x.OpeningBalance + x.InvoiceDue - x.OpeningPayments),
            x.CreditLimit,
            x.IsActive,
            x.LastPaymentDate,
            x.RowVersion)).ToList();
    }

    public async Task<CustomerDetailsModel?> GetDetailsAsync(long id)
    {
        await using var db = await factory.CreateDbContextAsync();
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (customer is null) return null;

        var invoices = await db.SalesInvoices.AsNoTracking()
            .Where(x => x.CustomerId == id)
            .OrderByDescending(x => x.InvoiceDate)
            .Select(x => new CustomerInvoiceRow(
                x.Id,
                x.InvoiceNumber,
                x.InvoiceDate,
                x.PaymentDueDate,
                x.GrossAmount,
                x.AmountPaid,
                x.AmountDue,
                x.PaymentStatus,
                x.Status))
            .ToListAsync();

        var payments = await db.CustomerPayments.AsNoTracking()
            .Where(x => x.CustomerId == id)
            .OrderByDescending(x => x.PaymentDate)
            .Select(x => new CustomerPaymentRow(
                x.Id,
                x.SalesInvoiceId,
                x.SalesInvoice != null ? x.SalesInvoice.InvoiceNumber : null,
                x.PaymentDate,
                x.Amount,
                x.PaymentMethod,
                x.ReferenceNumber,
                x.Notes))
            .ToListAsync();

        var posted = invoices.Where(x => x.Status == InvoiceStatus.Posted).ToList();
        var openingPayments = payments.Where(x => x.InvoiceId is null).Sum(x => x.Amount);
        var totalPurchases = posted.Sum(x => x.GrossAmount);
        var totalPaid = posted.Sum(x => x.AmountPaid) + openingPayments;
        var outstanding = Math.Max(0, customer.OpeningBalance + posted.Sum(x => x.AmountDue) - openingPayments);
        var overdue = posted
            .Where(x => x.AmountDue > 0 && x.PaymentDueDate.HasValue && x.PaymentDueDate.Value.Date < DateTime.Today)
            .Sum(x => x.AmountDue);

        return new CustomerDetailsModel(customer, totalPurchases, totalPaid, outstanding, overdue, invoices, payments);
    }

    public async Task SaveAsync(CustomerEditorModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
            throw new InvalidOperationException("اسم العميل مطلوب.");

        var actor = await currentUser.GetAsync();
        await using var db = await factory.CreateDbContextAsync();

        Customer entity;
        if (model.Id > 0)
        {
            entity = await db.Customers.FirstOrDefaultAsync(x => x.Id == model.Id)
                ?? throw new InvalidOperationException("العميل غير موجود.");
            if (model.RowVersion.Length > 0)
                db.Entry(entity).Property(x => x.RowVersion).OriginalValue = model.RowVersion;
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedByUserId = actor.UserId;
        }
        else
        {
            entity = new Customer { CreatedByUserId = actor.UserId };
            db.Customers.Add(entity);
        }

        entity.Name = model.Name.Trim();
        entity.Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim();
        entity.Region = string.IsNullOrWhiteSpace(model.Region) ? null : model.Region.Trim();
        entity.Address = string.IsNullOrWhiteSpace(model.Address) ? null : model.Address.Trim();
        entity.OpeningBalance = model.OpeningBalance;
        entity.CreditLimit = model.CreditLimit;
        entity.Notes = model.Notes;
        entity.IsActive = model.IsActive;

        db.AuditLogs.Add(new AuditLog
        {
            UserId = actor.UserId,
            IpAddress = actor.IpAddress,
            Action = model.Id > 0 ? "Update" : "Create",
            EntityName = nameof(Customer),
            EntityId = model.Id > 0 ? model.Id.ToString() : "New",
            NewValues = $"{entity.Name}|{entity.Phone}|{entity.CreditLimit:0.00}"
        });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("تم تعديل بيانات العميل من مستخدم آخر. أعد فتح السجل.");
        }
    }

    public async Task DeleteAsync(long id)
    {
        await currentUser.EnsureAdministratorAsync();
        var actor = await currentUser.GetAsync();
        await using var db = await factory.CreateDbContextAsync();
        var customer = await db.Customers.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("العميل غير موجود.");

        var hasActivity = await db.SalesInvoices.AnyAsync(x => x.CustomerId == id) ||
                          await db.CustomerPayments.AnyAsync(x => x.CustomerId == id);
        if (hasActivity)
        {
            customer.IsActive = false;
            customer.UpdatedAt = DateTime.UtcNow;
            customer.UpdatedByUserId = actor.UserId;
        }
        else
        {
            customer.IsDeleted = true;
            customer.DeletedAt = DateTime.UtcNow;
            customer.UpdatedByUserId = actor.UserId;
        }

        db.AuditLogs.Add(new AuditLog
        {
            UserId = actor.UserId,
            IpAddress = actor.IpAddress,
            Action = hasActivity ? "Deactivate" : "Delete",
            EntityName = nameof(Customer),
            EntityId = id.ToString(),
            OldValues = customer.Name
        });
        await db.SaveChangesAsync();
    }

    public async Task AddPaymentAsync(CustomerPaymentEditorModel model)
    {
        if (model.Amount <= 0) throw new InvalidOperationException("مبلغ السداد يجب أن يكون أكبر من صفر.");
        var actor = await currentUser.GetAsync();

        await using var strategyContext = await factory.CreateDbContextAsync();
        var strategy = strategyContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync();
            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                var customer = await db.Customers.FirstOrDefaultAsync(x => x.Id == model.CustomerId && x.IsActive)
                    ?? throw new InvalidOperationException("العميل غير موجود أو غير نشط.");

                SalesInvoice? invoice = null;
                if (model.InvoiceId.HasValue)
                {
                    invoice = await db.SalesInvoices.FirstOrDefaultAsync(x =>
                        x.Id == model.InvoiceId.Value &&
                        x.CustomerId == model.CustomerId &&
                        x.Status == InvoiceStatus.Posted)
                        ?? throw new InvalidOperationException("الفاتورة غير موجودة أو لا تخص هذا العميل.");

                    if (model.Amount > invoice.AmountDue)
                        throw new InvalidOperationException("مبلغ السداد أكبر من المتبقي على الفاتورة.");

                    invoice.AmountPaid += model.Amount;
                    invoice.AmountDue = Math.Max(0, invoice.GrossAmount - invoice.AmountPaid);
                    invoice.PaymentStatus = invoice.AmountDue <= 0
                        ? PaymentStatus.Paid
                        : PaymentStatus.Partial;
                    invoice.UpdatedAt = DateTime.UtcNow;
                    invoice.UpdatedByUserId = actor.UserId;
                }
                else
                {
                    var openingPaid = await db.CustomerPayments
                        .Where(x => x.CustomerId == model.CustomerId && x.SalesInvoiceId == null)
                        .SumAsync(x => (decimal?)x.Amount) ?? 0;
                    var openingDue = Math.Max(0, customer.OpeningBalance - openingPaid);
                    if (model.Amount > openingDue)
                        throw new InvalidOperationException("مبلغ السداد أكبر من الرصيد الافتتاحي المتبقي.");
                }

                var payment = new CustomerPayment
                {
                    CustomerId = model.CustomerId,
                    SalesInvoiceId = model.InvoiceId,
                    Amount = model.Amount,
                    PaymentDate = model.PaymentDate,
                    PaymentMethod = model.PaymentMethod,
                    ReferenceNumber = model.ReferenceNumber,
                    Notes = model.Notes,
                    CreatedByUserId = actor.UserId
                };
                db.CustomerPayments.Add(payment);
                await db.SaveChangesAsync();

                db.AuditLogs.Add(new AuditLog
                {
                    UserId = actor.UserId,
                    IpAddress = actor.IpAddress,
                    Action = "CustomerPayment",
                    EntityName = nameof(CustomerPayment),
                    EntityId = payment.Id.ToString(),
                    NewValues = $"Customer={model.CustomerId}|Invoice={model.InvoiceId}|Amount={model.Amount:0.00}"
                });
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        });
    }
}
