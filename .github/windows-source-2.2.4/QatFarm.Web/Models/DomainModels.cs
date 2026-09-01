using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace QatFarm.Web.Models;

public abstract class AuditableEntity
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    [MaxLength(450)] public string? UpdatedByUserId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}

public abstract class SyncableEntity : AuditableEntity
{
    [Required, MaxLength(32)] public string SyncKey { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class ApplicationUser : IdentityUser
{
    [Required, MaxLength(150)] public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}

public sealed class Farm : SyncableEntity
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [MaxLength(150)] public string? OwnerName { get; set; }
    [MaxLength(250)] public string? Location { get; set; }
    [MaxLength(30)] public string? Phone { get; set; }
    [MaxLength(1000)] public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<CultivationExpense> CultivationExpenses { get; set; } = [];
    public ICollection<SalesInvoice> SalesInvoices { get; set; } = [];
}

public sealed class Customer : SyncableEntity
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [MaxLength(30)] public string? Phone { get; set; }
    [MaxLength(150)] public string? Region { get; set; }
    [MaxLength(300)] public string? Address { get; set; }
    [Range(typeof(decimal), "0", "9999999999999999")] public decimal OpeningBalance { get; set; }
    [Range(typeof(decimal), "0", "9999999999999999")] public decimal CreditLimit { get; set; }
    [MaxLength(1000)] public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<SalesInvoice> Invoices { get; set; } = [];
    public ICollection<CustomerPayment> Payments { get; set; } = [];
}

public sealed class Creditor : SyncableEntity
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [MaxLength(30)] public string? Phone { get; set; }
    [MaxLength(250)] public string? Address { get; set; }
    [MaxLength(1000)] public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<CultivationExpense> CultivationExpenses { get; set; } = [];
    public ICollection<CultivationDebtPayment> Payments { get; set; } = [];
}

public sealed class CultivationExpenseType : SyncableEntity
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public enum CultivationExpensePaymentType
{
    Cash = 0,
    Credit = 1,
    Partial = 2
}

public enum CultivationDebtStatus
{
    NoDebt = 0,
    Unpaid = 1,
    Partial = 2,
    Paid = 3
}

public sealed class CultivationExpense : SyncableEntity
{
    [Range(1, long.MaxValue, ErrorMessage = "اختر المزرعة")] public long FarmId { get; set; }
    public Farm Farm { get; set; } = null!;
    [Range(1, long.MaxValue, ErrorMessage = "اختر نوع الخسارة")] public long ExpenseTypeId { get; set; }
    public CultivationExpenseType ExpenseType { get; set; } = null!;
    [Range(0.01, double.MaxValue)] public decimal Amount { get; set; }
    public DateTime ExpenseDate { get; set; } = DateTime.Today;
    public CultivationExpensePaymentType PaymentType { get; set; } = CultivationExpensePaymentType.Cash;
    public long? CreditorId { get; set; }
    public Creditor? Creditor { get; set; }
    public decimal PaidAmount { get; set; }
    public DateTime? DueDate { get; set; }
    public CultivationDebtStatus DebtStatus { get; set; } = CultivationDebtStatus.NoDebt;
    [MaxLength(1000)] public string? Notes { get; set; }
    [MaxLength(40)] public string ReceiptNumber { get; set; } = string.Empty;
    public ICollection<CultivationDebtPayment> DebtPayments { get; set; } = [];
}

