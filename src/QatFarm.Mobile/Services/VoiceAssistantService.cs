using QatFarm.Mobile.Models;

namespace QatFarm.Mobile.Services;

public sealed class VoiceAssistantService
{
    private readonly QatFarmService service;
    private readonly MobilePdfService pdf;

    public VoiceAssistantService(QatFarmService service, MobilePdfService pdf)
    {
        this.service = service;
        this.pdf = pdf;
    }

    public async Task<VoiceCommandProposal> InterpretAsync(string? transcript)
    {
        var original = transcript?.Trim() ?? string.Empty;
        var text = ArabicVoiceText.Normalize(original);
        if (text.Length == 0) return Info(original, "لم أسمع أمراً واضحاً.", "اضغط الميكروفون مرة أخرى أو اكتب الأمر.");

        try
        {
            if (LooksLikePdf(text)) return await BuildPdfProposalAsync(original, text);
            if (LooksLikeDeleteInvoice(text)) return await BuildDeleteInvoiceProposalAsync(original, text);
            if (LooksLikeEditInvoice(text)) return await BuildEditInvoiceProposalAsync(original, text);
            if (LooksLikeDeleteCultivation(text)) return await BuildDeleteCultivationProposalAsync(original, text);
            if (LooksLikeEditCultivation(text)) return await BuildEditCultivationProposalAsync(original, text);
            if (LooksLikeCreateCultivation(text)) return await BuildCultivationProposalAsync(original, text);
            if (LooksLikeCreateInvoice(text)) return await BuildInvoiceProposalAsync(original, text);

            var query = await BuildQueryProposalAsync(original, text);
            if (query is not null) return query;

            var navigation = BuildNavigationProposal(original, text);
            if (navigation is not null) return navigation;

            return Info(original, "الأمر غير مكتمل.",
                "أمثلة: «فاتورة لعبدالله 10 حبات أميال سعر 5000 دفع 20000»، «سجل خسارة سقي 25 ألف»، «احذف الفاتورة رقم 125»، «مبيعات اليوم»، «صدر فواتير أغسطس PDF».");
        }
        catch (Exception ex)
        {
            return Info(original, "تعذر تجهيز الأمر.", ex.Message);
        }
    }

    private static bool LooksLikePdf(string text)
        => ArabicVoiceText.ContainsAny(text, "pdf", "بي دي اف", "تقرير") &&
           ArabicVoiceText.ContainsAny(text, "صدر", "تصدير", "اعمل", "جهز", "انشئ", "pdf", "بي دي اف");

    private static bool LooksLikeDeleteInvoice(string text)
        => ArabicVoiceText.ContainsAny(text, "احذف", "حذف", "امسح", "الغ") && ArabicVoiceText.ContainsAny(text, "فاتوره", "فاتورة");

    private static bool LooksLikeEditInvoice(string text)
        => ArabicVoiceText.ContainsAny(text, "عدل", "تعديل", "غير", "خلي") && ArabicVoiceText.ContainsAny(text, "فاتوره", "فاتورة");

    private static bool LooksLikeCreateInvoice(string text)
        => ArabicVoiceText.ContainsAny(text, "فاتوره", "فاتورة", "بيع") &&
           ArabicVoiceText.ContainsAny(text, "سجل", "انشئ", "اعمل", "اضف", "فاتوره", "فاتورة");

    private static bool LooksLikeCreateCultivation(string text)
        => ArabicVoiceText.ContainsAny(text, "خساره", "خسارة", "مصروف تربيه", "مصروف التربية") &&
           ArabicVoiceText.ContainsAny(text, "سجل", "اضف", "انشئ", "اعمل");

    private static bool LooksLikeDeleteCultivation(string text)
        => ArabicVoiceText.ContainsAny(text, "احذف", "حذف", "امسح") && ArabicVoiceText.ContainsAny(text, "خساره", "خسارة");

    private static bool LooksLikeEditCultivation(string text)
        => ArabicVoiceText.ContainsAny(text, "عدل", "تعديل", "غير", "خلي") && ArabicVoiceText.ContainsAny(text, "خساره", "خسارة");

