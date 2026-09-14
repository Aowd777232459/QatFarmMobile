namespace AWAD.AIDeveloper.Core;

public sealed class AppState
{
    public string WorkspacePath { get; set; } = string.Empty;
    public string ApiKey { get; set; } = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
    public string Model { get; set; } = Environment.GetEnvironmentVariable("AWAD_AI_MODEL") ?? "gpt-5.6-sol";
    public string BaseUrl { get; set; } = Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "https://api.openai.com/v1";
    public bool ApprovalRequired { get; set; } = true;
    public string DataRoot { get; }
    public string BackupsRoot { get; }
    public string StagingRoot { get; }
    public Dictionary<string, AgentPlan> Plans { get; } = new(StringComparer.OrdinalIgnoreCase);

    public AppState()
    {
        DataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AWAD-AI-Developer");
        BackupsRoot = Path.Combine(DataRoot, "backups");
        StagingRoot = Path.Combine(DataRoot, "staging");
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(BackupsRoot);
        Directory.CreateDirectory(StagingRoot);
    }
}

public sealed class AgentPlan
{
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string? OriginalTask { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string Status { get; set; } = "continue";
    public List<AgentAction> Actions { get; set; } = [];
    public string Next { get; set; } = string.Empty;
}

public sealed class AgentAction
{
    public string Type { get; set; } = string.Empty;
    public string? Path { get; set; }
    public string? Content { get; set; }
    public string? Old { get; set; }
    public string? New { get; set; }
    public string? Command { get; set; }
    public string? Reason { get; set; }
}

public sealed record ActionLog(string Type, string Target, bool Success, string Message);
public sealed record CommandResult(int ExitCode, string Output);
public sealed record ApplyResult(bool Success, string Checkpoint, IReadOnlyList<ActionLog> Logs, string Status, string Summary)
{
    public string LastError => Logs.LastOrDefault(x => !x.Success)?.Message ?? string.Empty;
}
public sealed record AutoCycle(int Cycle, AgentPlan Plan, ApplyResult Apply);
public sealed record AutoDevelopResult(bool Success, IReadOnlyList<AutoCycle> Cycles, string Message);
public sealed record MemoryEntry(DateTimeOffset When, string Task, string Result);
public sealed record SettingsRequest(string? ApiKey, string? Model, string? BaseUrl, bool ApprovalRequired = true);
public sealed record OpenProjectRequest(string Path);
public sealed record ChatRequest(string Message);
public sealed record ApplyRequest(string PlanId);
public sealed record AutoRequest(string Message, int MaxCycles = 3);
