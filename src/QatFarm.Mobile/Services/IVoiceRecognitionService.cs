namespace QatFarm.Mobile.Services;

public sealed record VoiceRecognitionResult(bool Success, string? Text, string Message);

public interface IVoiceRecognitionService
{
    bool IsSupported { get; }
    Task<VoiceRecognitionResult> ListenAsync(CancellationToken cancellationToken = default);
}
