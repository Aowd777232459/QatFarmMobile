using SQLite;

namespace QatFarm.Mobile.Models;

public abstract class LocalEntity
{
    [PrimaryKey, AutoIncrement] public long Id { get; set; }
    [Unique] public string SyncKey { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
}

public enum UserRole { Administrator = 0, Accountant = 1, Employee = 2 }
public enum CultivationExpensePaymentType { Cash = 0, Credit = 1, Partial = 2 }
public enum CultivationDebtStatus { NoDebt = 0, Unpaid = 1, Partial = 2, Paid = 3 }
public enum InvoiceStatus { Draft = 0, Posted = 1, Cancelled = 2 }
public enum PaymentMethod { Cash = 0, Transfer = 1, Credit = 2, Mixed = 3 }
public enum PaymentStatus { Unpaid = 0, Partial = 1, Paid = 2 }
public enum ZakatPaymentStatus { Pending = 0, Paid = 1, NotApplicable = 2 }

[Table("AppUsers")]
public sealed class AppUser : LocalEntity
{
    // Email/PasswordHash remain only for compatibility with existing databases.
    // Login in the final app uses a six-character access code only.
    [Unique, NotNull] public string Email { get; set; } = string.Empty;
    [NotNull] public string FullName { get; set; } = string.Empty;
    [NotNull] public string PasswordHash { get; set; } = string.Empty;
    [NotNull] public string PasswordSalt { get; set; } = string.Empty;
    public string AccessCodeHash { get; set; } = string.Empty;
    public string AccessCodeSalt { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Employee;
    public bool CanEditInvoices { get; set; }
    public bool CanDeleteInvoices { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
}

[Table("Farms")]
public sealed class Farm : LocalEntity
{
    [Indexed, NotNull] public string Name { get; set; } = string.Empty;
    public string? OwnerName { get; set; }
    public string? Location { get; set; }
    public string? Phone { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

[Table("Customers")]
public sealed class Customer : LocalEntity
{
    [Indexed, NotNull] public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? SellerPhone { get; set; }
    public string? Region { get; set; }
    public string? Address { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal CreditLimit { get; set; } = 100000m;
    public bool DebtAlertEnabled { get; set; } = true;
    public decimal LastDebtAlertBalance { get; set; }
    public decimal LastDebtAlertLimit { get; set; }
    public DateTime? LastDebtAlertAt { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

[Table("Creditors")]
public sealed class Creditor : LocalEntity
{
    [Indexed, NotNull] public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

[Table("CultivationExpenseTypes")]
public sealed class CultivationExpenseType : LocalEntity
{
    [Unique, NotNull] public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

[Table("CultivationExpenses")]
public sealed class CultivationExpense : LocalEntity
{
    [Indexed] public long FarmId { get; set; }
    [Indexed] public long ExpenseTypeId { get; set; }
    public decimal Amount { get; set; }
    [Indexed] public DateTime ExpenseDate { get; set; } = DateTime.Today;
    public CultivationExpensePaymentType PaymentType { get; set; }
    [Indexed] public long? CreditorId { get; set; }
    public decimal PaidAmount { get; set; }
    public DateTime? DueDate { get; set; }
    [Indexed] public CultivationDebtStatus DebtStatus { get; set; }
    public string? Notes { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
}

[Table("CultivationDebtPayments")]
public sealed class CultivationDebtPayment : LocalEntity
{
    [Indexed] public long CultivationExpenseId { get; set; }
    [Indexed] public long CreditorId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.Now;
    public PaymentMethod PaymentMethod { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
}

[Table("QatTypes")]
public sealed class QatType : LocalEntity
{
    [Unique, NotNull] public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

[Table("DailyExpenseTypes")]
public sealed class DailyExpenseType : LocalEntity
{
    [Unique, NotNull] public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

[Table("SalesInvoices")]
public sealed class SalesInvoice : LocalEntity
{
    [Unique, NotNull] public string InvoiceNumber { get; set; } = string.Empty;
    [Indexed] public long FarmId { get; set; }
    [Indexed] public long? CustomerId { get; set; }
    [Indexed] public DateTime InvoiceDate { get; set; } = DateTime.Now;
    public DateTime? PaymentDueDate { get; set; }
    public string? BuyerName { get; set; }
    public string? BuyerPhone { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal ZakatPercent { get; set; } = 5m;
    public decimal ZakatAmount { get; set; }
    [Indexed] public ZakatPaymentStatus ZakatStatus { get; set; } = ZakatPaymentStatus.Pending;
    public DateTime? ZakatPaidAt { get; set; }
    public string? ZakatPaymentReference { get; set; }
    public string? ZakatRecipientName { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal NetAmount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal AmountDue { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    [Indexed] public PaymentStatus PaymentStatus { get; set; }
    [Indexed] public InvoiceStatus Status { get; set; } = InvoiceStatus.Posted;
    public string? Notes { get; set; }
}

[Table("SalesInvoiceItems")]
public sealed class SalesInvoiceItem : LocalEntity
{
    [Indexed] public long InvoiceId { get; set; }
    [Indexed] public long QatTypeId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
}

[Table("InvoiceExpenses")]
public sealed class InvoiceExpense : LocalEntity
{
    [Indexed] public long InvoiceId { get; set; }
    [Indexed] public long ExpenseTypeId { get; set; }
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
}

[Table("CustomerPayments")]
public sealed class CustomerPayment : LocalEntity
{
    [Indexed] public long CustomerId { get; set; }
    [Indexed] public long? SalesInvoiceId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.Now;
    public PaymentMethod PaymentMethod { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
}

[Table("AuditLogs")]
public sealed class AuditLog
{
    [PrimaryKey, AutoIncrement] public long Id { get; set; }
    public long? UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    [Indexed] public string Action { get; set; } = string.Empty;
    [Indexed] public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    [Indexed] public DateTime ActionDate { get; set; } = DateTime.Now;
}

[Table("SystemSettings")]
public sealed class SystemSetting
{
    [PrimaryKey] public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
