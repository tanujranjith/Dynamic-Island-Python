using System.Net.Http.Headers;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace DynamicIsland.Q.Core;

public sealed class QProviderRegistry(IEnumerable<IQProvider> providers) : IQProviderRegistry
{
    public IReadOnlyList<IQProvider> Providers { get; } = providers.ToArray();
    public IQProvider? Find(string id) => Providers.FirstOrDefault(p => string.Equals(p.Info.Id, id, StringComparison.OrdinalIgnoreCase));
}

internal static class ProviderJson
{
    public static string JsonString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    public static IEnumerable<string> TextValues(JsonElement root)
    {
        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.TryGetProperty("delta", out var delta))
                {
                    var text = JsonString(delta, "content");
                    if (text.Length > 0) yield return text;
                }
                else if (choice.TryGetProperty("message", out var message))
                {
                    var text = JsonString(message, "content");
                    if (text.Length > 0) yield return text;
                }
            }
        if (root.TryGetProperty("message", out var direct))
        {
            var text = JsonString(direct, "content");
            if (text.Length > 0) yield return text;
        }
    }

    public static bool HasReasoning(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array) return false;
        foreach (var choice in choices.EnumerateArray())
        {
            if (!choice.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object) continue;
            if (JsonString(delta, "reasoning").Length > 0 || JsonString(delta, "reasoning_content").Length > 0) return true;
            if (delta.TryGetProperty("reasoning_details", out var details) && details.ValueKind == JsonValueKind.Array && details.GetArrayLength() > 0) return true;
        }
        return false;
    }
}

public abstract class HttpQProvider(HttpClient? httpClient = null) : IQProvider
{
    protected readonly HttpClient Http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
    public abstract QProviderInfo Info { get; }
    public abstract IAsyncEnumerable<QStreamEvent> StreamAsync(
        QRequest request,
        string? credential,
        string? baseUrl,
        CancellationToken cancellationToken);

    public virtual async Task<IReadOnlyList<QModelInfo>> GetModelsAsync(string? credential, CancellationToken cancellationToken)
    {
        if (!Info.Capabilities.HasFlag(QProviderCapabilities.ModelDiscovery))
            return [new QModelInfo(Info.DefaultModel, Info.DefaultModel, Info.Capabilities, true)];
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint(Info.DefaultBaseUrl, Info.DefaultBaseUrl ?? "", "/models"));
        AddBearer(request, credential);
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var models = new List<QModelInfo>();
        if (json.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            foreach (var item in data.EnumerateArray())
            {
                var id = ProviderJson.JsonString(item, "id");
                if (id.Length > 0) models.Add(new QModelInfo(id, id, Info.Capabilities));
            }
        if (models.Count == 0) models.Add(new QModelInfo(Info.DefaultModel, Info.DefaultModel, Info.Capabilities, true));
        return models;
    }

    protected static string Endpoint(string? baseUrl, string fallback, string suffix)
    {
        var root = string.IsNullOrWhiteSpace(baseUrl) ? fallback : baseUrl!.TrimEnd('/');
        return root.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? root : root + suffix;
    }

    protected static void AddBearer(HttpRequestMessage request, string? credential)
    {
        if (!string.IsNullOrWhiteSpace(credential)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
    }

    protected static StringContent Json(object value) => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    protected static string ContextPrompt(QRequest request)
    {
        var context = QPromptComposer.ContextText(request.ScreenContext);
        var imageNote = request.IncludeImage ? " The request also contains an image of the active window." : "";
        return $"{context}{imageNote}";
    }

    protected static string TextPrompt(QRequest request) =>
        $"{ContextPrompt(request)}\n\nUser request:\n{request.Prompt}";

    protected static async IAsyncEnumerable<string> SseDataAsync(HttpResponseMessage response,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var data = line[5..].Trim();
            if (data.Length > 0 && data != "[DONE]") yield return data;
        }
    }

