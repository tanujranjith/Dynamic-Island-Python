namespace DynamicIsland.Windows.Models;

public enum AirPodsModel
{
    Unknown = 0,
    AirPods1,
    AirPods2,
    AirPods3,
    AirPods4,
    AirPods4Anc,
    AirPodsPro,
    AirPodsPro2,
    AirPodsPro2UsbC,
    AirPodsPro3,
    AirPodsMax,
    BeatsFitPro
}

public enum AirPodsSide
{
    Left = 0,
    Right = 1
}

public sealed record AirPodsState
{
    public bool IsAvailable { get; init; }
    public bool IsConnected { get; init; }
    public AirPodsModel Model { get; init; } = AirPodsModel.Unknown;
    public string ModelName { get; init; } = "AirPods";
    public string? DeviceName { get; init; }
    public string DisplayName => !string.IsNullOrWhiteSpace(DeviceName) ? DeviceName! : ModelName;

    public int? LeftBatteryPercent { get; init; }
    public int? RightBatteryPercent { get; init; }
    public int? CaseBatteryPercent { get; init; }

    public bool LeftBatteryAvailable => LeftBatteryPercent.HasValue;
    public bool RightBatteryAvailable => RightBatteryPercent.HasValue;
    public bool CaseBatteryAvailable => CaseBatteryPercent.HasValue;

    public bool LeftCharging { get; init; }
    public bool RightCharging { get; init; }
    public bool CaseCharging { get; init; }

    public bool LeftInEar { get; init; }
    public bool RightInEar { get; init; }
    public bool BothInEar => LeftInEar && RightInEar;
    public bool BothInCase { get; init; }
    public bool CaseLidOpen { get; init; }

    public int Rssi { get; init; }
    public DateTimeOffset LastUpdated { get; init; }

    public bool IsStale => !IsConnected || (DateTimeOffset.Now - LastUpdated).TotalSeconds > 12;

    // RSSI and freshness are transport metadata. They change frequently without changing
    // anything the user sees, so they must not turn every BLE packet into a UI update.
    public bool HasPresentationChangedFrom(AirPodsState other) =>
        IsAvailable != other.IsAvailable ||
        IsConnected != other.IsConnected ||
        Model != other.Model ||
        !string.Equals(ModelName, other.ModelName, StringComparison.Ordinal) ||
        !string.Equals(DeviceName, other.DeviceName, StringComparison.Ordinal) ||
        LeftBatteryPercent != other.LeftBatteryPercent ||
        RightBatteryPercent != other.RightBatteryPercent ||
        CaseBatteryPercent != other.CaseBatteryPercent ||
        LeftCharging != other.LeftCharging ||
        RightCharging != other.RightCharging ||
        CaseCharging != other.CaseCharging ||
        LeftInEar != other.LeftInEar ||
        RightInEar != other.RightInEar ||
        BothInCase != other.BothInCase ||
        CaseLidOpen != other.CaseLidOpen;

    public static AirPodsState Unavailable { get; } = new()
    {
        IsAvailable = false,
        IsConnected = false,
        Model = AirPodsModel.Unknown,
        ModelName = "AirPods",
        LastUpdated = DateTimeOffset.MinValue
    };

    public static AirPodsState Disconnected(bool bluetoothAvailable) => new()
    {
        IsAvailable = bluetoothAvailable,
        IsConnected = false,
        Model = AirPodsModel.Unknown,
        ModelName = "AirPods",
        LastUpdated = DateTimeOffset.MinValue
    };

    public static string GetModelName(AirPodsModel model) => model switch
    {
        AirPodsModel.AirPods1 => "AirPods",
        AirPodsModel.AirPods2 => "AirPods (2nd gen)",
        AirPodsModel.AirPods3 => "AirPods (3rd gen)",
        AirPodsModel.AirPods4 => "AirPods 4",
        AirPodsModel.AirPods4Anc => "AirPods 4 (ANC)",
        AirPodsModel.AirPodsPro => "AirPods Pro",
        AirPodsModel.AirPodsPro2 => "AirPods Pro (2nd gen)",
        AirPodsModel.AirPodsPro2UsbC => "AirPods Pro (2nd gen USB-C)",
        AirPodsModel.AirPodsPro3 => "AirPods Pro (3rd gen)",
        AirPodsModel.AirPodsMax => "AirPods Max",
        AirPodsModel.BeatsFitPro => "Beats Fit Pro",
        _ => "AirPods"
    };
}
