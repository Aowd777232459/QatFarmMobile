using System.ComponentModel.DataAnnotations;

namespace QatFarm.Web.Models;

public sealed class InvoiceEditorModel
{
    public long? Id { get; set; }
    public byte[] RowVersion { get; set; } = [];
    [Range(1, long.MaxValue, ErrorMessage = "اختر المزرعة")] public long FarmId { get; set; }
    public long? CustomerId { get; set; }
    public DateTime InvoiceDate { get; set; } = DateTime.Now;
    public DateTime? PaymentDueDate { get; set; }
    [MaxLength(150)] public string? BuyerName { get; set; }
    [MaxLength(30)] public string? BuyerPhone { get; set; }
    [Range(typeof(decimal), "0", "100", ErrorMessage = "نسبة الزكاة يجب أن تكون بين 0 و100")] public decimal ZakatPercent { get; set; } = 5m;
    [Range(typeof(decimal), "0", "9999999999999999", ErrorMessage = "المبلغ المدفوع لا يمكن أن يكون سالبًا")] public decimal AmountPaid { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    [MaxLength(1000)] public string? Notes { get; set; }
    public List<InvoiceItemEditorModel> Items { get; set; } = [new()];
    public List<InvoiceExpenseEditorModel> Expenses { get; set; } = [];
}

public sealed class InvoiceItemEditorModel
{
    [Range(1, long.MaxValue, ErrorMessage = "اختر نوع القات")] public long QatTypeId { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "الكمية يجب أن تكون أكبر من صفر")] public int Quantity { get; set; } = 1;
    [Range(0.01, double.MaxValue, ErrorMessage = "السعر يجب أن يكون أكبر من صفر")] public decimal UnitPrice { get; set; }
    public decimal Total => Quantity * UnitPrice;
}

