using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using QatFarm.Web.Models;

namespace QatFarm.Web.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Farm> Farms => Set<Farm>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerPayment> CustomerPayments => Set<CustomerPayment>();
    public DbSet<Creditor> Creditors => Set<Creditor>();
    public DbSet<CultivationExpenseType> CultivationExpenseTypes => Set<CultivationExpenseType>();
    public DbSet<CultivationExpense> CultivationExpenses => Set<CultivationExpense>();
    public DbSet<CultivationDebtPayment> CultivationDebtPayments => Set<CultivationDebtPayment>();
    public DbSet<QatType> QatTypes => Set<QatType>();
    public DbSet<DailyExpenseType> DailyExpenseTypes => Set<DailyExpenseType>();
    public DbSet<SalesInvoice> SalesInvoices => Set<SalesInvoice>();
    public DbSet<SalesInvoiceItem> SalesInvoiceItems => Set<SalesInvoiceItem>();
    public DbSet<InvoiceExpense> InvoiceExpenses => Set<InvoiceExpense>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<ChartOfAccount> ChartOfAccounts => Set<ChartOfAccount>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalEntryLine> JournalEntryLines => Set<JournalEntryLine>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Farm>().HasIndex(x => x.Name).IsUnique();
        builder.Entity<Farm>().HasIndex(x => x.SyncKey).IsUnique();
        builder.Entity<Customer>().HasIndex(x => x.SyncKey).IsUnique();
        builder.Entity<Creditor>().HasIndex(x => x.SyncKey).IsUnique();
        builder.Entity<CultivationExpenseType>().HasIndex(x => x.SyncKey).IsUnique();
        builder.Entity<CultivationExpense>().HasIndex(x => x.SyncKey).IsUnique();
        builder.Entity<CultivationDebtPayment>().HasIndex(x => x.SyncKey).IsUnique();
        builder.Entity<QatType>().HasIndex(x => x.SyncKey).IsUnique();
        builder.Entity<DailyExpenseType>().HasIndex(x => x.SyncKey).IsUnique();
        builder.Entity<SalesInvoice>().HasIndex(x => x.SyncKey).IsUnique();
        builder.Entity<SalesInvoiceItem>().HasIndex(x => x.SyncKey).IsUnique();
        builder.Entity<InvoiceExpense>().HasIndex(x => x.SyncKey).IsUnique();
        builder.Entity<CustomerPayment>().HasIndex(x => x.SyncKey).IsUnique();
        builder.Entity<Customer>().HasIndex(x => x.Phone);
        builder.Entity<Customer>().HasIndex(x => x.Name);
        builder.Entity<Creditor>().HasIndex(x => x.Name);
        builder.Entity<Creditor>().HasIndex(x => x.Phone);
        builder.Entity<CultivationExpenseType>().HasIndex(x => x.Name).IsUnique();
        builder.Entity<QatType>().HasIndex(x => x.Name).IsUnique();
        builder.Entity<DailyExpenseType>().HasIndex(x => x.Name).IsUnique();
        builder.Entity<SalesInvoice>().HasIndex(x => x.InvoiceNumber).IsUnique();
        builder.Entity<SalesInvoice>().HasIndex(x => new { x.CustomerId, x.PaymentDueDate });
        builder.Entity<CultivationExpense>().HasIndex(x => x.ReceiptNumber).IsUnique();
        builder.Entity<CultivationExpense>().HasIndex(x => new { x.CreditorId, x.DueDate, x.DebtStatus });
        builder.Entity<CultivationDebtPayment>().HasIndex(x => new { x.CultivationExpenseId, x.PaymentDate });
        builder.Entity<CultivationDebtPayment>().HasIndex(x => new { x.CreditorId, x.PaymentDate });
        builder.Entity<CustomerPayment>().HasIndex(x => new { x.CustomerId, x.PaymentDate });
        builder.Entity<ChartOfAccount>().HasIndex(x => x.Code).IsUnique();
        builder.Entity<ChartOfAccount>().HasIndex(x => x.Name);
        builder.Entity<JournalEntry>().HasIndex(x => x.EntryNumber).IsUnique();
        builder.Entity<JournalEntry>().HasIndex(x => new { x.SourceType, x.SourceId, x.Status });
        builder.Entity<JournalEntry>().HasIndex(x => x.EntryDate);
        builder.Entity<JournalEntryLine>().HasIndex(x => new { x.AccountId, x.JournalEntryId });
        builder.Entity<ChartOfAccount>().ToTable(t => t.HasCheckConstraint("CK_ChartOfAccounts_Category", "[Category] BETWEEN 0 AND 4"));
        builder.Entity<JournalEntry>().ToTable(t => t.HasCheckConstraint("CK_JournalEntries_Status", "[Status] BETWEEN 0 AND 1"));
        builder.Entity<JournalEntryLine>().ToTable(t => t.HasCheckConstraint(
            "CK_JournalEntryLines_DebitCredit",
            "[Debit] >= 0 AND [Credit] >= 0 AND (([Debit] > 0 AND [Credit] = 0) OR ([Credit] > 0 AND [Debit] = 0))"));

        builder.Entity<CultivationExpense>().Property(x => x.Amount).HasPrecision(18, 2);
        builder.Entity<CultivationExpense>().Property(x => x.PaidAmount).HasPrecision(18, 2);
        builder.Entity<CultivationDebtPayment>().Property(x => x.Amount).HasPrecision(18, 2);
        builder.Entity<Customer>().Property(x => x.OpeningBalance).HasPrecision(18, 2);
        builder.Entity<Customer>().Property(x => x.CreditLimit).HasPrecision(18, 2);
        builder.Entity<CustomerPayment>().Property(x => x.Amount).HasPrecision(18, 2);
        builder.Entity<SalesInvoice>().Property(x => x.GrossAmount).HasPrecision(18, 2);
        builder.Entity<SalesInvoice>().Property(x => x.ZakatPercent).HasPrecision(9, 4);
        builder.Entity<SalesInvoice>().Property(x => x.ZakatAmount).HasPrecision(18, 2);
        builder.Entity<SalesInvoice>().Property(x => x.TotalExpenses).HasPrecision(18, 2);
        builder.Entity<SalesInvoice>().Property(x => x.NetAmount).HasPrecision(18, 2);
        builder.Entity<SalesInvoice>().Property(x => x.AmountPaid).HasPrecision(18, 2);
        builder.Entity<SalesInvoice>().Property(x => x.AmountDue).HasPrecision(18, 2);
        builder.Entity<SalesInvoiceItem>().Property(x => x.UnitPrice).HasPrecision(18, 2);
        builder.Entity<SalesInvoiceItem>().Property(x => x.TotalPrice).HasPrecision(18, 2);
        builder.Entity<InvoiceExpense>().Property(x => x.Amount).HasPrecision(18, 2);
        builder.Entity<JournalEntryLine>().Property(x => x.Debit).HasPrecision(18, 2);
        builder.Entity<JournalEntryLine>().Property(x => x.Credit).HasPrecision(18, 2);

        builder.Entity<CultivationExpense>()
            .HasOne(x => x.Farm).WithMany(x => x.CultivationExpenses)
            .HasForeignKey(x => x.FarmId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CultivationExpense>()
            .HasOne(x => x.Creditor).WithMany(x => x.CultivationExpenses)
            .HasForeignKey(x => x.CreditorId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CultivationDebtPayment>()
            .HasOne(x => x.CultivationExpense).WithMany(x => x.DebtPayments)
            .HasForeignKey(x => x.CultivationExpenseId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CultivationDebtPayment>()
            .HasOne(x => x.Creditor).WithMany(x => x.Payments)
            .HasForeignKey(x => x.CreditorId).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<SalesInvoice>()
            .HasOne(x => x.Farm).WithMany(x => x.SalesInvoices)
            .HasForeignKey(x => x.FarmId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SalesInvoice>()
            .HasOne(x => x.Customer).WithMany(x => x.Invoices)
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SalesInvoiceItem>()
            .HasOne(x => x.Invoice).WithMany(x => x.Items)
            .HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<InvoiceExpense>()
            .HasOne(x => x.Invoice).WithMany(x => x.Expenses)
            .HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<CustomerPayment>()
            .HasOne(x => x.Customer).WithMany(x => x.Payments)
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CustomerPayment>()
            .HasOne(x => x.SalesInvoice).WithMany(x => x.CustomerPayments)
            .HasForeignKey(x => x.SalesInvoiceId).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ChartOfAccount>()
            .HasOne(x => x.Parent).WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<JournalEntry>()
            .HasOne(x => x.Farm).WithMany()
            .HasForeignKey(x => x.FarmId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<JournalEntry>()
            .HasOne(x => x.ReversesEntry).WithMany(x => x.ReversalEntries)
            .HasForeignKey(x => x.ReversesEntryId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<JournalEntryLine>()
            .HasOne(x => x.JournalEntry).WithMany(x => x.Lines)
            .HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<JournalEntryLine>()
            .HasOne(x => x.Account).WithMany(x => x.JournalLines)
            .HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<JournalEntryLine>()
            .HasOne(x => x.Customer).WithMany()
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<JournalEntryLine>()
            .HasOne(x => x.Creditor).WithMany()
            .HasForeignKey(x => x.CreditorId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<JournalEntryLine>()
            .HasOne(x => x.Farm).WithMany()
            .HasForeignKey(x => x.FarmId).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Farm>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<Customer>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<CustomerPayment>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<Creditor>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<CultivationExpenseType>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<CultivationExpense>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<CultivationDebtPayment>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<QatType>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<DailyExpenseType>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<SalesInvoice>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<SalesInvoiceItem>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<InvoiceExpense>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<ChartOfAccount>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<JournalEntry>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<JournalEntryLine>().HasQueryFilter(x => !x.IsDeleted);
    }
}
