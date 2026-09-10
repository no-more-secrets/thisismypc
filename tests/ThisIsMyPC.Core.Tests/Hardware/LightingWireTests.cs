using System.Buffers.Binary;
using System.Text;
using ThisIsMyPC.Core.Hardware.Detection;
using ThisIsMyPC.Core.Hardware.Lighting;

namespace ThisIsMyPC.Core.Tests.Hardware;

/// <summary>
/// The controller description is built here byte by byte from
/// RGBController::GetDeviceDescriptionData's field order, not from the
/// parser's own writer, so a shared misreading cannot pass.
/// </summary>
public sealed class LightingWireTests
{
    private sealed class Blob
    {
        private readonly List<byte> _bytes = [];

        public Blob U16(int value)
        {
            Span<byte> span = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(span, (ushort)value);
            _bytes.AddRange(span);
            return this;
        }

        public Blob U32(uint value)
        {
            Span<byte> span = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(span, value);
            _bytes.AddRange(span);
            return this;
        }

        public Blob I32(int value) => U32(unchecked((uint)value));

        public Blob Str(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            U16(bytes.Length + 1);
            _bytes.AddRange(bytes);
            _bytes.Add(0);
            return this;
        }

        public Blob Color(byte r, byte g, byte b) => U32(r | ((uint)g << 8) | ((uint)b << 16));

        /// <summary>Prefixes the 32-bit total size the server writes first.</summary>
        public byte[] AsReply()
        {
            var total = _bytes.Count + 4;
            var reply = new byte[total];
            BinaryPrimitives.WriteUInt32LittleEndian(reply, (uint)total);
            _bytes.CopyTo(reply, 4);
            return reply;
        }
    }

    /// <summary>A motherboard with two modes and two zones, serialized for protocol version 4.</summary>
    private static byte[] MotherboardV4() => new Blob()
        .U32(0)                                   // DEVICE_TYPE_MOTHERBOARD
        .Str("ASUS ROG STRIX B550-F")             // name
        .Str("ASUS")                              // vendor (v1+)
        .Str("ASUS Aura motherboard")             // description
        .Str("1.0")                               // version
        .Str("")                                  // serial
        .Str("I2C: /dev/i2c-0, address 0x4E")     // location
        .U16(2)                                   // num_modes
        .I32(1)                                   // active_mode
        // mode 0: Direct, per-LED colors, brightness (v3+)
        .Str("Direct").I32(0)
        .U32((uint)(LightingModeFlags.HasPerLedColor | LightingModeFlags.HasBrightness))
        .U32(0).U32(0)                            // speed min/max
        .U32(0).U32(100)                          // brightness min/max (v3+)
        .U32(0).U32(0)                            // colors min/max
        .U32(0)                                   // speed
        .U32(80)                                  // brightness (v3+)
        .U32(0)                                   // direction
        .U32(1)                                   // color mode: per LED
        .U16(0)                                   // no mode colors
        // mode 1: Breathing, mode-specific colors, speed, random, manual save
        .Str("Breathing").I32(2)
        .U32((uint)(LightingModeFlags.HasSpeed | LightingModeFlags.HasModeSpecificColor | LightingModeFlags.HasRandomColor | LightingModeFlags.ManualSave))
        .U32(1).U32(5)                            // speed min/max
        .U32(0).U32(0)                            // brightness min/max
        .U32(1).U32(2)                            // colors min/max
        .U32(3)                                   // speed
        .U32(0)                                   // brightness
        .U32(0)                                   // direction
        .U32(2)                                   // color mode: mode specific
        .U16(1).Color(255, 64, 0)                 // one orange color
        // zones
        .U16(2)
        .Str("Aura Header 1").U32(1)              // type linear
        .U32(1).U32(120).U32(4)                   // leds min/max/count
        .U16(0)                                   // no matrix map
        .U16(0)                                   // no segments (v4+)
        .Str("Keyboard").U32(2)                   // type matrix
        .U32(6).U32(6).U32(6)
        .U16(8 + 6 * 4).U32(2).U32(3)             // matrix: height 2, width 3, then 6 map entries
        .U32(0).U32(1).U32(2).U32(3).U32(4).U32(5)
        .U16(1).Str("Left half").U32(1).U32(0).U32(3)   // one segment (v4+)
        // leds
        .U16(10)
        .Str("LED 1").U32(0).Str("LED 2").U32(1).Str("LED 3").U32(2).Str("LED 4").U32(3)
        .Str("K1").U32(0).Str("K2").U32(1).Str("K3").U32(2).Str("K4").U32(3).Str("K5").U32(4).Str("K6").U32(5)
        // colors
        .U16(10)
        .Color(255, 0, 0).Color(255, 0, 0).Color(255, 0, 0).Color(255, 0, 0)
        .Color(0, 0, 255).Color(0, 0, 255).Color(0, 0, 255).Color(0, 0, 255).Color(0, 0, 255).Color(0, 0, 255)
        .AsReply();

