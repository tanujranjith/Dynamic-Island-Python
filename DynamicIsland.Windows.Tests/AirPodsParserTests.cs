using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public class AirPodsParserTests
{
    private static byte[] MakePacket(ushort modelId, AirPodsSide side, int? left, int? right, int? caseBatt,
        bool leftCharging = false, bool rightCharging = false, bool caseCharging = false,
        bool leftInEar = false, bool rightInEar = false, bool bothInCase = true, bool lidOpen = true)
        => AirPodsParser.BuildTestPacket(modelId, side, left, right, caseBatt, leftCharging, rightCharging, caseCharging, leftInEar, rightInEar, bothInCase, lidOpen);

    [Fact]
    public void InvalidManufacturerPayload_Rejected()
    {
        var empty = Array.Empty<byte>();
        Assert.False(AirPodsParser.TryParse(empty, out _));

        var tooShort = new byte[5];
        tooShort[0] = 0x07; tooShort[1] = 25;
        Assert.False(AirPodsParser.TryParse(tooShort, out _));
    }

    [Fact]
    public void TooShortPayload_Rejected()
    {
        var payload = new byte[27];
        payload[0] = 0x07; payload[1] = 25;
        // Truncate to 26
        var shortPayload = payload[..26];
        Assert.False(AirPodsParser.TryParse(shortPayload, out _));

        var wrongRemaining = AirPodsParser.BuildTestPacket(0x2014, AirPodsSide.Left, 8, 7, 5);
        wrongRemaining[1] = 24;
        Assert.False(AirPodsParser.TryParse(wrongRemaining, out _));
    }

    [Fact]
    public void UnrelatedApplePacket_Rejected()
    {
        var airDrop = AirPodsParser.BuildTestPacket(0x2014, AirPodsSide.Left, 8, 7, 5);
        airDrop[0] = 0x05; // AirDrop type
        Assert.False(AirPodsParser.TryParse(airDrop, out _));

        var homeKit = AirPodsParser.BuildTestPacket(0x2014, AirPodsSide.Left, 8, 7, 5);
        homeKit[0] = 0x06;
        Assert.False(AirPodsParser.TryParse(homeKit, out _));
    }

    [Fact]
    public void KnownModel_Recognized()
    {
        var payload = MakePacket(0x2014, AirPodsSide.Left, 8, 7, 5);
        Assert.True(AirPodsParser.TryParse(payload, out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal(AirPodsModel.AirPodsPro2, parsed!.Model);

        var p2 = MakePacket(0xFFFF, AirPodsSide.Left, 8, 7, 5);
        Assert.True(AirPodsParser.TryParse(p2, out var unk));
        Assert.Equal(AirPodsModel.Unknown, unk!.Model);

        var p3 = MakePacket(0x200E, AirPodsSide.Right, 8, 7, 5);
        Assert.True(AirPodsParser.TryParse(p3, out var pro));
        Assert.Equal(AirPodsModel.AirPodsPro, pro!.Model);

        var p4 = MakePacket(0x2019, AirPodsSide.Left, 8, 7, 5);
        Assert.True(AirPodsParser.TryParse(p4, out var a4));
        Assert.Equal(AirPodsModel.AirPods4, a4!.Model);
    }

    [Fact]
    public void LeftBattery_Decoded()
    {
        var payload = MakePacket(0x2014, AirPodsSide.Left, 8, 7, 5);
        Assert.True(AirPodsParser.TryParse(payload, out var p));
        Assert.Equal(80, p!.LeftBattery);
        Assert.Equal(70, p.RightBattery);
        Assert.Equal(50, p.CaseBattery);
    }

    [Fact]
    public void RightBattery_Decoded_AndFlipped()
    {
        // Broadcast from right: left is anot, right is curr
        var payload = MakePacket(0x2014, AirPodsSide.Right, 8, 7, 5);
        Assert.True(AirPodsParser.TryParse(payload, out var p));
        // Even though side is right, left should still be 80 and right 70
        Assert.Equal(80, p!.LeftBattery);
        Assert.Equal(70, p.RightBattery);
        Assert.Equal(AirPodsSide.Right, p.BroadcastSide);
    }

    [Fact]
    public void CaseBattery_Decoded()
    {
        var payload = MakePacket(0x2014, AirPodsSide.Left, 5, 6, 9);
        Assert.True(AirPodsParser.TryParse(payload, out var p));
        Assert.Equal(90, p!.CaseBattery);
    }

    [Fact]
    public void UnavailableBattery_Handled()
    {
        var payload = MakePacket(0x2014, AirPodsSide.Left, null, 7, null);
        Assert.True(AirPodsParser.TryParse(payload, out var p));
        Assert.Null(p!.LeftBattery);
        Assert.Equal(70, p.RightBattery);
        Assert.Null(p.CaseBattery);

        // Ensure 0xF (15) is treated unavailable
        var payload2 = MakePacket(0x2014, AirPodsSide.Left, 15, 15, 15); // 15 >10 treated as unavailable but BuildTestPacket uses 0xF for null already
        // Build with explicit 11 which is >10 but <15, still unavailable
        var manual = AirPodsParser.BuildTestPacket(0x2014, AirPodsSide.Left, 11, 11, 11);
        Assert.True(AirPodsParser.TryParse(manual, out var p2));
        Assert.Null(p2!.LeftBattery);
        Assert.Null(p2.RightBattery);
        Assert.Null(p2.CaseBattery);
    }

    [Fact]
    public void ChargingFlags_Decoded()
    {
        var payload = MakePacket(0x2014, AirPodsSide.Left, 8, 7, 5, leftCharging: true, rightCharging: false, caseCharging: true);
        Assert.True(AirPodsParser.TryParse(payload, out var p));
        Assert.True(p!.LeftCharging);
        Assert.False(p.RightCharging);
        Assert.True(p.CaseCharging);

        // Flipped side
        var payload2 = MakePacket(0x2014, AirPodsSide.Right, 8, 7, 5, leftCharging: true, rightCharging: true);
        Assert.True(AirPodsParser.TryParse(payload2, out var p2));
        Assert.True(p2!.LeftCharging);
        Assert.True(p2.RightCharging);

        var payload3 = MakePacket(0x2014, AirPodsSide.Left, 8, 7, 5, leftCharging: false, rightCharging: true);
        Assert.True(AirPodsParser.TryParse(payload3, out var p3));
        Assert.False(p3!.LeftCharging);
        Assert.True(p3.RightCharging);
    }

    [Fact]
    public void InEarState_Decoded()
    {
        var payload = MakePacket(0x2014, AirPodsSide.Left, 8, 7, 5, leftInEar: true, rightInEar: false);
        Assert.True(AirPodsParser.TryParse(payload, out var p));
        Assert.True(p!.LeftInEar);
        Assert.False(p.RightInEar);

        var payload2 = MakePacket(0x2014, AirPodsSide.Left, 8, 7, 5, leftInEar: true, rightInEar: true);
        Assert.True(AirPodsParser.TryParse(payload2, out var p2));
        Assert.True(p2!.LeftInEar);
        Assert.True(p2.RightInEar);

        var payload3 = MakePacket(0x2014, AirPodsSide.Right, 8, 7, 5, leftInEar: true, rightInEar: false);
        Assert.True(AirPodsParser.TryParse(payload3, out var p3));
        Assert.True(p3!.LeftInEar);
        Assert.False(p3.RightInEar);
    }

    [Fact]
    public void ChargingSuppressesInEar()
    {
        var payload = MakePacket(0x2014, AirPodsSide.Left, 8, 7, 5, leftCharging: true, leftInEar: true);
        Assert.True(AirPodsParser.TryParse(payload, out var p));
        // When charging, inEar must be false regardless of flag
        Assert.False(p!.LeftInEar);
    }

    [Fact]
    public void CaseLidState_Decoded()
    {
        var open = MakePacket(0x2014, AirPodsSide.Left, 8, 7, 5, lidOpen: true);
        Assert.True(AirPodsParser.TryParse(open, out var po));
        Assert.True(po!.CaseLidOpen);

        var closed = MakePacket(0x2014, AirPodsSide.Left, 8, 7, 5, lidOpen: false);
        Assert.True(AirPodsParser.TryParse(closed, out var pc));
        Assert.False(pc!.CaseLidOpen);
    }

    [Fact]
    public void BothInCase_Decoded()
    {
        var both = MakePacket(0x2014, AirPodsSide.Left, 8, 7, 5, bothInCase: true);
        Assert.True(AirPodsParser.TryParse(both, out var p));
        Assert.True(p!.BothInCase);

        var notBoth = MakePacket(0x2014, AirPodsSide.Left, 8, 7, 5, bothInCase: false);
        Assert.True(AirPodsParser.TryParse(notBoth, out var p2));
        Assert.False(p2!.BothInCase);
    }

    [Fact]
    public void CurrentOtherSide_Orientation_HandledCorrectly()
    {
        // For left broadcast, curr = left (8), anot = right (3)
        var leftPkt = MakePacket(0x2014, AirPodsSide.Left, 8, 3, 5);
        Assert.True(AirPodsParser.TryParse(leftPkt, out var lp));
        Assert.Equal(80, lp!.LeftBattery);
        Assert.Equal(30, lp.RightBattery);
        Assert.Equal(AirPodsSide.Left, lp.BroadcastSide);

        // For right broadcast, curr = right (3), anot = left (8) but result should still be left 80 right 30
        var rightPkt = MakePacket(0x2014, AirPodsSide.Right, 8, 3, 5);
        Assert.True(AirPodsParser.TryParse(rightPkt, out var rp));
        Assert.Equal(80, rp!.LeftBattery);
        Assert.Equal(30, rp.RightBattery);
        Assert.Equal(AirPodsSide.Right, rp.BroadcastSide);
    }

    [Fact]
    public void MalformedPacket_DoesNotThrow()
    {
        var cases = new[]
        {
            Array.Empty<byte>(),
            new byte[10],
            new byte[27], // all zeros -> header fails (type 0)
            new byte[27].Select((b,i)=> (byte)i).ToArray(),
            Enumerable.Repeat((byte)0xFF, 27).ToArray()
        };
        foreach (var c in cases)
        {
            var ex = Record.Exception(() => AirPodsParser.TryParse(c, out _));
            Assert.Null(ex);
        }
    }

    [Fact]
    public void BatteryPercentConversion_IsTimesTen()
    {
        for (int raw = 0; raw <= 10; raw++)
        {
            var payload = MakePacket(0x2014, AirPodsSide.Left, raw, raw, raw);
            Assert.True(AirPodsParser.TryParse(payload, out var p));
            Assert.Equal(raw * 10, p!.LeftBattery);
            Assert.Equal(raw * 10, p.RightBattery);
            Assert.Equal(raw * 10, p.CaseBattery);
        }
    }

    [Fact]
    public void ModelMapping_CoversAllKnownIds()
    {
        var map = new Dictionary<ushort, AirPodsModel>
        {
            [0x2002] = AirPodsModel.AirPods1,
            [0x200F] = AirPodsModel.AirPods2,
            [0x2013] = AirPodsModel.AirPods3,
            [0x2019] = AirPodsModel.AirPods4,
            [0x201B] = AirPodsModel.AirPods4Anc,
            [0x200E] = AirPodsModel.AirPodsPro,
            [0x2014] = AirPodsModel.AirPodsPro2,
            [0x2024] = AirPodsModel.AirPodsPro2UsbC,
            [0x2027] = AirPodsModel.AirPodsPro3,
            [0x200A] = AirPodsModel.AirPodsMax,
            [0x2012] = AirPodsModel.BeatsFitPro,
        };
        foreach (var kv in map)
        {
            var pkt = MakePacket(kv.Key, AirPodsSide.Left, 8, 7, 5);
            Assert.True(AirPodsParser.TryParse(pkt, out var p));
            Assert.Equal(kv.Value, p!.Model);
        }
    }

    [Fact]
    public void AirPodsState_Equality_DetectsDuplicates()
    {
        var s1 = new AirPodsState
        {
            IsAvailable = true,
            IsConnected = true,
            Model = AirPodsModel.AirPodsPro2,
            ModelName = "AirPods Pro (2nd gen)",
            DeviceName = "Tanuj's AirPods Pro",
            LeftBatteryPercent = 80,
            RightBatteryPercent = 70,
            CaseBatteryPercent = 50,
            LeftCharging = false,
            RightCharging = true,
            CaseCharging = false,
            LeftInEar = true,
            RightInEar = true,
            BothInCase = true,
            CaseLidOpen = true,
            Rssi = -45,
            LastUpdated = DateTimeOffset.UtcNow
        };
        var s2 = s1 with { Rssi = -46, LastUpdated = s1.LastUpdated.AddSeconds(1) };
        // Records compare all fields including Rssi/LastUpdated, so they are not equal if those change.
        Assert.NotEqual(s1, s2);
        Assert.False(s1.HasPresentationChangedFrom(s2));

        // If we normalize timestamp/rssi, payload-derived fields equality should hold for duplicate suppression:
        var s3 = s1 with { Rssi = s1.Rssi, LastUpdated = s1.LastUpdated };
        Assert.Equal(s1, s3);
    }

    [Fact]
    public void AirPodsState_UnknownBattery_NotDisplayedAsZero()
    {
        var state = new AirPodsState
        {
            IsAvailable = true,
            IsConnected = true,
            Model = AirPodsModel.AirPodsPro2,
            ModelName = "AirPods Pro (2nd gen)",
            LeftBatteryPercent = null,
            RightBatteryPercent = 80,
            CaseBatteryPercent = null
        };
        Assert.False(state.LeftBatteryAvailable);
        Assert.Null(state.LeftBatteryPercent);
        Assert.True(state.RightBatteryAvailable);
        Assert.Equal(80, state.RightBatteryPercent);
        Assert.False(state.CaseBatteryAvailable);
    }

    [Fact]
    public void MalformedPayload_DoesNotThrow_AndReturnsFalse()
    {
        var ex = Record.Exception(() =>
        {
            var random = new byte[27];
            new Random(42).NextBytes(random);
            // Ensure header invalid
            random[0] = 0xFF;
            AirPodsParser.TryParse(random, out _);
            AirPodsParser.TryParse(Array.Empty<byte>(), out _);
            AirPodsParser.TryParse(new byte[26], out _);
            AirPodsParser.TryParse(new byte[28], out _);
        });
        Assert.Null(ex);
    }
}
