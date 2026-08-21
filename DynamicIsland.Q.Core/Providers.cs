using System.Net.Http.Headers;
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
}

public abstract class HttpQProvider(HttpClient? httpClient = null) : IQProvider
{
    protected readonly HttpClient Http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
    public abstract QProviderInfo Info { get; }

    public virtual Task<IReadOnlyList<QModelInfo>> GetModelsAsync(string? credential, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<QModelInfo>>([new QModelInfo(Info.DefaultModel, Info.DefaultModel, Info.Capabilities, true)]);

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
        var messages = new List<object> { new { role = "system", content = QPromptComposer.SystemPrompt(request.Mode) } };
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
        string? baseUrl, string fallback, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var endpoint = Endpoint(baseUrl, fallback, "/chat/completions");
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = Json(new { model = request.Model, messages = OpenAiMessages(request), stream = true, temperature = 0.2, max_tokens = request.MaxResponseTokens } ) };
        AddBearer(message, credential);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var response = await Http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        yield return new QStreamEvent.Started();
        await foreach (var data in SseDataAsync(response, cancellationToken).ConfigureAwait(false))
        {
            using var json = JsonDocument.Parse(data);
            foreach (var text in ProviderJson.TextValues(json.RootElement)) yield return new QStreamEvent.Text(text);
        }
        yield return new QStreamEvent.Completed();
    }
}

public sealed class OpenAiQProvider(HttpClient? httpClient = null) : HttpQProvider(httpClient)
{
    public override QProviderInfo Info { get; } = new("openai", "OpenAI", QProviderCapabilities.Text | QProviderCapabilities.Images | QProviderCapabilities.Streaming, "gpt-4o-mini", "https://api.openai.com/v1");
    public override IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest request, string? credential, string? baseUrl, CancellationToken cancellationToken) =>
        StreamOpenAiCompatibleAsync(request, credential, baseUrl, Info.DefaultBaseUrl!, cancellationToken);
}

public sealed class GroqQProvider(HttpClient? httpClient = null) : HttpQProvider(httpClient)
{
    public override QProviderInfo Info { get; } = new("groq", "Groq", QProviderCapabilities.Text | QProviderCapabilities.Images | QProviderCapabilities.Streaming, "llama-3.2-11b-vision-preview", "https://api.groq.com/openai/v1");
    public override IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest request, string? credential, string? baseUrl, CancellationToken cancellationToken) =>
        StreamOpenAiCompatibleAsync(request, credential, baseUrl, Info.DefaultBaseUrl!, cancellationToken);
}

public sealed class XaiQProvider(HttpClient? httpClient = null) : HttpQProvider(httpClient)
{
    public override QProviderInfo Info { get; } = new("xai", "xAI / Grok", QProviderCapabilities.Text | QProviderCapabilities.Images | QProviderCapabilities.Streaming, "grok-2-vision-1212", "https://api.x.ai/v1");
    public override IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest request, string? credential, string? baseUrl, CancellationToken cancellationToken) =>
        StreamOpenAiCompatibleAsync(request, credential, baseUrl, Info.DefaultBaseUrl!, cancellationToken);
}

public sealed class OpenRouterQProvider(HttpClient? httpClient = null) : HttpQProvider(httpClient)
{
    public override QProviderInfo Info { get; } = new("openrouter", "OpenRouter", QProviderCapabilities.Text | QProviderCapabilities.Images | QProviderCapabilities.Streaming, "openai/gpt-4o-mini", "https://openrouter.ai/api/v1");
    public override IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest request, string? credential, string? baseUrl, CancellationToken cancellationToken) =>
        StreamOpenAiCompatibleAsync(request, credential, baseUrl, Info.DefaultBaseUrl!, cancellationToken);
}

public sealed class DeepSeekQProvider(HttpClient? httpClient = null) : HttpQProvider(httpClient)
{
    public override QProviderInfo Info { get; } = new("deepseek", "DeepSeek", QProviderCapabilities.Text | QProviderCapabilities.Streaming, "deepseek-chat", "https://api.deepseek.com/v1");
    public override IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest request, string? credential, string? baseUrl, CancellationToken cancellationToken) =>
        StreamOpenAiCompatibleAsync(request with { IncludeImage = false }, credential, baseUrl, Info.DefaultBaseUrl!, cancellationToken);
}

public sealed class OllamaQProvider(HttpClient? httpClient = null) : HttpQProvider(httpClient)
{
    public override QProviderInfo Info { get; } = new("ollama", "Ollama", QProviderCapabilities.Text | QProviderCapabilities.Images | QProviderCapabilities.Streaming, "llama3.2-vision", "http://localhost:11434/v1");
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
        var body = new { model = request.Model, max_tokens = request.MaxResponseTokens, system = QPromptComposer.SystemPrompt(request.Mode), messages = new[] { new { role = "user", content } }, stream = true };
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
        var body = new { system_instruction = new { parts = new[] { new { text = QPromptComposer.SystemPrompt(request.Mode) } } }, generationConfig = new { maxOutputTokens = request.MaxResponseTokens, temperature = 0.2 }, contents = new[] { new { role = "user", parts } } };
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
