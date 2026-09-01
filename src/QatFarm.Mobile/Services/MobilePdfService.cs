#if ANDROID
using AndroidCanvas = Android.Graphics.Canvas;
using AndroidColor = Android.Graphics.Color;
using AndroidPaint = Android.Graphics.Paint;
using AndroidPaintFlags = Android.Graphics.PaintFlags;
using AndroidPdfDocument = Android.Graphics.Pdf.PdfDocument;
using AndroidTypeface = Android.Graphics.Typeface;
using AndroidTypefaceStyle = Android.Graphics.TypefaceStyle;
#endif
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using QatFarm.Mobile.Models;

namespace QatFarm.Mobile.Services;

public sealed class MobilePdfService
{
    private readonly QatFarmService _service;
    public MobilePdfService(QatFarmService service) => _service = service;

    public async Task<string> CreateInvoicePdfAsync(long invoiceId)
    {
#if ANDROID
        var details = await _service.GetInvoiceDetailsAsync(invoiceId);
        var path = Path.Combine(FileSystem.CacheDirectory, $"Invoice-{details.Invoice.InvoiceNumber}.pdf");
        using var document = new AndroidPdfDocument();
        var writer = new PdfWriter(document, "فاتورة بيع", details.Farm.Name);
        WriteInvoiceDetails(writer, details);
        writer.Finish(path);
        return path;
#else
        throw new PlatformNotSupportedException();
#endif
    }

    public async Task<string> CreateAnnualInvoicesPdfAsync(long farmId, int year)
    {
#if ANDROID
        var summary = await _service.GetAnnualFinanceSummaryAsync(farmId, year);
        var rows = await _service.GetInvoicesAsync(farmId, year);
        var path = Path.Combine(FileSystem.CacheDirectory, $"Invoices-{farmId}-{year}.pdf");
        using var document = new AndroidPdfDocument();
        var writer = new PdfWriter(document, $"تقرير الفواتير {year}", summary.FarmName);
        writer.Title($"تقرير الفواتير السنوي — {summary.FarmName} — {year}");
        writer.Section("الملخص السنوي");
        writer.KeyValue("عدد الفواتير", rows.Count(x => x.Invoice.Status == InvoiceStatus.Posted).ToString("N0"));
        writer.KeyValue("إجمالي المبيعات", summary.GrossSales.ToString("N2"));
        writer.KeyValue("المبيعات المحصلة", summary.CollectedSales.ToString("N2"));
        writer.KeyValue("مصروفات الفواتير", summary.InvoiceExpenses.ToString("N2"));
        writer.KeyValue("الزكاة", summary.Zakat.ToString("N2"));
        writer.KeyValue("الربح المحاسبي", summary.AccountingProfit.ToString("N2"));
        foreach (var row in rows.OrderBy(x => x.Invoice.InvoiceDate))
        {
            writer.PageBreak();
            var details = await _service.GetInvoiceDetailsAsync(row.Invoice.Id);
            WriteInvoiceDetails(writer, details);
        }
        writer.Finish(path);
        return path;
#else
        throw new PlatformNotSupportedException();
#endif
    }

    public async Task<string> CreateMonthlyInvoicesPdfAsync(long? farmId, int year, int month)
    {
#if ANDROID
        if (month < 1 || month > 12) throw new InvalidOperationException("الشهر غير صحيح.");
        var rows = await _service.GetInvoicesAsync(farmId, year, month);
        var posted = rows.Where(x => x.Invoice.Status == InvoiceStatus.Posted).ToList();
        var farmName = farmId.HasValue && farmId.Value > 0
            ? rows.FirstOrDefault()?.FarmName ?? "المزرعة المحددة"
            : "كل المزارع";
        var monthName = new DateTime(year, month, 1).ToString("yyyy/MM");
        var path = Path.Combine(FileSystem.CacheDirectory, $"Invoices-Month-{farmId ?? 0}-{year}-{month:00}.pdf");
        using var document = new AndroidPdfDocument();
        var writer = new PdfWriter(document, $"فواتير شهر {monthName}", farmName);
        writer.Title($"تقرير المبيعات الشهري — {farmName} — {monthName}");
        writer.Section("ملخص الشهر");
        writer.KeyValue("عدد الفواتير", posted.Count.ToString("N0"));
        writer.KeyValue("إجمالي المبيعات", posted.Sum(x => x.Invoice.GrossAmount).ToString("N2"));
        writer.KeyValue("المدفوع", posted.Sum(x => x.Invoice.AmountPaid).ToString("N2"));
        writer.KeyValue("المتبقي", posted.Sum(x => x.Invoice.AmountDue).ToString("N2"));
        writer.KeyValue("الزكاة", posted.Sum(x => x.Invoice.ZakatAmount).ToString("N2"));
        foreach (var row in rows.OrderBy(x => x.Invoice.InvoiceDate).ThenBy(x => x.Invoice.Id))
        {
            writer.PageBreak();
            var details = await _service.GetInvoiceDetailsAsync(row.Invoice.Id);
            WriteInvoiceDetails(writer, details);
        }
        writer.Finish(path);
        return path;
#else
        throw new PlatformNotSupportedException();
#endif
    }

