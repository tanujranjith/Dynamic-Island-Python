using DynamicIsland.Windows.Models;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class AirPodsConnectionPolicyTests
{
    [Fact]
    public void OneMissingWindowsPollDoesNotDisconnectAnActiveDevice()
    {
        var misses = 0;

        Assert.True(AirPodsConnectionPolicy.ResolvePairedConnection(true, false, ref misses));
        Assert.Equal(1, misses);
        Assert.False(AirPodsConnectionPolicy.ResolvePairedConnection(true, false, ref misses));
    }

    [Fact]
    public void ExpiredBatteryBroadcastKeepsPairedDeviceConnected()
    {
        var connected = new AirPodsState
        {
            IsAvailable = true,
            IsConnected = true,
            DeviceName = "AirPods Pro",
            LeftBatteryPercent = 90,
            RightBatteryPercent = 90,
            CaseBatteryPercent = 40,
            LeftCharging = true
        };

        var expired = AirPodsConnectionPolicy.AdvertisementExpiredState(connected, true, true);

        Assert.True(expired.IsConnected);
        Assert.Equal("AirPods Pro", expired.DeviceName);
        Assert.Null(expired.LeftBatteryPercent);
        Assert.False(expired.LeftCharging);
    }

    [Fact]
    public void BannerOnlyTriggersForARealConnectionTransition()
    {
        var disconnected = AirPodsState.Disconnected(true);
        var connected = disconnected with { IsConnected = true };
        var metadataUpdate = connected with { CaseLidOpen = true, BothInCase = true, CaseCharging = true };

        Assert.True(AirPodsConnectionPolicy.IsNewConnection(disconnected, connected));
        Assert.False(AirPodsConnectionPolicy.IsNewConnection(connected, metadataUpdate));
    }
}
