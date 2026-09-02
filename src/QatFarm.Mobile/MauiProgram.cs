using Microsoft.Extensions.Logging;
using QatFarm.Mobile.Data;
using QatFarm.Mobile.Services;

namespace QatFarm.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif
        builder.Services.AddSingleton<MobileDb>();
        builder.Services.AddSingleton<AppSession>();
        builder.Services.AddSingleton<DebtSmsService>();
        builder.Services.AddSingleton<ZakatNotificationService>();
        builder.Services.AddSingleton<QatFarmService>();
        builder.Services.AddSingleton<MobilePdfService>();
        builder.Services.AddSingleton<BackupService>();
        builder.Services.AddSingleton<LocalSyncService>();
#if ANDROID
        builder.Services.AddSingleton<IVoiceRecognitionService, AndroidVoiceRecognitionService>();
#elif WINDOWS
        builder.Services.AddSingleton<IVoiceRecognitionService, WindowsVoiceRecognitionService>();
#else
        builder.Services.AddSingleton<IVoiceRecognitionService, UnsupportedVoiceRecognitionService>();
#endif
        builder.Services.AddSingleton<VoiceAssistantService>();
        builder.Services.AddSingleton<GuidedVoiceAssistantService>();
        return builder.Build();
    }
}
