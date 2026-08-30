using System.Text.Json;
using DynamicIsland.Windows.Services.Q;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class CodexTurnStartRequestTests
{
    [Fact]
    public void LunaLowIsSentExactlyToCodexAppServer()
    {
        var parameters = CodexTurnStartRequest.Create("thread-1",
            [new { type = "text", text = "hello" }], "gpt-5.6-luna", "low");

        var json = JsonSerializer.Serialize(parameters);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("gpt-5.6-luna", document.RootElement.GetProperty("model").GetString());
        Assert.Equal("low", document.RootElement.GetProperty("effort").GetString());
    }

    [Fact]
    public void AutoEffortUsesCodexDefault()
    {
        var parameters = CodexTurnStartRequest.Create("thread-1",
            [new { type = "text", text = "hello" }], "gpt-5.6-luna", "auto");

        Assert.False(parameters.ContainsKey("effort"));
    }
}
