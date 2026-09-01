using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QatFarm.Web.Data;
using QatFarm.Web.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace QatFarm.Web.Services;

public sealed class CultivationDebtPdfService(
    IDbContextFactory<ApplicationDbContext> factory,
    IConfiguration configuration)
{
    private string Currency => configuration["System:Currency"] ?? "ريال يمني";

    public async Task<string> GetFarmFileLabelAsync(long? farmId)
    {
        if (!farmId.HasValue || farmId.Value <= 0)
            return "كل-المزارع";

        await using var db = await factory.CreateDbContextAsync();
        var name = await db.Farms
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.Id == farmId.Value)
            .Select(x => x.Name)
            .FirstOrDefaultAsync();

        return string.IsNullOrWhiteSpace(name) ? $"مزرعة-{farmId.Value}" : name;
    }

    public async Task<byte[]> CreateAnnualPdfAsync(long? farmId, int year)
    {
        ValidateYear(year);
        var from = new DateTime(year, 1, 1);
        var toExclusive = from.AddYears(1);

        await using var db = await factory.CreateDbContextAsync();

        var expensesQuery = db.CultivationExpenses
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Farm)
            .Include(x => x.ExpenseType)
            .Include(x => x.Creditor)
            .Include(x => x.DebtPayments.Where(p => !p.IsDeleted))
            .Where(x => !x.IsDeleted && x.ExpenseDate >= from && x.ExpenseDate < toExclusive);

        if (farmId.HasValue && farmId.Value > 0)
            expensesQuery = expensesQuery.Where(x => x.FarmId == farmId.Value);

        var expenses = await expensesQuery
            .AsSplitQuery()
            .OrderBy(x => x.ExpenseDate)
            .ThenBy(x => x.Id)
            .ToListAsync();

        var invoiceQuery = db.SalesInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => !x.IsDeleted &&
                        x.Status == InvoiceStatus.Posted &&
                        x.InvoiceDate >= from &&
                        x.InvoiceDate < toExclusive);

        if (farmId.HasValue && farmId.Value > 0)
            invoiceQuery = invoiceQuery.Where(x => x.FarmId == farmId.Value);

        var salesTotals = await invoiceQuery
            .GroupBy(_ => 1)
            .Select(group => new
            {
                GrossSales = group.Sum(x => x.GrossAmount),
                CollectedSales = group.Sum(x => x.AmountPaid),
                CustomerReceivables = group.Sum(x => x.AmountDue),
                InvoiceExpenses = group.Sum(x => x.TotalExpenses),
                Zakat = group.Sum(x => x.ZakatAmount),
                NetSalesBeforeCultivation = group.Sum(x => x.NetAmount)
            })
            .FirstOrDefaultAsync();

        var grossSales = salesTotals?.GrossSales ?? 0m;
        var collectedSales = salesTotals?.CollectedSales ?? 0m;
        var customerReceivables = salesTotals?.CustomerReceivables ?? 0m;
        var invoiceExpenses = salesTotals?.InvoiceExpenses ?? 0m;
        var zakat = salesTotals?.Zakat ?? 0m;
        var netSalesBeforeCultivation = salesTotals?.NetSalesBeforeCultivation ?? 0m;

        var totalExpenses = expenses.Sum(x => x.Amount);
        var totalPaid = expenses.Sum(x => x.PaidAmount);
        var totalOutstanding = expenses.Sum(x => Math.Max(0m, x.Amount - x.PaidAmount));
        var accountingProfit = netSalesBeforeCultivation - totalExpenses;
        var cashAfterAllReserves = collectedSales - invoiceExpenses - zakat - totalExpenses;
        var safeDistributableProfit = Math.Max(0m, Math.Min(accountingProfit, cashAfterAllReserves));
        var overdueCount = expenses.Count(x =>
            x.Amount > x.PaidAmount && x.DueDate.HasValue && x.DueDate.Value.Date < DateTime.Today);

        var farmName = farmId.HasValue && farmId.Value > 0
            ? expenses.FirstOrDefault()?.Farm?.Name ?? await GetFarmFileLabelAsync(farmId)
            : "كل المزارع";

        var creditorGroups = expenses
            .Where(x => x.CreditorId.HasValue)
            .GroupBy(x => new
            {
                Id = x.CreditorId!.Value,
                Name = x.Creditor?.Name ?? "دائن غير معروف",
                Phone = x.Creditor?.Phone
            })
            .Select(group => new
            {
                group.Key.Name,
                group.Key.Phone,
                Total = group.Sum(x => x.Amount),
                Paid = group.Sum(x => x.PaidAmount),
                Outstanding = group.Sum(x => Math.Max(0m, x.Amount - x.PaidAmount)),
                OpenCount = group.Count(x => x.Amount > x.PaidAmount),
                OverdueCount = group.Count(x =>
                    x.Amount > x.PaidAmount && x.DueDate.HasValue && x.DueDate.Value.Date < DateTime.Today)
            })
            .OrderByDescending(x => x.Outstanding)
            .ThenBy(x => x.Name)
            .ToList();

        var paymentRows = expenses
            .SelectMany(expense => expense.DebtPayments.Select(payment => new
            {
                Expense = expense,
                Payment = payment
            }))
            .OrderBy(x => x.Payment.PaymentDate)
            .ThenBy(x => x.Payment.Id)
            .ToList();

        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(22);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(style => style
                    .FontFamily("Arial", "Tahoma", "Segoe UI", "Lato")
                    .FontColor(Colors.Black)
                    .FontSize(8));
                page.ContentFromRightToLeft();

                page.Header().Column(header =>
                {
                    header.Spacing(3);
                    header.Item().AlignCenter().Text("نظام إدارة مزارع وبيع القات")
                        .Bold().FontSize(17).FontColor(Colors.Green.Darken3);
                    header.Item().AlignCenter().Text("التقرير السنوي التفصيلي لخسائر التربية وديون الدائنين")
                        .Bold().FontSize(14);
                    header.Item().AlignCenter().Text($"{farmName} — سنة {year}")
                        .FontSize(11).FontColor(Colors.Grey.Darken2);
                });

                page.Content().PaddingVertical(10).Column(column =>
                {
                    column.Spacing(9);

                    column.Item().Table(summary =>
                    {
                        summary.ColumnsDefinition(columns =>
                        {
                            for (var i = 0; i < 10; i++) columns.RelativeColumn();
                        });

                        AddSummaryCell(summary, "إجمالي المبيعات", grossSales, Colors.Blue.Lighten4);
                        AddSummaryCell(summary, "المبيعات المحصلة", collectedSales, Colors.Green.Lighten4);
                        AddSummaryCell(summary, "ديون العملاء", customerReceivables, Colors.Red.Lighten4);
                        AddSummaryCell(summary, "صافي المبيعات قبل التربية", netSalesBeforeCultivation, Colors.Blue.Lighten4);
                        AddSummaryCell(summary, "إجمالي خسائر التربية", totalExpenses, Colors.Orange.Lighten4);
                        AddSummaryCell(summary, "المسدد للدائنين", totalPaid, Colors.Green.Lighten4);
                        AddSummaryCell(summary, "دين التربية المتبقي", totalOutstanding, Colors.Red.Lighten4);
                        AddSummaryCell(summary, "الربح المحاسبي", accountingProfit, Colors.Grey.Lighten3);
                        AddSummaryCell(summary, "الربح الآمن للتوزيع", safeDistributableProfit, Colors.Green.Lighten3);
                        AddSummaryCell(summary, "ديون متأخرة", overdueCount, Colors.Red.Lighten3, false);
                    });

                    if (totalOutstanding > 0)
                    {
                        column.Item()
                            .Border(1)
                            .BorderColor(Colors.Red.Lighten1)
                            .Background(Colors.Red.Lighten5)
                            .Padding(8)
                            .Text($"تنبيه: توجد ديون تربية غير مسددة بقيمة {totalOutstanding:N0} {Currency}. " +
                                  "يُخصم أصل المصروف مرة واحدة في الربح المحاسبي، بينما يعتمد الربح الآمن على النقد المحصل فعليًا بعد حجز المصروفات والزكاة وكامل التزامات التربية.")
                            .Bold().FontColor(Colors.Red.Darken3);
                    }

                    if (cashAfterAllReserves < 0)
                    {
                        column.Item()
                            .Border(1)
                            .BorderColor(Colors.Orange.Darken1)
                            .Background(Colors.Orange.Lighten5)
                            .Padding(8)
                            .Text($"تنبيه سيولة: النقد المحصل لا يغطي جميع الالتزامات السنوية، والعجز المحجوز يبلغ {Math.Abs(cashAfterAllReserves):N0} {Currency}.")
                            .Bold().FontColor(Colors.Orange.Darken4);
                    }

                    column.Item().Text("تفاصيل خسائر التربية").Bold().FontSize(12);
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(1.1f); // السند
                            columns.RelativeColumn(1.1f); // التاريخ
                            columns.RelativeColumn(1.3f); // المزرعة
                            columns.RelativeColumn(1.4f); // النوع
                            columns.RelativeColumn(1.5f); // الدائن
                            columns.RelativeColumn(1f);   // الإجمالي
                            columns.RelativeColumn(1f);   // المدفوع
                            columns.RelativeColumn(1f);   // المتبقي
                            columns.RelativeColumn(1.1f); // الاستحقاق
                            columns.RelativeColumn(1f);   // الحالة
                        });

                        table.Header(header =>
                        {
                            foreach (var title in new[]
                                     {
                                         "السند", "التاريخ", "المزرعة", "نوع الخسارة", "الدائن",
                                         "الإجمالي", "المدفوع", "المتبقي", "الاستحقاق", "الحالة"
                                     })
                            {
                                header.Cell().Element(HeaderCell).Text(title).Bold();
                            }
                        });

                        if (expenses.Count == 0)
                        {
                            table.Cell().ColumnSpan(10).Element(EmptyCell)
                                .Text("لا توجد خسائر تربية للمزرعة والسنة المحددتين.");
                        }
                        else
                        {
                            foreach (var expense in expenses)
                            {
                                var outstanding = Math.Max(0m, expense.Amount - expense.PaidAmount);
                                var overdue = outstanding > 0 && expense.DueDate.HasValue &&
                                              expense.DueDate.Value.Date < DateTime.Today;

                                table.Cell().Element(BodyCell).ContentFromLeftToRight().Text(expense.ReceiptNumber);
                                table.Cell().Element(BodyCell).ContentFromLeftToRight().Text(DateText(expense.ExpenseDate));
                                table.Cell().Element(BodyCell).Text(expense.Farm?.Name ?? "—");
                                table.Cell().Element(BodyCell).Text(expense.ExpenseType?.Name ?? "—");
                                table.Cell().Element(BodyCell).Text(expense.Creditor?.Name ?? "—");
                                table.Cell().Element(NumberCell).ContentFromLeftToRight().Text($"{expense.Amount:N0}");
                                table.Cell().Element(NumberCell).ContentFromLeftToRight().Text($"{expense.PaidAmount:N0}");
                                table.Cell().Element(NumberCell).ContentFromLeftToRight().Text($"{outstanding:N0}")
                                    .FontColor(outstanding > 0 ? Colors.Red.Darken2 : Colors.Green.Darken2);
                                table.Cell().Element(BodyCell).ContentFromLeftToRight()
                                    .Text(expense.DueDate.HasValue ? DateText(expense.DueDate.Value) : "—");
                                table.Cell().Element(BodyCell).Text(DebtStatusText(expense, overdue));
                            }
                        }
                    });

                    column.Item().Text("ملخص الديون حسب الدائن").Bold().FontSize(12);
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(1.3f);
                            columns.RelativeColumn(1.2f);
                            columns.RelativeColumn(1.2f);
                            columns.RelativeColumn(1.2f);
                            columns.RelativeColumn(1f);
                            columns.RelativeColumn(1f);
                        });

                        table.Header(header =>
                        {
                            foreach (var title in new[]
                                     {
                                         "الدائن", "الهاتف", "إجمالي الخسائر", "المسدد", "المتبقي", "مفتوحة", "متأخرة"
                                     })
                            {
                                header.Cell().Element(HeaderCell).Text(title).Bold();
                            }
                        });

                        if (creditorGroups.Count == 0)
                        {
                            table.Cell().ColumnSpan(7).Element(EmptyCell).Text("لا توجد ديون مرتبطة بدائنين.");
                        }
                        else
                        {
                            foreach (var creditor in creditorGroups)
                            {
                                table.Cell().Element(BodyCell).Text(creditor.Name);
                                table.Cell().Element(BodyCell).ContentFromLeftToRight().Text(creditor.Phone ?? "—");
                                table.Cell().Element(NumberCell).ContentFromLeftToRight().Text($"{creditor.Total:N0}");
                                table.Cell().Element(NumberCell).ContentFromLeftToRight().Text($"{creditor.Paid:N0}");
                                table.Cell().Element(NumberCell).ContentFromLeftToRight().Text($"{creditor.Outstanding:N0}")
                                    .FontColor(creditor.Outstanding > 0 ? Colors.Red.Darken2 : Colors.Green.Darken2);
                                table.Cell().Element(NumberCell).ContentFromLeftToRight().Text(creditor.OpenCount.ToString());
                                table.Cell().Element(NumberCell).ContentFromLeftToRight().Text(creditor.OverdueCount.ToString());
                            }
                        }
                    });

                    column.Item().Text("سجل دفعات ديون التربية").Bold().FontSize(12);
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(1.2f);
                            columns.RelativeColumn(1.4f);
                            columns.RelativeColumn(1.4f);
                            columns.RelativeColumn(1.3f);
                            columns.RelativeColumn(1f);
                            columns.RelativeColumn(1f);
                            columns.RelativeColumn(1.2f);
                            columns.RelativeColumn(2);
                        });

                        table.Header(header =>
                        {
                            foreach (var title in new[]
                                     {
                                         "تاريخ الدفع", "الدائن", "نوع الخسارة", "السند", "المبلغ",
                                         "الطريقة", "المرجع", "الملاحظات"
                                     })
                            {
                                header.Cell().Element(HeaderCell).Text(title).Bold();
                            }
                        });

                        if (paymentRows.Count == 0)
                        {
                            table.Cell().ColumnSpan(8).Element(EmptyCell).Text("لا توجد دفعات مسجلة في السنة المحددة.");
                        }
                        else
                        {
                            foreach (var row in paymentRows)
                            {
                                table.Cell().Element(BodyCell).ContentFromLeftToRight().Text(DateText(row.Payment.PaymentDate));
                                table.Cell().Element(BodyCell).Text(row.Expense.Creditor?.Name ?? "—");
                                table.Cell().Element(BodyCell).Text(row.Expense.ExpenseType?.Name ?? "—");
                                table.Cell().Element(BodyCell).ContentFromLeftToRight().Text(row.Expense.ReceiptNumber);
                                table.Cell().Element(NumberCell).ContentFromLeftToRight().Text($"{row.Payment.Amount:N0}");
                                table.Cell().Element(BodyCell).Text(PaymentMethodText(row.Payment.PaymentMethod));
                                table.Cell().Element(BodyCell).ContentFromLeftToRight().Text(row.Payment.ReferenceNumber ?? "—");
                                table.Cell().Element(BodyCell).Text(row.Payment.Notes ?? "—");
                            }
                        }
                    });
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("صفحة ");
                    text.CurrentPageNumber();
                    text.Span(" من ");
                    text.TotalPages();
                });
            });
        }).GeneratePdf();
    }

    private void AddSummaryCell(
        TableDescriptor table,
        string label,
        decimal value,
        string background,
        bool currency = true)
    {
        table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Background(background).Padding(7).Column(column =>
        {
            column.Item().Text(label).FontSize(7).FontColor(Colors.Grey.Darken2);
            var text = currency ? $"{value:N0} {Currency}" : $"{value:N0}";
            column.Item().ContentFromLeftToRight().Text(text).Bold().FontSize(9);
        });
    }

    private static string DateText(DateTime value) =>
        value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

    private static string DebtStatusText(CultivationExpense expense, bool overdue)
    {
        if (overdue) return "متأخر";
        return expense.DebtStatus switch
        {
            CultivationDebtStatus.Unpaid => "غير مدفوع",
            CultivationDebtStatus.Partial => "جزئي",
            CultivationDebtStatus.Paid => "مدفوع",
            _ => "بدون دين"
        };
    }

    private static string PaymentMethodText(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "نقدًا",
        PaymentMethod.Transfer => "تحويل",
        PaymentMethod.Credit => "آجل",
        _ => "مختلط"
    };

    private static void ValidateYear(int year)
    {
        if (year is < 2000 or > 2100)
            throw new ArgumentOutOfRangeException(nameof(year), "السنة المحددة غير صحيحة.");
    }

    private static IContainer HeaderCell(IContainer container) => container
        .Border(1)
        .BorderColor(Colors.Green.Darken1)
        .Background(Colors.Green.Lighten3)
        .Padding(5)
        .AlignCenter();

    private static IContainer BodyCell(IContainer container) => container
        .BorderBottom(1)
        .BorderColor(Colors.Grey.Lighten2)
        .Padding(5)
        .AlignCenter();

    private static IContainer NumberCell(IContainer container) => container
        .BorderBottom(1)
        .BorderColor(Colors.Grey.Lighten2)
        .Padding(5)
        .AlignCenter();

    private static IContainer EmptyCell(IContainer container) => container
        .Border(1)
        .BorderColor(Colors.Grey.Lighten2)
        .Background(Colors.Grey.Lighten5)
        .Padding(10)
        .AlignCenter();
}