    private async Task<VoiceCommandProposal> BuildInvoiceProposalAsync(string original, string text)
    {
        var farms = await service.GetFarmsAsync(true);
        var customers = await service.GetCustomerLookupsAsync();
        var qatTypes = await service.GetQatTypesAsync();

        var farm = ArabicVoiceText.FindMentioned(text, farms, x => x.Name) ?? (farms.Count == 1 ? farms[0] : null);
        var qat = ArabicVoiceText.FindMentioned(text, qatTypes, x => x.Name);
        var customer = ArabicVoiceText.FindMentioned(text, customers, x => x.Name);
        var cashRequested = ArabicVoiceText.ContainsAny(text, "نقدي", "كاش", "بدون عميل", "عميل نقدي");
        var quantity = ArabicVoiceText.ExtractQuantity(text);
        var price = ArabicVoiceText.ExtractMoneyAfter(text, "سعر الحبه", "سعر الحبة", "سعر الوحده", "السعر", "سعر");
        var paid = ArabicVoiceText.ExtractMoneyAfter(text, "المبلغ المدفوع", "المدفوع", "مدفوع", "دفع", "دفعت") ?? 0m;

        if (farm is null)
            return Info(original, "حدد المزرعة.", "يوجد أكثر من مزرعة؛ اذكر اسم المزرعة في الأمر.");
        if (qat is null)
            return Info(original, "حدد صنف القات.", "اذكر اسم الصنف كما هو مسجل في النظام.");
        if (!quantity.HasValue || quantity.Value <= 0)
            return Info(original, "حدد الكمية.", "مثال: 10 حبات أو «الكمية عشرة».");
        if (!price.HasValue || price.Value <= 0)
            return Info(original, "حدد سعر الوحدة.", "مثال: «سعر الحبة 5000».");
        if (customer is null && !cashRequested)
            return Info(original, "حدد العميل أو نوع البيع.", "اذكر اسم عميل مسجل، أو قل «بيع نقدي».");

        var model = new InvoiceEditorModel
        {
            FarmId = farm.Id,
            CustomerId = customer?.Id,
            InvoiceDate = DateTime.Today,
            BuyerName = customer?.Name ?? "بيع نقدي",
            BuyerPhone = customer?.Phone,
            AmountPaid = Math.Max(0m, paid),
            ZakatPercent = 5m,
            Items =
            [
                new InvoiceItemInput
                {
                    QatTypeId = qat.Id,
                    Quantity = quantity.Value,
                    UnitPrice = price.Value
                }
            ]
        };

        if (customer is null)
        {
            model.PaymentMethod = PaymentMethod.Cash;
            model.AmountPaid = model.GrossAmount;
        }
        else
        {
            if (model.AmountPaid > model.GrossAmount)
                return Info(original, "المبلغ المدفوع أكبر من قيمة الفاتورة.", $"إجمالي الفاتورة {model.GrossAmount:N0} ر.ي.");
            model.PaymentMethod = model.AmountDue == 0 ? PaymentMethod.Cash :
                model.AmountPaid > 0 ? PaymentMethod.Mixed : PaymentMethod.Credit;
        }

        var customerText = customer?.Name ?? "بيع نقدي";
        var proposal = new VoiceCommandProposal
        {
            Transcript = original,
            Title = "إنشاء فاتورة بيع",
            Summary = $"المزرعة: {farm.Name}\nالعميل: {customerText}\nالصنف: {qat.Name}\nالكمية: {quantity.Value:N0}\nسعر الوحدة: {price.Value:N0}\nالإجمالي: {model.GrossAmount:N0} ر.ي\nالمدفوع: {model.AmountPaid:N0} ر.ي\nالمتبقي على العميل: {model.AmountDue:N0} ر.ي",
            RequiresConfirmation = true,
            SpokenResponse = $"تم تجهيز فاتورة بإجمالي {model.GrossAmount:N0} ريال. راجعها ثم أكد التنفيذ."
        };
        return proposal.WithExecutor(async () =>
        {
            var id = await service.SaveInvoiceAsync(model);
            return new VoiceExecutionResult(true, $"تم إنشاء الفاتورة بنجاح. رقم السجل {id}.", "/invoices");
        });
    }