    protected static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var detail = body.Length > 400 ? body[..400] : body;
        throw new HttpRequestException($"Provider returned {(int)response.StatusCode} ({response.ReasonPhrase}): {detail}");
    }

    protected static object[] OpenAiMessages(QRequest request)
    {
        var messages = new List<object> { new { role = "system", content = QPromptComposer.SystemPrompt(request) } };
        foreach (var item in request.History) messages.Add(new { role = item.Role, content = item.Content });
        if (request.ScreenContext is { } context && request.IncludeImage && context.PngBytes is { Length: > 0 })
        {
            var content = new object[]
            {
                new { type = "text", text = TextPrompt(request) },
                new { type = "image_url", image_url = new { url = $"data:image/png;base64,{Convert.ToBase64String(context.PngBytes)}" } }
            };
            messages.Add(new { role = "user", content });
        }
        else messages.Add(new { role = "user", content = TextPrompt(request) });
        return messages.ToArray();
    }

    protected async IAsyncEnumerable<QStreamEvent> StreamOpenAiCompatibleAsync(QRequest request, string? credential,
        string? baseUrl, string fallback, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken,
        IReadOnlyDictionary<string, object?>? additionalBody = null)
    {
        var endpoint = Endpoint(baseUrl, fallback, "/chat/completions");
        var body = new Dictionary<string, object?>
        {
            ["model"] = request.Model,
            ["messages"] = OpenAiMessages(request),
            ["stream"] = true,
            ["temperature"] = 0.2,
            ["max_tokens"] = request.MaxResponseTokens
        };
        if (additionalBody is not null)
            foreach (var item in additionalBody) body[item.Key] = item.Value;
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = Json(body) };
        AddBearer(message, credential);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var response = await Http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        yield return new QStreamEvent.Started();
        var sawText = false;
        var sawReasoning = false;
        await foreach (var data in SseDataAsync(response, cancellationToken).ConfigureAwait(false))
        {
            using var json = JsonDocument.Parse(data);
            sawReasoning |= ProviderJson.HasReasoning(json.RootElement);
            foreach (var text in ProviderJson.TextValues(json.RootElement))
            {
                sawText = true;
                yield return new QStreamEvent.Text(text);
            }
        }
        if (!sawText && sawReasoning)
        {
            yield return new QStreamEvent.Failed("The model used its response budget for reasoning but did not return a final answer. Retry with a larger token budget or lower reasoning effort.");
            yield break;
        }
        yield return new QStreamEvent.Completed();
    }
}

public sealed class OpenAiQProvider(HttpClient? httpClient = null) : HttpQProvider(httpClient)
{
    public override QProviderInfo Info { get; } = new("openai", "OpenAI", QProviderCapabilities.Text | QProviderCapabilities.Images | QProviderCapabilities.Streaming | QProviderCapabilities.ModelDiscovery, "gpt-4o-mini", "https://api.openai.com/v1");
    public override IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest request, string? credential, string? baseUrl, CancellationToken cancellationToken) =>
        StreamOpenAiCompatibleAsync(request, credential, baseUrl, Info.DefaultBaseUrl!, cancellationToken);
}

public sealed class GroqQProvider(HttpClient? httpClient = null) : HttpQProvider(httpClient)
{
    public override QProviderInfo Info { get; } = new("groq", "Groq", QProviderCapabilities.Text | QProviderCapabilities.Images | QProviderCapabilities.Streaming | QProviderCapabilities.ModelDiscovery, "llama-3.2-11b-vision-preview", "https://api.groq.com/openai/v1");
    public override IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest request, string? credential, string? baseUrl, CancellationToken cancellationToken) =>
        StreamOpenAiCompatibleAsync(request, credential, baseUrl, Info.DefaultBaseUrl!, cancellationToken);
}

public sealed class XaiQProvider(HttpClient? httpClient = null) : HttpQProvider(httpClient)
{
    public override QProviderInfo Info { get; } = new("xai", "xAI / Grok", QProviderCapabilities.Text | QProviderCapabilities.Images | QProviderCapabilities.Streaming | QProviderCapabilities.ModelDiscovery, "grok-2-vision-1212", "https://api.x.ai/v1");
    public override IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest request, string? credential, string? baseUrl, CancellationToken cancellationToken) =>
        StreamOpenAiCompatibleAsync(request, credential, baseUrl, Info.DefaultBaseUrl!, cancellationToken);
}

public sealed class OpenRouterQProvider(HttpClient? httpClient = null) : HttpQProvider(httpClient)
{
    private sealed record ReasoningProfile(bool Mandatory, bool DefaultEnabled, string? DefaultEffort,
        string[] SupportedEfforts, bool SupportsMaxTokens);

    private readonly ConcurrentDictionary<string, ReasoningProfile> _reasoningProfiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _modelLoadGate = new(1, 1);
    private bool _profilesLoaded;

    public override QProviderInfo Info { get; } = new("openrouter", "OpenRouter", QProviderCapabilities.Text | QProviderCapabilities.Images | QProviderCapabilities.Streaming | QProviderCapabilities.ModelDiscovery, "openai/gpt-4o-mini", "https://openrouter.ai/api/v1");

