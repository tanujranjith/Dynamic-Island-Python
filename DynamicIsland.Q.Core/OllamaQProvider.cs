using System.Runtime.CompilerServices;
using System.Text.Json;

namespace DynamicIsland.Q.Core;

public sealed class OllamaQProvider(HttpClient? httpClient = null) : HttpQProvider(httpClient)
{
    public override QProviderInfo Info { get; } = new("ollama", "Ollama",
        QProviderCapabilities.Text | QProviderCapabilities.Images | QProviderCapabilities.Streaming | QProviderCapabilities.ModelDiscovery,
        "llama3.2-vision", "http://localhost:11434");

    public override async Task<IReadOnlyList<QModelInfo>> GetModelsAsync(string? credential,
        CancellationToken cancellationToken, string? baseUrl = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, NormalizeRoot(baseUrl) + "/api/tags");
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, Info.DisplayName, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var models = new List<QModelInfo>();
        if (document.RootElement.TryGetProperty("models", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                var id = ProviderJson.String(item, "name");
                if (id.Length == 0) id = ProviderJson.String(item, "model");
                if (id.Length > 0) models.Add(new QModelInfo(id, id, Info.Capabilities,
                    string.Equals(id, Info.DefaultModel, StringComparison.OrdinalIgnoreCase)));
            }
        }
        if (models.Count == 0) models.Add(new QModelInfo(Info.DefaultModel, Info.DefaultModel, Info.Capabilities, true));
        return models;
    }

    public override async IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest request, string? credential, string? baseUrl,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = request.Model,
            ["messages"] = Messages(request),
            ["stream"] = true,
            ["options"] = new { num_predict = request.MaxResponseTokens }
        };
        var effort = QProviderPolicy.NormalizeEffort(Info.Id, request.Model, request.ReasoningEffort);
        if (effort == "none") body["think"] = false;
        else if (effort != "auto") body["think"] = effort;

        using var message = new HttpRequestMessage(HttpMethod.Post, NormalizeRoot(baseUrl) + "/api/chat") { Content = Json(body) };
        using var response = await Http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, Info.DisplayName, cancellationToken).ConfigureAwait(false);
        yield return new QStreamEvent.Started();
        var sawText = false;
        var sawThinking = false;
        await foreach (var line in NdjsonAsync(response, cancellationToken).ConfigureAwait(false))
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (ProviderJson.ErrorMessage(root) is { } error)
            {
                yield return new QStreamEvent.Failed(Sanitize(error));
                yield break;
            }
            if (root.TryGetProperty("message", out var responseMessage))
            {
                var thinking = ProviderJson.String(responseMessage, "thinking");
                sawThinking |= thinking.Length > 0;
                var text = ProviderJson.String(responseMessage, "content");
                if (text.Length > 0) { sawText = true; yield return new QStreamEvent.Text(text); }
            }
        }
        if (!sawText && sawThinking)
        {
            yield return new QStreamEvent.Failed("Ollama returned thinking output but no final answer. Increase num_predict or lower thinking effort.");
            yield break;
        }
        if (!sawText)
        {
            yield return new QStreamEvent.Failed("Ollama completed without returning text.");
            yield break;
        }
        yield return new QStreamEvent.Completed();
    }

    private static object[] Messages(QRequest request)
    {
        var messages = new List<object> { new { role = "system", content = QPromptComposer.SystemPrompt(request) } };
        foreach (var item in request.History.Where(item => item.Role is "user" or "assistant"))
            messages.Add(new { role = item.Role, content = item.Content });
        if (request.IncludeImage && request.ScreenContext?.PngBytes is { Length: > 0 } image)
            messages.Add(new { role = "user", content = TextPrompt(request), images = new[] { Convert.ToBase64String(image) } });
        else messages.Add(new { role = "user", content = TextPrompt(request) });
        return messages.ToArray();
    }

    private string NormalizeRoot(string? baseUrl)
    {
        var root = (string.IsNullOrWhiteSpace(baseUrl) ? Info.DefaultBaseUrl! : baseUrl!).TrimEnd('/');
        if (root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) root = root[..^3];
        if (root.EndsWith("/api", StringComparison.OrdinalIgnoreCase)) root = root[..^4];
        return root.TrimEnd('/');
    }
}
