using DynamicIsland.Windows.Models;

namespace DynamicIsland.Windows.Services;

/// <summary>
/// Parses Apple Continuity Proximity Pairing advertisements carried in the Apple manufacturer data (company id 0x004C).
/// This is an independent C# reimplementation based on protocol facts observed from reference material, not a line-for-line port.
/// </summary>
public static class AirPodsParser
{
    public const ushort AppleCompanyId = 0x004C; // 76
    private const byte ProximityPairingType = 0x07;
    private const int ExpectedPayloadLength = 27;
    private const int ExpectedRemaining = 25;

    // Parsed intermediate result matching the wire format's logical fields.
    public sealed record ParsedAdvertisement
    {
        public AirPodsModel Model { get; init; } = AirPodsModel.Unknown;
        public AirPodsSide BroadcastSide { get; init; }
        public int? LeftBattery { get; init; } // 0-100 or null
        public int? RightBattery { get; init; }
        public int? CaseBattery { get; init; }
        public bool LeftCharging { get; init; }
        public bool RightCharging { get; init; }
        public bool CaseCharging { get; init; }
        public bool LeftInEar { get; init; }
        public bool RightInEar { get; init; }
        public bool BothInCase { get; init; }
        public bool CaseLidOpen { get; init; }
    }

    public static bool TryParse(ReadOnlySpan<byte> payload, out ParsedAdvertisement? result)
    {
        result = null;
        if (payload.Length != ExpectedPayloadLength) return false;
        // Validate header: packet type + remaining length.
        if (payload[0] != ProximityPairingType) return false;
        if (payload[1] != ExpectedRemaining) return false;

        // Model id is little-endian at bytes 3..4 (payload[2] is unk1).
        ushort modelId = (ushort)(payload[3] | (payload[4] << 8));
        var model = GetModel(modelId);

        // Flags at payload[5]:
        // bit1 currInEar (0x02), bit2 bothInCase (0x04), bit3 anotInEar (0x08), bit5 broadcastFromLeft (0x20)
        byte flags = payload[5];
        bool currInEar = (flags & 0x02) != 0;
        bool bothInCase = (flags & 0x04) != 0;
        bool anotInEar = (flags & 0x08) != 0;
        bool broadcastFromLeft = (flags & 0x20) != 0;
        var side = broadcastFromLeft ? AirPodsSide.Left : AirPodsSide.Right;

        // Battery bytes:
        // payload[6]: low nibble curr (0x0F), high nibble anot (0xF0)
        // payload[7]: low nibble caseBox (0x0F), bit4 currCharging (0x10), bit5 anotCharging (0x20), bit6 caseCharging (0x40)
        int currRaw = payload[6] & 0x0F;
        int anotRaw = (payload[6] >> 4) & 0x0F;
        int caseRaw = payload[7] & 0x0F;
        bool currCharging = (payload[7] & 0x10) != 0;
        bool anotCharging = (payload[7] & 0x20) != 0;
        bool caseCharging = (payload[7] & 0x40) != 0;

        bool lidOpen;
        {
            byte lid = payload[8];
            bool closed = (lid & 0x08) != 0; // bit3
            lidOpen = !closed;
        }

        // Map current/other to left/right based on broadcast side.
        int? leftVal, rightVal;
        bool leftCharging, rightCharging;
        bool leftInEar, rightInEar;

        if (broadcastFromLeft)
        {
            leftVal = DecodeBattery(currRaw);
            rightVal = DecodeBattery(anotRaw);
            leftCharging = currCharging;
            rightCharging = anotCharging;
            leftInEar = currInEar;
            rightInEar = anotInEar;
        }
        else
        {
            // Right is current when broadcasting from right.
            rightVal = DecodeBattery(currRaw);
            leftVal = DecodeBattery(anotRaw);
            rightCharging = currCharging;
            leftCharging = anotCharging;
            rightInEar = currInEar;
            leftInEar = anotInEar;
        }

        // Charging suppresses in-ear: if charging, report not in ear.
        if (leftCharging) leftInEar = false;
        if (rightCharging) rightInEar = false;

        // Convert battery units 0..10 to percent 0..100, null if unavailable.
        int? leftPct = leftVal.HasValue ? leftVal.Value * 10 : null;
        int? rightPct = rightVal.HasValue ? rightVal.Value * 10 : null;
        int? casePct = DecodeBattery(caseRaw) is int cv ? cv * 10 : null;

        // Note: case battery unavailable remains null; don't fabricate 0.
        result = new ParsedAdvertisement
        {
            Model = model,
            BroadcastSide = side,
            LeftBattery = leftPct,
            RightBattery = rightPct,
            CaseBattery = casePct,
            LeftCharging = leftCharging,
            RightCharging = rightCharging,
            CaseCharging = caseCharging,
            LeftInEar = leftInEar,
            RightInEar = rightInEar,
            BothInCase = bothInCase,
            CaseLidOpen = lidOpen
        };
        return true;
    }