    [Fact]
    public void ParseDevice_ReadsEveryFieldInWireOrder()
    {
        var device = LightingWire.ParseDevice(MotherboardV4(), 4, deviceIndex: 3);

        Assert.Equal(3, device.Index);
        Assert.Equal(LightingDeviceType.Motherboard, device.Type);
        Assert.Equal("ASUS ROG STRIX B550-F", device.Name);
        Assert.Equal("ASUS", device.Vendor);
        Assert.Equal("ASUS Aura motherboard", device.Description);
        Assert.Equal("1.0", device.Version);
        Assert.Equal("", device.Serial);
        Assert.Equal("I2C: /dev/i2c-0, address 0x4E", device.Location);
        Assert.Equal(1, device.ActiveModeIndex);
        Assert.Equal("Breathing", device.ActiveMode?.Name);

        var direct = device.Modes[0];
        Assert.Equal(0, direct.Index);
        Assert.True(direct.HasPerLedColor);
        Assert.True(direct.HasBrightness);
        Assert.Equal(100u, direct.BrightnessMax);
        Assert.Equal(80u, direct.Brightness);
        Assert.Equal(LightingColorMode.PerLed, direct.ColorMode);
        Assert.False(direct.HasSpeed);

        var breathing = device.Modes[1];
        Assert.Equal(2, breathing.Value);
        Assert.True(breathing.HasSpeed);
        Assert.Equal(1u, breathing.SpeedMin);
        Assert.Equal(5u, breathing.SpeedMax);
        Assert.Equal(3u, breathing.Speed);
        Assert.True(breathing.HasModeSpecificColor);
        Assert.True(breathing.HasRandomColor);
        Assert.True(breathing.CanSave);
        Assert.False(breathing.HasBrightness);
        Assert.Equal([new RgbColor(255, 64, 0)], breathing.Colors);

        Assert.Equal(2, device.Zones.Count);
        Assert.Equal("Aura Header 1", device.Zones[0].Name);
        Assert.Equal(LightingZoneType.Linear, device.Zones[0].Type);
        Assert.Equal(4u, device.Zones[0].LedCount);
        Assert.Equal(0u, device.Zones[0].MatrixHeight);
        Assert.Equal(LightingZoneType.Matrix, device.Zones[1].Type);
        Assert.Equal(2u, device.Zones[1].MatrixHeight);
        Assert.Equal(3u, device.Zones[1].MatrixWidth);
        Assert.Single(device.Zones[1].Segments);
        Assert.Equal("Left half", device.Zones[1].Segments[0].Name);
        Assert.Equal(3u, device.Zones[1].Segments[0].LedCount);

        Assert.Equal(10, device.Leds.Count);
        Assert.Equal("K6", device.Leds[9].Name);
        Assert.Equal(5u, device.Leds[9].Value);
        Assert.Equal(10, device.Colors.Count);
        Assert.Equal(new RgbColor(255, 0, 0), device.Colors[0]);
        Assert.Equal(new RgbColor(0, 0, 255), device.Colors[9]);
    }

    [Fact]
    public void ParseDevice_Version0_HasNoVendorBrightnessOrSegments()
    {
        var reply = new Blob()
            .U32(4).Str("Strip")                  // LED strip, name (no vendor at v0)
            .Str("d").Str("v").Str("s").Str("l")
            .U16(1).I32(0)
            .Str("Static").I32(0).U32(0x20)       // per-LED
            .U32(0).U32(0)                        // speed
            .U32(0).U32(0)                        // colors min/max (no brightness at v0)
            .U32(0)                               // speed
            .U32(0).U32(1).U16(0)                 // direction, color mode, colors
            .U16(1).Str("Z").U32(1).U32(1).U32(1).U32(1).U16(0)   // zone, no segments at v0
            .U16(1).Str("L").U32(0)
            .U16(1).Color(1, 2, 3)
            .AsReply();

        var device = LightingWire.ParseDevice(reply, 0, 0);

        Assert.Equal(LightingDeviceType.LedStrip, device.Type);
        Assert.Equal("", device.Vendor);
        Assert.Equal(0u, device.Modes[0].BrightnessMax);
        Assert.Empty(device.Zones[0].Segments);
        Assert.Equal(new RgbColor(1, 2, 3), device.Colors[0]);
    }

    [Fact]
    public void ParseDevice_TruncatedOrWrongSize_Throws()
    {
        var reply = MotherboardV4();
        Assert.Throws<InvalidDataException>(() => LightingWire.ParseDevice(reply.AsSpan(0, reply.Length - 10), 4, 0));

        var wrongSize = (byte[])reply.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(wrongSize, (uint)wrongSize.Length + 1);
        Assert.Throws<InvalidDataException>(() => LightingWire.ParseDevice(wrongSize, 4, 0));
    }

