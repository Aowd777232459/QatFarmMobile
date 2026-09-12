using System.Diagnostics;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QatFarm.Mobile.Models;

namespace QatFarm.Mobile.Services;

/// <summary>
/// مولد PDF لنسخة Windows. يحفظ كل الملفات تلقائياً داخل Documents\عواد سوفت.
/// </summary>
public sealed class MobilePdfService
{
    private readonly QatFarmService _service;
    private readonly AwadStorageService _storage;

    public MobilePdfService(QatFarmService service, AwadStorageService storage)
    {
        _service = service;
        _storage = storage;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<string> CreateInvoicePdfAsync(long invoiceId)
    {
        var d = await _service.GetInvoiceDetailsAsync(invoiceId);
        var path = _storage.GetInvoiceExportPath($"فاتورة-{Safe(d.Invoice.InvoiceNumber)}.pdf");
        Generate(path, $"فاتورة بيع رقم {d.Invoice.InvoiceNumber}", d.Farm.Name, column =>
        {
            KeyValue(column, "التاريخ", d.Invoice.InvoiceDate.ToString("yyyy/MM/dd"));
            KeyValue(column, "العميل", d.Customer?.Name ?? d.Invoice.BuyerName ?? "بيع نقدي");
            KeyValue(column, "الهاتف", d.Customer?.Phone ?? d.Invoice.BuyerPhone ?? "-");
            KeyValue(column, "طريقة الدفع", PaymentText(d.Invoice.PaymentMethod));
            Space(column);
            Section(column, "الأصناف");
            foreach (var item in d.Items)
            {
                var name = d.QatTypes.TryGetValue(item.QatTypeId, out var q) ? q : "صنف";
                Line(column, $"{name} — الكمية {item.Quantity:N0} × {item.UnitPrice:N2} = {item.TotalPrice:N2} ر.ي");
            }
            Space(column);
            KeyValue(column, "الإجمالي", Money(d.Invoice.GrossAmount));
            KeyValue(column, "الزكاة", Money(d.Invoice.ZakatAmount));
            KeyValue(column, "المصروفات", Money(d.Invoice.TotalExpenses));
            KeyValue(column, "الصافي", Money(d.Invoice.NetAmount));
            KeyValue(column, "المدفوع", Money(d.Invoice.AmountPaid));
            KeyValue(column, "المتبقي", Money(d.Invoice.AmountDue));
            if (!string.IsNullOrWhiteSpace(d.Invoice.Notes))
            {
                Space(column); Section(column, "ملاحظات"); Line(column, d.Invoice.Notes!);
            }
        });
        return path;
    }

    public async Task<string> CreateAnnualInvoicesPdfAsync(long farmId, int year)
    {
        var summary = await _service.GetAnnualFinanceSummaryAsync(farmId, year);
        var rows = await _service.GetInvoicesAsync(farmId, year);
        var path = _storage.GetReportExportPath($"فواتير-{Safe(summary.FarmName)}-{year}.pdf");
        Generate(path, $"تقرير الفواتير السنوي {year}", summary.FarmName, column =>
        {
            KeyValue(column, "إجمالي المبيعات", Money(summary.GrossSales));
            KeyValue(column, "المبيعات المحصلة", Money(summary.CollectedSales));
            KeyValue(column, "مصروفات الفواتير", Money(summary.InvoiceExpenses));
            KeyValue(column, "الزكاة", Money(summary.Zakat));
            KeyValue(column, "الربح المحاسبي", Money(summary.AccountingProfit));
            Space(column); Section(column, "الفواتير");
            foreach (var row in rows.OrderBy(x => x.Invoice.InvoiceDate))
                Line(column, $"{row.Invoice.InvoiceDate:yyyy/MM/dd} | {row.Invoice.InvoiceNumber} | {row.CustomerName} | {Money(row.Invoice.GrossAmount)} | متبقي {Money(row.Invoice.AmountDue)}");
        });
        return path;
    }

    public async Task<string> CreateMonthlyInvoicesPdfAsync(long? farmId, int year, int month)
    {
        if (month is < 1 or > 12) throw new InvalidOperationException("الشهر غير صحيح.");
        var rows = await _service.GetInvoicesAsync(farmId, year, month);
        var farmName = farmId.HasValue && farmId.Value > 0 ? rows.FirstOrDefault()?.FarmName ?? "المزرعة المحددة" : "كل المزارع";
        var path = _storage.GetReportExportPath($"فواتير-{year}-{month:00}-{Safe(farmName)}.pdf");
        Generate(path, $"تقرير المبيعات الشهري {year}/{month:00}", farmName, column =>
        {
            var posted = rows.Where(x => x.Invoice.Status == InvoiceStatus.Posted).ToList();
            KeyValue(column, "عدد الفواتير", posted.Count.ToString("N0"));
            KeyValue(column, "إجمالي المبيعات", Money(posted.Sum(x => x.Invoice.GrossAmount)));
            KeyValue(column, "إجمالي المدفوع", Money(posted.Sum(x => x.Invoice.AmountPaid)));
            KeyValue(column, "إجمالي المتبقي", Money(posted.Sum(x => x.Invoice.AmountDue)));
            Space(column); Section(column, "تفاصيل الفواتير");
            foreach (var row in posted.OrderBy(x => x.Invoice.InvoiceDate))
                Line(column, $"{row.Invoice.InvoiceDate:yyyy/MM/dd} | {row.Invoice.InvoiceNumber} | {row.CustomerName} | {Money(row.Invoice.GrossAmount)}");
        });
        return path;
    }

    public async Task<string> CreateExecutiveAccountingPdfAsync(long? farmId, int year)
    {
        var s = await _service.GetAccountingCenterAsync(year, farmId);
        var path = _storage.GetReportExportPath($"المحاسبة-{year}-{Safe(s.FarmName)}.pdf");
        Generate(path, $"المركز المالي والتحليل المحاسبي {year}", s.FarmName, column =>
        {
            KeyValue(column, "إجمالي المبيعات", Money(s.GrossSales));
            KeyValue(column, "المبيعات المحصلة", Money(s.CollectedSales));
            KeyValue(column, "ذمم العملاء", Money(s.CustomerReceivables));
            KeyValue(column, "مصروفات الفواتير", Money(s.InvoiceExpenses));
            KeyValue(column, "مصروفات التربية", Money(s.CultivationExpenses));
            KeyValue(column, "التزامات التربية", Money(s.CultivationPayables));
            KeyValue(column, "الزكاة المستحقة", Money(s.ZakatAccrued));
            KeyValue(column, "الزكاة المدفوعة", Money(s.ZakatPaid));
            KeyValue(column, "الربح المحاسبي", Money(s.AccountingProfit));
            KeyValue(column, "صافي التدفق النقدي", Money(s.NetCashFlow));
            KeyValue(column, "نسبة التحصيل", $"{s.CollectionPercent:N1}%");
            KeyValue(column, "هامش الربح", $"{s.NetMarginPercent:N1}%");
            Space(column); Section(column, "مقارنة المزارع");
            foreach (var farm in s.Farms)
                Line(column, $"{farm.FarmName} | مبيعات {Money(farm.Sales)} | تكاليف {Money(farm.Costs)} | صافي {Money(farm.NetProfit)}");
        });
        return path;
    }

    public async Task<string> CreateAnnualCultivationPdfAsync(long farmId, int year)
    {
        var rows = await _service.GetCultivationExpensesAsync(farmId, year);
        var farmName = rows.FirstOrDefault()?.FarmName ?? $"مزرعة {farmId}";
        var path = _storage.GetReportExportPath($"التربية-{year}-{Safe(farmName)}.pdf");
        Generate(path, $"تقرير خسائر ومصروفات التربية {year}", farmName, column =>
        {
            KeyValue(column, "إجمالي المصروفات", Money(rows.Sum(x => x.Expense.Amount)));
            KeyValue(column, "إجمالي المدفوع", Money(rows.Sum(x => x.Expense.PaidAmount)));
            KeyValue(column, "إجمالي المتبقي", Money(rows.Sum(x => x.Outstanding)));
            Space(column); Section(column, "الحركة");
            foreach (var row in rows.OrderBy(x => x.Expense.ExpenseDate))
                Line(column, $"{row.Expense.ExpenseDate:yyyy/MM/dd} | {row.ExpenseTypeName} | {Money(row.Expense.Amount)} | مدفوع {Money(row.Expense.PaidAmount)} | متبقي {Money(row.Outstanding)}");
        });
        return path;
    }

    public Task SharePdfAsync(string path, string title)
    {
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    private static void Generate(string path, string title, string subtitle, Action<ColumnDescriptor> content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(11));
                page.Header().Column(header =>
                {
                    header.Item().AlignRight().Text("AWAD SOFT — عواد سوفت").Bold().FontSize(11).FontColor(Colors.Green.Darken2);
                    header.Item().AlignRight().Text(title).Bold().FontSize(20).FontColor(Colors.Grey.Darken4);
                    header.Item().AlignRight().Text(subtitle).FontSize(11).FontColor(Colors.Grey.Darken1);
                    header.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Green.Medium);
                });
                page.Content().PaddingVertical(16).Column(content);
                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("نظام زراعي عواد سوفت  •  ");
                    x.CurrentPageNumber();
                    x.Span(" / ");
                    x.TotalPages();
                });
            });
        }).GeneratePdf(path);
    }

    private static void KeyValue(ColumnDescriptor column, string key, string value) =>
        column.Item().PaddingVertical(3).Row(row =>
        {
            row.RelativeItem().AlignRight().Text(value).Bold();
            row.RelativeItem().AlignRight().Text(key).FontColor(Colors.Grey.Darken1);
        });

    private static void Section(ColumnDescriptor column, string text) =>
        column.Item().PaddingBottom(5).AlignRight().Text(text).Bold().FontSize(14).FontColor(Colors.Green.Darken2);

    private static void Line(ColumnDescriptor column, string text) =>
        column.Item().PaddingVertical(3).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingBottom(5).AlignRight().Text(text);

    private static void Space(ColumnDescriptor column) => column.Item().Height(10);
    private static string Money(decimal value) => $"{value:N2} ر.ي";
    private static string PaymentText(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "نقدي",
        PaymentMethod.Transfer => "تحويل",
        PaymentMethod.Credit => "آجل",
        PaymentMethod.Mixed => "مختلط",
        _ => method.ToString()
    };
    private static string Safe(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "بدون-اسم" : value;
        foreach (var c in Path.GetInvalidFileNameChars()) text = text.Replace(c, '-');
        return text;
    }
}