public sealed class InvoiceExpenseEditorModel
{
    [Range(1, long.MaxValue, ErrorMessage = "اختر نوع المصروف")] public long ExpenseTypeId { get; set; }
    [Range(0.01, double.MaxValue)] public decimal Amount { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
}

public sealed class CustomerEditorModel
{
    public long Id { get; set; }
    public byte[] RowVersion { get; set; } = [];
    [Required(ErrorMessage = "اسم العميل مطلوب"), MaxLength(150)] public string Name { get; set; } = string.Empty;
    [MaxLength(30)] public string? Phone { get; set; }
    [MaxLength(150)] public string? Region { get; set; }
    [MaxLength(300)] public string? Address { get; set; }
    [Range(typeof(decimal), "0", "9999999999999999")] public decimal OpeningBalance { get; set; }
    [Range(typeof(decimal), "0", "9999999999999999")] public decimal CreditLimit { get; set; }
    [MaxLength(1000)] public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class CustomerPaymentEditorModel
{
    [Range(1, long.MaxValue)] public long CustomerId { get; set; }
    public long? InvoiceId { get; set; }
    [Range(0.01, double.MaxValue, ErrorMessage = "أدخل مبلغًا صحيحًا")] public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.Today;
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    [MaxLength(100)] public string? ReferenceNumber { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
}

public sealed class CultivationExpenseEditorModel
{
    public long Id { get; set; }
    public byte[] RowVersion { get; set; } = [];
    [Range(1, long.MaxValue, ErrorMessage = "اختر المزرعة")] public long FarmId { get; set; }
    [Range(1, long.MaxValue, ErrorMessage = "اختر نوع الخسارة")] public long ExpenseTypeId { get; set; }
    [Range(0.01, double.MaxValue, ErrorMessage = "المبلغ يجب أن يكون أكبر من صفر")] public decimal Amount { get; set; }
    public DateTime ExpenseDate { get; set; } = DateTime.Today;
    public CultivationExpensePaymentType PaymentType { get; set; } = CultivationExpensePaymentType.Cash;
    public long? CreditorId { get; set; }
    [Range(typeof(decimal), "0", "9999999999999999", ErrorMessage = "المبلغ المدفوع غير صحيح")]
    public decimal InitialPaidAmount { get; set; }
    public PaymentMethod InitialPaymentMethod { get; set; } = PaymentMethod.Cash;
    [MaxLength(100)] public string? InitialPaymentReference { get; set; }
    public DateTime? DueDate { get; set; }
    [MaxLength(1000)] public string? Notes { get; set; }
}

public sealed class CultivationDebtPaymentEditorModel
{
    [Range(1, long.MaxValue)] public long CultivationExpenseId { get; set; }
    [Range(0.01, double.MaxValue, ErrorMessage = "مبلغ الدفعة يجب أن يكون أكبر من صفر")]
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.Today;
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    [MaxLength(100)] public string? ReferenceNumber { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
}

public sealed class CreditorEditorModel
{
    public long Id { get; set; }
    public byte[] RowVersion { get; set; } = [];
    [Required(ErrorMessage = "اسم الدائن مطلوب"), MaxLength(150)] public string Name { get; set; } = string.Empty;
    [MaxLength(30)] public string? Phone { get; set; }
    [MaxLength(250)] public string? Address { get; set; }
    [MaxLength(1000)] public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed record CultivationExpenseRow(
    long Id,
    string ReceiptNumber,
    long FarmId,
    string FarmName,
    long ExpenseTypeId,
    string ExpenseTypeName,
    decimal Amount,
    decimal PaidAmount,
    decimal OutstandingAmount,
    DateTime ExpenseDate,
    CultivationExpensePaymentType PaymentType,
    long? CreditorId,
    string CreditorName,
    DateTime? DueDate,
    CultivationDebtStatus DebtStatus,
    bool IsOverdue,
    string? Notes,
    byte[] RowVersion);

public sealed record CultivationDebtPaymentRow(
    long Id,
    long CultivationExpenseId,
    DateTime PaymentDate,
    decimal Amount,
    PaymentMethod PaymentMethod,
    string? ReferenceNumber,
    string? Notes,
    string CreditorName);

public sealed record CreditorDebtRow(
    long CreditorId,
    string CreditorName,
    string? Phone,
    decimal TotalExpenses,
    decimal TotalPaid,
    decimal Outstanding,
    int OpenDebtCount,
    int OverdueDebtCount);

public sealed record CultivationAnnualSummary(
    decimal TotalExpenses,
    decimal TotalPaid,
    decimal OutstandingDebt,
    decimal GrossSales,
    decimal CollectedSales,
    decimal CustomerReceivables,
    decimal NetSalesBeforeCultivation,
    decimal AccountingProfit,
    decimal CashAfterAllReserves,
    decimal SafeDistributableProfit,
    int DebtCount,
    int OverdueDebtCount);

public sealed record CultivationExpenseOverview(
    IReadOnlyList<CultivationExpenseRow> Expenses,
    IReadOnlyList<CreditorDebtRow> Creditors,
    CultivationAnnualSummary Summary);

public sealed record CustomerListRow(
    long Id,
    string Name,
    string? Phone,
    string? Region,
    decimal TotalPurchases,
    decimal TotalPaid,
    decimal Outstanding,
    decimal CreditLimit,
    bool IsActive,
    DateTime? LastPaymentDate,
    byte[] RowVersion);

public sealed record CustomerInvoiceRow(
    long Id,
    string InvoiceNumber,
    DateTime InvoiceDate,
    DateTime? PaymentDueDate,
    decimal GrossAmount,
    decimal AmountPaid,
    decimal AmountDue,
    PaymentStatus PaymentStatus,
    InvoiceStatus Status);

public sealed record CustomerPaymentRow(
    long Id,
    long? InvoiceId,
    string? InvoiceNumber,
    DateTime PaymentDate,
    decimal Amount,
    PaymentMethod PaymentMethod,
    string? ReferenceNumber,
    string? Notes);

public sealed record CustomerDetailsModel(
    Customer Customer,
    decimal TotalPurchases,
    decimal TotalPaid,
    decimal Outstanding,
    decimal Overdue,
    IReadOnlyList<CustomerInvoiceRow> Invoices,
    IReadOnlyList<CustomerPaymentRow> Payments);

public sealed record DashboardSummary(
    int FarmCount,
    int CustomerCount,
    decimal TodaySales,
    decimal TodayExpenses,
    decimal TodayZakat,
    decimal TodayNet,
    decimal MonthSales,
    decimal MonthNet,
    decimal YearSales,
    decimal YearCollectedSales,
    decimal CultivationLosses,
    decimal CultivationPaid,
    decimal CultivationDebt,
    decimal CultivationOverdueDebt,
    decimal AccountingProfitYear,
    decimal SafeDistributableProfitYear,
    int TodayInvoices,
    decimal Receivables,
    decimal OverdueReceivables,
    decimal PendingZakat,
    int PendingZakatCount,
    decimal MonthGrowthPercent,
    string TopFarm,
    string TopQatType);

public sealed record MonthlyPoint(string Label, decimal Sales, decimal Expenses, decimal Net);
public sealed record LookupItem(long Id, string Name);

public sealed record ZakatPendingRow(
    long InvoiceId,
    string InvoiceNumber,
    string FarmName,
    string CustomerName,
    DateTime InvoiceDate,
    decimal GrossAmount,
    decimal ZakatAmount,
    int AgeDays);

public sealed record ZakatNotificationSummary(int Count, decimal Amount, DateTime? OldestDate);

public sealed record UserListRow(
    string Id,
    string FullName,
    string Email,
    string Role,
    bool IsActive,
    bool MustChangePassword,
    DateTime? LastLoginAt,
    DateTime CreatedAt,
    string? ConcurrencyStamp);

public sealed class UserEditorModel
{
    public string? Id { get; set; }

    [Required(ErrorMessage = "اسم المستخدم مطلوب")]
    [StringLength(150, ErrorMessage = "الاسم طويل جدًا")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "البريد الإلكتروني مطلوب")]
    [EmailAddress(ErrorMessage = "البريد الإلكتروني غير صحيح")]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "اختر الصلاحية")]
    public string Role { get; set; } = "Employee";

    public bool IsActive { get; set; } = true;

    [DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 10, ErrorMessage = "كلمة المرور يجب ألا تقل عن 10 أحرف")]
    public string? NewPassword { get; set; }

    public string? ConcurrencyStamp { get; set; }
}

public sealed record AuditLogRow(
    long Id,
    DateTime ActionDate,
    string UserName,
    string Action,
    string EntityName,
    string EntityId,
    string? OldValues,
    string? NewValues,
    string? IpAddress);