public sealed class CultivationDebtPayment : SyncableEntity
{
    [Range(1, long.MaxValue)] public long CultivationExpenseId { get; set; }
    public CultivationExpense CultivationExpense { get; set; } = null!;
    [Range(1, long.MaxValue)] public long CreditorId { get; set; }
    public Creditor Creditor { get; set; } = null!;
    [Range(0.01, double.MaxValue)] public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.Now;
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    [MaxLength(100)] public string? ReferenceNumber { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
}

public sealed class QatType : SyncableEntity
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class DailyExpenseType : SyncableEntity
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public enum InvoiceStatus { Draft = 0, Posted = 1, Cancelled = 2 }
public enum PaymentMethod { Cash = 0, Transfer = 1, Credit = 2, Mixed = 3 }
public enum PaymentStatus { Unpaid = 0, Partial = 1, Paid = 2 }
public enum ZakatPaymentStatus { Pending = 0, Paid = 1, NotApplicable = 2 }

public sealed class SalesInvoice : SyncableEntity
{
    [Required, MaxLength(40)] public string InvoiceNumber { get; set; } = string.Empty;
    [Range(1, long.MaxValue, ErrorMessage = "اختر المزرعة")] public long FarmId { get; set; }
    public Farm Farm { get; set; } = null!;
    public long? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public DateTime InvoiceDate { get; set; } = DateTime.Now;
    public DateTime? PaymentDueDate { get; set; }
    [MaxLength(150)] public string? BuyerName { get; set; }
    [MaxLength(30)] public string? BuyerPhone { get; set; }
    public decimal GrossAmount { get; set; }
    [Range(typeof(decimal), "0", "100", ErrorMessage = "نسبة الزكاة يجب أن تكون بين 0 و100")] public decimal ZakatPercent { get; set; } = 5m;
    public decimal ZakatAmount { get; set; }
    public ZakatPaymentStatus ZakatStatus { get; set; } = ZakatPaymentStatus.Pending;
    public DateTime? ZakatPaidAt { get; set; }
    [MaxLength(450)] public string? ZakatPaidByUserId { get; set; }
    [MaxLength(100)] public string? ZakatPaymentReference { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal NetAmount { get; set; }
    [Range(typeof(decimal), "0", "9999999999999999", ErrorMessage = "المبلغ المدفوع لا يمكن أن يكون سالبًا")] public decimal AmountPaid { get; set; }
    public decimal AmountDue { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Posted;
    [MaxLength(1000)] public string? Notes { get; set; }
    public ICollection<SalesInvoiceItem> Items { get; set; } = [];
    public ICollection<InvoiceExpense> Expenses { get; set; } = [];
    public ICollection<CustomerPayment> CustomerPayments { get; set; } = [];
}

public sealed class SalesInvoiceItem : SyncableEntity
{
    public long InvoiceId { get; set; }
    public SalesInvoice Invoice { get; set; } = null!;
    [Range(1, long.MaxValue, ErrorMessage = "اختر نوع القات")] public long QatTypeId { get; set; }
    public QatType QatType { get; set; } = null!;
    [Range(1, int.MaxValue)] public int Quantity { get; set; }
    [Range(0.01, double.MaxValue)] public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
}

public sealed class InvoiceExpense : SyncableEntity
{
    public long InvoiceId { get; set; }
    public SalesInvoice Invoice { get; set; } = null!;
    [Range(1, long.MaxValue, ErrorMessage = "اختر نوع المصروف")] public long ExpenseTypeId { get; set; }
    public DailyExpenseType ExpenseType { get; set; } = null!;
    [Range(0.01, double.MaxValue)] public decimal Amount { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
}

public sealed class CustomerPayment : SyncableEntity
{
    [Range(1, long.MaxValue)] public long CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public long? SalesInvoiceId { get; set; }
    public SalesInvoice? SalesInvoice { get; set; }
    [Range(0.01, double.MaxValue)] public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.Now;
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    [MaxLength(100)] public string? ReferenceNumber { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
}

public sealed class AuditLog
{
    public long Id { get; set; }
    [MaxLength(450)] public string? UserId { get; set; }
    [MaxLength(150)] public string Action { get; set; } = string.Empty;
    [MaxLength(150)] public string EntityName { get; set; } = string.Empty;
    [MaxLength(100)] public string EntityId { get; set; } = string.Empty;
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public DateTime ActionDate { get; set; } = DateTime.UtcNow;
    [MaxLength(64)] public string? IpAddress { get; set; }
}

public sealed class SystemSetting
{
    [Key, MaxLength(100)] public string Key { get; set; } = string.Empty;
    [MaxLength(1000)] public string Value { get; set; } = string.Empty;
    [MaxLength(500)] public string? Description { get; set; }
}
