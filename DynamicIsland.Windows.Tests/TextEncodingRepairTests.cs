using DynamicIsland.Windows.Infrastructure;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class TextEncodingRepairTests
{
    [Fact]
    public void RepairsTheDashCorruptionSeenInCodexOutput()
    {
        const string corrupted = "artistic â€“ MCQ:C approaches";

        Assert.Equal("artistic – MCQ:C approaches", TextEncodingRepair.RepairUtf8ReadAsWindows1252(corrupted));
    }

    [Fact]
    public void RepairsMojibakeWithoutDamagingRealUnicode()
    {
        const string corrupted = "Itâ€™s useful 😊 and café stays intact.";

        Assert.Equal("It’s useful 😊 and café stays intact.", TextEncodingRepair.RepairUtf8ReadAsWindows1252(corrupted));
    }
}
