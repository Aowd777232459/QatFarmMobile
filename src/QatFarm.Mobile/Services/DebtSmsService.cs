using Android.Telephony;
using Microsoft.Maui.ApplicationModel;
using QatFarm.Mobile.Models;

namespace QatFarm.Mobile.Services;

public sealed record DebtSmsResult(bool Sent, string Message);
public sealed record SaleSmsItem(string Name, int Quantity, decimal UnitPrice, decimal Total);

public sealed class SendSmsPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
    [
        (Android.Manifest.Permission.SendSms, true)
    ];
}

public sealed class DebtSmsService
{
    public Task<DebtSmsResult> SendCreditAlertAsync(Customer customer, decimal total, decimal paid,
        decimal balance, decimal limit, bool blocked)
    {
        if (!customer.DebtAlertEnabled)
            return Task.FromResult(new DebtSmsResult(false, "تنبيه الرسائل غير مفعل لهذا العميل."));
        if (string.IsNullOrWhiteSpace(customer.Phone))
            return Task.FromResult(new DebtSmsResult(false, "رقم العميل غير مسجل."));

        var seller = string.IsNullOrWhiteSpace(customer.SellerPhone)
            ? string.Empty
            : $" البائع: {NormalizePhone(customer.SellerPhone)}.";
        var status = blocked ? "تم إيقاف عملية تتجاوز الحد الائتماني" : "بلغ حسابك الحد الائتماني";
        var text = $"عواد سوفت: {status}. إجمالي الحساب {total:N0} ر.ي، المدفوع {paid:N0} ر.ي، المتبقي {balance:N0} ر.ي، الحد {limit:N0} ر.ي.{seller}";
        return SendAsync(customer.Phone, text);
    }

    public Task<DebtSmsResult> SendSaleReceiptAsync(string phone, string buyerName, SalesInvoice invoice,
        IReadOnlyList<SaleSmsItem> items)
    {
        var details = string.Join("، ", items.Select(x => $"{x.Name} {x.Quantity:N0}×{x.UnitPrice:N0}={x.Total:N0}"));
        var text = $"عواد سوفت - تفاصيل البيع {invoice.InvoiceNumber}: {details}. الإجمالي {invoice.GrossAmount:N0} ر.ي، المدفوع {invoice.AmountPaid:N0} ر.ي، المتبقي {invoice.AmountDue:N0} ر.ي. العميل: {buyerName}.";
        return SendAsync(phone, text);
    }

    public Task<DebtSmsResult> SendAccountUpdateAsync(Customer customer, decimal paymentAmount,
        decimal total, decimal paid, decimal balance)
    {
        if (string.IsNullOrWhiteSpace(customer.Phone))
            return Task.FromResult(new DebtSmsResult(false, "رقم العميل غير مسجل."));
        var text = $"عواد سوفت: تم تسجيل دفعة {paymentAmount:N0} ر.ي. إجمالي الحساب {total:N0} ر.ي، إجمالي المدفوع {paid:N0} ر.ي، المتبقي {balance:N0} ر.ي.";
        return SendAsync(customer.Phone, text);
    }

    private static async Task<DebtSmsResult> SendAsync(string phoneValue, string text)
    {
        try
        {
            var permission = await Permissions.CheckStatusAsync<SendSmsPermission>();
            if (permission != PermissionStatus.Granted)
            {
                permission = await MainThread.InvokeOnMainThreadAsync(
                    () => Permissions.RequestAsync<SendSmsPermission>());
            }
            if (permission != PermissionStatus.Granted)
                return new(false, "لم يتم منح التطبيق إذن إرسال الرسائل SMS.");

            var phone = NormalizePhone(phoneValue);
            if (string.IsNullOrWhiteSpace(phone)) return new(false, "رقم الهاتف غير صالح.");
            var sms = SmsManager.Default;
            if (sms is null) return new(false, "خدمة SMS غير متاحة على هذا الهاتف.");
            foreach (var part in sms.DivideMessage(text))
                sms.SendTextMessage(phone, null, part, null, null);
            return new(true, "تم إرسال رسالة SMS.");
        }
        catch (Exception ex)
        {
            return new(false, $"تعذر إرسال SMS: {ex.Message}");
        }
    }

    private static string NormalizePhone(string value)
    {
        var text = value.Trim();
        var plus = text.StartsWith('+');
        var digits = new string(text.Where(char.IsDigit).ToArray());
        return plus && digits.Length > 0 ? "+" + digits : digits;
    }
}
