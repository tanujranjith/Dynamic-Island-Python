using System.Text.Json;
using DynamicIsland.Windows.Services.Q;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class CodexModelCatalogTests
{
    [Fact]
    public void ParsesModelSpecificEffortsAndDefault()
    {
        using var document = JsonDocument.Parse("""
            {"data":[{"id":"gpt-5.6-luna","displayName":"GPT-5.6-Luna","isDefault":false,
            "supportedReasoningEfforts":[{"reasoningEffort":"low"},{"reasoningEffort":"medium"},{"reasoningEffort":"high"}],
            "defaultReasoningEffort":"medium","inputModalities":["text","image"]}]}
            """);

        var model = Assert.Single(CodexModelCatalogParser.Parse(document.RootElement));

        Assert.Equal("gpt-5.6-luna", model.Id);
        Assert.Equal(["low", "medium", "high"], model.SupportedReasoningEfforts);
        Assert.Equal("medium", model.DefaultReasoningEffort);
        Assert.True(model.SupportsImages);
    }

    [Fact]
    public void LunaRemovesMinimalAndMovesItToLowestSupportedEffort()
    {
        var luna = new CodexModel("gpt-5.6-luna", "GPT-5.6-Luna", true, false,
            ["low", "medium", "high", "xhigh", "max"], "medium");

        Assert.DoesNotContain("minimal", CodexModelSelectionPolicy.EffortOptions(luna));
        Assert.Equal("low", CodexModelSelectionPolicy.NormalizeEffort(luna, "minimal"));
    }
}