    public override async Task<IReadOnlyList<QModelInfo>> GetModelsAsync(string? credential, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint(Info.DefaultBaseUrl, Info.DefaultBaseUrl!, "/models"));
        AddBearer(request, credential);
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var models = new List<QModelInfo>();
        if (json.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            foreach (var item in data.EnumerateArray())
            {
                var id = ProviderJson.JsonString(item, "id");
                if (id.Length == 0) continue;
                var name = ProviderJson.JsonString(item, "name");
                var capabilities = QProviderCapabilities.Text | QProviderCapabilities.Streaming | QProviderCapabilities.ModelDiscovery;
                if (item.TryGetProperty("architecture", out var architecture) &&
                    architecture.TryGetProperty("input_modalities", out var modalities) &&
                    modalities.ValueKind == JsonValueKind.Array && modalities.EnumerateArray().Any(v => string.Equals(v.GetString(), "image", StringComparison.OrdinalIgnoreCase)))
                    capabilities |= QProviderCapabilities.Images;
                models.Add(new QModelInfo(id, name.Length == 0 ? id : name, capabilities, string.Equals(id, Info.DefaultModel, StringComparison.OrdinalIgnoreCase)));
                if (TryReadReasoningProfile(item, out var profile)) _reasoningProfiles[id] = profile;
            }
        _profilesLoaded = true;
        if (models.Count == 0) models.Add(new QModelInfo(Info.DefaultModel, Info.DefaultModel, Info.Capabilities, true));
        return models;
    }

    public override async IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest request, string? credential, string? baseUrl,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var profile = await GetReasoningProfileAsync(request.Model, credential, cancellationToken).ConfigureAwait(false);
        var extra = BuildReasoningBody(profile, request.MaxResponseTokens);
        await foreach (var item in StreamOpenAiCompatibleAsync(request, credential, baseUrl, Info.DefaultBaseUrl!, cancellationToken, extra).ConfigureAwait(false))
            yield return item;
    }

    private async Task<ReasoningProfile?> GetReasoningProfileAsync(string model, string? credential, CancellationToken cancellationToken)
    {
        if (_reasoningProfiles.TryGetValue(model, out var profile)) return profile;
        if (!_profilesLoaded)
        {
            await _modelLoadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_profilesLoaded) await GetModelsAsync(credential, cancellationToken).ConfigureAwait(false);
            }
            finally { _modelLoadGate.Release(); }
        }
        return _reasoningProfiles.TryGetValue(model, out profile) ? profile : null;
    }

    private static bool TryReadReasoningProfile(JsonElement model, out ReasoningProfile profile)
    {
        profile = default!;
        if (!model.TryGetProperty("reasoning", out var reasoning) || reasoning.ValueKind != JsonValueKind.Object) return false;
        var mandatory = reasoning.TryGetProperty("mandatory", out var mandatoryValue) && mandatoryValue.ValueKind == JsonValueKind.True;
        var defaultEnabled = reasoning.TryGetProperty("default_enabled", out var enabledValue) && enabledValue.ValueKind == JsonValueKind.True;
        var defaultEffort = ProviderJson.JsonString(reasoning, "default_effort");
        var supportsMaxTokens = reasoning.TryGetProperty("supports_max_tokens", out var maxValue) && maxValue.ValueKind == JsonValueKind.True;
        var efforts = reasoning.TryGetProperty("supported_efforts", out var effortsValue) && effortsValue.ValueKind == JsonValueKind.Array
            ? effortsValue.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToArray()
            : [];
        profile = new ReasoningProfile(mandatory, defaultEnabled, defaultEffort.Length == 0 ? null : defaultEffort, efforts, supportsMaxTokens);
        return true;
    }

    private static IReadOnlyDictionary<string, object?>? BuildReasoningBody(ReasoningProfile? profile, int maxResponseTokens)
    {
        if (profile is null || (!profile.Mandatory && !profile.DefaultEnabled)) return null;
        var reasoning = new Dictionary<string, object?> { ["exclude"] = true };
        if (profile.SupportsMaxTokens)
        {
            reasoning["max_tokens"] = Math.Clamp(maxResponseTokens / 4, 1024, Math.Max(1024, maxResponseTokens - 1024));
        }
        else
        {
            var preference = new[] { "minimal", "low", "medium", "high", "xhigh", "max" };
            var effort = preference.FirstOrDefault(candidate => profile.SupportedEfforts.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                ?? profile.DefaultEffort ?? "low";
            reasoning["effort"] = effort;
        }
        return new Dictionary<string, object?> { ["reasoning"] = reasoning };
    }
}

