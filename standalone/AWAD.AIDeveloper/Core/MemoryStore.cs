using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AWAD.AIDeveloper.Core;

public sealed class MemoryStore
{
    private readonly AppState _state;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public MemoryStore(AppState state) => _state = state;

    public IReadOnlyList<MemoryEntry> Load(string workspace)
    {
        var path = PathFor(workspace);
        if (!File.Exists(path)) return Array.Empty<MemoryEntry>();
        try { return JsonSerializer.Deserialize<List<MemoryEntry>>(File.ReadAllText(path), JsonOptions) ?? []; }
        catch { return Array.Empty<MemoryEntry>(); }
    }

    public void Append(string workspace, MemoryEntry entry)
    {
        var list = Load(workspace).ToList();
        list.Add(entry);
        if (list.Count > 120) list = list.Skip(Math.Max(0, list.Count - 120)).ToList();
        var path = PathFor(workspace);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(list, JsonOptions), new UTF8Encoding(false));
    }

    public string Summarize(string workspace) => string.Join("\n", Load(workspace).TakeLast(12).Select(x => $"[{x.When:yyyy-MM-dd HH:mm}] TASK: {x.Task}\nRESULT: {x.Result}"));

    private string PathFor(string workspace)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(workspace)))[..16];
        return Path.Combine(_state.DataRoot, "memory", $"{hash}.json");
    }
}
