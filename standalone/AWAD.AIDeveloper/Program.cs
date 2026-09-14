using System.Diagnostics;
using AWAD.AIDeveloper.Core;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:45873");
builder.Services.AddHttpClient();
builder.Services.AddSingleton<AppState>();
builder.Services.AddSingleton<WorkspaceService>();
builder.Services.AddSingleton<MemoryStore>();
builder.Services.AddSingleton<OpenAiResponsesClient>();
builder.Services.AddSingleton<AgentService>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/status", (AppState state) => Results.Ok(new
{
    app = "AWAD AI Developer",
    version = "1.0.0",
    workspace = state.WorkspacePath,
    model = state.Model,
    hasApiKey = !string.IsNullOrWhiteSpace(state.ApiKey),
    approvalRequired = state.ApprovalRequired,
    dataRoot = state.DataRoot
}));

app.MapPost("/api/settings", (SettingsRequest req, AppState state) =>
{
    if (!string.IsNullOrWhiteSpace(req.Model)) state.Model = req.Model.Trim();
    if (!string.IsNullOrWhiteSpace(req.ApiKey)) state.ApiKey = req.ApiKey.Trim();
    if (!string.IsNullOrWhiteSpace(req.BaseUrl)) state.BaseUrl = req.BaseUrl.Trim().TrimEnd('/');
    state.ApprovalRequired = req.ApprovalRequired;
    return Results.Ok(new { saved = true, model = state.Model, approvalRequired = state.ApprovalRequired });
});

app.MapPost("/api/project/open", (OpenProjectRequest req, AppState state, WorkspaceService workspace) =>
{
    try
    {
        state.WorkspacePath = workspace.OpenProject(req.Path);
        state.Plans.Clear();
        return Results.Ok(new { workspace = state.WorkspacePath, tree = workspace.GetTree(state.WorkspacePath, 240) });
    }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapGet("/api/project/tree", (AppState state, WorkspaceService workspace) =>
{
    if (string.IsNullOrWhiteSpace(state.WorkspacePath)) return Results.BadRequest(new { error = "اختر مجلد مشروع أولاً." });
    return Results.Ok(new { tree = workspace.GetTree(state.WorkspacePath, 500) });
});

app.MapPost("/api/project/stage", (AppState state, WorkspaceService workspace) =>
{
    if (string.IsNullOrWhiteSpace(state.WorkspacePath)) return Results.BadRequest(new { error = "اختر مشروعاً أولاً." });
    try
    {
        state.WorkspacePath = workspace.CreateStagingCopy(state.WorkspacePath, state.StagingRoot);
        state.Plans.Clear();
        return Results.Ok(new { workspace = state.WorkspacePath, message = "تم إنشاء نسخة تطوير آمنة والعمل عليها بدلاً من الأصل." });
    }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPost("/api/chat", async (ChatRequest req, AppState state, AgentService agent, CancellationToken ct) =>
{
    try
    {
        var plan = await agent.ProposeAsync(req.Message, ct);
        state.Plans[plan.Id] = plan;
        return Results.Ok(plan);
    }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPost("/api/apply", async (ApplyRequest req, AppState state, AgentService agent, CancellationToken ct) =>
{
    if (!state.Plans.TryGetValue(req.PlanId, out var plan)) return Results.BadRequest(new { error = "الخطة غير موجودة أو انتهت." });
    try { return Results.Ok(await agent.ApplyAsync(plan, ct)); }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPost("/api/auto", async (AutoRequest req, AgentService agent, CancellationToken ct) =>
{
    try { return Results.Ok(await agent.AutoDevelopAsync(req.Message, Math.Clamp(req.MaxCycles, 1, 5), ct)); }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapGet("/api/memory", (AppState state, MemoryStore memory) =>
{
    if (string.IsNullOrWhiteSpace(state.WorkspacePath)) return Results.Ok(Array.Empty<MemoryEntry>());
    return Results.Ok(memory.Load(state.WorkspacePath).TakeLast(30));
});

app.MapFallbackToFile("index.html");
app.Lifetime.ApplicationStarted.Register(() =>
{
    if (Environment.GetEnvironmentVariable("AWAD_AI_NO_BROWSER") == "1") return;
    try { Process.Start(new ProcessStartInfo("http://127.0.0.1:45873") { UseShellExecute = true }); } catch { }
});
app.Run();
