using System.Text.Json;

namespace QatFarm.Mobile.Models;

public sealed class LocalSyncBatch
{
    public string DeviceId { get; set; } = string.Empty;
    public List<LocalSyncRecord> Records { get; set; } = [];
}

public sealed class LocalSyncRecord
{
    public string Entity { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public JsonElement Data { get; set; }
}

public sealed class LocalSyncResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset ServerTime { get; set; }
    public int Received { get; set; }
    public List<LocalSyncRecord> Records { get; set; } = [];
}

public sealed class LocalSyncPreferences
{
    public string ServerUrl { get; set; } = string.Empty;
    public string PairingKey { get; set; } = string.Empty;
    public bool AutoSync { get; set; } = true;
    public DateTimeOffset? LastSuccessAt { get; set; }
    public string? LastMessage { get; set; }
}