    private async Task<VoiceCommandProposal> BuildCultivationProposalAsync(string original, string text)
    {
        var farms = await service.GetFarmsAsync(true);
        var types = await service.GetCultivationTypesAsync();
        var creditors = await service.GetCreditorsAsync(true);

        var farm = ArabicVoiceText.FindMentioned(text, farms, x => x.Name) ?? (farms.Count == 1 ? farms[0] : null);
        var type = ArabicVoiceText.FindMentioned(text, types, x => x.Name);
        var amount = ArabicVoiceText.ExtractMoneyAfter(text, "بمبلغ", "المبلغ", "مبلغ")
                     ?? ArabicVoiceText.ExtractAnyNumber(text, 100m)
                     ?? ArabicVoiceText.ExtractAnyNumber(text, 1m);

        if (farm is null) return Info(original, "حدد المزرعة.", "اذكر اسم المزرعة في أمر تسجيل الخسارة.");
        if (type is null) return Info(original, "حدد نوع الخسارة.", "اذكر نوع الخسارة مثل السقي أو العمال أو السماد حسب القوائم المسجلة.");
        if (!amount.HasValue || amount.Value <= 0) return Info(original, "حدد مبلغ الخسارة.", "مثال: «سجل خسارة سقي بمبلغ عشرين ألف».");

        var isPartial = ArabicVoiceText.ContainsAny(text, "جزئي", "جزء");
        var isCredit = isPartial || ArabicVoiceText.ContainsAny(text, "اجل", "آجل", "دين", "على الحساب");
        var creditor = isCredit ? ArabicVoiceText.FindMentioned(text, creditors, x => x.Name) : null;
        if (isCredit && creditor is null)
            return Info(original, "حدد الدائن.", "عند تسجيل خسارة آجلة اذكر اسم الدائن المسجل.");

        var paidNow = isPartial
            ? ArabicVoiceText.ExtractMoneyAfter(text, "المدفوع", "دفعت", "دفع") ?? 0m
            : isCredit ? 0m : amount.Value;

        var model = new CultivationExpense
        {
            FarmId = farm.Id,
            ExpenseTypeId = type.Id,
            Amount = amount.Value,
            ExpenseDate = DateTime.Today,
            PaymentType = isPartial ? CultivationExpensePaymentType.Partial : isCredit ? CultivationExpensePaymentType.Credit : CultivationExpensePaymentType.Cash,
            CreditorId = creditor?.Id,
            PaidAmount = paidNow,
            Notes = "تم الإدخال بواسطة المعاون الصوتي"
        };

        var remaining = AccountingMath.Outstanding(model.Amount, model.PaidAmount);
        var proposal = new VoiceCommandProposal
        {
            Transcript = original,
            Title = "تسجيل خسارة تربية",
            Summary = $"المزرعة: {farm.Name}\nالنوع: {type.Name}\nالمبلغ: {model.Amount:N0} ر.ي\nالمدفوع: {model.PaidAmount:N0} ر.ي\nالمتبقي: {remaining:N0} ر.ي" + (creditor is null ? string.Empty : $"\nالدائن: {creditor.Name}"),
            RequiresConfirmation = true,
            SpokenResponse = $"تم تجهيز خسارة بمبلغ {model.Amount:N0} ريال. أكد التنفيذ بعد المراجعة."
        };
        return proposal.WithExecutor(async () =>
        {
            await service.SaveCultivationExpenseAsync(model);
            return new VoiceExecutionResult(true, "تم تسجيل الخسارة بنجاح.", "/cultivation");
        });
    }

    private async Task<VoiceCommandProposal> BuildDeleteInvoiceProposalAsync(string original, string text)
    {
        var reference = ArabicVoiceText.ExtractIdAfter(text, "رقم الفاتوره", "رقم الفاتورة", "رقم");
        if (!reference.HasValue) return Info(original, "حدد رقم الفاتورة.", "مثال: «احذف الفاتورة رقم 125».");
        var row = await FindInvoiceAsync(reference.Value);
        if (row is null) return Info(original, "الفاتورة غير موجودة.", $"لم أجد فاتورة تطابق الرقم {reference.Value}.");

        var proposal = new VoiceCommandProposal
        {
            Transcript = original,
            Title = "حذف فاتورة",
            Summary = $"الفاتورة: {row.Invoice.InvoiceNumber}\nالعميل: {row.CustomerName}\nالإجمالي: {row.Invoice.GrossAmount:N0} ر.ي\nالمتبقي: {row.Invoice.AmountDue:N0} ر.ي\nهذه العملية تحتاج تأكيداً صريحاً.",
            RequiresConfirmation = true,
            SpokenResponse = "تم العثور على الفاتورة. تأكد من بياناتها ثم اضغط تنفيذ."
        };
        return proposal.WithExecutor(async () =>
        {
            await service.DeleteInvoiceAsync(row.Invoice.Id);
            return new VoiceExecutionResult(true, "تم حذف الفاتورة.", "/invoices");
        });
    }

