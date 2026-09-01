using System.ComponentModel.DataAnnotations;

namespace QatFarm.Web.Models;

public enum AccountCategory
{
    Asset = 0,
    Liability = 1,
    Equity = 2,
    Revenue = 3,
    Expense = 4
}

public enum JournalEntryStatus
{
    Posted = 0,
    Reversed = 1
}

public sealed class ChartOfAccount : AuditableEntity
{
    [Required, MaxLength(20)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    public AccountCategory Category { get; set; }
    public long? ParentId { get; set; }
    public ChartOfAccount? Parent { get; set; }
    public ICollection<ChartOfAccount> Children { get; set; } = [];
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public bool AllowPosting { get; set; } = true;
    [MaxLength(500)] public string? Notes { get; set; }
    public ICollection<JournalEntryLine> JournalLines { get; set; } = [];
}

public sealed class JournalEntry : AuditableEntity
{
    [Required, MaxLength(50)] public string EntryNumber { get; set; } = string.Empty;
    public DateTime EntryDate { get; set; } = DateTime.Now;
    [Required, MaxLength(500)] public string Description { get; set; } = string.Empty;
    [MaxLength(60)] public string? SourceType { get; set; }
    [MaxLength(100)] public string? SourceId { get; set; }
    [MaxLength(64)] public string? SourceHash { get; set; }
    public JournalEntryStatus Status { get; set; } = JournalEntryStatus.Posted;
    public bool IsAutomatic { get; set; }
    public long? FarmId { get; set; }
    public Farm? Farm { get; set; }
    public long? ReversesEntryId { get; set; }
    public JournalEntry? ReversesEntry { get; set; }
    public ICollection<JournalEntry> ReversalEntries { get; set; } = [];
    public ICollection<JournalEntryLine> Lines { get; set; } = [];
}

public sealed class JournalEntryLine : AuditableEntity
{
    public long JournalEntryId { get; set; }
    public JournalEntry JournalEntry { get; set; } = null!;
    public long AccountId { get; set; }
    public ChartOfAccount Account { get; set; } = null!;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
    public long? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public long? CreditorId { get; set; }
    public Creditor? Creditor { get; set; }
    public long? FarmId { get; set; }
    public Farm? Farm { get; set; }
}

public sealed record AccountingSummary(
    decimal CashBalance,
    decimal BankBalance,
    decimal Receivables,
    decimal Payables,
    decimal ZakatPayable,
    decimal RevenueYear,
    decimal ExpensesYear,
    decimal NetProfitYear,
    int PostedEntries,
    decimal UnbalancedAmount);

public sealed record TrialBalanceRow(
    long AccountId,
    string Code,
    string AccountName,
    AccountCategory Category,
    decimal Debit,
    decimal Credit,
    decimal Balance);

public sealed record JournalEntryRow(
    long Id,
    string EntryNumber,
    DateTime EntryDate,
    string Description,
    string SourceType,
    string FarmName,
    decimal Debit,
    decimal Credit,
    JournalEntryStatus Status,
    bool IsAutomatic);

public sealed record IncomeStatementModel(
    decimal Revenue,
    decimal CultivationExpenses,
    decimal SalesExpenses,
    decimal ZakatExpense,
    decimal OtherExpenses,
    decimal TotalExpenses,
    decimal NetProfit);

public sealed record FinancialPositionModel(
    decimal Cash,
    decimal Bank,
    decimal Receivables,
    decimal OtherAssets,
    decimal TotalAssets,
    decimal Payables,
    decimal ZakatPayable,
    decimal OtherLiabilities,
    decimal TotalLiabilities,
    decimal OpeningEquity,
    decimal AccumulatedResult,
    decimal TotalEquity,
    decimal LiabilitiesAndEquity,
    decimal Difference);

public sealed record GeneralLedgerRow(
    DateTime EntryDate,
    string EntryNumber,
    string Description,
    decimal Debit,
    decimal Credit,
    decimal RunningBalance,
    string SourceType,
    string FarmName);

public sealed class ManualJournalEditorModel
{
    public DateTime EntryDate { get; set; } = DateTime.Now;
    [Required(ErrorMessage = "وصف القيد مطلوب"), MaxLength(500)] public string Description { get; set; } = string.Empty;
    public long? FarmId { get; set; }
    public List<ManualJournalLineModel> Lines { get; set; } =
    [
        new(),
        new()
    ];
}

public sealed class ManualJournalLineModel
{
    [Range(1, long.MaxValue, ErrorMessage = "اختر الحساب")] public long AccountId { get; set; }
    [Range(typeof(decimal), "0", "9999999999999999")] public decimal Debit { get; set; }
    [Range(typeof(decimal), "0", "9999999999999999")] public decimal Credit { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
}
