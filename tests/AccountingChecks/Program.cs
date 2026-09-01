using QatFarm.Mobile.Models;

static void Equal(decimal expected, decimal actual, string name)
{
    if (expected != actual)
        throw new InvalidOperationException($"{name}: expected {expected}, actual {actual}");
}

var lines = new[]
{
    AccountingMath.LineTotal(10, 5000m),
    AccountingMath.LineTotal(5, 4000m),
    AccountingMath.LineTotal(3, 2000m)
};
var gross = AccountingMath.Gross(lines);
Equal(76000m, gross, "gross");

var zakat = AccountingMath.Zakat(gross, 2.5m);
Equal(1900m, zakat, "zakat");
Equal(74100m, AccountingMath.Net(gross, zakat, 0m), "net after zakat");

// Customer debt is based on the sale value, not on profit after zakat/expenses.
Equal(56000m, AccountingMath.Due(gross, 20000m), "customer due");
Equal(0m, AccountingMath.Due(gross, gross), "cash sale due");

Equal(65000m, AccountingMath.Net(100000m, 5000m, 30000m), "invoice net");
Equal(15000m, AccountingMath.Outstanding(25000m, 10000m), "cultivation outstanding");
Equal(85000m, AccountingMath.CustomerBalance(5000m, 120000m, 40000m), "customer balance");

Equal(0.01m, AccountingMath.Money(0.005m), "money rounding positive");
Equal(-0.01m, AccountingMath.Money(-0.005m), "money rounding negative");

Console.WriteLine("ACCOUNTING_CHECKS_OK");