    private async Task<VoiceCommandProposal> BuildEditInvoiceProposalAsync(string original, string text)
    {
        var reference = ArabicVoiceText.ExtractIdAfter(text, "رقم الفاتوره", "رقم الفاتورة", "رقم");
        if (!reference.HasValue) return Info(original, "حدد رقم الفاتورة.", "مثال: «عدل الفاتورة رقم 125».");
        var row = await FindInvoiceAsync(reference.Value);
        if (row is null) return Info(original, "الفاتورة غير موجودة.", $"لم أجد فاتورة تطابق الرقم {reference.Value}.");

        var paid = ArabicVoiceText.ExtractMoneyAfter(text, "خلي المدفوع", "اجعل المدفوع", "المدفوع", "مدفوع");
        if (!paid.HasValue)
        {
            return new VoiceCommandProposal
            {
                Transcript = original,
                Title = "فتح الفاتورة للتعديل",
                Summary = $"سأفتح الفاتورة {row.Invoice.InvoiceNumber} لتعديلها.",
                RequiresConfirmation = false,
                NavigateTo = $"/invoice/{row.Invoice.Id}",
                SpokenResponse = "تم فتح الفاتورة للتعديل."
            };
        }

        var model = await service.GetInvoiceEditorAsync(row.Invoice.Id);
        if (paid.Value < 0 || paid.Value > model.GrossAmount)
            return Info(original, "قيمة المدفوع غير صحيحة.", $"يجب أن تكون بين صفر و{model.GrossAmount:N0} ريال.");
        model.AmountPaid = paid.Value;
        model.PaymentMethod = model.AmountDue == 0 ? PaymentMethod.Cash : model.AmountPaid > 0 ? PaymentMethod.Mixed : PaymentMethod.Credit;

        var proposal = new VoiceCommandProposal
        {
            Transcript = original,
            Title = "تعديل الفاتورة",
            Summary = $"الفاتورة: {row.Invoice.InvoiceNumber}\nالمدفوع السابق: {row.Invoice.AmountPaid:N0} ر.ي\nالمدفوع الجديد: {model.AmountPaid:N0} ر.ي\nالمتبقي الجديد: {model.AmountDue:N0} ر.ي",
            RequiresConfirmation = true,
            SpokenResponse = "تم تجهيز التعديل. راجع المبلغ ثم أكد التنفيذ."
        };
        return proposal.WithExecutor(async () =>
        {
            await service.SaveInvoiceAsync(model);
            return new VoiceExecutionResult(true, "تم تعديل الفاتورة.", "/invoices");
        });
    }

    private async Task<VoiceCommandProposal> BuildDeleteCultivationProposalAsync(string original, string text)
    {
        var id = ArabicVoiceText.ExtractIdAfter(text, "رقم الخساره", "رقم الخسارة", "رقم");
        if (!id.HasValue) return Info(original, "حدد رقم الخسارة.", "مثال: «احذف الخسارة رقم 12».");
        var row = await FindCultivationAsync(id.Value);
        if (row is null) return Info(original, "الخسارة غير موجودة.", $"لم أجد خسارة برقم {id.Value}.");

        var proposal = new VoiceCommandProposal
        {
            Transcript = original,
            Title = "حذف خسارة",
            Summary = $"رقم السجل: {row.Expense.Id}\nالمزرعة: {row.FarmName}\nالنوع: {row.ExpenseTypeName}\nالمبلغ: {row.Expense.Amount:N0} ر.ي\nالمتبقي للدائن: {row.Outstanding:N0} ر.ي",
            RequiresConfirmation = true,
            SpokenResponse = "تم العثور على الخسارة. أكد الحذف بعد المراجعة."
        };
        return proposal.WithExecutor(async () =>
        {
            await service.DeleteCultivationExpenseAsync(row.Expense.Id);
            return new VoiceExecutionResult(true, "تم حذف الخسارة.", "/cultivation");
        });
    }

