using System.Text.Json;

namespace AWAD.AIDeveloper.Core;

public sealed class AgentService
{
    private readonly AppState _state;
    private readonly WorkspaceService _workspace;
    private readonly MemoryStore _memory;
    private readonly OpenAiResponsesClient _ai;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public AgentService(AppState state, WorkspaceService workspace, MemoryStore memory, OpenAiResponsesClient ai)
    {
        _state = state; _workspace = workspace; _memory = memory; _ai = ai;
    }

    public async Task<AgentPlan> ProposeAsync(string? message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_state.WorkspacePath)) throw new InvalidOperationException("اختر مجلد مشروع أولاً.");
        if (string.IsNullOrWhiteSpace(message)) throw new InvalidOperationException("اكتب المهمة المطلوبة.");

        var context = _workspace.BuildProjectContext(_state.WorkspacePath, message);
        var memory = _memory.Summarize(_state.WorkspacePath);
        var input = $"""
WORKSPACE: {_state.WorkspacePath}
USER TASK:
{message}

RECENT PROJECT MEMORY:
{memory}

PROJECT CONTEXT:
{context}
""";
        var raw = await _ai.GenerateAsync(AgentInstructions, input, ct);
        var plan = ParsePlan(raw);
        plan.Id = Guid.NewGuid().ToString("N");
        plan.CreatedAt = DateTimeOffset.Now;
        plan.OriginalTask = message;
        ValidatePlan(plan);
        _memory.Append(_state.WorkspacePath, new MemoryEntry(DateTimeOffset.Now, message, $"Proposed: {plan.Summary}"));
        return plan;
    }

    public async Task<ApplyResult> ApplyAsync(AgentPlan plan, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_state.WorkspacePath)) throw new InvalidOperationException("اختر المشروع أولاً.");
        var result = await _workspace.ApplyPlanAsync(_state.WorkspacePath, plan, _state.BackupsRoot, ct);
        _memory.Append(_state.WorkspacePath, new MemoryEntry(DateTimeOffset.Now, plan.OriginalTask ?? plan.Summary,
            result.Success ? "Applied successfully" : "Applied with errors: " + result.LastError));
        return result;
    }

    public async Task<AutoDevelopResult> AutoDevelopAsync(string? task, int maxCycles, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(task)) throw new InvalidOperationException("اكتب المهمة المطلوبة.");
        var cycles = new List<AutoCycle>();
        var prompt = task;
        for (var i = 1; i <= maxCycles; i++)
        {
            ct.ThrowIfCancellationRequested();
            var plan = await ProposeAsync(prompt, ct);
            var apply = await ApplyAsync(plan, ct);
            cycles.Add(new AutoCycle(i, plan, apply));

            if (apply.Success && string.Equals(plan.Status, "done", StringComparison.OrdinalIgnoreCase))
                return new AutoDevelopResult(true, cycles, "اكتملت المهمة ونجحت دورة التحقق.");
            if (apply.Success && plan.Actions.All(a => a.Type != "run_command"))
                return new AutoDevelopResult(true, cycles, "طُبقت التعديلات. لم تتضمن الخطة أمر بناء/اختبار تلقائي.");

            prompt = $"""
المهمة الأصلية: {task}
هذه نتيجة الدورة السابقة. أصلح الأخطاء فقط وواصل حتى ينجح البناء/الاختبار.
SUMMARY: {plan.Summary}
RESULT: {(apply.Success ? "success" : "failure")}
LOGS:
{string.Join("\n\n", apply.Logs.Select(l => $"[{l.Type}] {l.Target}\n{l.Message}"))}
""";
        }
        return new AutoDevelopResult(false, cycles, "انتهى الحد الأقصى لدورات الإصلاح. راجع السجل ثم شغّل دورة جديدة.");
    }

    private static AgentPlan ParsePlan(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNl = text.IndexOf('\n');
            if (firstNl >= 0) text = text[(firstNl + 1)..];
            var last = text.LastIndexOf("```", StringComparison.Ordinal);
            if (last >= 0) text = text[..last];
        }
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start) text = text[start..(end + 1)];
        try { return JsonSerializer.Deserialize<AgentPlan>(text, JsonOptions) ?? throw new InvalidOperationException("الخطة فارغة."); }
        catch (Exception ex) { throw new InvalidOperationException("تعذر قراءة خطة الذكاء الاصطناعي كـ JSON صحيح. " + ex.Message); }
    }

    private static void ValidatePlan(AgentPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.Summary)) plan.Summary = "خطة تطوير";
        plan.Status = plan.Status?.Trim().ToLowerInvariant() switch { "done" => "done", "blocked" => "blocked", _ => "continue" };
        plan.Actions ??= [];
        if (plan.Actions.Count > 24) throw new InvalidOperationException("الخطة تحتوي إجراءات أكثر من الحد الآمن.");
        foreach (var a in plan.Actions)
        {
            a.Type = a.Type?.Trim().ToLowerInvariant() ?? string.Empty;
            if (a.Type is not ("write_file" or "replace_text" or "delete_file" or "run_command" or "note"))
                throw new InvalidOperationException($"إجراء غير معروف: {a.Type}");
        }
    }

    private const string AgentInstructions = """
You are AWAD AI Developer, a senior software engineering agent operating on a user-selected local project.
Your job is to implement the user's requested change with the smallest correct set of edits and verification commands.

CRITICAL RULES:
1. Return ONLY valid JSON. No Markdown fences and no prose outside JSON.
2. Never request or expose secrets. Never edit .env, credentials, private keys, keystores, pfx/p12, or .git internals.
3. Never use absolute paths. Every file path must be relative to WORKSPACE.
4. Never use destructive system commands, privilege changes, disk commands, shutdown/reboot, registry edits, account changes, or shell chaining.
5. Commands must start with one of: dotnet, npm, npx, node, git, python, python3, py, pytest, cargo, go, mvn, mvnw, gradle, gradlew, java.
6. Prefer replace_text for small precise changes and write_file for new files or full rewrites.
7. Preserve existing business logic unless the task explicitly changes it.
8. If practical, include a final run_command that builds/tests the project. Status may be "done" only when the proposed changes are internally complete; use "continue" if another repair cycle is expected and "blocked" when human information is required.
9. Keep actions <= 12 unless absolutely necessary.
10. Do not modify the currently running AWAD AI Developer executable. Self-development must occur in a staging/source workspace.

JSON SCHEMA:
{
  "summary": "short Arabic summary",
  "status": "continue|done|blocked",
  "actions": [
    {"type":"write_file","path":"relative/file","content":"full file content","reason":"why"},
    {"type":"replace_text","path":"relative/file","old":"exact old text","new":"replacement","reason":"why"},
    {"type":"delete_file","path":"relative/file","reason":"why"},
    {"type":"run_command","command":"dotnet build ...","reason":"why"},
    {"type":"note","content":"human note only when needed","reason":"why"}
  ],
  "next": "short Arabic next step"
}
""";
}
