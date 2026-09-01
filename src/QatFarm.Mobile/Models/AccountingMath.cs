namespace QatFarm.Mobile.Models;

public static class AccountingMath
{
    public static decimal Money(decimal value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static decimal LineTotal(int quantity, decimal unitPrice)
        => quantity <= 0 || unitPrice <= 0 ? 0m : Money(quantity * unitPrice);

    public static decimal Gross(IEnumerable<decimal> lineTotals)
        => Money(lineTotals.Sum(x => Math.Max(0m, x)));

    public static decimal Zakat(decimal grossAmount, decimal percent)
    {
        if (grossAmount <= 0 || percent <= 0) return 0m;
        return Money(grossAmount * percent / 100m);
    }

    public static decimal Expenses(IEnumerable<decimal> amounts)
        => Money(amounts.Sum(x => Math.Max(0m, x)));

    public static decimal Net(decimal grossAmount, decimal zakatAmount, decimal expenses)
        => Money(grossAmount - zakatAmount - expenses);

    public static decimal Due(decimal grossAmount, decimal paidAmount)
        => Money(Math.Max(0m, grossAmount - paidAmount));

    public static decimal Outstanding(decimal amount, decimal paidAmount)
        => Money(Math.Max(0m, amount - paidAmount));

    public static decimal CustomerBalance(decimal openingBalance, decimal invoiced, decimal paid)
        => Money(Math.Max(0m, openingBalance + invoiced - paid));
}
