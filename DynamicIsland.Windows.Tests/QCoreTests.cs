using DynamicIsland.Q.Core;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class QCoreTests
{
    [Fact]
    public void AskPromptUsesVisibleContext()
    {
        var context = new QScreenContext("Browser", "chrome", 100, 100, "Why do you want this job?", null, DateTimeOffset.Now);
        var request = new QRequest(QMode.Ask, "What should I answer?", context, [], "test", false);

        var messages = QPromptComposer.BuildMessages(request);

        Assert.Contains("active window", messages[1].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Why do you want this job?", messages[1].Content);
        Assert.Contains("What should I answer?", messages[^1].Content);
    }

    [Fact]
    public void SayPromptUsesFirstPersonGuidance()
    {
        Assert.Contains("first-person", QPromptComposer.SystemPrompt(QMode.Say), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SessionStreamsAndKeepsShortHistory()
    {
        var provider = new FakeProvider();
        using var session = new QSessionController(new QProviderRegistry([provider]));
        await session.BeginAsync(QMode.Ask, "fake", "demo", null);
        await session.SubmitAsync("hello", QMode.Ask, "fake", "demo", "key", null, false);
        await session.SubmitAsync("follow up", QMode.Ask, "fake", "demo", "key", null, false);

        Assert.Equal(QRunState.Complete, session.Snapshot.State);
        Assert.Equal("hello world", session.Snapshot.Response);
        Assert.Equal(2, provider.LastRequest!.History.Count);
    }

    [Fact]
    public async Task SessionReportsProviderErrorsWithoutThrowing()
    {
        var provider = new FakeProvider { ShouldFail = true };
        using var session = new QSessionController(new QProviderRegistry([provider]));
        await session.SubmitAsync("hello", QMode.Ask, "fake", "demo", "key", null, false);

        Assert.Equal(QRunState.Error, session.Snapshot.State);
        Assert.Contains("fake provider failure", session.Snapshot.Error);
    }

    private sealed class FakeProvider : IQProvider
    {
        public bool ShouldFail { get; set; }
        public QRequest? LastRequest { get; private set; }
        public QProviderInfo Info { get; } = new("fake", "Fake", QProviderCapabilities.Text | QProviderCapabilities.Streaming, "demo");
        public Task<IReadOnlyList<QModelInfo>> GetModelsAsync(string? credential, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QModelInfo>>([new QModelInfo("demo", "Demo", Info.Capabilities, true)]);

        public async IAsyncEnumerable<QStreamEvent> StreamAsync(QRequest request, string? credential, string? baseUrl,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (ShouldFail) throw new InvalidOperationException("fake provider failure");
            yield return new QStreamEvent.Started();
            yield return new QStreamEvent.Text("hello ");
            await Task.Yield();
            yield return new QStreamEvent.Text("world");
            yield return new QStreamEvent.Completed();
        }
    }
}