public sealed class DeepSeekQProvider(HttpClient? httpClient = null) : HttpQProvider(httpClient)
{
    public override QProviderInfo Info { get; } = new("deepseek", "DeepSeek", QProviderCapabilities.Text | QProviderCapabilities.Streaming | QProviderCapabilities.ModelDiscovery, "deepseek-chat", "https://api.deepseek.com/v1");
    public override IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest request, string? credential, string? baseUrl, CancellationToken cancellationToken) =>
        StreamOpenAiCompatibleAsync(request with { IncludeImage = false }, credential, baseUrl, Info.DefaultBaseUrl!, cancellationToken);
}

public sealed class OllamaQProvider(HttpClient? httpClient = null) : HttpQProvider(httpClient)
{
    public override QProviderInfo Info { get; } = new("ollama", "Ollama", QProviderCapabilities.Text | QProviderCapabilities.Images | QProviderCapabilities.Streaming | QProviderCapabilities.ModelDiscovery, "llama3.2-vision", "http://localhost:11434/v1");
    public override IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest request, string? credential, string? baseUrl, CancellationToken cancellationToken) =>
        StreamOpenAiCompatibleAsync(request, credential, baseUrl, Info.DefaultBaseUrl!, cancellationToken);
}

public sealed class AnthropicQProvider(HttpClient? httpClient = null) : HttpQProvider(httpClient)
{
    public override QProviderInfo Info { get; } = new("anthropic", "Anthropic", QProviderCapabilities.Text | QProviderCapabilities.Images | QProviderCapabilities.Streaming, "claude-3-5-haiku-latest", "https://api.anthropic.com");

    public override async IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest request, string? credential, string? baseUrl,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var content = new List<object>();
        if (request.IncludeImage && request.ScreenContext?.PngBytes is { Length: > 0 } image)
            content.Add(new { type = "image", source = new { type = "base64", media_type = "image/png", data = Convert.ToBase64String(image) } });
        content.Add(new { type = "text", text = TextPrompt(request) });
        var body = new { model = request.Model, max_tokens = request.MaxResponseTokens, system = QPromptComposer.SystemPrompt(request), messages = new[] { new { role = "user", content } }, stream = true };
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint(baseUrl, Info.DefaultBaseUrl!, "/v1/messages")) { Content = Json(body) };
        if (!string.IsNullOrWhiteSpace(credential)) message.Headers.TryAddWithoutValidation("x-api-key", credential);
        message.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var response = await Http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        yield return new QStreamEvent.Started();
        await foreach (var data in SseDataAsync(response, cancellationToken).ConfigureAwait(false))
        {
            using var json = JsonDocument.Parse(data);
            if (json.RootElement.TryGetProperty("delta", out var delta))
            {
                var text = ProviderJson.JsonString(delta, "text");
                if (text.Length > 0) yield return new QStreamEvent.Text(text);
            }
        }
        yield return new QStreamEvent.Completed();
    }
}

public sealed class GeminiQProvider(HttpClient? httpClient = null) : HttpQProvider(httpClient)
{
    public override QProviderInfo Info { get; } = new("gemini", "Google Gemini", QProviderCapabilities.Text | QProviderCapabilities.Images | QProviderCapabilities.Streaming, "gemini-2.0-flash", "https://generativelanguage.googleapis.com");

    public override async IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest request, string? credential, string? baseUrl,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credential)) throw new InvalidOperationException("Gemini requires an API key.");
        var parts = new List<object> { new { text = TextPrompt(request) } };
        if (request.IncludeImage && request.ScreenContext?.PngBytes is { Length: > 0 } image)
            parts.Add(new { inline_data = new { mime_type = "image/png", data = Convert.ToBase64String(image) } });
        var body = new { system_instruction = new { parts = new[] { new { text = QPromptComposer.SystemPrompt(request) } } }, generationConfig = new { maxOutputTokens = request.MaxResponseTokens, temperature = 0.2 }, contents = new[] { new { role = "user", parts } } };
        var endpoint = Endpoint(baseUrl, Info.DefaultBaseUrl!, $"/v1beta/models/{Uri.EscapeDataString(request.Model)}:streamGenerateContent?alt=sse&key={Uri.EscapeDataString(credential)}");
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = Json(body) };
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var response = await Http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        yield return new QStreamEvent.Started();
        await foreach (var data in SseDataAsync(response, cancellationToken).ConfigureAwait(false))
        {
            using var json = JsonDocument.Parse(data);
            if (!json.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0) continue;
            var partsElement = candidates[0].GetProperty("content").GetProperty("parts");
            foreach (var part in partsElement.EnumerateArray())
            {
                var text = ProviderJson.JsonString(part, "text");
                if (text.Length > 0) yield return new QStreamEvent.Text(text);
            }
        }
        yield return new QStreamEvent.Completed();
    }
}