    public async Task<string> CreateExecutiveAccountingPdfAsync(long? farmId, int year)
    {
#if ANDROID
        var summary = await _service.GetAccountingCenterAsync(year, farmId);
        var path = Path.Combine(FileSystem.CacheDirectory, $"Accounting-{farmId ?? 0}-{year}.pdf");
        using var document = new AndroidPdfDocument();
        var writer = new PdfWriter(document, $"المركز المالي التنفيذي {year}", summary.FarmName);
        writer.Title($"التقرير المحاسبي التنفيذي — {summary.FarmName}");
        writer.Section("المؤشرات الرئيسية");
        writer.KeyValue("إجمالي المبيعات", summary.GrossSales.ToString("N2"));
        writer.KeyValue("المبيعات المحصلة", summary.CollectedSales.ToString("N2"));
        writer.KeyValue("ذمم العملاء", summary.CustomerReceivables.ToString("N2"));
        writer.KeyValue("مصروفات الفواتير", summary.InvoiceExpenses.ToString("N2"));
        writer.KeyValue("خسائر التربية", summary.CultivationExpenses.ToString("N2"));
        writer.KeyValue("التزامات التربية", summary.CultivationPayables.ToString("N2"));
        writer.KeyValue("الزكاة المستحقة", summary.ZakatAccrued.ToString("N2"));
        writer.KeyValue("الربح المحاسبي", summary.AccountingProfit.ToString("N2"));
        writer.KeyValue("نسبة التحصيل", $"{summary.CollectionPercent:N1}%");
        writer.KeyValue("هامش الربح", $"{summary.NetMarginPercent:N1}%");
        writer.KeyValue("صافي التدفق النقدي", summary.NetCashFlow.ToString("N2"));

        writer.Space(8);
        writer.Section("الأداء الشهري");
        writer.TableHeader("الشهر", "المبيعات", "التكاليف", "صافي الربح");
        foreach (var month in summary.Months)
            writer.TableRow(month.MonthName, month.Sales.ToString("N2"), month.Costs.ToString("N2"), month.NetProfit.ToString("N2"));

        if (summary.Farms.Count > 0)
        {
            writer.Space(8);
            writer.Section("مقارنة المزارع");
            writer.TableHeader("المزرعة", "المبيعات", "التكاليف", "الصافي");
            foreach (var farm in summary.Farms)
                writer.TableRow(farm.FarmName, farm.Sales.ToString("N2"), farm.Costs.ToString("N2"), farm.NetProfit.ToString("N2"));
        }

        if (summary.RecentCashMovements.Count > 0)
        {
            writer.Space(8);
            writer.Section("أحدث الحركة النقدية");
            writer.TableHeader("العملية", "التاريخ", "قبض", "صرف");
            foreach (var movement in summary.RecentCashMovements.Take(30))
                writer.TableRow(movement.Kind, movement.Date.ToString("yyyy/MM/dd"),
                    movement.Inflow.ToString("N2"), movement.Outflow.ToString("N2"));
        }

        writer.Finish(path);
        return path;
#else
        throw new PlatformNotSupportedException();
#endif
    }

#if ANDROID
    private static void WriteInvoiceDetails(PdfWriter writer,
        (SalesInvoice Invoice, Farm Farm, Customer? Customer, List<SalesInvoiceItem> Items,
         List<InvoiceExpense> Expenses, Dictionary<long,string> QatTypes, Dictionary<long,string> ExpenseTypes) details)
    {
        writer.Title($"فاتورة بيع رقم {details.Invoice.InvoiceNumber}");
        writer.KeyValue("المزرعة", details.Farm.Name);
        writer.KeyValue("العميل", details.Customer?.Name ?? details.Invoice.BuyerName ?? "نقدي");
        writer.KeyValue("هاتف العميل", details.Customer?.Phone ?? details.Invoice.BuyerPhone ?? "-");
        writer.KeyValue("التاريخ", details.Invoice.InvoiceDate.ToString("yyyy/MM/dd"));
        writer.KeyValue("طريقة الدفع", PaymentMethodText(details.Invoice.PaymentMethod));
        writer.KeyValue("حالة الفاتورة", details.Invoice.Status == InvoiceStatus.Cancelled ? "ملغاة" : "مرحلة");
        writer.Space(8);
        writer.Section("الأصناف");
        writer.TableHeader("الصنف", "الكمية", "السعر", "الإجمالي");
        foreach (var item in details.Items)
            writer.TableRow(details.QatTypes.GetValueOrDefault(item.QatTypeId, "غير معروف"),
                item.Quantity.ToString("N0"), item.UnitPrice.ToString("N2"), item.TotalPrice.ToString("N2"));

        writer.Space(8);
        writer.Section("المصروفات");
        if (details.Expenses.Count == 0) writer.Paragraph("لا توجد مصروفات مرتبطة بالفاتورة.");
        else
        {
            writer.TableHeader("النوع", "المبلغ", "الملاحظة", "");
            foreach (var expense in details.Expenses)
                writer.TableRow(details.ExpenseTypes.GetValueOrDefault(expense.ExpenseTypeId, "غير معروف"),
                    expense.Amount.ToString("N2"), expense.Notes ?? "-", "");
        }

        writer.Space(8);
        writer.Section("الملخص المالي");
        writer.KeyValue("إجمالي المبيعات", details.Invoice.GrossAmount.ToString("N2"));
        writer.KeyValue("الزكاة", details.Invoice.ZakatAmount.ToString("N2"));
        writer.KeyValue("حالة الزكاة", ZakatStatusText(details.Invoice.ZakatStatus));
        if (details.Invoice.ZakatStatus == ZakatPaymentStatus.Paid)
        {
            writer.KeyValue("مستلم الزكاة", details.Invoice.ZakatRecipientName ?? "-");
            writer.KeyValue("مرجع الزكاة", details.Invoice.ZakatPaymentReference ?? "-");
            writer.KeyValue("تاريخ دفع الزكاة", details.Invoice.ZakatPaidAt?.ToString("yyyy/MM/dd HH:mm") ?? "-");
        }
        writer.KeyValue("المصروفات", details.Invoice.TotalExpenses.ToString("N2"));
        writer.KeyValue("الصافي", details.Invoice.NetAmount.ToString("N2"));
        writer.KeyValue("المدفوع", details.Invoice.AmountPaid.ToString("N2"));
        writer.KeyValue("المتبقي", details.Invoice.AmountDue.ToString("N2"));
        if (!string.IsNullOrWhiteSpace(details.Invoice.Notes))
        {
            writer.Section("ملاحظات");
            writer.Paragraph(details.Invoice.Notes);
        }
    }
#endif

