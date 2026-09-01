using QatFarm.Mobile.Models;
using QatFarm.Mobile.Services;

static void Equal(decimal expected, decimal actual, string name)
{
    if (expected != actual)
        throw new InvalidOperationException($"{name}: expected {expected}, actual {actual}");
}

static void EqualInt(int expected, int? actual, string name)
{
    if (actual != expected)
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

Equal(20000m, ArabicVoiceText.ParseLeadingNumber("عشرين ألف") ?? -1m, "spoken twenty thousand");
Equal(25000m, ArabicVoiceText.ExtractMoneyAfter("سجل خسارة سقي بمبلغ خمسة وعشرين ألف", "مبلغ") ?? -1m, "spoken 25k loss");
Equal(5000m, ArabicVoiceText.ExtractMoneyAfter("فاتورة 10 حبات سعر 5000 دفع 20000", "سعر") ?? -1m, "voice unit price");
Equal(20000m, ArabicVoiceText.ExtractMoneyAfter("فاتورة 10 حبات سعر 5000 دفع 20000", "دفع") ?? -1m, "voice paid");
EqualInt(10, ArabicVoiceText.ExtractQuantity("فاتورة عشر حبات اميال سعر خمسة الاف"), "spoken quantity");
EqualInt(8, ArabicVoiceText.ExtractMonth("صدر فواتير أغسطس PDF"), "Arabic month");
EqualInt(2026, ArabicVoiceText.ExtractYear("تقرير سنة ٢٠٢٦"), "Arabic year digits");

Console.WriteLine("ACCOUNTING_CHECKS_OK");
Console.WriteLine("VOICE_PARSER_CHECKS_OK");
