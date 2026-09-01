namespace QatFarm.Mobile.Models;

public sealed class LoginModel
{
    public string AccessCode { get; set; } = string.Empty;
}

public sealed class FirstRunAdminModel
{
    public string AccessCode { get; set; } = string.Empty;
}

public sealed class InvoiceEditorModel
{
    public long Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public long FarmId { get; set; }
    public long? CustomerId { get; set; }
    public DateTime InvoiceDate { get; set; } = DateTime.Today;
    public DateTime? PaymentDueDate { get; set; }
    public string? BuyerName { get; set; }
    public string? BuyerPhone { get; set; }
    public decimal ZakatPercent { get; set; } = 5m;
    public decimal AmountPaid { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public string? Notes { get; set; }
    public List<InvoiceItemInput> Items { get; set; } = [new()];
    public List<InvoiceExpenseInput> Expenses { get; set; } = [];
    public decimal GrossAmount => Items.Sum(x => x.Quantity * x.UnitPrice);
    public decimal ZakatAmount => Math.Round(GrossAmount * ZakatPercent / 100m, 2);
    public decimal TotalExpenses => Expenses.Sum(x => x.Amount);
    public decimal NetAmount => GrossAmount - ZakatAmount - TotalExpenses;
    public decimal AmountDue => Math.Max(0, GrossAmount - AmountPaid);
}

public sealed class InvoiceItemInput
{
    public long QatTypeId { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal Total => Quantity * UnitPrice;
}

public sealed class InvoiceExpenseInput
{
    public long ExpenseTypeId { get; set; }
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
}

public sealed class InvoiceListRow
{
    public SalesInvoice Invoice { get; set; } = new();
    public string FarmName { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
}

public sealed class CultivationExpenseRow
{
    public CultivationExpense Expense { get; set; } = new();
    public string FarmName { get; set; } = string.Empty;
    public string ExpenseTypeName { get; set; } = string.Empty;
    public string CreditorName { get; set; } = string.Empty;
    public decimal Outstanding => Math.Max(0, Expense.Amount - Expense.PaidAmount);
    public bool IsOverdue => Outstanding > 0 && Expense.DueDate.HasValue && Expense.DueDate.Value.Date < DateTime.Today;
}

public sealed class DashboardSummary
{
    public decimal SalesToday { get; set; }
    public decimal SalesYear { get; set; }
    public decimal NetYear { get; set; }
    public decimal CustomerDebts { get; set; }
    public decimal CultivationDebts { get; set; }
    public decimal PendingZakat { get; set; }
    public int InvoiceCountYear { get; set; }
    public int OverdueDebtCount { get; set; }
}

public sealed class AnnualFinanceSummary
{
    public string FarmName { get; set; } = string.Empty;
    public int Year { get; set; }
    public decimal GrossSales { get; set; }
    public decimal CollectedSales { get; set; }
    public decimal InvoiceExpenses { get; set; }
    public decimal Zakat { get; set; }
    public decimal CultivationExpenses { get; set; }
    public decimal CultivationDebtOutstanding { get; set; }
    public decimal AccountingProfit { get; set; }
    public decimal SafeDistributableProfit { get; set; }
}

public sealed class CustomerBalanceRow
{
    public Customer Customer { get; set; } = new();
    public decimal Invoiced { get; set; }
    public decimal Paid { get; set; }
    public decimal Balance => Customer.OpeningBalance + Invoiced - Paid;
}

public sealed class UserEditModel
{
    public long Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string AccessCode { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Employee;
    public bool CanEditInvoices { get; set; }
    public bool CanDeleteInvoices { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class AccountingCenterSummary
{
    public int Year { get; set; }
    public string FarmName { get; set; } = "كل المزارع";
    public decimal GrossSales { get; set; }
    public decimal CollectedSales { get; set; }
    public decimal CustomerReceivables { get; set; }
    public decimal InvoiceExpenses { get; set; }
    public decimal CultivationExpenses { get; set; }
    public decimal CultivationPayables { get; set; }
    public decimal ZakatAccrued { get; set; }
    public decimal ZakatPaid { get; set; }
    public decimal AccountingProfit { get; set; }
    public decimal CashInflow { get; set; }
    public decimal CashOutflow { get; set; }
    public decimal NetCashFlow => CashInflow - CashOutflow;
    public decimal NetMarginPercent => GrossSales <= 0 ? 0 : AccountingProfit / GrossSales * 100m;
    public decimal CollectionPercent => GrossSales <= 0 ? 0 : CollectedSales / GrossSales * 100m;
    public decimal CostPercent => GrossSales <= 0 ? 0 : (InvoiceExpenses + CultivationExpenses + ZakatAccrued) / GrossSales * 100m;
    public int PostedInvoiceCount { get; set; }
    public int OverdueCustomerInvoiceCount { get; set; }
    public int OverdueCultivationDebtCount { get; set; }
    public List<MonthlyFinanceRow> Months { get; set; } = [];
    public List<FarmPerformanceRow> Farms { get; set; } = [];
    public List<CashMovementRow> RecentCashMovements { get; set; } = [];
}

public sealed class MonthlyFinanceRow
{
    public int Month { get; set; }
    public string MonthName { get; set; } = string.Empty;
    public decimal Sales { get; set; }
    public decimal Collected { get; set; }
    public decimal Costs { get; set; }
    public decimal NetProfit { get; set; }
}

public sealed class FarmPerformanceRow
{
    public long FarmId { get; set; }
    public string FarmName { get; set; } = string.Empty;
    public decimal Sales { get; set; }
    public decimal Collected { get; set; }
    public decimal Costs { get; set; }
    public decimal NetProfit { get; set; }
    public decimal Receivables { get; set; }
    public decimal Payables { get; set; }
    public decimal MarginPercent => Sales <= 0 ? 0 : NetProfit / Sales * 100m;
}

public sealed class CashMovementRow
{
    public DateTime Date { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public string FarmName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Inflow { get; set; }
    public decimal Outflow { get; set; }
    public decimal RunningBalance { get; set; }
}
