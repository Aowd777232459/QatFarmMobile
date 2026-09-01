namespace QatFarm.Mobile.Services;

public sealed record VoiceExecutionResult(
    bool Success,
    string Message,
    string? NavigateTo = null,
    string? FilePath = null);

public sealed class VoiceCommandProposal
{
    private Func<Task<VoiceExecutionResult>>? executor;

    public string Transcript { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public bool RequiresConfirmation { get; init; }
    public string? NavigateTo { get; init; }
    public string? SpokenResponse { get; init; }

    public VoiceCommandProposal WithExecutor(Func<Task<VoiceExecutionResult>> action)
    {
        executor = action;
        return this;
    }

    public Task<VoiceExecutionResult> ExecuteAsync()
        => executor is null
            ? Task.FromResult(new VoiceExecutionResult(true, SpokenResponse ?? Summary, NavigateTo))
            : executor();
}
