#if WINDOWS
using QatFarm.Mobile.Services;
using Windows.Media.SpeechRecognition;

namespace QatFarm.Mobile;

public sealed class WindowsVoiceRecognitionService : IVoiceRecognitionService
{
    public bool IsSupported => true;

    public async Task<VoiceRecognitionResult> ListenAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var recognizer = new SpeechRecognizer();
            var compilation = await recognizer.CompileConstraintsAsync().AsTask(cancellationToken);
            if (compilation.Status != SpeechRecognitionResultStatus.Success)
                return new(false, null, "تعذر تجهيز التعرف الصوتي في Windows. تأكد من تفعيل الميكروفون وحزمة الكلام في إعدادات النظام.");

            var result = await recognizer.RecognizeAsync().AsTask(cancellationToken);
            if (result.Status != SpeechRecognitionResultStatus.Success || string.IsNullOrWhiteSpace(result.Text))
                return new(false, null, "لم يتم التقاط أمر صوتي واضح.");

            return new(true, result.Text.Trim(), "تم التقاط الأمر.");
        }
        catch (OperationCanceledException)
        {
            return new(false, null, "تم إلغاء الاستماع.");
        }
        catch (Exception ex)
        {
            return new(false, null, $"تعذر تشغيل المعاون الصوتي في Windows: {ex.Message}");
        }
    }
}
#endif
