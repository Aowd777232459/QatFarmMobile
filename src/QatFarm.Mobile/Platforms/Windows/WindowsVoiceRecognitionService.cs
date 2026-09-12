using QatFarm.Mobile.Services;

namespace QatFarm.Mobile;

/// <summary>
/// نسخة Windows تحافظ على نفس واجهة المساعد الصوتي بدون الاعتماد على Android SpeechRecognizer.
/// يمكن تطوير التعرف الصوتي الخاص بـ Windows لاحقاً دون التأثير على المزامنة أو المحاسبة.
/// </summary>
public sealed class WindowsVoiceRecognitionService : IVoiceRecognitionService
{
    public bool IsSupported => false;

    public Task<VoiceRecognitionResult> ListenAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new VoiceRecognitionResult(false, null,
            "المساعد الصوتي مخصص حالياً لنسخة الجوال، بينما جميع وظائف النظام والمزامنة متاحة على الكمبيوتر."));
}
