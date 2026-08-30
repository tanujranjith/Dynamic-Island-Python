using System.Text.Json;
using DynamicIsland.Windows.Services.Q;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class CodexTurnNotificationTests
{
    [Fact]
    public void UsesCompletedAgentMessageWhenNoDeltaWasEmitted()
    {
        var accumulator = new CodexTurnNotificationAccumulator("thread-1");

        var update = accumulator.Process(Json("""
            {"method":"item/completed","params":{"threadId":"thread-1","item":{"id":"item-1","type":"agentMessage","text":"Final answer"}}}
            """));

        Assert.Equal(CodexTurnUpdateKind.Text, update?.Kind);
        Assert.Equal("Final answer", update?.Value);
    }

    [Fact]
    public void AuthoritativeCompletedItemDoesNotDuplicateStreamedText()
    {
        var accumulator = new CodexTurnNotificationAccumulator("thread-1");
        var delta = accumulator.Process(Json("""
            {"method":"item/agentMessage/delta","params":{"threadId":"thread-1","itemId":"item-1","delta":"Final answer"}}
            """));
        var completedItem = accumulator.Process(Json("""
            {"method":"item/completed","params":{"threadId":"thread-1","item":{"id":"item-1","type":"agentMessage","text":"Final answer"}}}
            """));

        Assert.Equal("Final answer", delta?.Value);
        Assert.Null(completedItem);
    }

    [Fact]
    public void ExposesFailedTurnMessage()
    {
        var accumulator = new CodexTurnNotificationAccumulator("thread-1");

        var update = accumulator.Process(Json("""
            {"method":"turn/completed","params":{"threadId":"thread-1","turn":{"status":"failed","error":{"message":"Unsupported reasoning effort 'minimal'"}}}}
            """));

        Assert.Equal(CodexTurnUpdateKind.Failed, update?.Kind);
        Assert.Equal("Unsupported reasoning effort 'minimal'", update?.Value);
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