    private async Task<VoiceCommandProposal> BuildEditCultivationProposalAsync(string original, string text)
    {
        var id = ArabicVoiceText.ExtractIdAfter(text, "رقم الخساره", "رقم الخسارة", "رقم");
        if (!id.HasValue) return Info(original, "حدد رقم الخسارة.", "مثال: «عدل الخسارة رقم 12 وخلي المبلغ 30000».");
        var row = await FindCultivationAsync(id.Value);
        if (row is null) return Info(original, "الخسارة غير موجودة.", $"لم أجد خسارة برقم {id.Value}.");
        var newAmount = ArabicVoiceText.ExtractMoneyAfter(text, "خلي المبلغ", "اجعل المبلغ", "المبلغ", "الى", "إلى");
        if (!newAmount.HasValue)
            return new VoiceCommandProposal
            {
                Transcript = original,
                Title = "فتح الخسائر",
                Summary = "سأفتح شاشة الخسائر. اختر السجل واضغط تعديل.",
                RequiresConfirmation = false,
                NavigateTo = "/cultivation",
                SpokenResponse = "تم فتح شاشة الخسائر."
            };
        if (newAmount.Value <= 0 || newAmount.Value < row.Expense.PaidAmount)
            return Info(original, "المبلغ الجديد غير صحيح.", $"لا يمكن أن يقل عن المدفوع المسجل {row.Expense.PaidAmount:N0} ريال.");

        var model = new CultivationExpense
        {
            Id = row.Expense.Id,
            FarmId = row.Expense.FarmId,
            ExpenseTypeId = row.Expense.ExpenseTypeId,
            Amount = newAmount.Value,
            ExpenseDate = row.Expense.ExpenseDate,
            PaymentType = row.Expense.PaymentType,
            CreditorId = row.Expense.CreditorId,
            PaidAmount = row.Expense.PaidAmount,
            DueDate = row.Expense.DueDate,
            DebtStatus = row.Expense.DebtStatus,
            Notes = row.Expense.Notes,
            ReceiptNumber = row.Expense.ReceiptNumber,
            CreatedAt = row.Expense.CreatedAt
        };

        var proposal = new VoiceCommandProposal
        {
            Transcript = original,
            Title = "تعديل خسارة",
            Summary = $"المزرعة: {row.FarmName}\nالنوع: {row.ExpenseTypeName}\nالمبلغ السابق: {row.Expense.Amount:N0} ر.ي\nالمبلغ الجديد: {newAmount.Value:N0} ر.ي",
            RequiresConfirmation = true,
            SpokenResponse = "تم تجهيز تعديل الخسارة. أكد التنفيذ بعد المراجعة."
        };
        return proposal.WithExecutor(async () =>
        {
            await service.SaveCultivationExpenseAsync(model);
            return new VoiceExecutionResult(true, "تم تعديل الخسارة.", "/cultivation");
        });
    }

