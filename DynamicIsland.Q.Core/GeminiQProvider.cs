using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace DynamicIsland.Q.Core;

public sealed class GeminiQProvider(HttpClient? httpClient = null) : HttpQProvider(httpClient)
{
    public override QProviderInfo Info { get; } = new("gemini", "Google Gemini",
        QProviderCapabilities.Text | QProviderCapabilities.Images | QProviderCapabilities.Streaming | QProviderCapabilities.ModelDiscovery,
        "gemini-3.7-flash", "https://generativelanguage.googleapis.com");

    public override async Task<IReadOnlyList<QModelInfo>> GetModelsAsync(string? credential,
        CancellationToken cancellationToken, string? baseUrl = null)
    {
        RequireKey(credential);
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint(baseUrl, Info.DefaultBaseUrl!, "/v1beta/models?pageSize=1000"));
        request.Headers.TryAddWithoutValidation("x-goog-api-key", credential);
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, Info.DisplayName, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var models = new List<QModelInfo>();
        if (document.RootElement.TryGetProperty("models", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("supportedGenerationMethods", out var methods) && methods.ValueKind == JsonValueKind.Array &&
                    !methods.EnumerateArray().Any(method => method.GetString() is "generateContent" or "streamGenerateContent")) continue;
                var id = ProviderJson.String(item, "name");
                if (id.StartsWith("models/", StringComparison.OrdinalIgnoreCase)) id = id[7..];
                if (id.Length == 0 || id.Contains("embedding", StringComparison.OrdinalIgnoreCase) || id.Contains("image", StringComparison.OrdinalIgnoreCase) ||
                    id.Contains("tts", StringComparison.OrdinalIgnoreCase) || id.Contains("live", StringComparison.OrdinalIgnoreCase)) continue;
                var name = ProviderJson.String(item, "displayName");
                models.Add(new QModelInfo(id, name.Length == 0 ? id : name, Info.Capabilities,
                    string.Equals(id, Info.DefaultModel, StringComparison.OrdinalIgnoreCase)));
            }
        }
        if (models.Count == 0) models.Add(new QModelInfo(Info.DefaultModel, Info.DefaultModel, Info.Capabilities, true));
        return models;
    }

    public override async IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest request, string? credential, string? baseUrl,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        RequireKey(credential);
        var generationConfig = new Dictionary<string, object?> { ["maxOutputTokens"] = request.MaxResponseTokens };
        var effort = QProviderPolicy.NormalizeEffort(Info.Id, request.Model, request.ReasoningEffort);
        if (effort != "auto") generationConfig["thinkingConfig"] = new { thinkingLevel = effort };
        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = QPromptComposer.SystemPrompt(request) } } },
            contents = Contents(request),
            generationConfig
        };
        var endpoint = Endpoint(baseUrl, Info.DefaultBaseUrl!, $"/v1beta/models/{Uri.EscapeDataString(request.Model)}:streamGenerateContent?alt=sse");
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = Json(body) };
        message.Headers.TryAddWithoutValidation("x-goog-api-key", credential);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var response = await Http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, Info.DisplayName, cancellationToken).ConfigureAwait(false);
        yield return new QStreamEvent.Started();

        var sawText = false;
        await foreach (var data in SseDataAsync(response, cancellationToken).ConfigureAwait(false))
        {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            if (ProviderJson.ErrorMessage(root) is { } error)
            {
                yield return new QStreamEvent.Failed(Sanitize(error));
                yield break;
            }
            if (root.TryGetProperty("promptFeedback", out var feedback))
            {
                var blockReason = ProviderJson.String(feedback, "blockReason");
                if (blockReason.Length > 0)
                {
                    yield return new QStreamEvent.Failed($"Gemini blocked the prompt: {Sanitize(blockReason)}.");
                    yield break;
                }
            }
            if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array) continue;
            foreach (var candidate in candidates.EnumerateArray())
            {
                var finishReason = ProviderJson.String(candidate, "finishReason");
                if (finishReason.Length > 0 && finishReason is not ("STOP" or "MAX_TOKENS"))
                {
                    yield return new QStreamEvent.Failed($"Gemini stopped the response: {Sanitize(finishReason)}.");
                    yield break;
                }
                if (!candidate.TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array) continue;
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True) continue;
                    var text = ProviderJson.String(part, "text");
                    if (text.Length > 0) { sawText = true; yield return new QStreamEvent.Text(text); }
                }
                if (finishReason == "MAX_TOKENS")
                {
                    yield return new QStreamEvent.Failed("Gemini reached maxOutputTokens before finishing. Increase the response budget or lower thinking level.");
                    yield break;
                }
            }
        }
        if (!sawText)
        {
            yield return new QStreamEvent.Failed("Gemini completed without returning text.");
            yield break;
        }
        yield return new QStreamEvent.Completed();
    }

    private static object[] Contents(QRequest request)
    {
        var contents = new List<object>();
        foreach (var item in request.History.Where(item => item.Role is "user" or "assistant"))
            contents.Add(new { role = item.Role == "assistant" ? "model" : "user", parts = new[] { new { text = item.Content } } });
        var parts = new List<object> { new { text = TextPrompt(request) } };
        if (request.IncludeImage && request.ScreenContext?.PngBytes is { Length: > 0 } image)
            parts.Add(new { inlineData = new { mimeType = "image/png", data = Convert.ToBase64String(image) } });
        contents.Add(new { role = "user", parts });
        return contents.ToArray();
    }

    private static void RequireKey(string? credential)
    {
        if (string.IsNullOrWhiteSpace(credential)) throw new InvalidOperationException("Google Gemini requires an API key.");
    }
}