    [Fact]
    public void RgbColor_WireLayoutIsRedLowByte()
    {
        var color = new RgbColor(0x11, 0x22, 0x33);
        Assert.Equal(0x00332211u, color.ToWire());
        Assert.Equal(color, RgbColor.FromWire(0x00332211u));
        Assert.Equal("#112233", color.ToHex());
        Assert.Equal(color, RgbColor.TryParseHex("112233"));
        Assert.Equal(color, RgbColor.TryParseHex("#112233"));
        Assert.Null(RgbColor.TryParseHex("#12345"));
        Assert.Null(RgbColor.TryParseHex("zzzzzz"));
    }

    [Fact]
    public void BuildUpdateLeds_CarriesSizeCountAndColors()
    {
        var packet = LightingWire.BuildUpdateLeds(2, [new RgbColor(1, 2, 3), new RgbColor(4, 5, 6)]);

        Assert.True(OpenRgbSdkProtocol.TryParseHeader(packet, out var header));
        Assert.Equal(2u, header.DeviceIndex);
        Assert.Equal(OpenRgbSdkProtocol.UpdateLeds, header.PacketId);
        Assert.Equal(14u, header.PayloadLength);
        var payload = packet.AsSpan(16);
        Assert.Equal(14u, BinaryPrimitives.ReadUInt32LittleEndian(payload));
        Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]));
        Assert.Equal(0x00030201u, BinaryPrimitives.ReadUInt32LittleEndian(payload[6..]));
        Assert.Equal(0x00060504u, BinaryPrimitives.ReadUInt32LittleEndian(payload[10..]));
    }

    [Fact]
    public void BuildUpdateZoneLeds_CarriesTheZoneIndex()
    {
        var packet = LightingWire.BuildUpdateZoneLeds(0, 1, [new RgbColor(9, 9, 9)]);

        var payload = packet.AsSpan(16);
        Assert.Equal(14u, BinaryPrimitives.ReadUInt32LittleEndian(payload));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(payload[4..]));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(payload[8..]));
        Assert.Equal(0x00090909u, BinaryPrimitives.ReadUInt32LittleEndian(payload[10..]));
    }

    [Fact]
    public void BuildUpdateMode_WritesTheDescriptionTheServerReadsBack()
    {
        var device = LightingWire.ParseDevice(MotherboardV4(), 4, 0);
        var breathing = device.Modes[1] with { Speed = 5, ColorMode = LightingColorMode.Random };

        var packet = LightingWire.BuildUpdateMode(7, breathing, 4);

        Assert.True(OpenRgbSdkProtocol.TryParseHeader(packet, out var header));
        Assert.Equal(7u, header.DeviceIndex);
        Assert.Equal(OpenRgbSdkProtocol.UpdateMode, header.PacketId);
        var payload = packet.AsSpan(16);
        Assert.Equal((uint)payload.Length, BinaryPrimitives.ReadUInt32LittleEndian(payload));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(payload[4..]));

        // The description is byte-identical to what the server sent for this mode, except the two changed fields.
        var expected = new Blob()
            .Str("Breathing").I32(2)
            .U32((uint)(LightingModeFlags.HasSpeed | LightingModeFlags.HasModeSpecificColor | LightingModeFlags.HasRandomColor | LightingModeFlags.ManualSave))
            .U32(1).U32(5).U32(0).U32(0).U32(1).U32(2)
            .U32(5)                               // speed changed
            .U32(0).U32(0)
            .U32(3)                               // color mode random
            .U16(1).Color(255, 64, 0)
            .AsReply()[4..];
        Assert.Equal(expected, payload[8..].ToArray());

        var save = LightingWire.BuildUpdateMode(7, breathing, 4, save: true);
        Assert.True(OpenRgbSdkProtocol.TryParseHeader(save, out var saveHeader));
        Assert.Equal(OpenRgbSdkProtocol.SaveMode, saveHeader.PacketId);
    }

    [Fact]
    public void BuildUpdateMode_Version6_OmitsTheModeValue()
    {
        var mode = new LightingMode { Index = 0, Name = "X", Value = 9 };
        var v4 = LightingWire.WriteMode(mode, 4);
        var v6 = LightingWire.WriteMode(mode, 6);

        Assert.Equal(v4.Length - 4, v6.Length);
    }

    [Fact]
    public void Mode_DirectionsFollowTheFlags()
    {
        var mode = new LightingMode
        {
            Index = 0,
            Name = "Wave",
            Flags = LightingModeFlags.HasDirectionLeftRight | LightingModeFlags.HasDirectionUpDown,
        };

        Assert.True(mode.HasDirection);
        Assert.Equal([LightingDirection.Left, LightingDirection.Right, LightingDirection.Up, LightingDirection.Down], mode.Directions);
        Assert.Empty(new LightingMode { Index = 0, Name = "Static" }.Directions);
    }
}