    private async Task<VoiceCommandProposal> BuildPdfProposalAsync(string original, string text)
    {
        var farms = await service.GetFarmsAsync(true);
        var farm = ArabicVoiceText.FindMentioned(text, farms, x => x.Name);
        var year = ArabicVoiceText.ExtractYear(text) ?? DateTime.Today.Year;
        var month = ArabicVoiceText.ExtractMonth(text);

        if (ArabicVoiceText.ContainsAny(text, "محاسبه", "محاسبة", "مالي", "الارباح", "الأرباح"))
        {
            return new VoiceCommandProposal
            {
                Transcript = original,
                Title = "تقرير محاسبي PDF",
                Summary = $"السنة: {year}\nالمزرعة: {farm?.Name ?? "كل المزارع"}",
                RequiresConfirmation = false,
                SpokenResponse = "سأجهز التقرير المحاسبي الآن."
            }.WithExecutor(async () =>
            {
                var path = await pdf.CreateExecutiveAccountingPdfAsync(farm?.Id, year);
                await pdf.SharePdfAsync(path, "التقرير المحاسبي");
                return new VoiceExecutionResult(true, "تم إنشاء التقرير المحاسبي PDF.");
            });
        }

        if (ArabicVoiceText.ContainsAny(text, "خساره", "خسارة", "خسائر"))
        {
            if (farm is null) return Info(original, "حدد المزرعة للتقرير.", "اذكر اسم المزرعة مع تقرير خسائر التربية.");
            return new VoiceCommandProposal
            {
                Transcript = original,
                Title = "تقرير خسائر التربية PDF",
                Summary = $"المزرعة: {farm.Name}\nالسنة: {year}",
                RequiresConfirmation = false,
                SpokenResponse = "سأجهز تقرير الخسائر الآن."
            }.WithExecutor(async () =>
            {
                var path = await pdf.CreateAnnualCultivationPdfAsync(farm.Id, year);
                await pdf.SharePdfAsync(path, "تقرير خسائر التربية");
                return new VoiceExecutionResult(true, "تم إنشاء تقرير الخسائر PDF.");
            });
        }

        if (month.HasValue)
        {
            return new VoiceCommandProposal
            {
                Transcript = original,
                Title = "فواتير شهرية PDF",
                Summary = $"الشهر: {month.Value:00}/{year}\nالمزرعة: {farm?.Name ?? "كل المزارع"}",
                RequiresConfirmation = false,
                SpokenResponse = "سأجهز فواتير الشهر الآن."
            }.WithExecutor(async () =>
            {
                var path = await pdf.CreateMonthlyInvoicesPdfAsync(farm?.Id, year, month.Value);
                await pdf.SharePdfAsync(path, "فواتير الشهر");
                return new VoiceExecutionResult(true, "تم إنشاء تقرير الفواتير الشهري PDF.");
            });
        }

        if (farm is null) return Info(original, "حدد المزرعة أو الشهر.", "للتقرير السنوي اذكر اسم المزرعة، أو اذكر الشهر لتقرير كل المزارع.");
        return new VoiceCommandProposal
        {
            Transcript = original,
            Title = "فواتير سنوية PDF",
            Summary = $"المزرعة: {farm.Name}\nالسنة: {year}",
            RequiresConfirmation = false,
            SpokenResponse = "سأجهز فواتير السنة الآن."
        }.WithExecutor(async () =>
        {
            var path = await pdf.CreateAnnualInvoicesPdfAsync(farm.Id, year);
            await pdf.SharePdfAsync(path, "تقرير الفواتير السنوي");
            return new VoiceExecutionResult(true, "تم إنشاء تقرير الفواتير السنوي PDF.");
        });
    }

    private async Task<VoiceCommandProposal?> BuildQueryProposalAsync(string original, string text)
    {
        var isQuestion = ArabicVoiceText.ContainsAny(text, "كم", "اعطني", "اعرض", "ما هو", "ماهي", "وش", "ايش");
        if (!isQuestion && !ArabicVoiceText.ContainsAny(text, "مبيعات اليوم", "ديون العملاء", "الربح", "الزكاه", "الزكاة")) return null;

        var customers = await service.GetCustomerLookupsAsync();
        var mentionedCustomer = ArabicVoiceText.FindMentioned(text, customers, x => x.Name);
        if (mentionedCustomer is not null && ArabicVoiceText.ContainsAny(text, "دين", "رصيد", "حساب", "متبقي"))
        {
            var balance = await service.GetCustomerBalanceAsync(mentionedCustomer.Id);
            var amount = balance?.Balance ?? 0m;
            return Info(original, $"رصيد {mentionedCustomer.Name}", $"المتبقي على العميل: {amount:N0} ر.ي", $"رصيد {mentionedCustomer.Name} هو {amount:N0} ريال.");
        }

        if (ArabicVoiceText.ContainsAny(text, "مبيعات اليوم", "بيع اليوم"))
        {
            var dashboard = await service.GetDashboardAsync();
            return Info(original, "مبيعات اليوم", $"{dashboard.SalesToday:N0} ر.ي", $"مبيعات اليوم {dashboard.SalesToday:N0} ريال.");
        }

        if (ArabicVoiceText.ContainsAny(text, "ديون العملاء", "ذمم العملاء"))
        {
            var dashboard = await service.GetDashboardAsync();
            return Info(original, "ديون العملاء", $"{dashboard.CustomerDebts:N0} ر.ي", $"إجمالي ديون العملاء {dashboard.CustomerDebts:N0} ريال.");
        }

        if (ArabicVoiceText.ContainsAny(text, "الزكاه", "الزكاة"))
        {
            var dashboard = await service.GetDashboardAsync();
            return Info(original, "الزكاة المعلقة", $"{dashboard.PendingZakat:N0} ر.ي", $"الزكاة المعلقة {dashboard.PendingZakat:N0} ريال.");
        }

        var year = ArabicVoiceText.ExtractYear(text) ?? DateTime.Today.Year;
        var accounting = await service.GetAccountingCenterAsync(year);
        if (ArabicVoiceText.ContainsAny(text, "الربح", "الارباح", "الأرباح", "الصافي"))
            return Info(original, "الربح المحاسبي", $"{accounting.AccountingProfit:N0} ر.ي", $"الربح المحاسبي {accounting.AccountingProfit:N0} ريال.");
        if (ArabicVoiceText.ContainsAny(text, "خسائر", "خساره", "خسارة"))
            return Info(original, "خسائر التربية", $"{accounting.CultivationExpenses:N0} ر.ي", $"خسائر التربية {accounting.CultivationExpenses:N0} ريال.");
        if (ArabicVoiceText.ContainsAny(text, "مبيعات", "المبيعات"))
        {
            var month = ArabicVoiceText.ExtractMonth(text);
            if (month.HasValue)
            {
                var rows = await service.GetInvoicesAsync(null, year, month.Value);
                var sales = rows.Where(x => x.Invoice.Status == InvoiceStatus.Posted).Sum(x => x.Invoice.GrossAmount);
                return Info(original, "مبيعات الشهر", $"{sales:N0} ر.ي", $"مبيعات الشهر {sales:N0} ريال.");
            }
            return Info(original, "مبيعات السنة", $"{accounting.GrossSales:N0} ر.ي", $"مبيعات السنة {accounting.GrossSales:N0} ريال.");
        }

        return null;
    }

