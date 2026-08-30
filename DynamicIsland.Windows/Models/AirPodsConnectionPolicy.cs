namespace DynamicIsland.Windows.Models;

internal static class AirPodsConnectionPolicy
{
    private const int DisconnectConfirmationPolls = 2;

    public static bool ResolvePairedConnection(bool previouslyConnected, bool observedConnected,
        ref int consecutiveDisconnectPolls)
    {
        if (observedConnected)
        {
            consecutiveDisconnectPolls = 0;
            return true;
        }

        if (!previouslyConnected)
        {
            consecutiveDisconnectPolls = 0;
            return false;
        }

        consecutiveDisconnectPolls++;
        return consecutiveDisconnectPolls < DisconnectConfirmationPolls;
    }

    public static AirPodsState AdvertisementExpiredState(AirPodsState current, bool bluetoothAvailable,
        bool pairedDeviceConnected)
    {
        if (!pairedDeviceConnected) return AirPodsState.Disconnected(bluetoothAvailable);

        return current with
        {
            IsAvailable = bluetoothAvailable,
            IsConnected = true,
            LeftBatteryPercent = null,
            RightBatteryPercent = null,
            CaseBatteryPercent = null,
            LeftCharging = false,
            RightCharging = false,
            CaseCharging = false,
            LeftInEar = false,
            RightInEar = false,
            BothInCase = false,
            CaseLidOpen = false,
            Rssi = 0
        };
    }

    public static bool IsNewConnection(AirPodsState previous, AirPodsState next) =>
        !previous.IsConnected && next.IsConnected && next.IsAvailable;
}
