using QatFarm.Mobile.Models;

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

        if (ArabicVoiceText.ContainsAny(text, "pdf", "بي دي اف", "تقرير", "تقارير", "صدر", "تصدير") ||
            ArabicVoiceText.ContainsAny(text, "كم", "رصيد", "دين", "حساب", "مبيعات", "الزكاه", "الزكاة", "الربح", "الارباح", "الأرباح"))
            return await legacy.InterpretAsync(original);

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
        var customer = FindCustomerSafely(text, customers);

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

        if (ArabicVoiceText.ContainsAny(text, "فاتوره", "فاتورة", "بيع", "عميل"))
        {
            return new VoiceCommandProposal
            {
                Transcript = original,
                Title = "فتح فاتورة جديدة",
                Summary = "لم أجد اسماً وحيداً مطابقاً لعميل مسجل. سأفتح شاشة الفاتورة، ثم اختر العميل يدويًا أو أعد ذكر جزء أوضح من اسمه.",
                RequiresConfirmation = false,
                NavigateTo = "/invoice/new",
                SpokenResponse = "تم فتح فاتورة جديدة. اختر العميل وطريقة الدفع وأكمل البيانات."
            };
        }

        return await legacy.InterpretAsync(original);
    }

    private static Customer? FindCustomerSafely(string text, IReadOnlyList<Customer> customers)
    {
        var exact = ArabicVoiceText.FindMentioned(text, customers, x => x.Name);
        if (exact is not null) return exact;

        var normalized = ArabicVoiceText.Normalize(text);
        var ignored = new HashSet<string>(StringComparer.Ordinal)
        {
            "فاتوره", "بيع", "العميل", "عميل", "للعميل", "افتح", "افتحلي", "اعمل", "انشئ", "سوي", "جديد", "اسم", "لو سمحت"
        };
        var spoken = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length >= 2 && !ignored.Contains(x))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (spoken.Length == 0) return null;

        var ranked = customers.Select(customer =>
        {
            var name = ArabicVoiceText.Normalize(customer.Name);
            var nameTokens = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var matches = spoken.Count(word => nameTokens.Any(token => token == word ||
                (word.Length >= 4 && token.StartsWith(word, StringComparison.Ordinal)) ||
                (token.Length >= 4 && word.StartsWith(token, StringComparison.Ordinal))));
            return new { Customer = customer, Matches = matches, NameTokenCount = nameTokens.Length };
        })
        .Where(x => x.Matches > 0)
        .OrderByDescending(x => x.Matches)
        .ThenBy(x => x.NameTokenCount)
        .ToList();

        if (ranked.Count == 0) return null;
        var best = ranked[0];
        if (spoken.Length > 1 && best.Matches < Math.Min(2, spoken.Length)) return null;
        var tied = ranked.Count(x => x.Matches == best.Matches);
        return tied == 1 ? best.Customer : null;
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
