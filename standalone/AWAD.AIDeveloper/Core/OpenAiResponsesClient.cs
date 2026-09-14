using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace AWAD.AIDeveloper.Core;

public sealed class OpenAiResponsesClient
{
    private readonly IHttpClientFactory _factory;
    private readonly AppState _state;
    public OpenAiResponsesClient(IHttpClientFactory factory, AppState state) { _factory = factory; _state = state; }

    public async Task<string> GenerateAsync(string instructions, string input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_state.ApiKey))
            throw new InvalidOperationException("أدخل مفتاح API من الإعدادات أو عرّف OPENAI_API_KEY قبل التشغيل.");

        using var client = _factory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _state.ApiKey);
        var payload = new { model = _state.Model, instructions, input, store = false, max_output_tokens = 12000 };
        using var response = await client.PostAsJsonAsync($"{_state.BaseUrl}/responses", payload, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"فشل اتصال الذكاء الاصطناعي ({(int)response.StatusCode}): {Trim(json, 1600)}");

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("output_text", out var top) && top.ValueKind == JsonValueKind.String)
            return top.GetString() ?? string.Empty;

        var sb = new StringBuilder();
        if (doc.RootElement.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
                foreach (var part in content.EnumerateArray())
                    if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String) sb.AppendLine(text.GetString());
            }
        }
        if (sb.Length == 0) throw new InvalidOperationException("لم يُرجع النموذج نصاً قابلاً للقراءة.");
        return sb.ToString().Trim();
    }

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..max];
}
