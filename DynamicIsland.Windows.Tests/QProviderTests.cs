using System.Net;
using System.Text;
using System.Text.Json;
using DynamicIsland.Q.Core;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class QProviderTests
{
    [Fact]
    public async Task OpenAiUsesCompletionTokenParameterAndConfiguredReasoningEffort()
    {
        var handler = new OpenAiHandler();
        var provider = new OpenAiQProvider(new HttpClient(handler));
        var request = new QRequest(QMode.Ask, "answer this", null, [], "gpt-5-mini", false, 8192, null, "high");
        var text = new StringBuilder();

        await foreach (var item in provider.StreamAsync(request, "key", null, CancellationToken.None))
            if (item is QStreamEvent.Text chunk) text.Append(chunk.Value);

        Assert.Equal("answer", text.ToString());
        Assert.NotNull(handler.ChatRequestBody);
        using var json = JsonDocument.Parse(handler.ChatRequestBody!);
        Assert.Equal(8192, json.RootElement.GetProperty("max_completion_tokens").GetInt32());
        Assert.False(json.RootElement.TryGetProperty("max_tokens", out _));
        Assert.Equal("high", json.RootElement.GetProperty("reasoning_effort").GetString());
        Assert.False(json.RootElement.TryGetProperty("temperature", out _));
    }

    [Fact]
    public async Task OpenRouterUsesLowestSupportedReasoningEffortAndKeepsAnswerBudget()
    {
        var handler = new OpenRouterHandler(answerWithText: true);
        var provider = new OpenRouterQProvider(new HttpClient(handler));
        var request = new QRequest(QMode.Ask, "answer this", null, [], "stealth/ox-alpha", false, 8192);
        var text = new StringBuilder();

        await foreach (var item in provider.StreamAsync(request, "key", null, CancellationToken.None))
            if (item is QStreamEvent.Text chunk) text.Append(chunk.Value);

        Assert.Equal("answer", text.ToString());
        Assert.NotNull(handler.ChatRequestBody);
        using var json = JsonDocument.Parse(handler.ChatRequestBody!);
        Assert.Equal(8192, json.RootElement.GetProperty("max_tokens").GetInt32());
        var reasoning = json.RootElement.GetProperty("reasoning");
        Assert.Equal("low", reasoning.GetProperty("effort").GetString());
        Assert.True(reasoning.GetProperty("exclude").GetBoolean());
    }

    [Fact]
    public async Task OpenRouterReportsReasoningOnlyStreamsInsteadOfCompletingEmpty()
    {
        var handler = new OpenRouterHandler(answerWithText: false);
        var provider = new OpenRouterQProvider(new HttpClient(handler));
        var request = new QRequest(QMode.Ask, "answer this", null, [], "stealth/ox-alpha", false, 8192);
        QStreamEvent.Failed? failure = null;

        await foreach (var item in provider.StreamAsync(request, "key", null, CancellationToken.None))
            failure ??= item as QStreamEvent.Failed;

        Assert.NotNull(failure);
        Assert.Contains("reasoning", failure!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("final answer", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class OpenRouterHandler(bool answerWithText) : HttpMessageHandler
    {
        public string? ChatRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath.EndsWith("/models", StringComparison.OrdinalIgnoreCase) == true)
            {
                const string models = """
                    {"data":[{"id":"stealth/ox-alpha","name":"Ox Alpha","architecture":{"input_modalities":["text","image"]},"reasoning":{"mandatory":true,"default_enabled":true,"default_effort":"max","supported_efforts":["max","high","low"]}}]}
                    """;
                return JsonResponse(models);
            }

            ChatRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var stream = answerWithText
                ? "data: {\"choices\":[{\"delta\":{\"content\":\"answer\"}}]}\n\ndata: [DONE]\n\n"
                : "data: {\"choices\":[{\"delta\":{\"reasoning\":\"thinking\"},\"finish_reason\":\"length\"}]}\n\ndata: [DONE]\n\n";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(stream, Encoding.UTF8, "text/event-stream")
            };
        }

        private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class OpenAiHandler : HttpMessageHandler
    {
        public string? ChatRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ChatRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            const string stream = "data: {\"choices\":[{\"delta\":{\"content\":\"answer\"}}]}\n\ndata: [DONE]\n\n";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(stream, Encoding.UTF8, "text/event-stream")
            };
        }
    }
}