    private static VoiceCommandProposal? BuildNavigationProposal(string original, string text)
    {
        string? route = null;
        string? title = null;
        if (ArabicVoiceText.ContainsAny(text, "فاتوره جديده", "فاتورة جديدة", "انشاء فاتوره", "انشاء فاتورة")) { route = "/invoice/new"; title = "فاتورة جديدة"; }
        else if (ArabicVoiceText.ContainsAny(text, "الفواتير", "قائمه الفواتير", "قائمة الفواتير")) { route = "/invoices"; title = "الفواتير"; }
        else if (ArabicVoiceText.ContainsAny(text, "الخسائر", "خسائر التربيه", "خسائر التربية")) { route = "/cultivation"; title = "الخسائر"; }
        else if (ArabicVoiceText.ContainsAny(text, "المحاسبه", "المحاسبة", "المركز المالي")) { route = "/accounting"; title = "المحاسبة"; }
        else if (ArabicVoiceText.ContainsAny(text, "التقارير")) { route = "/reports"; title = "التقارير"; }
        else if (ArabicVoiceText.ContainsAny(text, "العملاء")) { route = "/customers"; title = "العملاء"; }
        else if (ArabicVoiceText.ContainsAny(text, "المزارع")) { route = "/farms"; title = "المزارع"; }
        else if (ArabicVoiceText.ContainsAny(text, "الزكاه", "الزكاة")) { route = "/zakat"; title = "الزكاة"; }
        if (route is null) return null;
        return new VoiceCommandProposal
        {
            Transcript = original,
            Title = title!,
            Summary = $"فتح شاشة {title}",
            RequiresConfirmation = false,
            NavigateTo = route,
            SpokenResponse = $"تم فتح {title}."
        };
    }

    private async Task<InvoiceListRow?> FindInvoiceAsync(long reference)
    {
        var years = Enumerable.Range(0, 8).Select(x => DateTime.Today.Year - x).Append(DateTime.Today.Year + 1).Distinct();
        foreach (var year in years)
        {
            var rows = await service.GetInvoicesAsync(null, year);
            var match = rows.FirstOrDefault(x => x.Invoice.Id == reference) ??
                        rows.FirstOrDefault(x => x.Invoice.InvoiceNumber.Contains(reference.ToString(), StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        return null;
    }

    private async Task<CultivationExpenseRow?> FindCultivationAsync(long id)
    {
        foreach (var year in Enumerable.Range(0, 8).Select(x => DateTime.Today.Year - x).Append(DateTime.Today.Year + 1).Distinct())
        {
            var rows = await service.GetCultivationExpensesAsync(null, year);
            var match = rows.FirstOrDefault(x => x.Expense.Id == id);
            if (match is not null) return match;
        }
        return null;
    }

    private static VoiceCommandProposal Info(string transcript, string title, string summary, string? spoken = null)
        => new()
        {
            Transcript = transcript,
            Title = title,
            Summary = summary,
            RequiresConfirmation = false,
            SpokenResponse = spoken ?? summary
        };
}
