using Android.App;
using Android.Speech;
using Microsoft.Maui.ApplicationModel;
using QatFarm.Mobile.Services;

namespace QatFarm.Mobile;

public sealed class AndroidVoiceRecognitionService : IVoiceRecognitionService
{
    public bool IsSupported
    {
        get
        {
            try { return SpeechRecognizer.IsRecognitionAvailable(Application.Context); }
            catch { return false; }
        }
    }

    public async Task<VoiceRecognitionResult> ListenAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
            return new(false, null, "خدمة التعرف على الصوت غير متاحة على هذا الجهاز. يمكنك كتابة الأمر بدلاً من ذلك.");

        try
        {
            var text = await MainActivity.CaptureSpeechAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(text))
                return new(false, null, "لم يتم التقاط أمر صوتي واضح.");
            return new(true, text.Trim(), "تم التقاط الأمر.");
        }
        catch (OperationCanceledException)
        {
            return new(false, null, "تم إلغاء الاستماع.");
        }
        catch (Exception ex)
        {
            return new(false, null, $"تعذر تشغيل التعرف الصوتي: {ex.Message}");
        }
    }
}