    private static int? DecodeBattery(int raw)
    {
        // Valid range 0..10 inclusive. 0xF (15) traditionally means unavailable / disconnected; any >10 is treated unavailable.
        if (raw >= 0 && raw <= 10) return raw;
        return null;
    }

    public static AirPodsModel GetModel(ushort modelId) => modelId switch
    {
        0x2002 => AirPodsModel.AirPods1,
        0x200F => AirPodsModel.AirPods2,
        0x2013 => AirPodsModel.AirPods3,
        0x2019 => AirPodsModel.AirPods4,
        0x201B => AirPodsModel.AirPods4Anc,
        0x200E => AirPodsModel.AirPodsPro,
        0x2014 => AirPodsModel.AirPodsPro2,
        0x2024 => AirPodsModel.AirPodsPro2UsbC,
        0x2027 => AirPodsModel.AirPodsPro3,
        0x200A => AirPodsModel.AirPodsMax,
        0x2012 => AirPodsModel.BeatsFitPro,
        _ => AirPodsModel.Unknown
    };

    // Helper for tests: build a synthetic payload that encodes the desired logical state.
    public static byte[] BuildTestPacket(
        ushort modelId,
        AirPodsSide side,
        int? leftBattery10, // 0..10 or null for unavailable
        int? rightBattery10,
        int? caseBattery10,
        bool leftCharging = false,
        bool rightCharging = false,
        bool caseCharging = false,
        bool leftInEar = false,
        bool rightInEar = false,
        bool bothInCase = true,
        bool lidOpen = true)
    {
        var payload = new byte[27];
        payload[0] = ProximityPairingType;
        payload[1] = ExpectedRemaining;
        payload[2] = 0x00; // unk1
        payload[3] = (byte)(modelId & 0xFF);
        payload[4] = (byte)(modelId >> 8);

        byte flags = 0;
        // Map logical left/right inEar to curr/anot based on side.
        bool currInEar, anotInEar;
        byte currChargingBit, anotChargingBit;
        int currRaw, anotRaw;

        if (side == AirPodsSide.Left)
        {
            currInEar = leftInEar;
            anotInEar = rightInEar;
            currRaw = leftBattery10 ?? 0x0F;
            anotRaw = rightBattery10 ?? 0x0F;
            currChargingBit = leftCharging ? (byte)1 : (byte)0;
            anotChargingBit = rightCharging ? (byte)1 : (byte)0;
            flags |= 0x20; // broadcast from left
        }
        else
        {
            currInEar = rightInEar;
            anotInEar = leftInEar;
            currRaw = rightBattery10 ?? 0x0F;
            anotRaw = leftBattery10 ?? 0x0F;
            currChargingBit = rightCharging ? (byte)1 : (byte)0;
            anotChargingBit = leftCharging ? (byte)1 : (byte)0;
        }

        if (currInEar) flags |= 0x02;
        if (bothInCase) flags |= 0x04;
        if (anotInEar) flags |= 0x08;
        payload[5] = flags;

        payload[6] = (byte)((anotRaw & 0x0F) << 4 | (currRaw & 0x0F));
        int caseRaw = caseBattery10 ?? 0x0F;
        byte b7 = (byte)(caseRaw & 0x0F);
        if (currChargingBit != 0) b7 |= 0x10;
        if (anotChargingBit != 0) b7 |= 0x20;
        if (caseCharging) b7 |= 0x40;
        payload[7] = b7;

        byte lid = 0;
        if (!lidOpen) lid |= 0x08;
        // switchCount etc zero
        payload[8] = lid;
        payload[9] = 0x00; // color white
        payload[10] = 0x00;
        // unk12 16 bytes remain zero
        return payload;
    }
}
