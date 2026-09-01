using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QatFarm.Web.Data;
using QatFarm.Web.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace QatFarm.Web.Services;

public sealed class PdfReportService(IDbContextFactory<ApplicationDbContext> factory, IConfiguration configuration)
{
    private string Currency => configuration["System:Currency"] ?? "ريال يمني";

    public async Task<byte[]?> CreateInvoicePdfAsync(long id)
    {
        await using var db = await factory.CreateDbContextAsync();

        // IgnoreQueryFilters is intentional for historical printing: an invoice must remain printable
        // even if its farm, customer, item type, or expense type was later disabled or soft-deleted.
        var invoice = await db.SalesInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Farm)
            .Include(x => x.Customer)
            .Include(x => x.Items).ThenInclude(x => x.QatType)
            .Include(x => x.Expenses).ThenInclude(x => x.ExpenseType)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id);

        if (invoice is null)
            return null;

        var items = invoice.Items.OrderBy(x => x.Id).ToList();
        var expenses = invoice.Expenses.OrderBy(x => x.Id).ToList();
        var farmName = invoice.Farm?.Name ?? $"مزرعة رقم {invoice.FarmId}";
        var customerName = invoice.Customer?.Name ?? invoice.BuyerName ?? "بيع نقدي";
        var customerPhone = invoice.Customer?.Phone ?? invoice.BuyerPhone ?? "—";
        var itemTotal = items.Sum(x => x.TotalPrice);
        var expenseTotal = expenses.Sum(x => x.Amount);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(24);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x
                    .FontFamily("Arial", "Tahoma", "Segoe UI", "Lato")
                    .FontColor(Colors.Black)
                    .FontSize(10));
                page.ContentFromRightToLeft();

                page.Header().Column(header =>
                {
                    header.Spacing(4);
                    header.Item().AlignCenter().Text("نظام إدارة مزارع وبيع القات")
                        .Bold().FontSize(18).FontColor(Colors.Green.Darken3);
                    header.Item().AlignCenter().Text($"فاتورة بيع محصول — {farmName} — سنة {invoice.InvoiceDate.Year}")
                        .Bold().FontSize(14);
                    header.Item().AlignCenter().Text(InvoiceStatusText(invoice.Status))
                        .Bold()
                        .FontColor(invoice.Status == InvoiceStatus.Cancelled
                            ? Colors.Red.Darken2
                            : Colors.Green.Darken2);
                });

                page.Content().PaddingVertical(12).Column(col =>
                {
                    col.Spacing(9);

                    if (invoice.Status == InvoiceStatus.Cancelled || invoice.IsDeleted)
                    {
                        col.Item()
                            .Border(1)
                            .BorderColor(Colors.Red.Medium)
                            .Background(Colors.Red.Lighten4)
                            .Padding(8)
                            .AlignCenter()
                            .Text(invoice.IsDeleted ? "فاتورة محذوفة منطقيًا" : "فاتورة ملغاة")
                            .Bold().FontColor(Colors.Red.Darken3);
                    }

                    col.Item().Table(info =>
                    {
                        info.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        info.Cell().Element(InvoiceInfoCell).Column(c =>
                        {
                            c.Item().Text("رقم الفاتورة").FontSize(9).FontColor(Colors.Grey.Darken2);
                            c.Item().ContentFromLeftToRight().Text(invoice.InvoiceNumber).Bold();
                        });
                        info.Cell().Element(InvoiceInfoCell).Column(c =>
                        {
                            c.Item().Text("تاريخ الفاتورة").FontSize(9).FontColor(Colors.Grey.Darken2);
                            c.Item().ContentFromLeftToRight().Text(GregorianDateTime(invoice.InvoiceDate)).Bold();
                        });
                        info.Cell().Element(InvoiceInfoCell).Column(c =>
                        {
                            c.Item().Text("المزرعة").FontSize(9).FontColor(Colors.Grey.Darken2);
                            c.Item().Text(farmName).Bold();
                        });
                        info.Cell().Element(InvoiceInfoCell).Column(c =>
                        {
                            c.Item().Text("العميل / المشتري").FontSize(9).FontColor(Colors.Grey.Darken2);
                            c.Item().Text(customerName).Bold();
                        });
                        info.Cell().Element(InvoiceInfoCell).Column(c =>
                        {
                            c.Item().Text("الهاتف").FontSize(9).FontColor(Colors.Grey.Darken2);
                            c.Item().ContentFromLeftToRight().Text(customerPhone).Bold();
                        });
                        info.Cell().Element(InvoiceInfoCell).Column(c =>
                        {
                            c.Item().Text("طريقة الدفع").FontSize(9).FontColor(Colors.Grey.Darken2);
                            c.Item().Text(PaymentMethodText(invoice.PaymentMethod)).Bold();
                        });
                    });

                    col.Item().PaddingTop(3).Text("تفاصيل أصناف القات").Bold().FontSize(12);
                    col.Item().Table(table =>
                    {
                        // With RTL direction, the first declared column appears on the right.
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(1.6f);
                            columns.RelativeColumn(1.7f);
                            columns.ConstantColumn(28);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(InvoiceHeaderCell).Text("الصنف");
                            header.Cell().Element(InvoiceHeaderCell).Text("الكمية");
                            header.Cell().Element(InvoiceHeaderCell).Text("سعر الوحدة");
                            header.Cell().Element(InvoiceHeaderCell).Text("الإجمالي");
                            header.Cell().Element(InvoiceHeaderCell).Text("م");
                        });

                        if (items.Count == 0)
                        {
                            table.Cell().ColumnSpan(5).Element(InvoiceEmptyCell)
                                .Text("لا توجد أصناف محفوظة لهذه الفاتورة. افتح الفاتورة وعدّلها ثم أضف صنفًا واحدًا على الأقل.")
                                .FontColor(Colors.Red.Darken2);
                        }
                        else
                        {
                            for (var index = 0; index < items.Count; index++)
                            {
                                var item = items[index];
                                var itemName = item.QatType?.Name ?? $"صنف رقم {item.QatTypeId}";

                                table.Cell().Element(InvoiceBodyCell).Text(itemName);
                                table.Cell().Element(InvoiceNumberCell).ContentFromLeftToRight().Text(item.Quantity.ToString("N0"));
                                table.Cell().Element(InvoiceNumberCell).ContentFromLeftToRight().Text($"{item.UnitPrice:N0}");
                                table.Cell().Element(InvoiceNumberCell).ContentFromLeftToRight().Text($"{item.TotalPrice:N0}").Bold();
                                table.Cell().Element(InvoiceNumberCell).ContentFromLeftToRight().Text((index + 1).ToString());
                            }
                        }
                    });

                    col.Item().AlignLeft().ContentFromLeftToRight()
                        .Text($"مجموع الأصناف: {itemTotal:N0} {Currency}")
                        .Bold().FontSize(11);

                    col.Item().PaddingTop(3).Text("المصروفات المضافة إلى الفاتورة").Bold().FontSize(12);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2.4f);
                            columns.RelativeColumn(1.4f);
                            columns.RelativeColumn(3);
                            columns.ConstantColumn(28);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(ExpenseHeaderCell).Text("نوع المصروف");
                            header.Cell().Element(ExpenseHeaderCell).Text("المبلغ");
                            header.Cell().Element(ExpenseHeaderCell).Text("البيان");
                            header.Cell().Element(ExpenseHeaderCell).Text("م");
                        });

                        if (expenses.Count == 0)
                        {
                            table.Cell().ColumnSpan(4).Element(InvoiceEmptyCell)
                                .Text("لا توجد مصروفات مضافة إلى هذه الفاتورة.")
                                .FontColor(Colors.Grey.Darken2);
                        }
                        else
                        {
                            for (var index = 0; index < expenses.Count; index++)
                            {
                                var expense = expenses[index];
                                var expenseName = expense.ExpenseType?.Name ?? $"مصروف رقم {expense.ExpenseTypeId}";

                                table.Cell().Element(InvoiceBodyCell).Text(expenseName);
                                table.Cell().Element(InvoiceNumberCell).ContentFromLeftToRight().Text($"{expense.Amount:N0}").Bold();
                                table.Cell().Element(InvoiceBodyCell).Text(expense.Notes ?? "—");
                                table.Cell().Element(InvoiceNumberCell).ContentFromLeftToRight().Text((index + 1).ToString());
                            }
                        }
                    });

                    col.Item().AlignLeft().ContentFromLeftToRight()
                        .Text($"مجموع المصروفات: {expenseTotal:N0} {Currency}")
                        .Bold().FontSize(11);

                    col.Item().PaddingTop(5).AlignLeft().Table(summary =>
                    {
                        summary.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(1.7f);
                            columns.RelativeColumn(1.3f);
                        });

                        AddInvoiceSummaryRow(summary, "إجمالي البيع", invoice.GrossAmount, Currency, false);
                        AddInvoiceSummaryRow(summary, $"الزكاة ({invoice.ZakatPercent:N2}%)", invoice.ZakatAmount, Currency, false);
                        AddInvoiceSummaryRow(summary, "إجمالي المصروفات", invoice.TotalExpenses, Currency, false);
                        AddInvoiceSummaryRow(summary, "صافي العملية", invoice.NetAmount, Currency, true);
                        AddInvoiceSummaryRow(summary, "المبلغ المدفوع", invoice.AmountPaid, Currency, false);
                        AddInvoiceSummaryRow(summary, "المبلغ المتبقي", invoice.AmountDue, Currency, true);
                    });

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Element(InvoiceInfoCell).Column(c =>
                        {
                            c.Item().Text("حالة السداد").FontSize(9).FontColor(Colors.Grey.Darken2);
                            c.Item().Text(PaymentStatusText(invoice.PaymentStatus)).Bold();
                        });
                        row.RelativeItem().Element(InvoiceInfoCell).Column(c =>
                        {
                            c.Item().Text("حالة الزكاة").FontSize(9).FontColor(Colors.Grey.Darken2);
                            c.Item().Text(ZakatStatusText(invoice.ZakatStatus)).Bold()
                                .FontColor(invoice.ZakatStatus == ZakatPaymentStatus.Paid
                                    ? Colors.Green.Darken2
                                    : Colors.Red.Darken2);
                        });
                    });

                    if (!string.IsNullOrWhiteSpace(invoice.Notes))
                    {
                        col.Item().Element(InvoiceInfoCell).Column(c =>
                        {
                            c.Item().Text("ملاحظات الفاتورة").Bold();
                            c.Item().Text(invoice.Notes);
                        });
                    }

                    col.Item().PaddingTop(14).Row(row =>
                    {
                        row.RelativeItem().AlignCenter().Text("توقيع المستلم: __________________");
                        row.RelativeItem().AlignCenter().Text("توقيع المسؤول: __________________");
                    });
                });

                page.Footer().Column(footer =>
                {
                    footer.Item().AlignCenter().Text(x =>
                    {
                        x.Span("صفحة ");
                        x.CurrentPageNumber();
                        x.Span(" من ");
                        x.TotalPages();
                    });
                    footer.Item().AlignCenter().ContentFromLeftToRight()
                        .Text($"Printed: {GregorianDateTime(DateTime.Now)}")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf();
    }

    public async Task<string> GetFarmFileLabelAsync(long? farmId)
    {
        if (!farmId.HasValue || farmId.Value <= 0)
            return "كل المزارع";

        await using var db = await factory.CreateDbContextAsync();
        return await db.Farms
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.Id == farmId.Value)
            .Select(x => x.Name)
            .FirstOrDefaultAsync() ?? $"مزرعة-{farmId.Value}";
    }

    public async Task<string> GetInvoiceFileNameAsync(long id)
    {
        await using var db = await factory.CreateDbContextAsync();
        var invoice = await db.SalesInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { x.FarmId, x.InvoiceDate, x.InvoiceNumber })
            .FirstOrDefaultAsync();

        if (invoice is null)
            return $"فاتورة-{id}.pdf";

        var farmName = await GetFarmFileLabelAsync(invoice.FarmId);
        return $"فاتورة-{farmName}-{invoice.InvoiceDate.Year}-{invoice.InvoiceNumber}.pdf";
    }

    public async Task<byte[]> CreateCultivationExpensesPdfAsync(long? farmId, int year)
    {
        ValidateYear(year);
        var from = new DateTime(year, 1, 1);
        var toExclusive = from.AddYears(1);

        await using var db = await factory.CreateDbContextAsync();
        var query = db.CultivationExpenses
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Farm)
            .Include(x => x.ExpenseType)
            .Where(x => !x.IsDeleted && x.ExpenseDate >= from && x.ExpenseDate < toExclusive)
            .AsQueryable();

        if (farmId.HasValue && farmId.Value > 0)
            query = query.Where(x => x.FarmId == farmId.Value);

        var rows = await query
            .OrderBy(x => x.Farm.Name)
            .ThenBy(x => x.ExpenseDate)
            .ToListAsync();

        var farmName = await GetFarmFileLabelAsync(farmId);
        var title = $"خسائر التربية — {farmName} — سنة {year}";

        return CreateSimpleReport(
            title,
            rows.Select(x => new[]
            {
                x.Farm.Name,
                x.ExpenseType.Name,
                x.Amount.ToString("N2"),
                GregorianDate(x.ExpenseDate),
                x.Notes ?? "—"
            }).ToList(),
            new[] { "المزرعة", "نوع الخسارة", "المبلغ", "التاريخ", "ملاحظات" },
            rows.Sum(x => x.Amount),
            farmName,
            year);
    }

    public async Task<byte[]> CreateSalesReportPdfAsync(long? farmId, int year)
    {
        ValidateYear(year);
        var from = new DateTime(year, 1, 1);
        var toExclusive = from.AddYears(1);

        await using var db = await factory.CreateDbContextAsync();

        // تقرير سنوي تفصيلي: نحمّل المزرعة والعميل وجميع الأصناف والمصروفات
        // حتى تظهر كل بيانات كل فاتورة داخل ملف PDF، وليس الملخص المالي فقط.
        var query = db.SalesInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Farm)
            .Include(x => x.Customer)
            .Include(x => x.Items).ThenInclude(x => x.QatType)
            .Include(x => x.Expenses).ThenInclude(x => x.ExpenseType)
            .AsSplitQuery()
            .Where(x => !x.IsDeleted &&
                        x.Status == InvoiceStatus.Posted &&
                        x.InvoiceDate >= from &&
                        x.InvoiceDate < toExclusive);

        if (farmId.HasValue && farmId.Value > 0)
            query = query.Where(x => x.FarmId == farmId.Value);

        var invoices = await query
            .OrderBy(x => x.Farm!.Name)
            .ThenBy(x => x.InvoiceDate)
            .ThenBy(x => x.Id)
            .ToListAsync();

        var selectedFarmName = await GetFarmFileLabelAsync(farmId);
        var totalGross = invoices.Sum(x => x.GrossAmount);
        var totalZakat = invoices.Sum(x => x.ZakatAmount);
        var totalExpenses = invoices.Sum(x => x.TotalExpenses);
        var totalNet = invoices.Sum(x => x.NetAmount);
        var totalPaid = invoices.Sum(x => x.AmountPaid);
        var totalDue = invoices.Sum(x => x.AmountDue);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(24);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x
                    .FontFamily("Arial", "Tahoma", "Segoe UI", "Lato")
                    .FontColor(Colors.Black)
                    .FontSize(9));
                page.ContentFromRightToLeft();

                page.Header().Column(header =>
                {
                    header.Spacing(3);
                    header.Item().AlignCenter().Text("نظام إدارة مزارع وبيع القات")
                        .Bold().FontSize(17).FontColor(Colors.Green.Darken3);
                    header.Item().AlignCenter().Text($"التقرير السنوي التفصيلي للفواتير — {selectedFarmName} — سنة {year}")
                        .Bold().FontSize(13);
                });

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(8);

                    col.Item().Border(1).BorderColor(Colors.Green.Lighten1)
                        .Background(Colors.Green.Lighten5).Padding(10).Column(summary =>
                    {
                        summary.Item().AlignCenter().Text("الملخص المالي السنوي").Bold().FontSize(13);
                        summary.Item().PaddingTop(7).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });

                            AddAnnualSummaryCell(table, "عدد الفواتير", invoices.Count.ToString("N0", CultureInfo.InvariantCulture), "");
                            AddAnnualSummaryCell(table, "إجمالي المبيعات", totalGross.ToString("N0", CultureInfo.InvariantCulture), Currency);
                            AddAnnualSummaryCell(table, "إجمالي الزكاة", totalZakat.ToString("N0", CultureInfo.InvariantCulture), Currency);
                            AddAnnualSummaryCell(table, "إجمالي المصروفات", totalExpenses.ToString("N0", CultureInfo.InvariantCulture), Currency);
                            AddAnnualSummaryCell(table, "إجمالي الصافي", totalNet.ToString("N0", CultureInfo.InvariantCulture), Currency);
                            AddAnnualSummaryCell(table, "إجمالي المدفوع", totalPaid.ToString("N0", CultureInfo.InvariantCulture), Currency);
                            AddAnnualSummaryCell(table, "إجمالي المتبقي", totalDue.ToString("N0", CultureInfo.InvariantCulture), Currency);
                            AddAnnualSummaryCell(table, "المزرعة", selectedFarmName, "");
                            AddAnnualSummaryCell(table, "السنة", year.ToString(CultureInfo.InvariantCulture), "");
                        });
                    });

                    if (invoices.Count == 0)
                    {
                        col.Item().Element(EmptyDataBox)
                            .Text("لا توجد فواتير مرحلة للمزرعة والسنة المحددتين.")
                            .AlignCenter().FontSize(13).FontColor(Colors.Red.Darken2);
                    }
                    else
                    {
                        for (var index = 0; index < invoices.Count; index++)
                        {
                            col.Item().PageBreak();
                            ComposeAnnualInvoiceDetails(col, invoices[index], index + 1, invoices.Count);
                        }
                    }
                });

                page.Footer().Row(footer =>
                {
                    footer.RelativeItem().AlignRight().Text($"{selectedFarmName} — سنة {year}")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                    footer.RelativeItem().AlignCenter().Text(x =>
                    {
                        x.Span("صفحة ");
                        x.CurrentPageNumber();
                        x.Span(" من ");
                        x.TotalPages();
                    });
                    footer.RelativeItem().AlignLeft().ContentFromLeftToRight()
                        .Text($"Printed: {GregorianDateTime(DateTime.Now)}")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf();
    }

    private void ComposeAnnualInvoiceDetails(
        ColumnDescriptor col,
        SalesInvoice invoice,
        int sequence,
        int invoiceCount)
    {
        var items = invoice.Items.OrderBy(x => x.Id).ToList();
        var expenses = invoice.Expenses.OrderBy(x => x.Id).ToList();
        var farmName = invoice.Farm?.Name ?? $"مزرعة رقم {invoice.FarmId}";
        var customerName = invoice.Customer?.Name ?? invoice.BuyerName ?? "بيع نقدي مباشر";
        var customerPhone = invoice.Customer?.Phone ?? invoice.BuyerPhone ?? "—";

        col.Item().BorderBottom(2).BorderColor(Colors.Green.Darken2).PaddingBottom(7).Row(row =>
        {
            row.RelativeItem().Column(c =>
            {
                c.Item().Text($"الفاتورة رقم {sequence} من {invoiceCount}").FontSize(9).FontColor(Colors.Grey.Darken1);
                c.Item().ContentFromLeftToRight().Text(invoice.InvoiceNumber).Bold().FontSize(14);
            });
            row.RelativeItem().AlignLeft().Column(c =>
            {
                c.Item().Text(farmName).Bold().FontSize(13).FontColor(Colors.Green.Darken3);
                c.Item().ContentFromLeftToRight().Text(GregorianDateTime(invoice.InvoiceDate)).Bold();
            });
        });

        col.Item().Table(info =>
        {
            info.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.RelativeColumn();
            });

            AddAnnualInfoCell(info, "العميل / المشتري", customerName, false);
            AddAnnualInfoCell(info, "رقم الهاتف", customerPhone, true);
            AddAnnualInfoCell(info, "طريقة الدفع", PaymentMethodText(invoice.PaymentMethod), false);
            AddAnnualInfoCell(info, "حالة السداد", PaymentStatusText(invoice.PaymentStatus), false);
            AddAnnualInfoCell(info, "تاريخ الاستحقاق", invoice.PaymentDueDate.HasValue ? GregorianDate(invoice.PaymentDueDate.Value) : "—", true);
            AddAnnualInfoCell(info, "حالة الزكاة", ZakatStatusText(invoice.ZakatStatus), false);
        });

        col.Item().PaddingTop(2).Text("تفاصيل الأصناف المباعة").Bold().FontSize(12);
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(3.2f);
                columns.RelativeColumn(1);
                columns.RelativeColumn(1.7f);
                columns.RelativeColumn(1.8f);
                columns.ConstantColumn(28);
            });

            table.Header(header =>
            {
                header.Cell().Element(InvoiceHeaderCell).Text("نوع القات");
                header.Cell().Element(InvoiceHeaderCell).Text("الكمية");
                header.Cell().Element(InvoiceHeaderCell).Text("سعر الوحدة");
                header.Cell().Element(InvoiceHeaderCell).Text("الإجمالي");
                header.Cell().Element(InvoiceHeaderCell).Text("م");
            });

            if (items.Count == 0)
            {
                table.Cell().ColumnSpan(5).Element(InvoiceEmptyCell)
                    .Text("لا توجد تفاصيل أصناف محفوظة لهذه الفاتورة.")
                    .FontColor(Colors.Red.Darken2);
            }
            else
            {
                for (var index = 0; index < items.Count; index++)
                {
                    var item = items[index];
                    var itemName = item.QatType?.Name ?? $"صنف رقم {item.QatTypeId}";

                    table.Cell().Element(InvoiceBodyCell).Text(itemName);
                    table.Cell().Element(InvoiceNumberCell).ContentFromLeftToRight().Text(item.Quantity.ToString("N0", CultureInfo.InvariantCulture));
                    table.Cell().Element(InvoiceNumberCell).ContentFromLeftToRight().Text(item.UnitPrice.ToString("N0", CultureInfo.InvariantCulture));
                    table.Cell().Element(InvoiceNumberCell).ContentFromLeftToRight().Text(item.TotalPrice.ToString("N0", CultureInfo.InvariantCulture)).Bold();
                    table.Cell().Element(InvoiceNumberCell).ContentFromLeftToRight().Text((index + 1).ToString(CultureInfo.InvariantCulture));
                }
            }
        });

        col.Item().PaddingTop(2).Text("تفاصيل مصروفات الفاتورة").Bold().FontSize(12);
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(2.4f);
                columns.RelativeColumn(1.4f);
                columns.RelativeColumn(3.2f);
                columns.ConstantColumn(28);
            });

            table.Header(header =>
            {
                header.Cell().Element(ExpenseHeaderCell).Text("نوع المصروف");
                header.Cell().Element(ExpenseHeaderCell).Text("المبلغ");
                header.Cell().Element(ExpenseHeaderCell).Text("البيان / الملاحظة");
                header.Cell().Element(ExpenseHeaderCell).Text("م");
            });

            if (expenses.Count == 0)
            {
                table.Cell().ColumnSpan(4).Element(InvoiceEmptyCell)
                    .Text("لا توجد مصروفات مضافة إلى هذه الفاتورة.");
            }
            else
            {
                for (var index = 0; index < expenses.Count; index++)
                {
                    var expense = expenses[index];
                    var expenseName = expense.ExpenseType?.Name ?? $"مصروف رقم {expense.ExpenseTypeId}";

                    table.Cell().Element(InvoiceBodyCell).Text(expenseName);
                    table.Cell().Element(InvoiceNumberCell).ContentFromLeftToRight().Text(expense.Amount.ToString("N0", CultureInfo.InvariantCulture)).Bold();
                    table.Cell().Element(InvoiceBodyCell).Text(expense.Notes ?? "—");
                    table.Cell().Element(InvoiceNumberCell).ContentFromLeftToRight().Text((index + 1).ToString(CultureInfo.InvariantCulture));
                }
            }
        });

        col.Item().PaddingTop(4).AlignLeft().Width(300).Table(summary =>
        {
            summary.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(1.7f);
                columns.RelativeColumn(1.3f);
            });

            AddInvoiceSummaryRow(summary, "إجمالي البيع", invoice.GrossAmount, Currency, false);
            AddInvoiceSummaryRow(summary, $"الزكاة ({invoice.ZakatPercent:N2}%)", invoice.ZakatAmount, Currency, false);
            AddInvoiceSummaryRow(summary, "إجمالي المصروفات", invoice.TotalExpenses, Currency, false);
            AddInvoiceSummaryRow(summary, "صافي العملية", invoice.NetAmount, Currency, true);
            AddInvoiceSummaryRow(summary, "المبلغ المدفوع", invoice.AmountPaid, Currency, false);
            AddInvoiceSummaryRow(summary, "المبلغ المتبقي", invoice.AmountDue, Currency, true);
        });

        col.Item().Row(row =>
        {
            row.RelativeItem().Element(InvoiceInfoCell).Column(c =>
            {
                c.Item().Text("مرجع دفع الزكاة").FontSize(8).FontColor(Colors.Grey.Darken2);
                c.Item().Text(invoice.ZakatPaymentReference ?? "—").Bold();
            });
            row.RelativeItem().Element(InvoiceInfoCell).Column(c =>
            {
                c.Item().Text("تاريخ تأكيد الزكاة").FontSize(8).FontColor(Colors.Grey.Darken2);
                c.Item().ContentFromLeftToRight().Text(invoice.ZakatPaidAt.HasValue ? GregorianDateTime(invoice.ZakatPaidAt.Value) : "—").Bold();
            });
        });

        if (!string.IsNullOrWhiteSpace(invoice.Notes))
        {
            col.Item().Element(InvoiceInfoCell).Column(c =>
            {
                c.Item().Text("ملاحظات الفاتورة").Bold();
                c.Item().Text(invoice.Notes);
            });
        }

        col.Item().PaddingTop(10).Row(row =>
        {
            row.RelativeItem().AlignCenter().Text("توقيع المستلم: __________________");
            row.RelativeItem().AlignCenter().Text("توقيع المسؤول: __________________");
        });
    }

    public async Task<byte[]> CreateFarmProfitReportPdfAsync(long? farmId, int year)
    {
        ValidateYear(year);
        var from = new DateTime(year, 1, 1);
        var toExclusive = from.AddYears(1);

        await using var db = await factory.CreateDbContextAsync();
        var sales = db.SalesInvoices
            .AsNoTracking()
            .Where(x => x.Status == InvoiceStatus.Posted &&
                        x.InvoiceDate >= from &&
                        x.InvoiceDate < toExclusive)
            .AsQueryable();

        var cultivation = db.CultivationExpenses
            .AsNoTracking()
            .Where(x => x.ExpenseDate >= from && x.ExpenseDate < toExclusive)
            .AsQueryable();

        if (farmId.HasValue && farmId.Value > 0)
        {
            sales = sales.Where(x => x.FarmId == farmId.Value);
            cultivation = cultivation.Where(x => x.FarmId == farmId.Value);
        }

        var salesRows = await sales.ToListAsync();
        var cultivationRows = await cultivation.ToListAsync();
        var gross = salesRows.Sum(x => x.GrossAmount);
        var zakat = salesRows.Sum(x => x.ZakatAmount);
        var dailyExpenses = salesRows.Sum(x => x.TotalExpenses);
        var operatingNet = salesRows.Sum(x => x.NetAmount);
        var cultivationLosses = cultivationRows.Sum(x => x.Amount);
        var finalProfit = operatingNet - cultivationLosses;
        var farmName = await GetFarmFileLabelAsync(farmId);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(35);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontFamily("Arial", "Tahoma", "Segoe UI", "Lato").FontColor(Colors.Black).FontSize(12));
                page.ContentFromRightToLeft();
                page.Header().AlignCenter().Column(c =>
                {
                    c.Item().Text("تقرير الربح الحقيقي السنوي").Bold().FontSize(20);
                    c.Item().Text(farmName).Bold().FontSize(15).FontColor(Colors.Green.Darken3);
                    c.Item().Text($"السنة المالية: {year}").FontSize(12);
                });
                page.Content().PaddingVertical(25).Column(col =>
                {
                    col.Spacing(10);
                    col.Item().Element(SummaryBox).Row(row => { row.RelativeItem().Text("إجمالي المبيعات"); row.RelativeItem().AlignLeft().Text($"{gross:N2} {Currency}").Bold(); });
                    col.Item().Element(SummaryBox).Row(row => { row.RelativeItem().Text("الزكاة قبل المصروفات"); row.RelativeItem().AlignLeft().Text($"{zakat:N2} {Currency}").Bold(); });
                    col.Item().Element(SummaryBox).Row(row => { row.RelativeItem().Text("مصروفات فواتير المحصول"); row.RelativeItem().AlignLeft().Text($"{dailyExpenses:N2} {Currency}").Bold(); });
                    col.Item().Element(SummaryBox).Row(row => { row.RelativeItem().Text("صافي عمليات البيع"); row.RelativeItem().AlignLeft().Text($"{operatingNet:N2} {Currency}").Bold(); });
                    col.Item().Element(SummaryBox).Row(row => { row.RelativeItem().Text("خسائر التربية السنوية"); row.RelativeItem().AlignLeft().Text($"{cultivationLosses:N2} {Currency}").Bold(); });
                    col.Item().PaddingTop(12).Background(finalProfit >= 0 ? Colors.Green.Lighten4 : Colors.Red.Lighten4).Border(1).BorderColor(finalProfit >= 0 ? Colors.Green.Medium : Colors.Red.Medium).Padding(18).Column(c =>
                    {
                        c.Item().AlignCenter().Text($"الربح النهائي لـ {farmName} في سنة {year}").Bold();
                        c.Item().AlignCenter().Text($"{finalProfit:N2} {Currency}").Bold().FontSize(25);
                    });
                    col.Item().PaddingTop(14).AlignCenter().Text("الربح النهائي = صافي البيع − خسائر التربية").FontSize(10);
                });
                page.Footer().AlignCenter().Text($"{farmName} — سنة {year} — أُصدر في {GregorianDateTime(DateTime.Now)}");
            });
        }).GeneratePdf();
    }

    public async Task<byte[]?> CreateCustomerStatementPdfAsync(long customerId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == customerId);
        if (customer is null) return null;

        var invoices = await db.SalesInvoices.AsNoTracking()
            .Where(x => x.CustomerId == customerId)
            .OrderBy(x => x.InvoiceDate)
            .ToListAsync();
        var payments = await db.CustomerPayments.AsNoTracking()
            .Include(x => x.SalesInvoice)
            .Where(x => x.CustomerId == customerId)
            .OrderBy(x => x.PaymentDate)
            .ToListAsync();

        var posted = invoices.Where(x => x.Status == InvoiceStatus.Posted).ToList();
        var openingPayments = payments.Where(x => x.SalesInvoiceId == null).Sum(x => x.Amount);
        var totalPurchases = posted.Sum(x => x.GrossAmount);
        var totalPaid = posted.Sum(x => x.AmountPaid) + openingPayments;
        var outstanding = Math.Max(0, customer.OpeningBalance + posted.Sum(x => x.AmountDue) - openingPayments);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(28);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontFamily("Arial", "Tahoma", "Segoe UI", "Lato").FontColor(Colors.Black).FontSize(9));
                page.ContentFromRightToLeft();
                page.Header().Column(c =>
                {
                    c.Item().AlignCenter().Text("كشف حساب عميل").Bold().FontSize(20);
                    c.Item().AlignCenter().Text(customer.Name).Bold().FontSize(15);
                    c.Item().AlignCenter().Text($"الهاتف: {customer.Phone ?? "—"} — المنطقة: {customer.Region ?? "—"}").FontSize(10);
                });
                page.Content().PaddingVertical(14).Column(col =>
                {
                    col.Spacing(10);
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Element(SummaryBox).Column(c => { c.Item().Text("إجمالي المشتريات"); c.Item().Text($"{totalPurchases:N2} {Currency}").Bold().FontSize(14); });
                        row.RelativeItem().Element(SummaryBox).Column(c => { c.Item().Text("إجمالي المدفوع"); c.Item().Text($"{totalPaid:N2} {Currency}").Bold().FontSize(14); });
                        row.RelativeItem().Element(SummaryBox).Column(c => { c.Item().Text("الرصيد المتبقي"); c.Item().Text($"{outstanding:N2} {Currency}").Bold().FontSize(14); });
                    });

                    col.Item().Text("الفواتير").Bold().FontSize(13);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2); columns.RelativeColumn(); columns.RelativeColumn();
                            columns.RelativeColumn(); columns.RelativeColumn(); columns.RelativeColumn();
                        });
                        table.Header(header =>
                        {
                            foreach (var value in new[] { "الفاتورة", "التاريخ", "الاستحقاق", "الإجمالي", "المدفوع", "المتبقي" })
                                header.Cell().Element(HeaderCell).Text(value);
                        });
                        if (invoices.Count == 0)
                        {
                            table.Cell().ColumnSpan(6).Element(BodyCell).Text("لا توجد فواتير لهذا العميل.");
                        }
                        else
                        {
                            foreach (var invoice in invoices)
                            {
                                table.Cell().Element(BodyCell).Text(invoice.InvoiceNumber);
                                table.Cell().Element(BodyCell).Text(GregorianDate(invoice.InvoiceDate));
                                table.Cell().Element(BodyCell).Text(invoice.PaymentDueDate.HasValue ? GregorianDate(invoice.PaymentDueDate.Value) : "—");
                                table.Cell().Element(BodyCell).Text(invoice.GrossAmount.ToString("N2"));
                                table.Cell().Element(BodyCell).Text(invoice.AmountPaid.ToString("N2"));
                                table.Cell().Element(BodyCell).Text(invoice.AmountDue.ToString("N2"));
                            }
                        }
                    });

                    col.Item().Text("سندات القبض").Bold().FontSize(13);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(); columns.RelativeColumn(2); columns.RelativeColumn();
                            columns.RelativeColumn(); columns.RelativeColumn(2);
                        });
                        table.Header(header =>
                        {
                            foreach (var value in new[] { "التاريخ", "الفاتورة", "المبلغ", "الطريقة", "المرجع" })
                                header.Cell().Element(HeaderCell).Text(value);
                        });
                        if (payments.Count == 0)
                        {
                            table.Cell().ColumnSpan(5).Element(BodyCell).Text("لا توجد سندات قبض لهذا العميل.");
                        }
                        else
                        {
                            foreach (var payment in payments)
                            {
                                table.Cell().Element(BodyCell).Text(GregorianDate(payment.PaymentDate));
                                table.Cell().Element(BodyCell).Text(payment.SalesInvoice?.InvoiceNumber ?? "رصيد سابق");
                                table.Cell().Element(BodyCell).Text(payment.Amount.ToString("N2"));
                                table.Cell().Element(BodyCell).Text(PaymentMethodText(payment.PaymentMethod));
                                table.Cell().Element(BodyCell).Text(payment.ReferenceNumber ?? "—");
                            }
                        }
                    });
                });
                page.Footer().AlignCenter().Text($"أُصدر في {GregorianDateTime(DateTime.Now)}");
            });
        }).GeneratePdf();
    }

    private byte[] CreateSimpleReport(string title, List<string[]> rows, string[] headers, decimal total, string farmName, int year)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(25);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontFamily("Arial", "Tahoma", "Segoe UI", "Lato").FontColor(Colors.Black).FontSize(9));
                page.ContentFromRightToLeft();
                page.Header().AlignCenter().Column(header =>
                {
                    header.Item().Text(title).Bold().FontSize(18);
                    header.Item().Text($"{farmName} — سنة {year}").FontSize(11).FontColor(Colors.Grey.Darken2);
                });
                page.Content().PaddingVertical(12).Column(col =>
                {
                    if (rows.Count == 0)
                    {
                        col.Item().Element(EmptyDataBox)
                            .Text("لا توجد بيانات للمزرعة والسنة المحددتين. اختر مزرعة أخرى أو سنة تحتوي على عمليات محفوظة.")
                            .AlignCenter().FontSize(13).FontColor(Colors.Red.Darken2);
                    }
                    else
                    {
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns => { foreach (var _ in headers) columns.RelativeColumn(); });
                            table.Header(header => { foreach (var h in headers) header.Cell().Element(HeaderCell).Text(h); });
                            foreach (var row in rows)
                                foreach (var value in row)
                                    table.Cell().Element(BodyCell).Text(value);
                        });
                    }
                    col.Item().PaddingTop(12).AlignRight().Text($"عدد السجلات: {rows.Count:N0} — الإجمالي: {total:N2} {Currency}").Bold().FontSize(13);
                });
                page.Footer().AlignCenter().Text(x => { x.CurrentPageNumber(); x.Span(" / "); x.TotalPages(); });
            });
        }).GeneratePdf();
    }

    private static void AddAnnualSummaryCell(TableDescriptor table, string label, string value, string suffix)
    {
        table.Cell().Border(1).BorderColor(Colors.Green.Lighten2)
            .Background(Colors.White).Padding(7).Column(c =>
        {
            c.Item().Text(label).FontSize(8).FontColor(Colors.Grey.Darken2);
            var displayedValue = string.IsNullOrWhiteSpace(suffix) ? value : $"{value} {suffix}";
            c.Item().ContentFromLeftToRight().Text(displayedValue).Bold().FontSize(10);
        });
    }

    private static void AddAnnualInfoCell(TableDescriptor table, string label, string value, bool leftToRight)
    {
        table.Cell().Element(InvoiceInfoCell).Column(c =>
        {
            c.Item().Text(label).FontSize(8).FontColor(Colors.Grey.Darken2);
            if (leftToRight)
                c.Item().ContentFromLeftToRight().Text(value).Bold();
            else
                c.Item().Text(value).Bold();
        });
    }

    private static string GregorianDate(DateTime value) =>
        value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

    private static string GregorianDateTime(DateTime value) =>
        value.ToString("yyyy/MM/dd - HH:mm", CultureInfo.InvariantCulture);

    private static void ValidateYear(int year)
    {
        if (year < 2000 || year > DateTime.Today.Year + 1)
            throw new ArgumentOutOfRangeException(nameof(year), "السنة المحددة غير صحيحة.");
    }

    private static void AddInvoiceSummaryRow(
        TableDescriptor table,
        string label,
        decimal value,
        string currency,
        bool emphasize)
    {
        var background = emphasize ? Colors.Green.Lighten4 : Colors.Grey.Lighten4;
        var border = emphasize ? Colors.Green.Lighten1 : Colors.Grey.Lighten2;

        var labelText = table.Cell().Border(1).BorderColor(border).Background(background).Padding(7)
            .AlignRight().Text(label);

        if (emphasize)
            labelText.Bold();

        var valueText = table.Cell().Border(1).BorderColor(border).Background(background).Padding(7)
            .AlignLeft().ContentFromLeftToRight().Text($"{value:N0} {currency}");

        if (emphasize)
            valueText.Bold().FontSize(12);
    }

    private static string InvoiceStatusText(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Posted => "فاتورة مرحلة",
        InvoiceStatus.Cancelled => "فاتورة ملغاة",
        _ => "فاتورة مسودة"
    };

    private static string PaymentStatusText(PaymentStatus status) => status switch
    {
        PaymentStatus.Paid => "مسددة بالكامل",
        PaymentStatus.Partial => "مسددة جزئيًا",
        _ => "غير مسددة"
    };

    private static string ZakatStatusText(ZakatPaymentStatus status) => status switch
    {
        ZakatPaymentStatus.Paid => "تم تأكيد دفع الزكاة",
        ZakatPaymentStatus.NotApplicable => "غير مستحقة",
        _ => "الزكاة معلقة ولم تؤكد"
    };

    private static IContainer InvoiceInfoCell(IContainer container) => container
        .Border(1)
        .BorderColor(Colors.Grey.Lighten2)
        .Background(Colors.Grey.Lighten4)
        .Padding(8);

    private static IContainer InvoiceHeaderCell(IContainer container) => container
        .Border(1)
        .BorderColor(Colors.Green.Darken1)
        .Background(Colors.Green.Lighten3)
        .Padding(6)
        .AlignCenter();

    private static IContainer ExpenseHeaderCell(IContainer container) => container
        .Border(1)
        .BorderColor(Colors.Orange.Darken1)
        .Background(Colors.Orange.Lighten3)
        .Padding(6)
        .AlignCenter();

    private static IContainer InvoiceBodyCell(IContainer container) => container
        .Border(1)
        .BorderColor(Colors.Grey.Lighten2)
        .Padding(6)
        .AlignRight();

    private static IContainer InvoiceNumberCell(IContainer container) => container
        .Border(1)
        .BorderColor(Colors.Grey.Lighten2)
        .Padding(6)
        .AlignCenter();

    private static IContainer InvoiceEmptyCell(IContainer container) => container
        .Border(1)
        .BorderColor(Colors.Grey.Lighten2)
        .Background(Colors.Grey.Lighten5)
        .Padding(9)
        .AlignCenter();

    private static string PaymentMethodText(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "نقدي",
        PaymentMethod.Transfer => "تحويل",
        PaymentMethod.Credit => "آجل",
        PaymentMethod.Mixed => "مختلط",
        _ => "غير محدد"
    };

    private static IContainer EmptyDataBox(IContainer c) => c
        .Border(1).BorderColor(Colors.Red.Lighten2)
        .Background(Colors.Red.Lighten5).Padding(18);

    private static IContainer SummaryBox(IContainer c) => c.Border(1).BorderColor(Colors.Grey.Lighten2).Background(Colors.Grey.Lighten4).Padding(14);
    private static IContainer HeaderCell(IContainer c) => c.Border(1).Background(Colors.Grey.Lighten2).Padding(6).AlignCenter();
    private static IContainer BodyCell(IContainer c) => c.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignCenter();
}
