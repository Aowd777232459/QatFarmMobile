using QatFarm.Mobile.Data;
using QatFarm.Mobile.Models;

#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;
using Microsoft.Maui.ApplicationModel;
#endif

namespace QatFarm.Mobile.Services;

#if ANDROID
public sealed class PostNotificationsPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
    [
        (Android.Manifest.Permission.PostNotifications, true)
    ];
}
#endif

public sealed class ZakatNotificationService
{
    private readonly MobileDb _db;
    private const int NotificationId = 4201;
    private const string ChannelId = "qatfarm-zakat";

    public ZakatNotificationService(MobileDb db) => _db = db;

    public async Task RefreshAsync(bool requestPermission = true)
    {
#if ANDROID
        try
        {
            var db = await _db.GetAsync();
            var rows = await db.Table<SalesInvoice>()
                .Where(x => !x.IsDeleted && x.Status == InvoiceStatus.Posted &&
                            x.ZakatStatus == ZakatPaymentStatus.Pending && x.ZakatAmount > 0)
                .ToListAsync();
            var manager = (NotificationManager?)Android.App.Application.Context.GetSystemService(Context.NotificationService);
            if (manager is null) return;
            EnsureChannel(manager);
            if (rows.Count == 0)
            {
                manager.Cancel(NotificationId);
                return;
            }

            if ((int)Build.VERSION.SdkInt >= 33)
            {
                var status = await Permissions.CheckStatusAsync<PostNotificationsPermission>();
                if (status != PermissionStatus.Granted && requestPermission)
                    status = await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<PostNotificationsPermission>());
                if (status != PermissionStatus.Granted) return;
            }

            var total = rows.Sum(x => x.ZakatAmount);
            Notification.Builder builder = (int)Build.VERSION.SdkInt >= 26
                ? new Notification.Builder(Android.App.Application.Context, ChannelId)
                : new Notification.Builder(Android.App.Application.Context);
            var notification = builder
                .SetContentTitle("الزكاة المعلقة")
                .SetContentText($"{rows.Count} فاتورة — {total:N0} ر.ي بانتظار تأكيد الوصول")
                .SetSmallIcon(Android.Resource.Drawable.IcDialogAlert)
                .SetAutoCancel(true)
                .Build();
            manager.Notify(NotificationId, notification);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ZAKAT_NOTIFICATION_ERROR {ex}");
        }
#else
        await Task.CompletedTask;
#endif
    }

#if ANDROID
    private static void EnsureChannel(NotificationManager manager)
    {
        if ((int)Build.VERSION.SdkInt < 26) return;
        if (manager.GetNotificationChannel(ChannelId) is not null) return;
        manager.CreateNotificationChannel(new NotificationChannel(ChannelId, "تنبيهات الزكاة", NotificationImportance.High)
        {
            Description = "تنبيه عند وجود زكاة معلقة تحتاج إلى تأكيد الدفع والوصول."
        });
    }
#endif
}