    public async Task<string> CreateAnnualCultivationPdfAsync(long farmId, int year)
    {
#if ANDROID
        var summary = await _service.GetAnnualFinanceSummaryAsync(farmId, year);
        var rows = await _service.GetCultivationExpensesAsync(farmId, year);
        var payments = await _service.GetCultivationDebtPaymentsAsync(rows.Select(x => x.Expense.Id));
        var paymentsByExpense = payments.GroupBy(x => x.CultivationExpenseId)
            .ToDictionary(x => x.Key, x => x.OrderBy(p => p.PaymentDate).ToList());
        var path = Path.Combine(FileSystem.CacheDirectory, $"Cultivation-{farmId}-{year}.pdf");
        using var document = new AndroidPdfDocument();
        var writer = new PdfWriter(document, $"تقرير خسائر التربية {year}", summary.FarmName);
        writer.Title($"تقرير خسائر التربية — {summary.FarmName} — {year}");
        writer.Section("الملخص");
        writer.KeyValue("إجمالي المبيعات", summary.GrossSales.ToString("N2"));
        writer.KeyValue("المبيعات المحصلة", summary.CollectedSales.ToString("N2"));
        writer.KeyValue("خسائر التربية", summary.CultivationExpenses.ToString("N2"));
        writer.KeyValue("ديون التربية المتبقية", summary.CultivationDebtOutstanding.ToString("N2"));
        writer.KeyValue("الربح المحاسبي", summary.AccountingProfit.ToString("N2"));
        writer.KeyValue("الربح الآمن للتوزيع", summary.SafeDistributableProfit.ToString("N2"));
        writer.Space(10);
        writer.Section("تفاصيل الخسائر والديون");
        writer.TableHeader("النوع / الدائن", "الإجمالي", "المدفوع", "المتبقي");
        foreach (var row in rows)
        {
            var title = row.ExpenseTypeName +
                        (string.IsNullOrWhiteSpace(row.CreditorName) ? "" : $" — {row.CreditorName}");
            writer.TableRow(title, row.Expense.Amount.ToString("N2"),
                row.Expense.PaidAmount.ToString("N2"), row.Outstanding.ToString("N2"));
            writer.SmallText($"{row.Expense.ExpenseDate:yyyy/MM/dd} | الاستحقاق: {(row.Expense.DueDate.HasValue ? row.Expense.DueDate.Value.ToString("yyyy/MM/dd") : "-")} | السند: {row.Expense.ReceiptNumber}");
            if (!string.IsNullOrWhiteSpace(row.Expense.Notes)) writer.SmallText($"ملاحظة: {row.Expense.Notes}");
            if (paymentsByExpense.TryGetValue(row.Expense.Id, out var expensePayments))
                foreach (var payment in expensePayments)
                    writer.SmallText($"دفعة {payment.PaymentDate:yyyy/MM/dd}: {payment.Amount:N2} — {PaymentMethodText(payment.PaymentMethod)} — {payment.ReferenceNumber ?? "بدون مرجع"}");
        }
        writer.Finish(path);
        return path;
#else
        throw new PlatformNotSupportedException();
#endif
    }

