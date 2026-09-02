namespace QatFarm.Mobile.Services;

/// <summary>
/// Voice layer used by the UI. Invoice-related commands are intentionally guided:
/// the assistant only opens the correct screen and selects the customer. The user
/// remains responsible for choosing payment type, entering items and pressing Save.
/// Other safe queries/reports continue to use the existing assistant service.
/// </summary>
public sealed class GuidedVoiceAssistantService
{
    private readonly QatFarmService service;
    private readonly VoiceAssistantService legacy;

    public GuidedVoiceAssistantService(QatFarmService service, VoiceAssistantService legacy)
    {
        this.service = service;
        this.legacy = legacy;
    }

    public async Task<VoiceCommandProposal> InterpretAsync(string? transcript)
    {
        var original = transcript?.Trim() ?? string.Empty;
        var text = ArabicVoiceText.Normalize(original);
        if (text.Length == 0)
            return Info(original, "لم أسمع اسماً أو أمراً واضحاً.", "قل اسم العميل أو اسم الشاشة التي تريد فتحها.");

        // Reports and read-only accounting questions keep their existing behavior.
        if (ArabicVoiceText.ContainsAny(text, "pdf", "بي دي اف", "تقرير", "تقارير", "صدر", "تصدير") ||
            ArabicVoiceText.ContainsAny(text, "كم", "رصيد", "دين", "حساب", "مبيعات", "الزكاه", "الزكاة", "الربح", "الارباح", "الأرباح"))
            return await legacy.InterpretAsync(original);

        // Editing/deleting invoices by voice is disabled. Move the user to the list instead.
        if (ArabicVoiceText.ContainsAny(text, "فاتوره", "فاتورة") &&
            ArabicVoiceText.ContainsAny(text, "عدل", "تعديل", "غير", "احذف", "حذف", "امسح", "الغ"))
        {
            return new VoiceCommandProposal
            {
                Transcript = original,
                Title = "فتح سجل الفواتير",
                Summary = "سأفتح سجل الفواتير. اختر الفاتورة بنفسك ثم استخدم تعديل أو حذف حسب صلاحيتك.",
                RequiresConfirmation = false,
                NavigateTo = "/invoices",
                SpokenResponse = "تم فتح سجل الفواتير. اختر الفاتورة التي تريدها."
            };
        }

        var customers = await service.GetCustomerLookupsAsync();
        var customer = ArabicVoiceText.FindMentioned(text, customers, x => x.Name);

        // Saying only the customer's name is enough to start a guided invoice.
        if (customer is not null)
        {
            return new VoiceCommandProposal
            {
                Transcript = original,
                Title = $"فاتورة: {customer.Name}",
                Summary = $"تم العثور على العميل {customer.Name}. سأفتح شاشة الفاتورة وأحدد العميل فقط. اختر أنت نقدي أو آجل أو مختلط، ثم أدخل الأصناف والمدفوع واضغط حفظ.",
                RequiresConfirmation = false,
                NavigateTo = $"/invoice/new?voiceCustomerId={customer.Id}",
                SpokenResponse = $"تم تحديد العميل {customer.Name}. اختر طريقة الدفع وأكمل الفاتورة بنفسك."
            };
        }

        // Any invoice/بيع command is guided only; it never creates/saves an invoice.
        if (ArabicVoiceText.ContainsAny(text, "فاتوره", "فاتورة", "بيع", "عميل"))
        {
            return new VoiceCommandProposal
            {
                Transcript = original,
                Title = "فتح فاتورة جديدة",
                Summary = "لم أجد اسم عميل مسجل في الكلام. سأفتح شاشة الفاتورة، ثم اختر العميل يدويًا أو أعد المحاولة بذكر اسمه كما هو مسجل.",
                RequiresConfirmation = false,
                NavigateTo = "/invoice/new",
                SpokenResponse = "تم فتح فاتورة جديدة. اختر العميل وطريقة الدفع وأكمل البيانات."
            };
        }

        return await legacy.InterpretAsync(original);
    }

    private static VoiceCommandProposal Info(string transcript, string title, string summary)
        => new()
        {
            Transcript = transcript,
            Title = title,
            Summary = summary,
            RequiresConfirmation = false,
            SpokenResponse = summary
        };
}
