using DynamicIsland.Windows.Infrastructure;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class WindowSizingPolicyTests
{
    [Fact]
    public void RequestedHeightWinsWhileLayoutStillReportsOldHeight()
    {
        Assert.Equal(402d, WindowSizingPolicy.EffectiveDimension(402d, 330d));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AutoSizedWindowFallsBackToMeasuredDimension(double requested)
    {
        Assert.Equal(330d, WindowSizingPolicy.EffectiveDimension(requested, 330d));
    }

    [Fact]
    public void DynamicContentCanGrowPastConfiguredHeightWhenAutoGrowIsOff()
    {
        Assert.Equal(386d, WindowSizingPolicy.AntiClippingDimension(362d, 386d, 390d));
    }

    [Fact]
    public void DynamicContentGrowthStaysInsideHostWindow()
    {
        Assert.Equal(390d, WindowSizingPolicy.AntiClippingDimension(362d, 410d, 390d));
    }
}