    public Task SharePdfAsync(string path, string title) =>
        Share.Default.RequestAsync(new ShareFileRequest { Title = title, File = new ShareFile(path) });

    private static string ZakatStatusText(ZakatPaymentStatus status) => status switch
    {
        ZakatPaymentStatus.Pending => "معلقة",
        ZakatPaymentStatus.Paid => "مدفوعة",
        ZakatPaymentStatus.NotApplicable => "غير مطبقة",
        _ => status.ToString()
    };

    private static string PaymentMethodText(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "نقدي",
        PaymentMethod.Transfer => "تحويل",
        PaymentMethod.Credit => "آجل",
        PaymentMethod.Mixed => "مختلط",
        _ => method.ToString()
    };

#if ANDROID
    private sealed class PdfWriter
    {
        private readonly AndroidPdfDocument _document;
        private readonly string _header;
        private readonly string _subHeader;
        private AndroidPdfDocument.Page? _page;
        private AndroidCanvas? _canvas;
        private readonly AndroidPaint _paint = new(AndroidPaintFlags.AntiAlias);
        private int _pageNumber;
        private float _y;

        private const int Width = 595;
        private const int Height = 842;
        private const float Right = 555;
        private const float Left = 40;
        private const float Bottom = 800;

        public PdfWriter(AndroidPdfDocument document, string header, string subHeader)
        {
            _document = document;
            _header = header;
            _subHeader = subHeader;
            NewPage();
        }

        private void NewPage()
        {
            if (_page is not null) _document.FinishPage(_page);
            _pageNumber++;
            var info = new AndroidPdfDocument.PageInfo.Builder(Width, Height, _pageNumber).Create();
            _page = _document.StartPage(info)
                ?? throw new InvalidOperationException("تعذر إنشاء صفحة تقرير PDF.");
            _canvas = _page.Canvas
                ?? throw new InvalidOperationException("تعذر تهيئة لوحة رسم تقرير PDF.");
            _canvas.DrawColor(AndroidColor.White);

            _paint.SetTypeface(AndroidTypeface.Create("sans-serif", AndroidTypefaceStyle.Bold));
            _paint.TextAlign = AndroidPaint.Align.Right;
            _paint.Color = AndroidColor.Rgb(15, 81, 50);
            _paint.TextSize = 16;
            _canvas.DrawText(_header, Right, 35, _paint);

            _paint.SetTypeface(AndroidTypeface.Create("sans-serif", AndroidTypefaceStyle.Normal));
            _paint.TextSize = 10;
            _paint.Color = AndroidColor.DarkGray;
            _canvas.DrawText(_subHeader, Right, 53, _paint);
            _paint.TextAlign = AndroidPaint.Align.Left;
            _canvas.DrawText($"صفحة {_pageNumber}", Left, 35, _paint);
            _y = 75;
        }

        private void Ensure(float required) { if (_y + required > Bottom) NewPage(); }

        public void PageBreak() => NewPage();

        public void Title(string text)
        {
            Ensure(50);
            _paint.SetTypeface(AndroidTypeface.Create("sans-serif", AndroidTypefaceStyle.Bold));
            _paint.TextSize = 20;
            _paint.Color = AndroidColor.Rgb(15, 81, 50);
            _paint.TextAlign = AndroidPaint.Align.Center;
            _canvas!.DrawText(text, Width / 2f, _y + 25, _paint);
            _y += 48;
        }

