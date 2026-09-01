using System.Text.Json;

namespace QatFarm.Web.Models;

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