        public void Section(string text)
        {
            Ensure(36);
            _paint.SetTypeface(AndroidTypeface.Create("sans-serif", AndroidTypefaceStyle.Bold));
            _paint.TextSize = 14;
            _paint.Color = AndroidColor.Rgb(45, 106, 79);
            _paint.TextAlign = AndroidPaint.Align.Right;
            _canvas!.DrawText(text, Right, _y + 20, _paint);
            _paint.StrokeWidth = 1;
            _canvas.DrawLine(Left, _y + 27, Right, _y + 27, _paint);
            _y += 34;
        }

        public void KeyValue(string label, string value)
        {
            Ensure(26);
            _paint.TextSize = 11;
            _paint.SetTypeface(AndroidTypeface.Create("sans-serif", AndroidTypefaceStyle.Bold));
            _paint.Color = AndroidColor.Black;
            _paint.TextAlign = AndroidPaint.Align.Right;
            _canvas!.DrawText(label, Right, _y + 17, _paint);
            _paint.SetTypeface(AndroidTypeface.Create("sans-serif", AndroidTypefaceStyle.Normal));
            _paint.TextAlign = AndroidPaint.Align.Left;
            _canvas.DrawText(value, Left, _y + 17, _paint);
            _y += 24;
        }

        public void Paragraph(string text)
        {
            foreach (var line in Wrap(text, 75))
            {
                Ensure(20);
                _paint.SetTypeface(AndroidTypeface.Create("sans-serif", AndroidTypefaceStyle.Normal));
                _paint.TextSize = 10;
                _paint.Color = AndroidColor.Black;
                _paint.TextAlign = AndroidPaint.Align.Right;
                _canvas!.DrawText(line, Right, _y + 15, _paint);
                _y += 19;
            }
        }

        public void TableHeader(string c1, string c2, string c3, string c4)
        {
            Ensure(30);
            _paint.Color = AndroidColor.Rgb(216, 243, 220);
            _paint.SetStyle(AndroidPaint.Style.Fill);
            _canvas!.DrawRect(Left, _y, Right, _y + 26, _paint);
            DrawTableText(c1, c2, c3, c4, true);
            _y += 28;
        }

        public void TableRow(string c1, string c2, string c3, string c4)
        {
            Ensure(34);
            _paint.Color = AndroidColor.Rgb(210, 220, 214);
            _paint.SetStyle(AndroidPaint.Style.Stroke);
            _canvas!.DrawRect(Left, _y, Right, _y + 30, _paint);
            _paint.SetStyle(AndroidPaint.Style.Fill);
            DrawTableText(Trim(c1, 28), Trim(c2, 16), Trim(c3, 20), Trim(c4, 16), false);
            _y += 31;
        }

        private void DrawTableText(string c1, string c2, string c3, string c4, bool bold)
        {
            var xs = new[] { Right - 5, 385f, 245f, 105f };
            var values = new[] { c1, c2, c3, c4 };
            _paint.SetTypeface(AndroidTypeface.Create("sans-serif", bold ? AndroidTypefaceStyle.Bold : AndroidTypefaceStyle.Normal));
            _paint.TextSize = 9;
            _paint.Color = AndroidColor.Black;
            _paint.TextAlign = AndroidPaint.Align.Right;
            for (var i = 0; i < values.Length; i++)
                _canvas!.DrawText(values[i] ?? string.Empty, xs[i], _y + 18, _paint);
        }

        public void SmallText(string text)
        {
            Ensure(18);
            _paint.SetTypeface(AndroidTypeface.Create("sans-serif", AndroidTypefaceStyle.Normal));
            _paint.TextSize = 8;
            _paint.Color = AndroidColor.DarkGray;
            _paint.TextAlign = AndroidPaint.Align.Right;
            _canvas!.DrawText(Trim(text, 90), Right, _y + 12, _paint);
            _y += 16;
        }

        public void Space(float amount) { Ensure(amount); _y += amount; }

        public void Finish(string path)
        {
            if (_page is not null) { _document.FinishPage(_page); _page = null; }
            using var stream = File.Create(path);
            _document.WriteTo(stream);
        }

        private static IEnumerable<string> Wrap(string text, int max)
        {
            if (string.IsNullOrWhiteSpace(text)) yield break;
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var line = string.Empty;
            foreach (var word in words)
            {
                if (line.Length + word.Length + 1 > max)
                {
                    yield return line;
                    line = word;
                }
                else line = string.IsNullOrEmpty(line) ? word : $"{line} {word}";
            }
            if (!string.IsNullOrEmpty(line)) yield return line;
        }

        private static string Trim(string? value, int max)
        {
            value ??= string.Empty;
            return value.Length <= max ? value : value[..(max - 1)] + "…";
        }
    }
#endif
}
